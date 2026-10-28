# Card Knowledge Base — Haywire Cards and Upgrades

Reference tables for the cards that are not plain programming cards. **Not implemented yet** (see
[install/todo.md](../install/todo.md): Haywire cards, Option card effects); this is what the
implementation will be built from.

There are **two separate upgrade lists** in this project, and they do not match:

| List | Source | Used by |
|---|---|---|
| **A. Rulebook upgrades** — 40 cards (24 permanent, 16 temporary) | `documents/RoboRally_Rulebook_WEB.pdf` (2023 Hasbro rulebook), pp. 17–23 | The Renegade rules the project follows (Energy costs, Spam, Reboot) |
| **B. Database `Options`** — 58 rows | `install/MRRDatabase.sql` (`Options` table, seed data); loaded by `DataService.LoadOptionCardsFromDatabase()` | The code that exists today: `OptionCard`/`OptionCardList`, `RobotOptions` |

List B is the older Robo Rally "option card" set (no Energy cost) plus six rows the data itself marks
`Custom SRR Option`. **It is unresolved which list the game should deal from** — see
"Open questions" at the end.

---

## 1. Haywire cards

### 1.1 What a Haywire card is

One of the two kinds of damage card (the other is Spam). The damage deck has 40 cards; the
rulebook does not say how many are Haywire. In the code a Haywire is `MoveCard.tCardType.Haywire`,
type id 11, abbreviation "H" (`MRR.Contracts/CardList.cs`).

### 1.2 Rules (rulebook pp. 17–19, 22–23)

| Situation | Rule |
|---|---|
| Taking damage | Draw damage cards one at a time from the damage deck. A Haywire is **not** shown and **not** discarded: it is placed **face down under the programming card in the current register** of the player mat. |
| Limit | Max **1 face-down Haywire per register**. Any extra Haywire drawn for the same register goes **face up into the damage discard pile**, and the player takes no further damage for it. |
| Chaining | A newly drawn Haywire may be placed under a Haywire that was just resolved in that same register. |
| Next round | Face-down Haywires stay in the register where they were earned and **cannot be moved**; the player may look at them. Programming cards from the hand go in the *other* registers (rulebook example: Haywire in Register 3 → hand cards go in Registers 1, 2, 4 and 5). |
| Resolving | The Haywire is revealed and its printed effect happens when that register resolves. |
| End of round | Discard only **face-up** cards from the registers. A resolved (now face-up) Haywire goes to the **damage discard pile**. An unresolved one stays face down. |
| Rules precedence | A Haywire's text can break the rulebook; the card wins. |
| Again card | After an upgrade or Haywire that placed several programming cards, Again repeats only the **second** (last-resolved) card. |
| Brakes upgrade | Does **not** apply to a Haywire with a Move 1 effect. |
| Shutdown | All damage cards (Spam and Haywire) in registers, hand and discard pile go to the damage discard pile. |
| Reboot | Haywires in registers → damage discard pile (Spam → player's discard pile). Then draw 2 damage cards: a Haywire goes face down in the register where the robot fell; a second Haywire goes face up into the damage discard pile. Firewall upgrade skips this draw when falling into a pit. |
| Damage deck empty | Shuffle the damage discard pile into a new deck; if that is empty too, no damage is taken. |

### 1.3 Haywire cards known from the rulebook

The rulebook prints the effect of only the cards below, as examples and in its "Notes on Specific
Haywire Cards". **The full deck is not in the rulebook text** — fill the rest in from the physical
cards.

| Card text | Effect | Notes (rulebook) |
|---|---|---|
| Move 1, Rotate Right, Move 1 | Performs those three actions in order | p. 18 example; useful for getting round a corner. Brakes can't turn the Move 1 into Move 0. |
| Move 2 Left or Right, without changing facing | Slide 2 spaces sideways | The side of the robot becomes its leading edge; facing does not change; it can still push robots; walls still stop it. |
| Install a permanent upgrade from your collection of uninstalled upgrades; at end of round place that card into the upgrade discard pile | Install one uninstalled permanent upgrade, free | Ignore if there is none. Allowed even if it makes more than 3 permanent upgrades. The upgrade is active for the rest of the round, then goes to the upgrade discard pile. |
| Move 3, U-Turn | Move 3, then U-Turn | Named in the Laser Kata note (a U-Turn from a Haywire triggers Laser Kata). Exact printed wording not in the text. |

### 1.4 Upgrades that interact with Haywire

| Upgrade | Interaction |
|---|---|
| Rewire (temporary) | Upgrade Phase only: add all face-down Haywires in your registers to your hand; you must program them this round, placing them where you wish. |
| Lucky Booster (temporary) | Reveal damage-deck cards until a Haywire appears; replace a register card you just revealed with it, or discard it. |
| Brakes (permanent) | Does not apply to a Haywire Move 1. |
| Laser Kata (permanent) | A Haywire that makes you U-Turn triggers it. |

### 1.5 Discrepancy with what the project says today

`.claude/agents/robo-rally-dev.md` and the todo item "Haywire cards" say a Haywire means **"execute 5
random cards from your deck."** That is **not** what the 2023 rulebook says: each Haywire card has
its own printed effect and sits face down in a register. Decide which is intended before
implementing; this doc follows the rulebook.

---

## 2. List A — Rulebook upgrades (2023 Hasbro rulebook, pp. 20–23)

Max **3 permanent** (face up on the mat) and **3 temporary** (beside the mat) installed at a time.
Temporary upgrades are single-use and go to the upgrade discard pile once used; **Movement
Upgrades** are used immediately before or after your Programming Card Activation, other temporary
upgrades at any time. "Instead of firing your main laser" weapons: only one such weapon per register.

### 2.1 Permanent upgrades (24)

| Upgrade | Cost | Effect |
|---|---|---|
| Brakes | 2 | Move 1 cards in your registers may be treated as Move 0 (decided after revealing). Not for Again after a Move 1, nor Haywire Move 1. |
| Chaos Theory | 2 | When you reveal SPAM in Register 1, 2 or 3, gain 1 Energy. The replacing card enters face up, so a second SPAM doesn't trigger it again. |
| Crab Legs | 5 | May place a Move 1 in the same register as a Rotate Left/Right; the robot then moves 1 space left/right without rotating (instead of the normal Move 1). |
| Deflector Shield | 2 | When you would take laser/weapon damage, pay 1 Energy to take none this register. Doesn't stop pit/edge damage; covers robot and board lasers. |
| Double-Barrel Laser | 2 | Main laser deals 1 additional damage to robots. Combines with Laser Kata and Rail Gun. |
| Drifting | 4 | After resolving a Rotate Left card, may move 1 space forward. |
| Energy Conversion | 3 | After taking damage from a board laser, may move 1 space forward or backward. |
| Firewall | 1 | No damage cards drawn when Rebooting after falling into a pit. |
| Flash Drive | 4 | Draw 1 additional programming card at the start of each Programming Phase. |
| Hover Unit | 1 | May pass over pits during Programming Card Activation, but falls in if the move ends on one. |
| Laser Kata | 1 | After performing a U-Turn, fire the main laser in all 4 directions this register. (A U-Turn *maneuver* only — not "rotated to any facing".) |
| Memory Cards | 3 | At end of Programming Phase, may place any non-damage cards from your hand on this card; add them to your hand at the start of the Upgrade Phase. |
| Mini Howitzer | 3 | Instead of the main laser, pay 1 Energy: 2 damage to the target, then push it 1 space in the direction of fire. No Energy → can't fire. |
| Modular Chassis | 1 | After you push another robot in Programming Card Activation, give that player this card and take one of their installed upgrades; both install immediately. |
| Power Slide | 4 | After resolving a Rotate Right card, may move 1 space forward. |
| Pressor Beam | 3 | Instead of the main laser: push the target robot 1 space away from you (a push can move more than one robot). |
| Rail Gun | 2 | Main laser shoots through any number of walls/robots; each robot hit takes 1 damage. |
| Ramming Gear | 2 | If you push (or try to push) an adjacent robot in Programming Card Activation, it takes 1 damage. Pushes from upgrades/board elements don't trigger it. |
| Rear Laser | 2 | A rear-firing laser as well as the main laser, both firing together. It is not the "main laser", so no Double-Barrel/Rail Gun bonus. |
| Scrambler | 4 | Instead of the main laser: replace the target's next register card with the top card of their programming deck. Not in Register 5. |
| Self-Diagnostics | 2 | When you reach a new Checkpoint, may remove a card in your hand or discard pile from the game. |
| Spam Filter | 3 | After refilling your hand at the start of the Programming Phase, flip the top card of your programming deck face up. |
| Spiky | 2 | When an adjacent robot moves or is pushed into your space, it takes 1 damage. Moving *next to* you doesn't trigger it. |
| Tractor Beam | 3 | Instead of the main laser: pull the target robot 1 space toward you. Not usable on adjacent robots. |

### 2.2 Temporary upgrades (16)

| Upgrade | Cost | Effect |
|---|---|---|
| Abort Switch | 1 | Replace a register card you just revealed with the top card of your programming deck (damage → damage discard; programming card → your discard). |
| All Aboard | 1 | Movement Upgrade. Activate all conveyor belts, for your robot only: blue first, then green. Doesn't replace the normal activation. |
| Boink | 1 | Movement Upgrade. Move to an unoccupied adjacent space without changing facing. |
| Calibration Protocol | 2 | Return all damage cards in your hand to the damage discard pile, then draw that many programming cards. |
| Displacing Blast | 2 | Instead of the main laser: relocate the target robot to the Reboot token on the board it occupies, facing unchanged. |
| Lucky Booster | 1 | Reveal damage-deck cards until a Haywire appears; may replace a register card you just revealed with it, or discard it. |
| Magnetic | 1 | When an adjacent robot moves via a register card, you may move with it. |
| Memory Swap | 2 | Draw 3 cards from your programming deck, then put 3 cards from your hand on top of the deck in any order. |
| Overclocked | 2 | Movement Upgrade. Move 2. |
| Piercing Drill | 1 | When you push another robot in Programming Card Activation, it takes 1 damage and you may rotate it to any facing. |
| Pressure Release | 2 | Movement Upgrade. Move back 5 spaces, stopping if you would push a robot, hit a wall, or fall into a pit. |
| Re-Initialize | 1 | Give the Priority Token to any player (including yourself). |
| Recharge | 0 | Gain 3 Energy. |
| Rewire | 1 | Upgrade Phase only: add all face-down Haywires in your registers to your hand; you must program them this round, anywhere. |
| Switch | 2 | Movement Upgrade. Swap places with an adjacent robot, facing unchanged. |
| Zoop | 1 | Movement Upgrade. Rotate to face any direction. |

---

## 3. List B — Database `Options` (what the code loads today)

Generated from the seed data in `install/MRRDatabase.sql`. Column meanings:

- **Effect / Notes** — the `Text` and `SRR_Text` columns, verbatim; the data writes `-` where punctuation would go, so the text reads a little oddly.
  `([quantity])` / `([direction])` in a name is a placeholder the code fills in.
- **Qty** — `Quantity`; `-1`/`-2` are sentinel values (not a count).
- **Damage** — `Damage` the option deals, where it has one.
- **Editor** — `tOptionEditorType` (`MRR.Contracts/OptionCard.cs`): how the player sets the option up
  (Automatic = always on; PhaseSelector/SinglePhase = pick phase(s); TurnSelector = on/off per turn;
  CardToSave; Direction; OverloadPhase; OptionalWeapon; Runtime = "not implemented"). Rows 39 and 58
  use editor value 10, which is not in the enum.
- **Status** — the `Functional` column, i.e. `tFunctionalStatus`: how far the option is implemented
  (NotGoingToAddress, GoingToAddress, Addressing, UnderConstruction, Phase1–4, Untested, Functional).
  Whether an option is actually correct in play is **not** recorded here.
- **In draw** — `DataService.GetNextOption` only hands out options with `Functional > 7`
  (Untested or Functional).

| ID | Name | Effect | Notes | Qty | Damage | Editor | Status | In draw |
|---|---|---|---|---|---|---|---|---|
| 1 | Ablative Coat ([quantity]) | Your robot is now covered with a special coat that takes 3 points of damage from any direction or source. Discard after your robot takes a total of 3 points of damage. | When you die you will lose this option as it tries to take damage | 3 | -1 | Automatic | Functional (9) | yes |
| 2 | Abort Switch | Instead of revealing a program card- ignore it and draw a new one randomly from the deck. Once switch is activated- draw program cards randomly from the deck for the remainder of the turn. |  | -2 | 0 | Runtime | NotGoingToAddress (0) | no |
| 3 | Big Gun ([quantity]) | You may fire the Big Gun instead of firing your robot's main laser. The Big Gun causes 2 points of damage in addition to pushing your robot back 1 square. |  | 5 | 2 | OptionalWeapon | GoingToAddress (1) | no |
| 4 | Big Jet | When activated- your robot flies forward 8 squares before landing and executing its program. The Big Jet allows you to fly over walls and other robots. Your robot takes 2 points of damage when it lands. |  | 1 | 0 | PhaseSelector | Phase2 (5) | no |
| 5 | Bio Option | When you receive this option- immediately take another option and place it on Bio Option. Each time your robot powers down- discard the other option and draw another. If you exchange either Bio Option or the other option to prevent damage- discard both options (does not apply if Bio Option variant is used). |  | -1 | 0 | Undefined | GoingToAddress (1) | no |
| 6 | Brakes | Your robot may now choose to move zero when it is executing a Move 1. Priority is that of the Move 1. | This option is Turn Programmed- not Runtime | -2 | 0 | PhaseSelector | Untested (8) | yes |
| 7 | Bridge Layer ([quantity]) | When activated- place a bridge token in the square in front of your robot. Treat robots moving over this square as if they were moving over open floor. |  | 2 | 0 | PhaseSelector | Phase4 (7) | no |
| 8 | Buzz Bomb ([quantity]) | When activated (and each turn until the buzz bomb explodes)- take five program cards and use them to program the buzz bomb. If the bomb hits a robot or a wall- the bomb explodes. |  | 3 | 4 | Undefined | NotGoingToAddress (0) | no |
| 9 | Circuit Breaker | Any time your robot ends a turn with 3 or more points of damage- it will automatically begin the next turn powered down. |  | -2 | 3 | Automatic | Functional (9) | yes |
| 10 | Conditional | After programming all five registers- you may place one of the remaining program cards on this option. This conditional program may then be substituted for any program card in your registers before cards for that phase are revealed. Discard the conditional program at the end of the turn- but keep this option. |  | -1 | 0 | Runtime | NotGoingToAddress (0) | no |
| 11 | Converter | When your robot is damaged- place an energy counter on this option instead of taking a damage chit. When your robot executes its next movement card- remove an energy counter and move 1 extra square. If there are more than two counters on this option at any time- it explodes for 2 points of damage. |  | -1 | 2 | Automatic | Phase4 (7) | no |
| 12 | Crab Legs | You may place a Move 1 in the same register as a Rotate Left or Rotate Right card- and during that phase your robot will move 1 square to the left or right- respectively- without rotating. |  | -1 | 0 | OverloadPhase | Phase3 (6) | no |
| 13 | Double Barrel Laser | Your robot's main laser has been modified to fire two shots. May be used with {Fire Control} and/or {High Power Laser.} |  | -1 | 1 | Automatic | Functional (9) | yes |
| 14 | Drone Launcher ([quantity]) | You may launch a drone instead of firing your robot's main laser. Drones fly 3 squares toward the target robot each register phase- and explode for 2 points of damage in addition to pushing the target robot back 1 square. Priority of drones: 880- 870- 860. |  | 3 | 2 | OptionalWeapon | NotGoingToAddress (0) | no |
| 15 | Dual Processor | You may place both a movement and a rotate program card in a single register.When executing the movement card- move one square less and then execute the rotate card. If the rotate card is a U-Turn- move two squares less. |  | -1 | 0 | OverloadPhase | Phase2 (5) | no |
| 16 | Extra Memory ([quantity]) | Your robot receives one extra program card per turn. This option does not prevent your robot from being destroyed when it has reached 10 points of damage. |  | 1 | 0 | Automatic | Functional (9) | yes |
| 17 | Fire Control | You have targeting control of your robot's main laser. When scoring a point of damage- you may choose to use the damage to lock a register or destroy a particular option. |  | -1 | 0 | Runtime | NotGoingToAddress (0) | no |
| 18 | Flywheel | After programming all five registers- you may place one of the remaining movement cards on this option. In a subsequent turn that card may be added to the program cards dealt to you. For example- this gives an undamaged robot 10 program cards (9 normal and 1 from the flywheel). |  | -1 | 0 | CardToSave | Untested (8) | yes |
| 19 | Fourth Gear | Your robot may now choose to move forward 4 squares when it is executing a Move 3. Priority is that of the Move 3. | This option is Phase Programmed- not Runtime | -2 | 0 | PhaseSelector | Untested (8) | yes |
| 20 | Frog Legs | You may now treat your robot as if it were flying when it is executing a Move 2 or Move 3. This option cannot be activated while your robot is flying. | This option is Phase Programmed- not Runtime | -2 | 0 | PhaseSelector | Phase3 (6) | no |
| 21 | Goo Dropper ([quantity]) | When activated- place a goo token in your robots square. If a robot passes over or stops on the goo- the robot cannot leave that square until it attempts to move a total of four squares in any direction. |  | 3 | 0 | PhaseSelector | Phase1 (4) | no |
| 22 | Gyroscopic Stabilizer | On any turn you choose to activate this option- your robot is not rotated by gears or conveyor belts. |  | -2 | 0 | TurnSelector | Functional (9) | yes |
| 23 | High Power Laser | Your robot's main laser has been modified to shoot through one wall or robot to reach a target robot. If you shoot through a robot- that robot also receives damages. May be used with {Fire Control} and/or {Double Barrel Laser}. |  | -2 | 0 | Automatic | Untested (8) | yes |
| 24 | Homing Device | 1) You may place a homing device token on a target robot instead of firing your robot's main laser. 2) When activated- ignore your hand and move forward 3 squares during each register phase if doing so will bring you closer to the target robot. Otherwise- rotate right. |  | -1 | 0 | OptionalWeapon | GoingToAddress (1) | no |
| 25 | Interceptor | You may place an intercept token on a target robot instead of firing your robot's main laser. After cards are dealt on subsequent turns- you may choose to exchange cards with the player whose robot has your intercept token. Take the intercept token back. |  | -1 | 0 | OptionalWeapon | GoingToAddress (1) | no |
| 26 | Mechanical Arm | Any time your robot ends a register phase on one of the four squares bordering a checkpoint- it may use the mechanical arm to "tag" the checkpoint. A wall will block the arm- but another robot on the checkpoint will not. | This option is Enabled or disabled for Each Phase- not Runtime. | -2 | 0 | PhaseSelector | Phase2 (5) | no |
| 27 | Mine Layer ([quantity]) | When activated- place a mine token in your robot's square. If a robot passes over or stops on the mine- the mine explodes. |  | 3 | 4 | PhaseSelector | Phase4 (7) | no |
| 28 | Mini Howitzer ([quantity]) | You now have the option of firing a mini howitzer instead of your main laser. The mini howitzer will cause 1 point of damage in addition to pushing the target robot 1 square away from you. After 5 shots- discard this option. |  | 5 | 0 | OptionalWeapon | Phase2 (5) | no |
| 29 | Missile Launcher ([quantity]) | You may launch a missile instead of firing your robot's main laser. When launched- place the missile in your robot's square. During each subsequent phase- move the missile forward 2 squares. Priority of missiles: 735- 725- 715. |  | 3 | 4 | OptionalWeapon | GoingToAddress (1) | no |
| 30 | Option Damping Field | When activated- all options(except this one) within a 3-square radius of your robot are deactivated or cannot be used. Devices already released by options continue to function normally. |  | -2 | 0 | PhaseSelector | Phase4 (7) | no |
| 31 | Overload Override | You may place two program cards in a single register and execute both in that register phase- or you may leave a register unprogrammed. Your robot takes a point of damage each time this option is used. |  | -2 | 0 | OverloadPhase | Phase2 (5) | no |
| 32 | Portable Teleporter ([quantity]) | When activated- place a portable teleporter token in your robot's square. Treat the portable teleporter as if it were a teleporter board element. |  | 1 | 0 | PhaseSelector | Phase1 (4) | no |
| 33 | Power Down Shield | When your robot powers down- a shield comes out on each of the robot's four sides. Each shield protects the robot from 1 point of damage per register phase. When the robot powers up- the shields retract. |  | -2 | 0 | Automatic | Untested (8) | yes |
| 34 | Presser Beam | You now have the option of firing a presser beam instead of your robot's main laser. The presser beam will push a target robot 1 squares away from you. |  | -1 | 0 | OptionalWeapon | Phase2 (5) | no |
| 35 | Proximity Mine ([quantity]) | When activated- place a proximity mine token in your robot's square. If a robot passes within 1 square of the mine- the mine explodes. |  | 3 | 4 | PhaseSelector | Phase2 (5) | no |
| 36 | Radio Control | You now have the option of using a radio control beam instead of your robot's main laser. The radio control beam can only target a robot within 6 squares- and it replaces that robot's entire program with a copy of your robot's program. In cases of card priority- the target robot moves after your robot. |  | -2 | 0 | OptionalWeapon | Phase2 (5) | no |
| 37 | Ramming Gear | When your robot pushes another robot- that robot receives a point of damage in addition to being pushed. Even if the target robot can't be moved- it still receives a point of damage. |  | -2 | 0 | Automatic | Functional (9) | yes |
| 38 | Rear Laser | Your robot has a rear-firing laser in addition to its main laser. | The turret will also affect the direction of the rear laser | -2 | 0 | Automatic | Functional (9) | yes |
| 39 | Recompile | You may receive a new hand once per turn before your robot is programmed. Your robot takes a point of damage after you receive the new hand. | Your robot will take 1 point of damage BEFORE you receive your new hand.  You must Enable Recompile and submit your hand and you will receive a new hand. | -2 | 1 | Type 10 (not in tOptionEditorType) | Untested (8) | yes |
| 40 | Re-engineering Unit | When your robot pushes another robot- you may exchange this option for an option on the other robot. |  | -1 | 0 | OptionalWeapon | NotGoingToAddress (0) | no |
| 41 | Reflector ([direction]) | When your robot is hit by a laser- your robot takes damages and the laser is reflected back to its source. Program the direction the reflector faces by turning this card to indicate front- back- right or left. |  | -2 | 0 | Direction | Untested (8) | yes |
| 42 | Retro-Rockets ([quantity]) | When activated- your robot flies back 2 squares per fuel token before executing its program. |  | 3 | 0 | PhaseSelector | Phase2 (5) | no |
| 43 | Reverse Gears | Your robot may now choose to back up 2 squares when it is executing a Back-Up. Priority is that of the Back-Up. | This option is Turn Programmed- not Runtime | -2 | 0 | PhaseSelector | Untested (8) | yes |
| 44 | Robo Copter ([quantity]) | Program by placing an unused movement card on this option. During each register phase- execute the movement card and then execute your program card. While robo copter is active- your robot is flying. | This option may only be used once | 1 | 0 | TurnSelector | Phase4 (7) | no |
| 45 | Scrambler | You now have the option of firing a scrambler instead of your robot's main laser. The scrambler allows you to replace the next programmed card of a target robot with a random one from the deck. This option cannot be used on the fifth register phase. |  | -2 | 0 | OptionalWeapon | Phase2 (5) | no |
| 46 | Scrambler Bomb ([quantity]) | When activated- place a scrambler token in your robot's square. At the beginning of the next turn- all robots within 6 squares of the bomb execute program cards at random for the entire turn. |  | 1 | 0 | PhaseSelector | Phase1 (4) | no |
| 47 | Self-Destruct ([quantity]) | Program by placing this card in a register. The option will be destroyed at the beginning of that register phase. If destroyed or exchanged to prevent damage- this option explodes. |  | 1 | 16 | SinglePhase | Untested (8) | yes |
| 48 | Shield ([direction]) | Your robot now has a shield that protects the robot from 1 point of damage per register phase. Program the direction the shield faces by turning this card to indicate front- back- right or left. |  | -2 | 0 | Direction | Untested (8) | yes |
| 49 | Superior Archive Copy | You may withdraw your next archive copy undamaged- even if you discard this option when you robot is destroyed. |  | -2 | 0 | Automatic | Functional (9) | yes |
| 50 | The Big One ([quantity]) | When activated- place a Big One token in your robot's square. At the beginning of the next turn- The Big One explodes. |  | 1 | 64 | SinglePhase | Phase4 (7) | no |
| 51 | Tractor Beam | You now have the option of firing a tractor beam instead of your robot\'s main laser. The tractor beam will pull a target robot 1 square toward you. The beam may not be used if the target robot is in an adjacent square. |  | -2 | 0 | OptionalWeapon | Phase2 (5) | no |
| 52 | Turret ([direction]) | Your robot now has a turret for its main laser and optional weapons. Program the direction the turret faces by turning this card to indicate front- back- right or left. | The turret will also affect the direction of the rear laser | -2 | 0 | Direction | Untested (8) | yes |
| 53 | Explosive Laser ([quantity]) | Your robot has an optional Explosive Laser.  When fired, target robot will receive 2 points of explosive damage. | Custom SRR Option | 2 | 2 | TurnSelector | Untested (8) | yes |
| 55 | Point Sucker | When your robot fires it's main laser, you can remove points from the target | Custom SRR Option | 1 | 0 | PhaseSelector | Untested (8) | yes |
| 56 | EMP ([quantity]) | When activated, your robot will transmit an EMP before Phase 1, locking in your program and clearing the programs for all other robots.  Your robot will play out its program, then reset for one turn to clear the locked program.  No damage will be added or cleared by the EMP. | Custom SRR Option | 1 | 0 | TurnSelector | Untested (8) | yes |
| 57 | Damage Eraser ([quantity]) | Before Phase 1, all damage is erased from your robot | Custom SRR Option | 1 | 0 | TurnSelector | Untested (8) | yes |
| 58 | Reboot | Your robot will immediately shut down and repair all damage | Custom SRR Option | -1 | 0 | Type 10 (not in tOptionEditorType) | Untested (8) | yes |
| 59 | Additional Laser ([quantity]) | Your robot's main laser has been modified to fire an additional shot. May be used with {Fire Control} and/or {High Power Laser.} | Custom SRR Option | 1 | 0 | Automatic | Untested (8) | yes |

---

## Open questions

1. **Which upgrade list is the game's?** List A is the Renegade/2023 rulebook set (with Energy costs and
   a permanent/temporary split); List B is the data the code actually loads. They overlap in name
   (Brakes, Crab Legs, Rear Laser, Ramming Gear, Scrambler, Tractor Beam, Pressor Beam, Mini Howitzer,
   Abort Switch…) but differ in rules (e.g. List B's Mini Howitzer: 1 damage + push, 5 shots; List A's:
   2 damage for 1 Energy). Replacing List B's rows with List A would be a data + code change.
2. **Haywire effect**: rulebook (face-down, per-card effect) vs. the "5 random cards" described in
   `robo-rally-dev.md` and todo.md (see §1.5).
3. **The rest of the Haywire deck** is not in the rulebook text — needs a pass over the physical cards.
