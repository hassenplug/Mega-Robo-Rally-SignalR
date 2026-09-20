namespace MRR.Tools.RobotSpriteDraw;

public static class RobotSprite
{
    public static readonly SpriteRectangle[] Rectangles =
    {
        new(24, 6, 20, 2, RgbColor.FromHex("D93AD7")),
        new(18, 8, 32, 10, RgbColor.FromHex("ED43E6")),
        new(24, 9, 14, 2, RgbColor.FromHex("FF9BFA")),
        new(12, 12, 6, 6, RgbColor.FromHex("FFFFFF")),
        new(16, 18, 36, 4, RgbColor.FromHex("E8E8EE")),
        new(18, 21, 32, 2, RgbColor.FromHex("A7A7B0")),
        new(46, 22, 8, 16, RgbColor.FromHex("751071")),
        new(50, 24, 4, 4, RgbColor.FromHex("D52FCF")),
        new(50, 31, 4, 4, RgbColor.FromHex("D52FCF")),
        new(20, 18, 4, 13, RgbColor.FromHex("77777F")),
        new(24, 16, 16, 4, RgbColor.FromHex("77777F")),
        new(40, 18, 4, 13, RgbColor.FromHex("77777F")),
        new(29, 17, 6, 6, RgbColor.FromHex("F4F4F8")),
        new(22, 26, 24, 4, RgbColor.FromHex("202025")),
        new(16, 30, 36, 4, RgbColor.FromHex("202025")),
        new(12, 34, 44, 18, RgbColor.FromHex("202025")),
        new(16, 52, 36, 4, RgbColor.FromHex("202025")),
        new(22, 56, 24, 4, RgbColor.FromHex("202025")),
        new(24, 34, 20, 3, RgbColor.FromHex("F3F3F7")),
        new(20, 37, 28, 12, RgbColor.FromHex("F3F3F7")),
        new(24, 49, 20, 3, RgbColor.FromHex("F3F3F7")),
        new(32, 34, 4, 6, RgbColor.FromHex("B61CAD")),
        new(42, 42, 6, 4, RgbColor.FromHex("B61CAD")),
        new(32, 47, 4, 5, RgbColor.FromHex("B61CAD")),
        new(20, 42, 6, 4, RgbColor.FromHex("B61CAD")),
        new(27, 39, 14, 10, RgbColor.FromHex("B61CAD")),
        new(30, 41, 8, 6, RgbColor.FromHex("FFFFFF"))
    };
}

public readonly record struct SpriteRectangle(
    int X,
    int Y,
    int Width,
    int Height,
    RgbColor Color
);

public readonly record struct RgbColor(int Red, int Green, int Blue)
{
    public static RgbColor FromHex(string value)
    {
        value = value.TrimStart('#');

        return new RgbColor(
            Convert.ToInt32(value[..2], 16),
            Convert.ToInt32(value.Substring(2, 2), 16),
            Convert.ToInt32(value.Substring(4, 2), 16)
        );
    }
}
