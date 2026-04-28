# AIM Robot — ws_img Format (UNKNOWN — Needs Empirical Testing)

## Endpoint

```
ws://{ipAddress}:80/ws_img
```

Documented in `robo-rally-dev.md §2.1` as "image channel (camera feed, optional)."

## Current Status

The wire format has **never been tested against a live robot** (as of 2026-04-28). `GridAlignmentAgent.ExtractImageBytes()` handles two guesses:

1. **Raw JPEG** — bytes starting with `0xFF 0xD8 0xFF`
2. **JSON with base64 image** — a JSON object with one of these keys: `"image"`, `"data"`, `"frame"`, `"jpeg"`

If neither matches, `Image.Load<Rgb24>()` will throw and `AnalyzeImage` returns `HasLines = false` with a console error.

## How to Determine the Format

Connect to `ws_img` and log raw bytes before any parsing:

```csharp
// Temporary diagnostic — add to GetCameraImageAsync or a test endpoint
Console.WriteLine($"ws_img first bytes: {BitConverter.ToString(combined[..Math.Min(32, combined.Length)])}");
Console.WriteLine($"ws_img length: {combined.Length}");
if (combined[0] == '{')
    Console.WriteLine($"ws_img JSON: {Encoding.UTF8.GetString(combined[..Math.Min(200, combined.Length)])}");
```

## What to Update if the Format Differs

- **Different JSON key**: add the key to the `foreach` loop in `ExtractImageBytes()` in `GridAlignmentAgent.cs`
- **MJPEG stream** (continuous frames, no `EndOfMessage`): change `GetCameraImageAsync()` in `Players.cs` to detect JPEG frame boundaries (`0xFF 0xD9` end-of-image marker) rather than relying on WebSocket `EndOfMessage`
- **Proprietary binary header**: strip the header bytes before calling `Image.Load`

## Also Verify

- Does `ws_img` send frames continuously once connected, or only after a trigger command?
- Is the frame rate controllable?
- Does it require `program_init` first (like `ws_cmd` does)?
