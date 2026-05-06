using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using MRR.Hubs;
using MRR.Services;

namespace MRR
{
    /// <summary>
    /// Manages the touchscreen programming UI for a single AIM robot.
    /// Each robot shows its player's 9 hand cards in a ring and 5 program
    /// slots in a horizontal row at screen center.  Touch events on the robot
    /// LCD replicate the same procUpdateCardPlayed calls made by the phone UI.
    /// </summary>
    public class RobotScreenUI
    {
        private readonly Player _player;
        private readonly DataService _dataService;
        private readonly IHubContext<DataHub> _hubContext;

        // Cached state — refreshed from player before every render
        private int[] _dealtTypeIds  = Array.Empty<int>();
        private int[] _playedTypeIds = new int[5];
        private bool _isLocked;
        private int _currentGameState;
        private bool _clearOnNextRender = true; // set true by LoadHand; cleared after first draw

        // ── Layout constants ──────────────────────────────────────────────────

        // Screen is 240×240; center = (120,120); usable radius ≈ 115 px.
        private const int CenterX    = 120;
        private const int CenterY    = 120;
        private const int RingRadius = 95;   // px from center to hand-button centers
        private const int BtnRadius  = 24;   // tap-target radius for hand buttons
        private const int SlotW      = 40;   // program slot width
        private const int SlotH      = 46;   // program slot height
        private const int SlotGap    = 3;    // gap between slots

        // Hand button ring angles (degrees, clockwise from 12-o'clock)
        private static readonly int[] HandAngles =
        {
            315, 345, 15, 45,          // H1–H4 (top arc) — 40° spacing
            120, 150, 180, 210, 240    // H5–H9 (bottom arc)
        };

        // Precomputed hand button centers (x, y)
        private static readonly (int cx, int cy)[] HandCenters;

        // Program slot centers (x, y) — horizontal row at y = CenterY
        private static readonly (int cx, int cy)[] SlotCenters;

        static RobotScreenUI()
        {
            // Compute hand button centers from ring angles
            HandCenters = new (int, int)[HandAngles.Length];
            for (int i = 0; i < HandAngles.Length; i++)
            {
                double rad = HandAngles[i] * Math.PI / 180.0;
                int cx = CenterX + (int)(RingRadius * Math.Sin(rad));
                int cy = CenterY - (int)(RingRadius * Math.Cos(rad));
                HandCenters[i] = (cx, cy);
            }

            // Compute slot centers: row starts at x = (240 - (5*38 + 4*3)) / 2 = 19
            // slot center x = startX + slotIndex*(SlotW+SlotGap) + SlotW/2
            int startX = (240 - (5 * SlotW + 4 * SlotGap)) / 2; // = 19
            SlotCenters = new (int, int)[5];
            for (int i = 0; i < 5; i++)
            {
                int cx = startX + i * (SlotW + SlotGap) + SlotW / 2;
                SlotCenters[i] = (cx, CenterY-6);
            }
        }

        // ── Card abbreviation lookup ──────────────────────────────────────────

        private static readonly Dictionary<int, string> CardAbbrev = new()
        {
            { 0,  "-" },
            { 1,  "U" },
            { 2,  "R" },
            { 3,  "L" },
            { 4,  "B" },
            { 5,  "1" },
            { 6,  "2" },
            { 7,  "3" },
            { 8,  "A" },
            { 9,  "P" },
            { 10, "S" },
            { 11, "H" },
            { 30, "O" },
        };

        private static string Abbrev(int typeId) =>
            CardAbbrev.TryGetValue(typeId, out var s) ? s : "?";

        // ── Constructor ───────────────────────────────────────────────────────

        public RobotScreenUI(Player player, DataService dataService, IHubContext<DataHub> hubContext)
        {
            _player      = player;
            _dataService = dataService;
            _hubContext  = hubContext;
        }

        // ── Data refresh ──────────────────────────────────────────────────────

        /// <summary>
        /// Re-reads CardsDealtStr / CardsPlayedStr from the Player object.
        /// Must be called before every RenderAsync().
        /// </summary>
        public void RefreshFromPlayer()
        {
            _dealtTypeIds = (_player.CardsDealtStr ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var v) ? v : 0)
                .Take(9)
                .ToArray();

            var played = (_player.CardsPlayedStr ?? "0,0,0,0,0")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => int.TryParse(s, out var v) ? v : 0)
                .ToArray();

            for (int i = 0; i < 5; i++)
                _playedTypeIds[i] = i < played.Length ? played[i] : 0;
        }

        // ── Rendering ─────────────────────────────────────────────────────────

        /// <summary>
        /// Full redraw of the programming UI on the robot LCD.
        /// Always calls RefreshFromPlayer() first to ensure current state.
        /// </summary>
        public async Task RenderAsync()
        {
            if (!_player.isConnected) return;

            RefreshFromPlayer();

            var (bgR, bgG, bgB) = ColorHelper.ParseHex(_player.Color,     50,  50, 150);
            var (fgR, fgG, fgB) = ColorHelper.ParseHex(_player.ForeColor, 255, 255, 255);

            // 1. Clear screen only on the first render after a new deal
            if (_clearOnNextRender)
            {
                await _player.SendCommandAsync(new { cmd_id = "lcd_clear_screen", r = bgR, g = bgG, b = bgB });
                _clearOnNextRender = false;
            }

            // 2. Player name — same position/font as Connect(): row 6, column centered
            await _player.SendCommandAsync(new { cmd_id = "lcd_set_pen_color", r = fgR, g = fgG, b = fgB });
            await _player.SendCommandAsync(new
            {
                cmd_id = "lcd_set_cursor",
                row = 6,
                col = Math.Max(0, (15 - _player.Name.Length) / 2)
            });
            await _player.SendCommandAsync(new { cmd_id = "lcd_print", @string = _player.Name });

            // 3. Hand buttons — all grey; CardsDealt contains only unplayed cards
            await _player.SendCommandAsync(new { cmd_id = "lcd_set_font", fontname = "MONO15" });
            await _player.SendCommandAsync(new { cmd_id = "lcd_set_pen_color", r = fgR, g = fgG, b = fgB });

            for (int i = 0; i < HandCenters.Length; i++)
            {
                var (cx, cy) = HandCenters[i];
                int typeId = i < _dealtTypeIds.Length ? _dealtTypeIds[i] : 0;

                await _player.SendCommandAsync(new
                {
                    cmd_id = "lcd_draw_circle",
                    x = cx, y = cy, radius = BtnRadius,
                    r = 100, g = 100, b = 100,
                    transparent = false
                });

                if (typeId != 0)
                    await _player.SendCommandAsync(new
                    {
                        cmd_id = "lcd_print_at", @string = Abbrev(typeId),
                        x = cx - 5, y = cy + 5, b_opaque = false
                    });
            }

            // 4. Program slots — grey; only print abbreviation when filled
            await _player.SendCommandAsync(new { cmd_id = "lcd_set_font", fontname = "MONO12" });
            await _player.SendCommandAsync(new { cmd_id = "lcd_set_pen_color", r = fgR, g = fgG, b = fgB });

            for (int i = 0; i < 5; i++)
            {
                int typeId = _playedTypeIds[i];
                var (cx, cy) = SlotCenters[i];

                int slotR = _isLocked && typeId != 0 ? 200 : 100;
                int slotG = _isLocked && typeId != 0 ? 160 : 100;
                int slotB = _isLocked && typeId != 0 ?   0 : 100;

                await _player.SendCommandAsync(new
                {
                    cmd_id = "lcd_draw_rectangle",
                    x = cx - SlotW / 2, y = cy - SlotH / 2,
                    width = SlotW, height = SlotH,
                    r = slotR, g = slotG, b = slotB,
                    transparent = false
                });

                if (typeId != 0)
                    await _player.SendCommandAsync(new
                    {
                        cmd_id = "lcd_print_at", @string = Abbrev(typeId),
                        x = cx - 4, y = cy + 3, b_opaque = false
                    });
            }
        }

        /// <summary>
        /// Show an idle/status display on the robot LCD based on current game state.
        /// </summary>
        public async Task RenderIdleAsync(int gameState)
        {
            if (!_player.isConnected) return;

            _currentGameState = gameState;

            if (gameState == 5)
            {
                // Locked programming view
                _isLocked = true;
                await RenderAsync();
                return;
            }

            if (gameState >= 6 && gameState <= 11)
            {
                // Executing — show excited emoji (EmojiType EXCITED = 0)
                await _player.SendCommandAsync(new { cmd_id = "show_emoji", name = 0, look = 0 });
                return;
            }

            // States 0–4 (startup/waiting), 12–16 (reset/next turn) — happy emoji
            // EmojiType HAPPY = 3 (based on the 0-indexed enum: EXCITED=0, CONFIDENT=1, SILLY=2, HAPPY=3)
            await _player.SendCommandAsync(new { cmd_id = "show_emoji", name = 3, look = 0 });
        }

        // ── Public lifecycle API ──────────────────────────────────────────────

        /// <summary>
        /// Called when entering programming states (2, 3, 4).
        /// Updates current game state, refreshes card data, and renders the full UI.
        /// </summary>
        public async Task LoadHand(int gameState)
        {
            _currentGameState = gameState;
            _isLocked = false;
            _clearOnNextRender = true;
            _dataService.RefreshPlayerCards(_player.ID);
            await RenderAsync();
        }

        /// <summary>
        /// Called when state 5 is entered (programs locked).
        /// Re-renders with gold locked slots.
        /// </summary>
        public async Task LockAsync()
        {
            _isLocked = true;
            _dataService.RefreshPlayerCards(_player.ID);
            await RenderAsync();
        }

        // ── Touch polling ─────────────────────────────────────────────────────

        /// <summary>
        /// Background touch polling loop. Run as a fire-and-forget Task;
        /// cancel via the token when the robot disconnects or UseRobotScreen is disabled.
        /// </summary>
        public async Task StartPollingAsync(CancellationToken ct)
        {
            bool wasTouching = false;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (!_player.isConnected)
                    {
                        await Task.Delay(500, ct);
                        continue;
                    }

                    var status = await _player.GetStatusAsync();
                    bool isTouching = status.Robot.TouchFlags != "0x0000";

                    if (isTouching && !wasTouching)
                        await HandleTapAsync(status.Robot.TouchX, status.Robot.TouchY);

                    wasTouching = isTouching;
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ScreenUI {_player.ID}] Poll error: {ex.Message}");
                }

                try { await Task.Delay(100, ct); }
                catch (OperationCanceledException) { break; }
            }

            Console.WriteLine($"[ScreenUI {_player.ID}] Polling stopped.");
        }

        // ── Touch hit-testing ─────────────────────────────────────────────────

        private static bool HitCircle(int tx, int ty, int cx, int cy, int r)
            => (tx - cx) * (tx - cx) + (ty - cy) * (ty - cy) <= r * r;

        private static bool HitRect(int tx, int ty, int cx, int cy, int w, int h)
            => tx >= cx - w / 2 && tx <= cx + w / 2 &&
               ty >= cy - h / 2 && ty <= cy + h / 2;

        private async Task HandleTapAsync(int x, int y)
        {
            await _player.SendCommandAsync(new { cmd_id = "play_sound", name = "blinker", volume = 80 });

            // Only respond during programming states
            if (_currentGameState < 2 || _currentGameState > 4)
                return;

            if (_isLocked)
                return;

            // Refresh state from DB before acting on a tap
            _dataService.RefreshPlayerCards(_player.ID);
            RefreshFromPlayer();

            // Check hand buttons H1–H9
            for (int i = 0; i < HandCenters.Length; i++)
            {
                if (i >= _dealtTypeIds.Length) break;

                var (cx, cy) = HandCenters[i];
                if (!HitCircle(x, y, cx, cy, BtnRadius)) continue;

                int typeId = _dealtTypeIds[i];
                if (typeId == 0) return; // no card here

                // Play the card into the next empty slot
                _dataService.ExecuteSQL(
                    $"call procUpdateCardPlayed({_player.ID},{typeId},-1);");

                // Broadcast update so phones stay in sync
                _dataService.RefreshPlayerCards(_player.ID);
                var allDataJson = _dataService.GetAllDataJson();
                await _hubContext.Clients.All.SendAsync("AllDataUpdate", allDataJson);

                // Re-render screen with updated state
                await RenderAsync();
                return;
            }

            // Check program slots P1–P5
            for (int i = 0; i < SlotCenters.Length; i++)
            {
                var (cx, cy) = SlotCenters[i];
                if (!HitRect(x, y, cx, cy, SlotW, SlotH)) continue;

                // Only remove if the slot is filled (non-zero) and not locked
                if (_playedTypeIds[i] == 0) return;

                // Remove the card from this register slot (1-based)
                _dataService.ExecuteSQL(
                    $"call procUpdateCardPlayed({_player.ID},-1,{i + 1});");

                // Broadcast update
                _dataService.RefreshPlayerCards(_player.ID);
                var allDataJson = _dataService.GetAllDataJson();
                await _hubContext.Clients.All.SendAsync("AllDataUpdate", allDataJson);

                // Re-render
                await RenderAsync();
                return;
            }

            // No hit — ignore tap
        }
    }
}
