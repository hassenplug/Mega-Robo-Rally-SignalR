using System.Data;
using System.Text.Json;
using MRR;
using MRR.Config;
using MRR.Config.BoardPhotoImport;
using Microsoft.AspNetCore.Http.Features;
using MySqlConnector;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// Configuration & Authoring host (mrr-config.service, :5001).
// Owns Boards / BoardItems / BoardItemActions / GameData. Never writes CurrentGameData,
// Robots, MoveCards or CommandList -- those belong to the game host. See
// API_DECOMPOSITION_DESIGN.md sections 5.2 and 6.

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<BoardData>();
builder.Services.AddSingleton(sp => new BoardPhotoImportService(new TileTemplateCatalog(
    Path.Combine(sp.GetRequiredService<IWebHostEnvironment>().WebRootPath, "images"))));
var app = builder.Build();

// UseDefaultFiles before UseStaticFiles, so "/" serves board-editor.html. The game host has
// these the other way round, which is why "/" returns 404 there and the phones have to be
// pointed at an explicit filename.
app.UseDefaultFiles(new DefaultFilesOptions { DefaultFileNames = ["board-editor.html"] });
app.UseStaticFiles();

// Liveness probe for mrr-health.service. No DB access, so it answers even if MariaDB is down.
app.MapGet("/api/health", () => Results.Ok(new { status = "ok", role = "config" }));

// ── Board Editor API ────────────────────────────────────────────────────────
// Route paths are unchanged from when these lived in the game host, so board-editor.html
// keeps working untouched. Renaming them to /api/boards per the design doc is a follow-up.

app.MapGet("/api/boardeditor/types", (BoardData data) =>
{
    var items = data.GetQueryResults(
        "SELECT X, Y, SquareType, Rotation FROM BoardItems WHERE BoardID=0 ORDER BY SquareType;");
    var actions = data.GetQueryResults(
        "SELECT X, Y, SquareAction, ActionSequence, Phase, Parameter FROM BoardItemActions WHERE BoardID=0;");

    var actLookup = new Dictionary<string, List<object>>();
    foreach (DataRow r in actions.Rows)
    {
        var key = $"{r["X"]},{r["Y"]}";
        if (!actLookup.TryGetValue(key, out var list)) actLookup[key] = list = new List<object>();
        list.Add(new
        {
            squareAction = Convert.ToInt32(r["SquareAction"]),
            actionName = Enum.GetName(typeof(SquareAction), Convert.ToInt32(r["SquareAction"])) ?? "Unknown",
            sequence = Convert.ToInt32(r["ActionSequence"]),
            phase = Convert.ToInt32(r["Phase"]),
            parameter = Convert.ToInt32(r["Parameter"])
        });
    }

    var types = new List<object>();
    foreach (DataRow r in items.Rows)
    {
        int typeId = Convert.ToInt32(r["SquareType"]);
        int rotation = Convert.ToInt32(r["Rotation"]);
        var key = $"{r["X"]},{r["Y"]}";
        types.Add(new
        {
            name = Enum.GetName(typeof(SquareType), typeId) ?? typeId.ToString(),
            typeId,
            defaultRotation = rotation,
            canRotate = rotation != 0,
            actions = actLookup.TryGetValue(key, out var al) ? al : new List<object>()
        });
    }
    return Results.Ok(types);
});

app.MapGet("/api/boardeditor/boards", (BoardData data) =>
    Results.Content(data.GetQueryResultsJson(
        "SELECT BoardID, BoardName, X, Y, LaserDamage, PhaseCount FROM Boards ORDER BY BoardID;",
        "boards"), "application/json"));

app.MapGet("/api/boardeditor/{boardId:int}", (int boardId, BoardData data) =>
{
    var boardTable = data.GetQueryResults(
        "SELECT BoardID, BoardName, X, Y, LaserDamage, PhaseCount, GameType FROM Boards WHERE BoardID=@id;",
        ("@id", boardId));
    if (boardTable.Rows.Count == 0)
        return Results.NotFound(new { error = $"Board {boardId} not found" });

    var br = boardTable.Rows[0];

    var items = new List<object>();
    foreach (DataRow row in data.GetQueryResults(
        "SELECT X, Y, SquareType, Rotation FROM BoardItems WHERE BoardID=@id;", ("@id", boardId)).Rows)
    {
        items.Add(new
        {
            x = Convert.ToInt32(row["X"]),
            y = Convert.ToInt32(row["Y"]),
            squareType = Convert.ToInt32(row["SquareType"]),
            rotation = Convert.ToInt32(row["Rotation"])
        });
    }

    var actions = new List<object>();
    foreach (DataRow row in data.GetQueryResults(
        "SELECT X, Y, SquareAction, ActionSequence, Phase, Parameter FROM BoardItemActions WHERE BoardID=@id;",
        ("@id", boardId)).Rows)
    {
        actions.Add(new
        {
            x = Convert.ToInt32(row["X"]),
            y = Convert.ToInt32(row["Y"]),
            squareAction = Convert.ToInt32(row["SquareAction"]),
            sequence = Convert.ToInt32(row["ActionSequence"]),
            phase = Convert.ToInt32(row["Phase"]),
            parameter = Convert.ToInt32(row["Parameter"])
        });
    }

    return Results.Ok(new
    {
        boardId = Convert.ToInt32(br["BoardID"]),
        boardName = br["BoardName"]?.ToString() ?? "",
        x = BoardData.AsInt(br["X"]),
        y = BoardData.AsInt(br["Y"]),
        laserDamage = BoardData.AsInt(br["LaserDamage"], 1),
        // PhaseCount is NULL on most boards; 5 is the schema default used everywhere else.
        phaseCount = BoardData.AsInt(br["PhaseCount"], 5),
        gameType = BoardData.AsInt(br["GameType"]),
        items,
        actions
    });
});

app.MapPost("/api/boardeditor", async (BoardData data, HttpRequest request) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        using var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
        var root = doc.RootElement;

        var boardName = root.GetProperty("boardName").GetString() ?? "New Board";
        var w = root.GetProperty("x").GetInt32();
        var h = root.GetProperty("y").GetInt32();
        var laserDamage = root.GetProperty("laserDamage").GetInt32();
        var phaseCount = root.GetProperty("phaseCount").GetInt32();
        var gameType = root.GetProperty("gameType").GetInt32();

        // Boards has no AUTO_INCREMENT -- calculate the next id manually (skip template 0).
        var newBoardId = data.GetIntFromDB("SELECT COALESCE(MAX(BoardID), 0) + 1 FROM Boards WHERE BoardID > 0;");
        if (newBoardId < 1) newBoardId = 1;

        using var connection = data.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var insert = new MySqlCommand(
            "INSERT INTO Boards (BoardID, BoardName, X, Y, LaserDamage, PhaseCount, GameType) " +
            "VALUES (@id, @name, @w, @h, @laser, @phases, @gameType);", connection, transaction))
        {
            insert.Parameters.AddWithValue("@id", newBoardId);
            insert.Parameters.AddWithValue("@name", boardName);
            insert.Parameters.AddWithValue("@w", w);
            insert.Parameters.AddWithValue("@h", h);
            insert.Parameters.AddWithValue("@laser", laserDamage);
            insert.Parameters.AddWithValue("@phases", phaseCount);
            insert.Parameters.AddWithValue("@gameType", gameType);
            insert.ExecuteNonQuery();
        }

        var values = new List<string>(w * h);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                values.Add($"({newBoardId},{x},{y},0,0)");
        if (values.Count > 0)
        {
            using var blank = new MySqlCommand(
                "INSERT INTO BoardItems (BoardID, X, Y, SquareType, Rotation) VALUES " +
                string.Join(',', values) + ";", connection, transaction);
            blank.ExecuteNonQuery();
        }

        transaction.Commit();
        return Results.Ok(new { boardId = newBoardId });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPut("/api/boardeditor/{boardId:int}", async (int boardId, BoardData data, HttpRequest request) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        using var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
        var root = doc.RootElement;

        var boardName = root.GetProperty("boardName").GetString() ?? "Board";
        var w = root.GetProperty("x").GetInt32();
        var h = root.GetProperty("y").GetInt32();
        var laserDamage = root.GetProperty("laserDamage").GetInt32();
        var phaseCount = root.GetProperty("phaseCount").GetInt32();
        var gameType = root.GetProperty("gameType").GetInt32();
        var squares = root.GetProperty("squares");

        // TotalFlags is the highest flag number, not the count of flag squares: a board can
        // have several squares carrying the same numbered checkpoint. Matches
        // BoardElementCollection.CalcTotalFlags().
        int totalFlags = 0, totalPlayers = 0;
        foreach (var sq in squares.EnumerateArray())
        {
            if (!sq.TryGetProperty("actions", out var acts)) continue;
            foreach (var act in acts.EnumerateArray())
            {
                var sa = act.GetProperty("squareAction").GetInt32();
                var param = act.GetProperty("parameter").GetInt32();
                if (sa == (int)SquareAction.Flag && param > totalFlags) totalFlags = param;
                if (sa == (int)SquareAction.PlayerStart && param > totalPlayers) totalPlayers = param;
            }
        }

        // One transaction for the whole replace. Previously this deleted every square and
        // then re-inserted with no transaction, so a malformed request left the board empty.
        using var connection = data.OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var header = new MySqlCommand(
            "UPDATE Boards SET BoardName=@name, X=@w, Y=@h, LaserDamage=@laser, " +
            "PhaseCount=@phases, GameType=@gameType, TotalFlags=@totalFlags WHERE BoardID=@id;",
            connection, transaction))
        {
            header.Parameters.AddWithValue("@name", boardName);
            header.Parameters.AddWithValue("@w", w);
            header.Parameters.AddWithValue("@h", h);
            header.Parameters.AddWithValue("@laser", laserDamage);
            header.Parameters.AddWithValue("@phases", phaseCount);
            header.Parameters.AddWithValue("@gameType", gameType);
            header.Parameters.AddWithValue("@totalFlags", totalFlags);
            header.Parameters.AddWithValue("@id", boardId);
            header.ExecuteNonQuery();
        }

        void Run(string sql)
        {
            using var command = new MySqlCommand(sql, connection, transaction);
            command.ExecuteNonQuery();
        }

        Run($"DELETE FROM BoardItemActions WHERE BoardID={boardId};");
        Run($"DELETE FROM BoardItems WHERE BoardID={boardId};");

        var itemValues = new List<string>();
        var actionValues = new List<string>();
        foreach (var sq in squares.EnumerateArray())
        {
            var sx = sq.GetProperty("x").GetInt32();
            var sy = sq.GetProperty("y").GetInt32();
            itemValues.Add($"({boardId},{sx},{sy}," +
                           $"{sq.GetProperty("squareType").GetInt32()},{sq.GetProperty("rotation").GetInt32()})");

            if (!sq.TryGetProperty("actions", out var acts)) continue;
            foreach (var act in acts.EnumerateArray())
            {
                actionValues.Add($"({boardId},{sx},{sy}," +
                    $"{act.GetProperty("squareAction").GetInt32()},{act.GetProperty("sequence").GetInt32()}," +
                    $"{act.GetProperty("phase").GetInt32()},{act.GetProperty("parameter").GetInt32()})");
            }
        }

        if (itemValues.Count > 0)
            Run("INSERT INTO BoardItems (BoardID, X, Y, SquareType, Rotation) VALUES " +
                string.Join(',', itemValues) + ";");
        if (actionValues.Count > 0)
            Run("INSERT INTO BoardItemActions (BoardID, X, Y, SquareAction, ActionSequence, Phase, Parameter) VALUES " +
                string.Join(',', actionValues) + ";");

        transaction.Commit();
        return Results.Ok(new { success = true, totalFlags, totalPlayers });
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

// ── Validation ──────────────────────────────────────────────────────────────
// Catches board problems that otherwise fail at runtime inside the rules engine. Verified
// against the live DB: 6 of 89 boards have gaps in their flag numbering, which makes them
// unwinnable because a flag is only touched when LastFlag + 1 == its number.

app.MapGet("/api/boardeditor/{boardId:int}/validate", (int boardId, BoardData data) =>
{
    var header = data.GetQueryResults(
        "SELECT BoardName, X, Y, TotalFlags FROM Boards WHERE BoardID=@id;", ("@id", boardId));
    if (header.Rows.Count == 0)
        return Results.NotFound(new { error = $"Board {boardId} not found" });

    int cols = BoardData.AsInt(header.Rows[0]["X"]);
    int rows = BoardData.AsInt(header.Rows[0]["Y"]);
    int storedTotalFlags = BoardData.AsInt(header.Rows[0]["TotalFlags"]);

    var errors = new List<string>();
    var warnings = new List<string>();

    var flags = new List<int>();
    var starts = new List<int>();
    foreach (DataRow row in data.GetQueryResults(
        "SELECT SquareAction, Parameter FROM BoardItemActions WHERE BoardID=@id AND SquareAction IN (@flag, @start);",
        ("@id", boardId), ("@flag", (int)SquareAction.Flag), ("@start", (int)SquareAction.PlayerStart)).Rows)
    {
        int parameter = Convert.ToInt32(row["Parameter"]);
        if (Convert.ToInt32(row["SquareAction"]) == (int)SquareAction.Flag) flags.Add(parameter);
        else starts.Add(parameter);
    }

    // Flags must be numbered contiguously from 1: the rules only advance a robot when it
    // reaches LastFlag + 1, so a gap makes every flag past it unreachable.
    if (flags.Count == 0)
    {
        warnings.Add("Board has no flags, so it cannot be won.");
    }
    else
    {
        var distinct = flags.Distinct().OrderBy(f => f).ToList();
        int highest = distinct[^1];
        var missing = Enumerable.Range(1, highest).Where(n => !distinct.Contains(n)).ToList();
        if (missing.Count > 0)
            errors.Add($"Flag numbering has gaps ({string.Join(", ", missing)} missing of 1..{highest}). " +
                       "Robots cannot advance past a missing flag, so the board is unwinnable.");
        if (storedTotalFlags != highest)
            warnings.Add($"Boards.TotalFlags is {storedTotalFlags} but the highest flag on the board is {highest}. " +
                         "Saving the board corrects this.");
    }

    // Start positions must be unique -- two robots assigned the same square collide at setup.
    var duplicateStarts = starts.GroupBy(s => s).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
    if (duplicateStarts.Count > 0)
        errors.Add($"Duplicate player start positions: {string.Join(", ", duplicateStarts)}.");

    // Squares outside the declared board size are unreachable.
    int stray = data.GetIntFromDB(
        "SELECT COUNT(*) FROM BoardItems WHERE BoardID=@id AND (X >= @cols OR Y >= @rows);",
        ("@id", boardId), ("@cols", cols), ("@rows", rows));
    if (stray > 0)
        errors.Add($"{stray} square(s) lie outside the declared board size of {cols}x{rows}.");

    int missingSquares = (cols * rows) - data.GetIntFromDB(
        "SELECT COUNT(*) FROM BoardItems WHERE BoardID=@id AND X < @cols AND Y < @rows;",
        ("@id", boardId), ("@cols", cols), ("@rows", rows));
    if (missingSquares > 0)
        warnings.Add($"{missingSquares} of {cols * rows} cells have no BoardItems row.");

    return Results.Ok(new
    {
        boardId,
        boardName = header.Rows[0]["BoardName"]?.ToString() ?? "",
        valid = errors.Count == 0,
        flagCount = flags.Distinct().Count(),
        playerStarts = starts.Distinct().Count(),
        errors,
        warnings
    });
});

// ── .srx import / export ────────────────────────────────────────────────────

app.MapPost("/api/boardeditor/{boardId:int}/import", async (int boardId, BoardData data, HttpRequest request) =>
{
    var path = (await new StreamReader(request.Body).ReadToEndAsync()).Trim();
    if (string.IsNullOrWhiteSpace(path))
        return Results.BadRequest(new { error = "Request body must be the path to a .srx file." });

    var board = BoardData.LoadBoardFile(path);
    if (board == null)
        return Results.BadRequest(new { error = $"Could not read a board from {path}" });

    data.BoardSaveToDB(boardId, board);
    return Results.Ok(new
    {
        boardId,
        squares = board.BoardElements.Count,
        totalFlags = board.CalcTotalFlags(),
        cols = board.BoardCols,
        rows = board.BoardRows
    });
});

// ── Photo import ────────────────────────────────────────────────────────────
// Photo of the physical tiles -> board, by matching each cell against the tile images. See
// BOARD_PHOTO_IMPORT_DESIGN.md. Like every other endpoint on this host there is no login:
// anyone who can reach :5001 can overwrite a board with this, as they already can with PUT.
// Replaces every square of board {boardId}; board-import.html confirms before overwriting.

const long MaxPhotoBytes = 12 * 1024 * 1024;
const int MaxPhotoPixels = 40_000_000;  // a phone camera is ~12-50 MP; board-import.html sends <= 2000px
const int MaxImportCells = 40;          // same limit as the editor's W/H inputs

app.MapPost("/api/boardeditor/{boardId:int}/import-photo", async (
    int boardId, BoardData data, BoardPhotoImportService importer, HttpRequest request) =>
{
    if (boardId == 0)
        return Results.BadRequest(new { error = "Board 0 is the palette template and cannot be imported over." });

    var sizeLimit = request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (sizeLimit is { IsReadOnly: false }) sizeLimit.MaxRequestBodySize = MaxPhotoBytes + 64 * 1024;
    if (request.ContentLength > MaxPhotoBytes + 64 * 1024 || !request.HasFormContentType)
        return Results.BadRequest(new { error = $"Send multipart/form-data with a photo of at most {MaxPhotoBytes / 1024 / 1024} MB." });

    IFormCollection form;
    try { form = await request.ReadFormAsync(); }
    catch (Exception ex) when (ex is InvalidDataException or BadHttpRequestException)
    {
        return Results.BadRequest(new { error = $"Could not read the upload: {ex.Message}" });
    }

    var file = form.Files.GetFile("photo");
    if (file == null || file.Length == 0)
        return Results.BadRequest(new { error = "No photo uploaded (form field 'photo')." });
    if (file.Length > MaxPhotoBytes)
        return Results.BadRequest(new { error = $"Photo is larger than {MaxPhotoBytes / 1024 / 1024} MB." });
    if (!file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        return Results.BadRequest(new { error = $"Expected an image, got {file.ContentType}." });

    if (!int.TryParse(form["cols"], out int cols) || !int.TryParse(form["rows"], out int rows)
        || cols < 1 || rows < 1 || cols > MaxImportCells || rows > MaxImportCells)
        return Results.BadRequest(new { error = $"cols and rows must be whole numbers from 1 to {MaxImportCells}." });

    // corners = "x,y;x,y;x,y;x,y" in photo pixels: top-left, top-right, bottom-right, bottom-left.
    var corners = new List<PointF>();
    foreach (var pair in form["corners"].ToString().Split(';', StringSplitOptions.RemoveEmptyEntries))
    {
        var xy = pair.Split(',');
        if (xy.Length != 2
            || !float.TryParse(xy[0], System.Globalization.CultureInfo.InvariantCulture, out float cx)
            || !float.TryParse(xy[1], System.Globalization.CultureInfo.InvariantCulture, out float cy))
            return Results.BadRequest(new { error = $"Bad corner '{pair}'; expected x,y." });
        corners.Add(new PointF(cx, cy));
    }
    if (corners.Count != 4)
        return Results.BadRequest(new { error = $"Expected 4 corners (top-left, top-right, bottom-right, bottom-left), got {corners.Count}." });

    var modeText = form["mode"].ToString();
    var mode = MatchMode.Grayscale;
    if (modeText.Length > 0 && !Enum.TryParse(modeText, ignoreCase: true, out mode))
        return Results.BadRequest(new { error = $"Unknown mode '{modeText}'; use {string.Join(", ", Enum.GetNames<MatchMode>())}." });

    float threshold = BoardPhotoImportService.DefaultThreshold;
    if (form.ContainsKey("threshold")
        && !float.TryParse(form["threshold"], System.Globalization.CultureInfo.InvariantCulture, out threshold))
        return Results.BadRequest(new { error = "threshold must be a number." });

    var header = data.GetQueryResults(
        "SELECT BoardName, LaserDamage, GameType FROM Boards WHERE BoardID=@id;", ("@id", boardId));
    if (header.Rows.Count == 0)
        return Results.NotFound(new { error = $"Board {boardId} not found. Create it first." });

    // The template supplies every matched tile's actions. Without it the board would import
    // with no walls, belts or flags and still look right in the editor.
    var template = data.BoardLoadFromDB(0);
    if (template.BoardElements.Count == 0)
        return Results.Conflict(new { error = "The palette template (BoardID 0) is empty, so imported tiles would have no actions. " +
                                              "Seed it with POST /api/boardeditor/template/seed and try again." });

    Image<Rgb24> photo;
    try
    {
        await using var stream = file.OpenReadStream();
        var info = await Image.IdentifyAsync(stream);
        if ((long)info.Width * info.Height > MaxPhotoPixels)
            return Results.BadRequest(new { error = $"Photo is {info.Width}x{info.Height}; the limit is {MaxPhotoPixels / 1_000_000} megapixels." });
        stream.Position = 0;
        photo = await Image.LoadAsync<Rgb24>(stream);
    }
    catch (Exception ex) when (ex is UnknownImageFormatException or InvalidImageContentException)
    {
        return Results.BadRequest(new { error = $"Could not read the photo: {ex.Message}" });
    }

    using (photo)
    {
        // Phone JPEGs are often stored sideways with an EXIF flag; the browser showed the
        // turned version, so the corners are in those coordinates.
        photo.Mutate(ctx => ctx.AutoOrient());
        if (corners.Any(p => p.X < 0 || p.Y < 0 || p.X > photo.Width || p.Y > photo.Height))
            return Results.BadRequest(new { error = $"A corner lies outside the {photo.Width}x{photo.Height} photo." });

        BoardElementCollection board;
        List<ImportedCell> cells;
        try
        {
            (board, cells) = importer.Import(photo, corners, cols, rows, mode, threshold, template);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        // BoardSaveToDB writes these header fields from the collection; keep the target board's.
        var row = header.Rows[0];
        board.BoardName = row["BoardName"]?.ToString();
        board.LaserDamage = BoardData.AsInt(row["LaserDamage"], 1);
        board.GameType = BoardData.AsInt(row["GameType"]);
        data.BoardSaveToDB(boardId, board);

        return Results.Ok(new
        {
            boardId,
            cols,
            rows,
            mode = mode.ToString(),
            threshold,
            totalFlags = board.CalcTotalFlags(),
            needsReview = cells.Count(c => c.NeedsReview),
            cells
        });
    }
});

// ── Square-type palette template (BoardID 0) ────────────────────────────────
// The editor's palette reads BoardID 0. The host had a SeedBoardTemplate function meant to
// populate it from install/BoardTemplate.srx, but nothing ever called it, so BoardID 0 is
// empty in the live database and the palette comes back empty. This endpoint is that
// capability, in the host that owns board data.

app.MapPost("/api/boardeditor/template/seed", (BoardData data, IWebHostEnvironment env) =>
{
    string[] candidates =
    [
        Path.Combine(Directory.GetCurrentDirectory(), "..", "install", "BoardTemplate.srx"),
        Path.Combine(AppContext.BaseDirectory, "..", "install", "BoardTemplate.srx"),
        Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "install", "BoardTemplate.srx"),
    ];
    var found = candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    if (found == null)
        return Results.NotFound(new { error = "BoardTemplate.srx not found", searched = candidates.Select(Path.GetFullPath) });

    var template = BoardData.LoadBoardFile(found);
    if (template == null)
        return Results.BadRequest(new { error = $"Could not parse {found}" });

    data.ExecuteSQL(
        "INSERT INTO Boards (BoardID, BoardName, X, Y, LaserDamage, PhaseCount, GameType, TotalFlags, Players) " +
        "VALUES (0, 'Template', @cols, @rows, @laser, 5, @gameType, @flags, 0) " +
        "ON DUPLICATE KEY UPDATE BoardName='Template', X=VALUES(X), Y=VALUES(Y), " +
        "LaserDamage=VALUES(LaserDamage), GameType=VALUES(GameType), TotalFlags=VALUES(TotalFlags);",
        ("@cols", template.BoardCols), ("@rows", template.BoardRows),
        ("@laser", template.LaserDamage), ("@gameType", template.GameType),
        ("@flags", template.CalcTotalFlags()));

    // BoardSaveToDB writes BoardName from the collection, and a .srx carries none, so
    // without this the row ends up called "Imported" and reads like a playable board in
    // the editor's list.
    template.BoardName = "Template";
    data.BoardSaveToDB(0, template);

    return Results.Ok(new
    {
        boardId = 0,
        squares = template.BoardElements.Count,
        actions = template.BoardElements.Sum(be => be.ActionList.Count),
        source = found
    });
});

// ── Game Data API ───────────────────────────────────────────────────────────

app.MapGet("/api/boardeditor/gamedata", (BoardData data) =>
    Results.Content(data.GetQueryResultsJson(
        "SELECT GameDataID, Description, GameCode, BoardID, GameType, TotalFlags, LaserDamage, " +
        "PhaseCount, BoardCols, BoardRows, OptionCount, PlayerListID FROM GameData ORDER BY GameDataID;",
        "gameData"), "application/json"));

app.MapPut("/api/boardeditor/gamedata/{gameDataId:int}", async (int gameDataId, BoardData data, HttpRequest request) =>
{
    try
    {
        using var reader = new StreamReader(request.Body);
        using var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
        var boardId = doc.RootElement.GetProperty("boardId").GetInt32();

        if (data.GetIntFromDB("SELECT COUNT(*) FROM Boards WHERE BoardID=@id;", ("@id", boardId)) == 0)
            return Results.NotFound(new { error = $"Board {boardId} not found" });
        if (data.GetIntFromDB("SELECT COUNT(*) FROM GameData WHERE GameDataID=@id;", ("@id", gameDataId)) == 0)
            return Results.NotFound(new { error = $"GameData {gameDataId} not found" });

        data.ExecuteSQL(
            "UPDATE GameData g JOIN Boards b ON b.BoardID=@boardId " +
            "SET g.BoardID=b.BoardID, g.LaserDamage=b.LaserDamage, g.PhaseCount=b.PhaseCount, " +
            "g.BoardCols=b.X, g.BoardRows=b.Y, g.TotalFlags=b.TotalFlags, " +
            "g.GameType=b.GameType, g.Description=b.BoardName " +
            "WHERE g.GameDataID=@gameDataId;",
            ("@boardId", boardId), ("@gameDataId", gameDataId));

        // NOTE: this used to also run
        //     UPDATE CurrentGameData SET iValue={gameDataId} WHERE iKey=26
        // which made the edited GameData the active game. That is deliberately gone.
        // CurrentGameData belongs to the game host, and a config process cannot invalidate
        // the host's in-memory copy of it. It was also only a half-activation -- it set
        // GameDataID without copying the board, flags or phase count, unlike
        // GameController.LoadGameData(). Use the GM panel's game selection to activate a
        // game; that path does the full copy.

        var result = data.GetQueryResultsJson(
            "SELECT GameDataID, Description, GameCode, BoardID, GameType, TotalFlags, LaserDamage, " +
            "PhaseCount, BoardCols, BoardRows, OptionCount, PlayerListID FROM GameData WHERE GameDataID=@id;",
            "gameData", ("@id", gameDataId));
        return Results.Content(result, "application/json");
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.Run();
