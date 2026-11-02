# AIM Robot — ws_img Format (frame format confirmed 2026-10-29)

## Endpoint

```
ws://{Hostname}:80/ws_img
```

Documented in `robo-rally-dev.md §2.1` as "image channel (camera feed, optional)."

## Current Status

**Confirmed on AIM-07, 2026-10-29:** after connecting and sending one `0x01` byte, the robot sends **one binary WebSocket message containing a raw JPEG** (starts `FF D8 FF FE`, ~23 KB, 640×480). No `program_init` and no `ws_cmd` connection were needed. Still unknown: whether more frames follow (only the first was read) and whether the frame rate is controllable.

`GridAlignmentAgent.ExtractImageBytes()` handles two formats:

1. **Raw JPEG** — bytes starting with `0xFF 0xD8 0xFF`
2. **JSON with base64 image** — a JSON object with one of these keys: `"image"`, `"data"`, `"frame"`, `"jpeg"`

If neither matches, `Image.Load<Rgb24>()` will throw and `FindPluses` returns `Found = false` with a console error.

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
