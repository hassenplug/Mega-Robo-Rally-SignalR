# AllPlayers Working Copy: Simulation Only (Discard Pattern)

## Revised Understanding

**Correct Pattern**:
```
START ExecuteTurn
  ↓
workingPlayers = AllPlayers.DeepCopy()  // Simulate with a copy
  ↓
Execute phases on COPY
  - Move robots on working copy (for collision/physics checks)
  - Apply damage on working copy (for death checks)
  - Record all changes in ListOfCommands
  - NO database writes
  ↓
END ExecuteTurn
  ↓
Discard workingPlayers ❌
  - Working copy is THROWN AWAY
  - AllPlayers remains UNCHANGED
  - Only ListOfCommands is written to database
  ↓
Later: CommandProcess reads CommandList and executes commands
  - THAT'S when database gets updated with final positions/damage
```

## Why This Pattern?

**Current (broken) flow**:
```
ExecuteTurn mutates AllPlayers directly
    ↓
Scattered ExecuteSQL calls write to DB mid-turn
    ↓
Result: AllPlayers and DB both have partial/inconsistent state
```

**Correct flow**:
```
ExecuteTurn simulates on COPY (AllPlayers untouched)
    ↓
Build CommandList describing what will happen
    ↓
Discard working copy
    ↓
Later: CommandProcess executes commands and updates DB
    ↓
Result: AllPlayers and DB stay in sync; updates are transactional
```

## Implementation

### Step 1: Create DeepCopy Method
**File**: `MRR/Players.cs`

```csharp
/// <summary>
/// Creates a deep copy of all players for turn simulation.
/// The copy is used for physics/collision checks during turn planning.
/// Working copy is discarded after turn execution (not saved back).
/// </summary>
public Players DeepCopy()
{
    var copy = new Players();
    foreach (var player in this)
    {
        copy.Add(new Player(player));  // Player already has copy constructor
    }
    return copy;
}
```

### Step 2: Update ExecuteTurn

**File**: `MRR/CreateCommands.cs`

```csharp
public string ExecuteTurn()
{
    if (GameState != 6)
        return ("Wrong State:" + GameState.ToString());

    g_BoardElements = _dataService.BoardLoadFromDB(BoardID);
    _dataService.ReloadAllData();

    // ✅ Create working copy for simulation
    Players workingPlayers = _dataService.AllPlayers.DeepCopy();

    ListOfCommands.Clear();

    // Update card priorities (read-only, OK)
    foreach (MoveCard thiscard in GameCards)
    {
        thiscard.Priority = workingPlayers.GetPlayer(thiscard.Owner)?.Priority ?? 0;
    }

    // Execute phases on working copy (not real AllPlayers)
    for (int RunningPhase = 1; RunningPhase < PhaseCount + 1; RunningPhase++)
    {
        ExecutePhase(RunningPhase, workingPlayers);
    }

    // Post-process on working copy
    RefreshWorkingPlayers(workingPlayers);
    
    foreach (Player thisplayer in workingPlayers)
    {
        // Turn robot logic on working copy
        CommandItem? lastcommand = null;
        // ... existing logic ...
    }

    // ... existing command finalization ...

    AddCommandsToDatabase();

    Console.WriteLine("Added " + ListOfCommands.Count + " commands");
    
    // ❌ DISCARD working copy (don't save back)
    // workingPlayers is thrown away here
    // AllPlayers remains unchanged
    // Database is updated LATER when CommandProcess executes commands

    return ("Added " + ListOfCommands.Count + " commands");
}
```

### Step 3: Update ExecutePhase Signature

**Change**: Accept working copy parameter

```csharp
public void ExecutePhase(int p_PhaseNumber, Players workingPlayers, bool AllowOptions = true)
{
    ListOfCommands.Phase = p_PhaseNumber;
    
    // Use workingPlayers instead of AllPlayers
    var firstplayer = workingPlayers.OrderBy(ob => ob.Priority).FirstOrDefault();
    ListOfCommands.AddCommand("Run Phase " + p_PhaseNumber.ToString(), firstplayer);
    
    // Process moves on working copy
    foreach (MoveCard thiscard in GameCards.Where(gc => gc.PhasePlayed == p_PhaseNumber))
    {
        Player? thisplayer = workingPlayers.GetPlayer(thiscard.Owner);
        if (thisplayer != null && thisplayer.IsRunning)
        {
            ProcessMove(thiscard, workingPlayers);  // Pass working copy
        }
    }
    
    // Rest of phase execution...
}
```

### Step 4: Update Dependent Methods

All methods that currently accept implicit AllPlayers need to accept working copy:

```csharp
public void ProcessMove(MoveCard? p_movecard, Players workingPlayers)
{
    if (p_movecard == null) return;
    Player? thisplayer = workingPlayers.GetPlayer(p_movecard.Owner);
    if (thisplayer == null) return;
    // ... rest of method
}

public int CalcMoveDistance(Player p_Player, int p_Distance, Direction p_Direction, 
    SquareAction p_MoveType, Players workingPlayers)
{
    // ... use workingPlayers instead of AllPlayers
    Player? l_PushPlayer = workingPlayers.GetPlayer(l_newsquare);
    if (l_PushPlayer != null)
    {
        // Collision on working copy
    }
    // ...
}

public bool MoveRobot(Player p_Robot, RobotLocation p_NewLocation, int p_Distance, 
    Direction p_Direction, SquareAction p_MoveType, Players workingPlayers)
{
    p_Robot.NextPos.SetLocation(p_NewLocation);
    // ... simulation on working copy
    p_Robot.SetLocation();  // Update working player's position
    return StillAlive;
}

public void AddDamage(Player p_Player, int p_Damage, Player? p_DamagedBy = null, 
    Players workingPlayers = null)
{
    p_Player.Damage += p_Damage;
    ListOfCommands.AddCommand(p_Player, SquareAction.Damage, p_Damage);
    
    if (p_Player.IsDead)
    {
        ListOfCommands.AddCommand(p_Player, SquareAction.Dead);
    }
}
```

## Database Write Flow

**Timeline**:

```
ExecuteTurn (state 6)
    ↓
Simulate on workingPlayers copy
    ↓
Build ListOfCommands
    ↓
AddCommandsToDatabase()  ← ONLY database write from ExecuteTurn
    ↓
Discard workingPlayers
    ↓
AllPlayers unchanged ✅
    ↓
State 7: CommandProcess starts
    ↓
For each command in CommandList:
    ProcessDbCommand(command)
        ↓
        Execute command action
        ↓
        Update Robots table (position, damage, etc.)
        ↓
        Update AllPlayers via DataService wrapper
    ↓
State 8+: Turn execution
    ↓
AllPlayers synced with database
```

## Key Points

1. **No working copy persistence**: Simulation results are NOT saved back to AllPlayers or database
2. **Single write point**: Only `AddCommandsToDatabase()` writes to DB (the command records)
3. **Actual updates happen later**: CommandProcess reads commands and executes them, updating the database
4. **AllPlayers consistency**: Stays unchanged during turn planning; only updated when CommandProcess executes
5. **Simulation isolation**: Turn planning doesn't affect other game systems

## Files to Modify

| File | Change | Purpose |
|------|--------|---------|
| **Players.cs** | Add `DeepCopy()` | Create simulation copy |
| **CreateCommands.cs** | Update `ExecuteTurn()` and all called methods | Use working copy for simulation |
| **DataService.cs** | No changes | Don't save working copy back |

## Success Criteria

- ✅ Working copy created at start of ExecuteTurn
- ✅ All collision/physics checks use working copy
- ✅ No mutations to real AllPlayers during turn execution
- ✅ Working copy discarded (garbage collected)
- ✅ Only CommandList written to database
- ✅ CommandProcess later executes commands and updates database
- ✅ AllPlayers reflects final state only after CommandProcess completes
