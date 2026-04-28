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
