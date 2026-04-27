# Plan: Make CommandItem an EF Entity (CommandList table)

## Context

`CommandItem` (in `CommandList.cs`) is the in-memory game command object. It is built during turn calculation and then written to the `CommandList` DB table via raw SQL string concatenation in `CreateCommands.AddOneCommandToDB()`. Reading back from the DB (in `CommandProcess.cs`) uses a separate EF entity `PendingCommandEntity` that duplicates the DB schema. The goal is to eliminate this duplication: make `CommandItem` itself the EF entity so the same object is used end-to-end — game logic, DB write, and DB read.

## What changes

### 1. `MRR/CommandList.cs` — Add EF attributes to `CommandItem`

Add `using` directives for `System.ComponentModel.DataAnnotations` and `.Schema`.

Add class-level: `[Table("CommandList")]`

**New stored properties (not currently on CommandItem):**
- `[Key][DatabaseGenerated(DatabaseGeneratedOption.Identity)] public int CommandID { get; set; }` — auto-assigned by the existing DB trigger
- `public int Turn { get; set; }` — set from `Turn` before saving

**Column-rename attributes on existing properties:**
- `[Column("Parameter")] public int Value { get; set; }` (rename in DB only)
- `[Column("ParameterB")] public int ValueB { get; set; }`
- `[Column("CommandSequence")] public int NormalSequence { get; set; }`
- `[Column("CommandSubSequence")] public int RunningCounter { get; set; }`

**New int-bridge properties (expose enums as ints for DB):**
- `[Column("CommandTypeID")] public int CommandTypeID { get => (int)CommandType; set => CommandType = (SquareAction)value; }`
- `[Column("StatusID")] public int StatusID { get => (int)Status; set => Status = (CommandStatus)value; }`

**New stored properties for decomposed/computed DB columns:**
- `[Column("BTCommand")] public string BTCommand { get; set; } = "";` — stored; set from `StringCommand` before saving
- `public int PositionRow { get; set; }` — set from `EndPos.Y` before saving
- `public int PositionCol { get; set; }` — set from `EndPos.X` before saving
- `public int PositionDir { get; set; }` — set from `(int)EndPos.Direction` before saving
- `public int CommandCatID { get; set; }` — set from `(int)Category` before saving

**`Description` backing field (computed + stored):**
Change from computed-only to:
```csharp
private string? _dbDescription;
[Column("Description")]
public string Description {
    get => _dbDescription ?? GetCommandDetails.Description;
    set => _dbDescription = value;
}
```
EF loads the stored value; game logic falls back to computed.

**`RobotID` — fix empty setter, decouple from `Robot`:**

Change to a plain stored property:
```csharp
public int RobotID { get; set; }
```
In the constructor, set it explicitly: `RobotID = p_Robot?.ID ?? 0;` (instead of deriving it via the Robot getter).
`Robot` stays `[NotMapped]` and is only populated during in-memory game logic (construction, CreateCommands). It is never populated when loading from DB — callers that need the player look it up themselves.

**`[NotMapped]` on properties with no DB column:**
`Robot`, `CommandDirection`, `StartPos`, `EndPos`, `PhaseStep`, `PhaseStepAdder`, `ExpressSequence`, `CommandType`, `CommandTypeInt`, `Status`, `text`, `GetCommandDetails`, `Category` (computed), `CommandSequence` (computed — differs from DB CommandSequence), `StringCommand`, `IsRobotMoveCommand`, comparison helpers.

---

### 2. `MRR/Data/MRRDbContext.cs` — Swap entity type

Replace:
```csharp
public DbSet<PendingCommandEntity> PendingCommands { get; set; }
```
With:
```csharp
public DbSet<CommandItem> CommandItems { get; set; }
```
Update `OnModelCreating` configuration to reference `CommandItem`.

---

### 3. `MRR/CreateCommands.cs` — Replace raw SQL INSERT with EF

In `AddCommandsToDatabase()`, open one `MRRDbContext` for the batch.

Replace `AddOneCommandToDB(CommandItem)` body: populate all flat DB fields from the rich object, then `ctx.CommandItems.Add(command)`.

Before saving each command, set:
```csharp
cmd.CommandID = 0;            // let trigger assign
cmd.Turn = Turn;
cmd.CommandTypeID = cmd.CommandTypeInt;
cmd.StatusID = (int)cmd.Status;
cmd.BTCommand = cmd.StringCommand;
cmd.PositionRow = cmd.EndPos.Y;
cmd.PositionCol = cmd.EndPos.X;
cmd.PositionDir = (int)cmd.EndPos.Direction;
cmd.CommandCatID = (int)cmd.Category;
```
Then `ctx.SaveChanges()` once after all commands are added (or per-command if needed for trigger compat).

---

### 4. `MRR/DataService.cs` — Replace raw SQL SELECT/UPDATE in `ProcessDbCommand` with EF

Replace the manual `MySqlConnection` SELECT block with:
```csharp
using var ctx = CreateDbContext();
var cmd = ctx.CommandItems.Find(p_CommandID);
if (cmd == null) return p_NewStatus == -1 ? 6 : p_NewStatus;
```
Read `cmd.CommandTypeID`, `cmd.RobotID`, `cmd.Value` (Parameter), `cmd.ValueB` (ParameterB), `cmd.PositionRow`, `cmd.PositionCol`, `cmd.PositionDir`.

Replace the final raw `UPDATE CommandList SET StatusID` with:
```csharp
cmd.StatusID = p_NewStatus;
ctx.SaveChanges();
```

Remove the `ChangeTracker.Tracked` hook in `CreateDbContext()` entirely — it is no longer needed since `Robot` is never populated from DB.

---

### 5. `MRR/CommandProcess.cs` — Use `CommandItem` instead of `PendingCommandEntity`

- Change `GetActiveCommandList()` return type and query to use `ctx.CommandItems`
- Change `ProcessCommand(PendingCommandEntity)` → `ProcessCommand(CommandItem)`
- Replace `command.RobotPlayer` with a local lookup: `var robot = _dataService.AllPlayers.GetPlayer(p => p.ID == command.RobotID);`
- Remove `using MRR.Data.Entities;`

---

### 6. `MRR/AIMRobot.cs` — Update `SendRobotCommandAsync` signature

Change `SendRobotCommandAsync(PendingCommandEntity Command)` to accept `CommandItem`:
```csharp
public async Task SendRobotCommandAsync(CommandItem cmd)
{
    await SendRobotCommandAsync(cmd.CommandID, cmd.Value, cmd.ValueB,
        (cmd.CommandCatID == 1) ? 1 : 0);
}
```

---

### 7. `MRR/Data/PendingCommand.cs` — Delete

This file is fully replaced by the EF attributes on `CommandItem`.

---

## Critical files

| File | Change |
|---|---|
| `MRR/CommandList.cs` | EF attributes on `CommandItem` |
| `MRR/Data/MRRDbContext.cs` | `DbSet<CommandItem>` |
| `MRR/CreateCommands.cs` | EF INSERT replaces raw SQL |
| `MRR/DataService.cs` | EF SELECT/UPDATE replaces raw SQL; tracker updated |
| `MRR/CommandProcess.cs` | Use `CommandItem` throughout |
| `MRR/AIMRobot.cs` | Signature update |
| `MRR/Data/PendingCommand.cs` | Delete |

## Verification

1. Build: `dotnet build MRR/` — should compile with zero errors
2. Start server: `dotnet run --project MRR/` — confirm startup, DB connection OK
3. Start a game and run a full turn — commands appear in DB `CommandList` with correct values
4. Confirm `CommandProcess` executes each command and updates `StatusID` via EF
5. Confirm `Description` and `BTCommand` columns are populated in the DB rows
