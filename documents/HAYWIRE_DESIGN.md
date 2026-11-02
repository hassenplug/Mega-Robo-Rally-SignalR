# Haywire Cards — Design

**Plan only, not implemented (2026-11-02).** Draft guidance written from a read of the code and
[CARD_KNOWLEDGE_BASE.md](CARD_KNOWLEDGE_BASE.md) §1. Not every file was read — see
"Not yet checked". Verify against the code before building from it.

## 1. Decisions made

| Question | Decision |
|---|---|
| Which rule? | **Rulebook.** Each Haywire has its own printed effect and sits face down in the register where it was earned. The "5 random cards" text in `robo-rally-dev.md` and `install/todo.md` is wrong for this rules version and should be removed when this is built. |
| Spam vs. Haywire on damage | **Real shared damage deck** with a damage discard pile and reshuffle (rulebook: 40 cards). |
| Source of the Haywire effects | **User supplies the list** from the physical cards (see §6). |
| Free-upgrade Haywire | **Deferred** — blocked on upgrade work. |
| Spam handling | **Rework to match the damage deck.** See §3.2 for what that actually changes. |
| Damage deck storage | **`MoveCards` with `Owner = -1`** and new `CardLocation` values (decided 2026-11-02, §7 Q4). An earlier "new table" decision was reversed. |
| Damage deck lifetime | **Rebuilt and shuffled at game start**, alongside `GameNewAddCards`. |
| Effects table | **Seeded SQL only** (`install/MRRDatabase.sql`); no GM-page editing. |
| Deck makeup | **One of each of the 17 Haywire cards; 23 Spam** (derived from the 40-card total, see §7 Q1). |
| Player choices | **Made at reveal / lock-in**, not mid-phase (§7 Q5). |
| Card 6 priority | Owner's `Robots.Priority` becomes 1, others keep their order; if two robots resolve it, the later-resolving one wins (§7 Q6). |

## 2. What exists today

- `MoveCard.tCardType.Haywire` = 11 (`MRR.Contracts/CardList.cs`) and the `MoveCardTypes` row. Nothing deals
  it and `CreateCommands.ProcessMove` ignores it.
- There is **no damage deck**. `AddDamage` (`MRR.Rules/CreateCommands.cs`) queues
  `SquareAction.DealSpamCard`, and `DataService.DealSpamToPlayer` inserts a brand-new Spam row
  (`CardLocation` 3) into that robot's discard pile. Spam supply is unlimited.
- Revealed Spam is replaced by drawing from `TurnRequest.DrawPiles` until a non-Spam card appears; spent
  Spam goes to `CardLocation` 5 via `TurnPlan.SpamConsumed` / `RetireSpamCards`.
- `CardLocation`: 0 deck, 1 hand, 2 register, 3 discard, 4 locked register, 5 damage discard.
- Shutdown (`ApplyShutDownAtLockIn`) already sends Haywire (type 11) to location 5, and
  `MoveCardsShuffleAndDeal` step 1 deletes location-5 damage cards of type 10 and 11.
- `KillRobot` and pit/reboot code queue two `DealSpamCard` commands.

## 3. Design

### 3.1 Damage deck (new)

- A shared deck of damage cards: Spam and Haywire (one Haywire row per effect on the card).
- Draw one card at a time per point of damage. Empty deck → shuffle the damage discard pile into a new
  deck; if both are empty, no damage is dealt.
- Lives in **`MoveCards` with `Owner = -1`** (decided, §7 Q4): the shared deck is `CardLocation` 6 (new), the
  damage discard pile is the existing `CardLocation` 5, both with `Owner = -1`. Drawing a card sets `Owner` to
  the robot and moves it to its discard pile (Spam) or locked register (Haywire); resolving or discarding sets
  `Owner` back to -1 and `CardLocation` to 5. Details and the existing queries this touches are in §7 Q4.
- The planner must stay reproducible (`TurnRequest.DrawPiles` exists for this reason). Draws for damage
  have to be made in the request or from a seed, not with `RAND()` while planning.

### 3.2 Spam

- Rulebook: a drawn Spam goes to the player's discard pile. That is what the code already does, so the
  placement stays; what changes is the **source** — from the damage deck, not a new row.
- Resolved Spam returns to the damage discard pile (`CardLocation` 5 today) instead of being deleted at the
  next deal. Today `MoveCardsShuffleAndDeal` deletes location-5 rows; that has to change to a move.

### 3.3 Haywire placement

- A drawn Haywire is **not** shown and **not** discarded. It is placed face down under the programming
  card in the **current register**, as a locked card: `CardLocation` 4, `PhasePlayed` = that register.
- Max 1 face-down Haywire per register. An extra goes face up to the damage discard pile and deals no
  further damage.
- A Haywire may chain under one that just resolved in the same register.
- Persists across rounds in its register, and cannot be moved. Hand cards go in the other registers.

### 3.4 Programming phase

- Locked registers are skipped by auto-fill and refused by `UpdateCardPlayed`.
- `UpdateProgrammingStatus` compares the register count with `PhaseCount`; check that a locked Haywire
  counts correctly.
- The deal gives 9 cards today; check how locked registers should change that.

### 3.5 Resolving

- In `CreatePhase`, a register holding a Haywire resolves its effect. Add a Haywire case to `ProcessMove`
  that dispatches on a per-card effect id (new column on `MoveCards`, with an effects lookup table; type 11
  stays "Haywire" so existing shutdown/delete queries keep working).
- Reuse the existing forward, rotate and `CalcMoveDistance` code for each effect.
- Brakes must not apply to a Haywire Move 1 (needs a flag around the Brakes check in `ProcessMove`).
- After resolving, the Haywire becomes face up and goes to the damage discard pile. An unresolved one stays.
- Again after a Haywire repeats only the last-resolved card; check the existing `Again` branch.
- A U-Turn from a Haywire triggers Laser Kata once that upgrade exists.

### 3.6 Reboot, shutdown, death

- Reboot: Haywires in registers go to the damage discard pile (Spam to the player's discard pile), then draw
  2 damage cards; a Haywire goes face down in the register where the robot fell, a second one goes face up
  to the damage discard. The Firewall upgrade skips the draw on a pit fall.
- Shutdown already clears Haywire; it must keep working with the shared deck.
- Replace the two `DealSpamCard` calls in `KillRobot` and the pit/reboot path with a damage-deck draw.

### 3.7 UI

- Phone UI and the ESP32 station: show a locked register with a face-down card marker.
- `loadrobots.js` already notes a Haywire panel is meant to go in the status panel.
- The ESP32 firmware reads `GET /api/state`, so the payload needs locked-register info.

## 4. Order of work

1. Answer §7 questions.
2. Schema: `HaywireEffectID` column on `MoveCards`, the `HaywireEffects` table, and a `MoveCardLocations` row
   for the damage deck (location 6) (via the `mrr-database` agent). Then make the `Owner = -1` rows safe in
   the existing deal/shuffle queries (§7 Q4) before anything else uses them.
3. Damage deck draw and discard, Spam moved onto it.
4. Haywire placement and locked-register programming.
5. Planner effects plus tests.
6. Reboot and shutdown.
7. UI.
8. Update CARD_KNOWLEDGE_BASE.md, `todo.md`, and `robo-rally-dev.md`.

## 5. Testing

Add tests beside `MRR.Tests/PitRebootTests.cs`: each effect's end position and facing; one-per-register
limit; Brakes not applying; unresolved Haywire surviving the round; deterministic replanning; damage deck
reshuffle and the empty-deck case; shutdown and reboot clearing. Then a simulated game, and only then
physical robots. The existing manual checklist in `todo.md` Section 8 is still open.

## 6. Haywire effects

Read from a photo of the physical cards (2026-11-02): 17 distinct cards, all legible. The photo shows one of
each, so **copy counts are unknown** (§7 Q1). "Choice" marks effects where the player decides something
while the card resolves; the command pipeline has no step for that yet (§7 Q5).

| # | Printed text | Choice | Needs |
|---|---|---|---|
| 1 | Move 2 left or right, without changing facing | Left or right | Sideways move without a turn |
| 2 | Rotate to any facing. Move 1. | Facing | |
| 3 | Install a permanent upgrade from your collection of uninstalled upgrades. At end of round, place that card into the upgrade discard pile. | Which upgrade | **Deferred** — needs upgrades |
| 4 | Pay 0–8 energy, then move 2 spaces for each energy you paid. | Amount (0–8) | Energy check and spend |
| 5 | Move back 3. | | |
| 6 | Move 1, then take the priority token. Do not move the priority token at the end of this round. | | Priority-token / turn-order code |
| 7 | Move 2, rotate left. | | |
| 8 | Move 1, rotate left, move 1. | | |
| 9 | Draw and reveal the top 2 cards of your programming deck. Resolve both in the order of your choosing. | Order | Draw pile; resolving two cards in one register |
| 10 | Move 1. Your robot's main laser deals 1 additional damage this register. | | One-register laser damage modifier |
| 11 | Move 1, rotate right, move 1. | | |
| 12 | Move 5. If you hit a wall this register, take 1 damage. | | Wall-hit detection |
| 13 | Move back 1. Remove a card in your discard pile from the game. | Which card | Remove-from-game for a card |
| 14 | Rotate right or rotate left. | Direction | |
| 15 | Move back 1. U-turn. | | |
| 16 | Move 3, U-turn. | | |
| 17 | Move 2, rotate right. | | |

Notes:
- The rulebook's own example is card 11 (rotate right); the deck also has the left version, card 8.
- A U-Turn from card 15 or 16 triggers Laser Kata once that upgrade exists.
- Brakes does not apply to the Move 1 in cards 2, 8, 10 or 11 (rulebook: not for a Haywire Move 1).
- Cards 9 and 13 touch the programming deck and discard pile, so they interact with the Spam draw code.

Plain move-and-rotate cards (5, 7, 8, 11, 15, 16, 17) can reuse `ProcessMove`'s existing primitives. The rest
need new mechanics, listed in the "Needs" column.

## 7. Open questions

1. ~~Deck makeup~~ **Answered:** one copy of each of the 17 Haywire cards. Spam fills the rest of the
   40-card deck: **23 Spam** (40 − 17). The 40 total comes from CARD_KNOWLEDGE_BASE.md §1.1, which notes the
   rulebook does not state the Haywire count — so the 23 is derived, not read from a card. Seed both counts
   in SQL so they are easy to change.
2. **Open:** does a robot with a locked Haywire still get 9 cards dealt, or fewer? Unverified — the rulebook
   PDF could not be rendered in this session (`pdftoppm` missing). Check rulebook pp. 17–19, or install
   poppler so it can be read. Until then, build it so the hand size is one setting that is easy to change.
3. ~~Resolved Spam~~ **Answered:** Spam and Haywire cards in the damage deck have their `Owner` cleared
   (they belong to no robot while in the damage deck or its discard pile). So a resolved Spam goes back to
   the damage discard pile with no owner, not deleted as today. With storage in `MoveCards`
   (Q4), "cleared" means `Owner = -1`: a card moving between a robot and the damage deck sets or clears it.
4. ~~Damage-deck storage~~ **Answered: B** — damage cards stay in `MoveCards` with `Owner = -1`; no new
   deck table. This reverses the earlier "new table" decision. What the code shows
   (`install/MRRDatabase.sql`, `DataService.GameNewAddCards`, `DataService.MoveCardsShuffleAndDeal`):
   - `MoveCards` has primary key `(CardID, Owner)`, `Owner` is `NOT NULL DEFAULT -1`. `GameNewAddCards`
     deletes all `MoveCards` and copies deck set 4 from `MoveCardsCompleteList` once per robot. That is
     where the shared damage cards get seeded, so the deck resets per game as decided.
   - Plan: seed 23 Spam + 17 Haywire as `Owner = -1`, `CardLocation` 6 (new, "damage deck") with a
     shuffled `CurrentOrder`; add a `MoveCardLocations` row for 6. Add a nullable `HaywireEffectID` column
     to `MoveCards` and a seeded `HaywireEffects(HaywireEffectID, Name, Text, Copies)` table.
   - **CardID collisions:** the key is `(CardID, Owner)`, so a damage card moving from `Owner = -1` to a robot
     must not reuse a `CardID` that robot already has. Give damage cards `CardID`s in a range the robot decks
     never use (the per-robot decks are the set-4 IDs), or reassign on draw as `DealSpamToPlayer` does.
   - **Existing queries that would misbehave with `Owner = -1` rows in `MoveCards`** (found by reading
     `MoveCardsShuffleAndDeal`; each must be changed and tested):
     - Step 6 `UPDATE MoveCards SET CardLocation = 1 WHERE CurrentOrder <= 9` has no owner or location
       filter. Step 4 ranks cards per owner, so the `Owner = -1` rows get their own 1..N ranks and the first
       9 would be **dealt into a hand with no owner**. It must be restricted to real robots and the deck
       location.
     - Step 3 joins `MoveCardLocations` on `CardLocation`, so location 6 needs a row there (with a
       `DealPriority`) or those cards silently drop out of the shuffle; and the shuffle also re-ranks
       damage cards each deal, which would reshuffle the shared deck mid-game. Exclude them.
     - Step 1 `DELETE ... CardLocation = 5 AND CardTypeID IN (10, 11)` is what removes spent Spam today. It
       must become "set `Owner = -1`, keep in 5" for resolved Spam and Haywire.
     - Step 5 (move discards back into a deck when it has < 9 cards) groups by `Owner`; an `Owner = -1`
       group must not be treated as a robot deck.
     - `RefreshCardCount`, `LoadGameCardsFromDatabase`/`GameCards` lookups (`c.ID == id && c.Owner == robot`),
       `RetireSpamCards`, `ApplyShutDownAtLockIn` and `GetNextCard` all assume every row has a robot owner.
   - Because so many existing queries assume a robot owner, this is the riskiest part of the work; it should
     be done first, behind tests (§5), before any Haywire effect is added.
5. ~~Player choices during resolution~~ **Answered:** the player **chooses at reveal / lock-in**, not
   mid-phase. Applies to cards 1, 2, 4, 9, 13, 14 (and 3 when upgrades exist). Consequences:
   - No new blocking prompt is needed in the command pipeline; the planner reads the stored choice.
   - A stored choice needs a home: a `Choice` value on the register's Haywire row in `MoveCards`.
   - The choice is made before the board is seen, which is a deliberate departure from the rulebook (the
     physical game decides when the card resolves).
   - Card 4 needs the Energy amount validated at lock-in (0–8, no more than the robot has), and re-checked
     when it resolves because Energy can change during the round.
   - Card 9 (order of two drawn cards) needs the top 2 cards known at lock-in; they come from the same
     deterministic draw pile as Spam (`TurnRequest.DrawPiles`), so they must not be drawn twice.
   - Card 13 (card to remove from discard) is chosen from the discard pile as it is at lock-in; if that
     card is gone by resolution time, the effect does nothing.
   - **Open:** where the choice UI goes (phone, ESP32 station, robot screen) — the status panel noted in
     `loadrobots.js` is the likely place for the phone.
6. ~~Card 6 / priority token~~ **Answered:** when executed, set that robot's `Robots.Priority` to 1 and keep
   the relative order of the other robots unchanged.
   Notes from the code (`DataService.UpdatePlayerPriority`, `DataService.Players.cs`):
   - Priorities are 1..N and every robot rotates by -1 (wrapping) each turn inside `MoveCardsShuffleAndDeal`,
     i.e. at the next deal. "Do not move the token at the end of this round" therefore means the new
     value must be what holds *after* that rotation, so the reset has to be applied after the rotation (or
     the stored value pre-compensated), not before it.
   - Implementation: renumber the others in their current order into 2..N, and give the card's owner 1.
   - **Checked:** priority 1 acts first. `CreateCommands` copies the owner's `Robots.Priority` onto each card
     (line ~587) and then resolves cards with `OrderBy(gc => gc.Priority)` (ascending), and the first-player
     lookup uses `OrderBy(ob => ob.Priority).First()`. So card 6 makes its robot act first in later rounds.
   - **Answered:** if two robots resolve card 6 in the same round, the **later-resolving** robot ends up
     with priority 1 (each resolution sets its robot to 1 and shifts the others).
7. ~~Is Energy tracked?~~ **Answered:** yes, the game tracks Energy. The planner's `PowerUp` case already
   reads `thisplayer.Energy` and writes it with `ListOfCommands.SetEnergy`, so card 4 can check and spend
   Energy the same way. What remains for card 4 is only how the player chooses the 0–8 amount (Q5).

Answered: damage deck in `MoveCards` with `Owner = -1`, reset per game, effects seeded in SQL — see §1.

**Still open:** Q2 (hand size) and where the choice UI goes (end of Q5).

## Not yet checked

`CommandProcess`, the phone and ESP32 UI code, and how a Haywire register interacts with the existing
`Locked` flag. (`GameNewAddCards` was read — see Q4.)

## Risks

- `AddDamage` is used by lasers, pushes, reboot and pits, so changing it affects all of them.
- Schema change needs a migration for existing games.
- Per `todo.md` (2026-09-17), do not remove the existing "dead" Haywire code before this is picked up.
