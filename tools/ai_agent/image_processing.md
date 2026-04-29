# Image Processing Library Choice

**Always use SixLabors.ImageSharp for image processing in the MRR project.**

## Why

The game server is a Raspberry Pi 5 running linux-arm64. Each alternative has a problem on that platform:

| Library | Problem on Raspberry Pi |
|---|---|
| `System.Drawing.Common` | Requires native GDI+ (`libgdiplus`); throws `PlatformNotSupportedException` on Linux without it |
| `SkiaSharp` | Ships native `.so` blobs; requires the correct `linux-arm64` runtime NuGet package |
| `SixLabors.ImageSharp` | Pure managed .NET, zero native dependencies — runs on any arch/OS where .NET runs |

## Current State

`SixLabors.ImageSharp 3.1.12` is in `MRR/MRR.csproj`. The first usage is in `MRR/GridAlignmentAgent.cs`.

## Usage Pattern

```csharp
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

using var img = Image.Load<Rgb24>(imageBytes);
img.ProcessPixelRows(accessor =>
{
    for (int y = 0; y < img.Height; y++)
    {
        var row = accessor.GetRowSpan(y);
        // access row[x].R, row[x].G, row[x].B
    }
});
```

Always dispose with `using var` to avoid memory pressure on the Pi.

---

## Receiving Images from ws_img

The AIM robot's `ws_img` socket (`ws://{ip}:80/ws_img`) streams JPEG frames on demand.

### Protocol (from AIM WebSocket Library v1.0.1)

| Action | Send |
|--------|------|
| Start streaming | byte `0x01` |
| Stop streaming | byte `0x00` |

- First frame latency: ~300 ms
- Subsequent frames: immediate
- Format: **raw JPEG bytes** (per API documentation; pending hardware validation — see `ws_img_format.md`)

### C# Receive + Decode Pattern

```csharp
// Start the stream
await wsImg.SendAsync(new ArraySegment<byte>(new byte[] { 0x01 }),
    WebSocketMessageType.Binary, true, CancellationToken.None);

// Receive one frame (accumulate until EndOfMessage)
var buffer = new byte[65536];
using var ms = new MemoryStream();
WebSocketReceiveResult result;
do
{
    result = await wsImg.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    ms.Write(buffer, 0, result.Count);
} while (!result.EndOfMessage);

byte[] jpegBytes = ms.ToArray();

// Stop the stream
await wsImg.SendAsync(new ArraySegment<byte>(new byte[] { 0x00 }),
    WebSocketMessageType.Binary, true, CancellationToken.None);

// Decode with ImageSharp
using var img = Image.Load<Rgb24>(jpegBytes);
```

### Notes

- JPEG data starts with `0xFF 0xD8 0xFF`; verify the first bytes in diagnostics if behavior is unexpected.
- If the robot streams continuously without `EndOfMessage`, detect JPEG frame boundaries via the end-of-image marker `0xFF 0xD9` instead of relying on WebSocket framing.
- See `ws_img_format.md` for the full list of open questions and diagnostic code.
