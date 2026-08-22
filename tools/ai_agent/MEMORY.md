# Memory Index — Mega Robo Rally

- [project_overview.md](project_overview.md) — What MRR is, hardware stack, architecture summary
- **`database_schema.md` — MISSING.** Referenced here but not present in this directory. For
  the schema use [install/MRRDatabase.sql](../../install/MRRDatabase.sql) (the source of truth)
  or [.claude/agents/mrr-database.md](../../.claude/agents/mrr-database.md). Note the DB has
  had no views, procedures, functions, or triggers since the C# migration.
- [image_processing.md](image_processing.md) — Always use SixLabors.ImageSharp; System.Drawing.Common and SkiaSharp fail on Raspberry Pi (linux-arm64)
- [grid_alignment_agent.md](grid_alignment_agent.md) — GridAlignmentAgent.cs: camera-based board centering, detection logic, calibration constants, REST endpoint
- [ws_img_format.md](ws_img_format.md) — AIM robot ws_img WebSocket format is unconfirmed; what to test and where to update if format differs
