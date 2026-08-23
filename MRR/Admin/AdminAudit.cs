using System.Collections.Concurrent;

namespace MRR.Admin
{
    /// <summary>One statement run through the Admin API.</summary>
    public record AdminAuditEntry(
        DateTime WhenUtc, string Caller, string Statement, int Turn, int Phase,
        int RowsAffected, bool Mutating, string? Error);

    /// <summary>
    /// Audit trail for hand-edits made through the Admin API.
    ///
    /// This is the thing the old /api/table endpoint most lacked: when a game goes strange
    /// in round 4, "what did someone change, and when" is otherwise unanswerable. Kept in
    /// memory for the running session and appended to a file so it survives a restart.
    /// </summary>
    public class AdminAudit
    {
        private const int MaxInMemory = 500;
        private readonly ConcurrentQueue<AdminAuditEntry> _entries = new();
        private readonly string _logPath;
        private readonly object _fileLock = new();

        public AdminAudit(string? logPath = null)
        {
            _logPath = logPath ?? Path.Combine(AppContext.BaseDirectory, "admin-audit.log");
        }

        public void Record(AdminAuditEntry entry)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > MaxInMemory) _entries.TryDequeue(out _);

            var line = $"{entry.WhenUtc:O}\t{entry.Caller}\tturn={entry.Turn}\tphase={entry.Phase}\t" +
                       $"rows={entry.RowsAffected}\tmutating={entry.Mutating}\t" +
                       $"{(entry.Error is null ? "OK" : "ERROR: " + entry.Error)}\t" +
                       entry.Statement.Replace('\n', ' ').Replace('\r', ' ');
            try
            {
                lock (_fileLock) File.AppendAllText(_logPath, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                // Never let auditing break the operation it is recording; the in-memory copy
                // still has it, and the failure is visible in the console.
                Console.WriteLine($"[admin-audit] could not write {_logPath}: {ex.Message}");
            }
        }

        public IReadOnlyList<AdminAuditEntry> Recent(int count = 100) =>
            [.. _entries.Reverse().Take(count)];

        public string LogPath => _logPath;
    }
}
