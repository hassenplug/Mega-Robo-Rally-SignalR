# Code reference

Generated index of the functions in this repository: what each one is, and where it is called. **Regenerate** (do not hand-edit):

```
dotnet run --project MRR.Tools.CodeReference
```

## How reliable is it?

This is a regex and brace-depth scan, **not** a compiler. Treat it as a map to search from, not as proof.

- **Callers are matched by name only.** If a name is declared on several types (or is a common framework name like `Add`), the table flags it with ⚠ and the caller list may include calls to other types' methods. Most names here are unique.
- **Missed calls:** method-group references (`Task.Run(Method)`, `+= Handler`), reflection, calls through string names (other than SignalR `invoke`), local functions, and anything inside an unparsed construct.
- **"What it does"** is the member's `///` summary (C#) or the `//` comment block right above it (JS). `—` means there is none; the text was not invented. Adding a doc comment in the source fills it in on the next run.
- **Not covered:** the ESP32 firmware (`esp32-player-station/`, C++), shell scripts in `install/`, SQL, and properties/fields (only methods and constructors).

## Contents

| File | Covers | Members |
|---|---|---|
| [MRR.md](MRR.md) | C# methods and constructors in `MRR/` | 232 |
| [MRR.Config.md](MRR.Config.md) | C# methods and constructors in `MRR.Config/` | 31 |
| [MRR.Contracts.md](MRR.Contracts.md) | C# methods and constructors in `MRR.Contracts/` | 114 |
| [MRR.Rules.md](MRR.Rules.md) | C# methods and constructors in `MRR.Rules/` | 24 |
| [MRR.Tests.md](MRR.Tests.md) | C# methods and constructors in `MRR.Tests/` | 64 |
| [MRR.Tools.CodeReference.md](MRR.Tools.CodeReference.md) | C# methods and constructors in `MRR.Tools.CodeReference/` | 5 |
| [MRR.Tools.FirmwareCompare.md](MRR.Tools.FirmwareCompare.md) | C# methods and constructors in `MRR.Tools.FirmwareCompare/` | 14 |
| [MRR.Tools.RobotSpriteDraw.md](MRR.Tools.RobotSpriteDraw.md) | C# methods and constructors in `MRR.Tools.RobotSpriteDraw/` | 10 |
| [web.md](web.md) | JavaScript in `MRR/wwwroot` (including inline page scripts) | 192 |
| [api.md](api.md) | 38 HTTP endpoints, SignalR events, hub methods | — |
| [data.md](data.md) | Database tables, robot `cmd_id` commands, `appsettings.json` keys | — |
| [unreferenced.md](unreferenced.md) | Functions with no call sites found | 32 |

Related hand-written docs: [PROJECT_STATUS.md](../../PROJECT_STATUS.md) (how to run it), [CLAUDE.md](../../CLAUDE.md) (architecture and state machine), [API_DECOMPOSITION_DESIGN.md](../API_DECOMPOSITION_DESIGN.md).
