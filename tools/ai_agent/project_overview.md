---
name: project_overview
description: What Mega Robo Rally is, hardware stack, key architecture facts, and agent locations
type: project
---

Mega Robo Rally (MRR) is a physical/digital hybrid Robo Rally (Renegade edition) game.

**Hardware:**
- Raspberry Pi 5 + Sense HAT — game server, 8×8 LED display, joystick input
- 6 × VEX AIM robots — physical playing pieces on a printed game board, controlled via WebSocket
- 6 × phones (browser/SignalR) — player programming UI

**Tech stack:** C# / ASP.NET Core 9, MySQL (server: mrobopi3, db: rally), SignalR for real-time

**Key files:** GameController.cs (state machine 0–16), CreateCommands.cs (cards→commands), CommandProcess.cs (command executor), AIMRobot.cs (robot WebSocket client), DataService.cs (MySQL), DataHub.cs (SignalR hub)

**Agents created:**
- `.claude/agents/robo-rally-dev.md` — full Robo Rally Renegade rules, VEX AIM command reference, Sense HAT notes, implementation gaps list
- `CLAUDE.md` (project root) — project context for main Claude Code session

**Why:** User wants a computerized version with real robots as pieces, phones as player interfaces, Pi as host.

**How to apply:** Always use the robo-rally-dev subagent for game logic tasks. Follow the state machine and command pipeline patterns. All new code in C#. Board element activation order: blue conveyors → all conveyors → pushers → gears → board lasers → robot lasers → flags.
