using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using MRR.Controller;
using MRR.Hubs;
using MRR.Services;

namespace MRR.Admin
{
    /// <summary>
    /// Admin and diagnostics: edit any table, run direct SQL to fix game state mid-session.
    /// Replaces the old /api/table endpoints. See API_DECOMPOSITION_DESIGN.md section 5.7.
    ///
    /// Three things the endpoint it replaces did not do:
    ///
    /// 1. Reload after a write. Master holds game state in memory, so a direct
    ///    "UPDATE Robots ..." would otherwise be invisible to the running game, and then be
    ///    overwritten by the next write-through. Every mutation reloads and republishes.
    /// 2. Audit. Every statement is recorded with turn and phase.
    /// 3. Refuse remote callers. Arbitrary SQL must not be reachable from the phone WiFi.
    ///
    /// It also stops mutating on GET: "/api/table/{t}/{filter}/{setvalue}" ran an UPDATE
    /// from a GET, which any crawler or link prefetch could have triggered.
    /// </summary>
    public static class AdminApi
    {
        public static void MapAdminApi(this WebApplication app, AdminAudit audit, AdminAccess access)
        {
            // ── access control ──────────────────────────────────────────────────
            // Loopback is always allowed. Remote use is possible but must be turned on and
            // keyed -- see AdminAccess. The game host binds 0.0.0.0:5000 so six phones can
            // reach it, so anything allowed here is allowed from the game WiFi.
            bool IsLocal(HttpContext http) => access.Check(http) is
                AdminAccess.Decision.AllowedLocal or AdminAccess.Decision.AllowedRemote;

            IResult Refused(HttpContext http)
            {
                var decision = access.Check(http);
                Console.WriteLine($"[admin] refused {http.Request.Method} {http.Request.Path} " +
                                  $"from {http.Connection.RemoteIpAddress}: {decision}");
                return Results.Problem(access.ExplainDenial(decision),
                                       statusCode: StatusCodes.Status403Forbidden);
            }

            static bool IsMutating(string sql)
            {
                var t = sql.TrimStart();
                return !(t.StartsWith("select", StringComparison.OrdinalIgnoreCase)
                      || t.StartsWith("show",   StringComparison.OrdinalIgnoreCase)
                      || t.StartsWith("describe", StringComparison.OrdinalIgnoreCase)
                      || t.StartsWith("explain", StringComparison.OrdinalIgnoreCase));
            }

            // ── tables ──────────────────────────────────────────────────────────

            app.MapGet("/api/admin/tables", (HttpContext http, DataService data) =>
                !IsLocal(http) ? Refused(http) : Results.Ok(new { tables = data.GetTableList() }));

            // Read-only. The old route took a "setvalue" segment and ran an UPDATE from a
            // GET; changing data now requires POST /api/admin/sql.
            app.MapGet("/api/admin/tables/{tablename}", (string tablename, string? filter,
                                                        HttpContext http, DataService data) =>
            {
                if (!IsLocal(http)) return Refused(http);
                var where = string.IsNullOrWhiteSpace(filter) ? "" : " where " + filter;
                var json = data.GetQueryResultsJson($"Select * from {tablename}{where};", tablename);
                return Results.Content(json, "application/json");
            });

            app.MapPost("/api/admin/tables/{tablename}", async (string tablename, HttpContext http,
                                                               DataService data, IHubContext<DataHub> hub) =>
            {
                if (!IsLocal(http)) return Refused(http);
                using var reader = new StreamReader(http.Request.Body);
                var json = await reader.ReadToEndAsync();
                try
                {
                    var result = data.SaveTableData(tablename, json);
                    audit.Record(new AdminAuditEntry(DateTime.UtcNow, Caller(http),
                        $"[save rows] {tablename}", data.Turn, data.Phase, 0, true, null));
                    await ResyncAsync(data, hub);
                    return Results.Ok(result);
                }
                catch (Exception ex)
                {
                    audit.Record(new AdminAuditEntry(DateTime.UtcNow, Caller(http),
                        $"[save rows] {tablename}", data.Turn, data.Phase, 0, true, ex.Message));
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            // ── direct SQL ──────────────────────────────────────────────────────

            app.MapPost("/api/admin/sql", async (HttpContext http, DataService data,
                                                 IHubContext<DataHub> hub) =>
            {
                if (!IsLocal(http)) return Refused(http);

                using var reader = new StreamReader(http.Request.Body);
                var body = (await reader.ReadToEndAsync()).Trim();
                string sql = body;
                if (body.StartsWith('{'))
                {
                    try { sql = JsonDocument.Parse(body).RootElement.GetProperty("sql").GetString() ?? ""; }
                    catch (Exception ex) { return Results.BadRequest(new { error = "Body must be raw SQL or {\"sql\":\"...\"}: " + ex.Message }); }
                }
                if (string.IsNullOrWhiteSpace(sql))
                    return Results.BadRequest(new { error = "No SQL supplied." });

                bool mutating = IsMutating(sql);
                try
                {
                    if (!mutating)
                    {
                        // A read changes nothing, so it neither reloads nor republishes.
                        var json = data.GetQueryResultsJson(sql, "rows");
                        audit.Record(new AdminAuditEntry(DateTime.UtcNow, Caller(http), sql,
                            data.Turn, data.Phase, 0, false, null));
                        return Results.Content(json, "application/json");
                    }

                    int rows = data.ExecuteSQL(sql);
                    audit.Record(new AdminAuditEntry(DateTime.UtcNow, Caller(http), sql,
                        data.Turn, data.Phase, rows, true, null));
                    await ResyncAsync(data, hub);
                    return Results.Ok(new { rowsAffected = rows, reloaded = true });
                }
                catch (Exception ex)
                {
                    audit.Record(new AdminAuditEntry(DateTime.UtcNow, Caller(http), sql,
                        data.Turn, data.Phase, 0, mutating, ex.Message));
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            app.MapGet("/api/admin/sql/history", (HttpContext http, int? limit, AdminAudit a) =>
                !IsLocal(http) ? Refused(http)
                               : Results.Ok(new { logPath = a.LogPath, entries = a.Recent(limit ?? 100) }));

            // ── diagnostics ─────────────────────────────────────────────────────
            // Whether the in-memory game state still agrees with the database. Drift here is
            // the symptom a hand-edit without a reload used to cause.

            app.MapGet("/api/admin/diagnostics", (HttpContext http, DataService data,
                                                  GameController game) =>
            {
                if (!IsLocal(http)) return Refused(http);

                int dbState = data.GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE iKey=10;");
                int dbTurn  = data.GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE iKey=2;");
                int dbFlags = data.GetIntFromDB("SELECT iValue FROM CurrentGameData WHERE iKey=7;");

                return Results.Ok(new
                {
                    inMemory = new { data.GameState, data.Turn, data.Phase, data.TotalFlags, players = data.AllPlayers.Count },
                    inDatabase = new { GameState = dbState, Turn = dbTurn, TotalFlags = dbFlags },
                    drift = new
                    {
                        gameState  = data.GameState != dbState,
                        turn       = data.Turn != dbTurn,
                        totalFlags = data.TotalFlags != dbFlags,
                    },
                    robotsConnected = data.AllPlayers.Count(p => p.isConnected),
                    auditLog = audit.LogPath,
                });
            });
        }

        /// <summary>
        /// Who made the call, for the audit log. Remote callers are tagged, so "who changed
        /// this" distinguishes the operator at the Pi from someone across the network.
        /// </summary>
        private static string Caller(HttpContext http)
        {
            var address = http.Connection.RemoteIpAddress;
            var who = address?.ToString() ?? "unknown";
            return AdminAccess.IsLoopback(address) ? who : who + " (remote)";
        }

        /// <summary>
        /// Re-read game state and push it to the clients. Without this a hand-edit is
        /// invisible to the running game and gets overwritten by the next write-through.
        /// </summary>
        private static async Task ResyncAsync(DataService data, IHubContext<DataHub> hub)
        {
            data.ReloadAllData();
            await hub.Clients.All.SendAsync("AllDataUpdate", data.GetAllDataJson());
        }
    }
}
