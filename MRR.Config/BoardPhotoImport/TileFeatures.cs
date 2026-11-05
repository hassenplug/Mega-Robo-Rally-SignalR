using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MRR.Config.BoardPhotoImport
{
    /// <summary>
    /// How a tile image is turned into a comparison vector. Swappable because the physical
    /// tiles are 3D prints: if they reproduce the artwork as relief in one filament colour,
    /// colour comparison will match poorly and the edge mode is the fallback
    /// (BOARD_PHOTO_IMPORT_DESIGN.md, "Known risk"). Which one works has not been checked
    /// against a real photo yet.
    /// </summary>
    public enum MatchMode
    {
        Color,
        Grayscale,
        Edges,
        /// <summary>
        /// Each pixel is reduced to a colour class (black, gray, white, red, orange, yellow, green,
        /// blue, purple) and the classes are compared as coarse maps. The catalog art and the
        /// printed tiles are the same designs in different palettes (dark vs bright green, a
        /// dark-red vs black gear ring), and this ignores the palette while keeping the colours
        /// that carry meaning (green/blue belt speed, green/red rotator direction).
        /// </summary>
        Semantic,
    }

    /// <summary>
    /// Feature vectors for template matching. The catalog and the photographed cells both go
    /// through <see cref="Compute"/>, so they are always resized, cropped and normalized the
    /// same way.
    /// </summary>
    public static class TileFeatures
    {
        /// <summary>Side length the tile is resized to before comparing.</summary>
        public const int Size = 48;

        /// <summary>
        /// Fraction trimmed from each side before resizing. In a photo the cell edges hold the
        /// gaps between tiles and small corner-placement errors; trimming the same amount from
        /// the reference artwork keeps the two comparable. (Walls, whose artwork is only an edge
        /// bar, are not matched -- see TileTemplateCatalog.ExcludedFiles.)
        /// </summary>
        public const float Inset = 0.08f;

        /// <summary>
        /// Crops, resizes and converts an image to a zero-mean, unit-length vector, so the dot
        /// product of two vectors is their normalized cross-correlation (1 = identical,
        /// 0 = unrelated). That makes the score independent of overall brightness and
        /// contrast, which a phone photo under room lighting will not match.
        /// </summary>
        public static float[] Compute(Image<Rgb24> tile, MatchMode mode, float blackMax = DefaultBlackMax)
        {
            using var small = tile.Clone(ctx =>
            {
                int insetX = (int)(tile.Width * Inset);
                int insetY = (int)(tile.Height * Inset);
                ctx.Crop(new Rectangle(insetX, insetY, tile.Width - 2 * insetX, tile.Height - 2 * insetY))
                   .Resize(Size, Size);
            });

            float[] vector = mode switch
            {
                MatchMode.Color => ColorVector(small),
                MatchMode.Grayscale => GrayVector(small),
                MatchMode.Edges => EdgeVector(GrayVector(small)),
                MatchMode.Semantic => SemanticVector(small, blackMax),
                _ => throw new ArgumentOutOfRangeException(nameof(mode)),
            };
            Normalize(vector);
            return vector;
        }

        /// <summary>Normalized cross-correlation of two vectors from <see cref="Compute"/>.</summary>
        public static float Score(float[] a, float[] b)
        {
            float sum = 0;
            for (int i = 0; i < a.Length; i++) sum += a[i] * b[i];
            return sum;
        }

        /// <summary>Value (0-1) below which a pixel is "black", fine for the catalog art whose blacks are pure.</summary>
        public const float DefaultBlackMax = 0.28f;

        /// <summary>
        /// Black/gray cutoff for a whole photographed board. The printed black is dark gray and
        /// a phone's exposure moves it a lot (V 0.25-0.5), so the cutoff is found per photo:
        /// Otsu's split of the brightness of the unsaturated pixels (tile surface vs black).
        /// </summary>
        public static float AdaptiveBlackMax(Image<Rgb24> board)
        {
            var hist = new double[256];
            double total = 0;
            for (int y = 0; y < board.Height; y++)
                for (int x = 0; x < board.Width; x++)
                {
                    var p = board[x, y];
                    int max = Math.Max(p.R, Math.Max(p.G, p.B)), min = Math.Min(p.R, Math.Min(p.G, p.B));
                    if (max == 0 || (max - min) / (float)max >= 0.28f) continue;
                    hist[max]++; total++;
                }
            if (total == 0) return DefaultBlackMax;
            double sumAll = 0;
            for (int i = 0; i < 256; i++) sumAll += i * hist[i];
            double wB = 0, sumB = 0, bestVar = -1; int best = (int)(DefaultBlackMax * 255);
            for (int t = 0; t < 256; t++)
            {
                wB += hist[t]; if (wB == 0) continue;
                double wF = total - wB; if (wF == 0) break;
                sumB += t * hist[t];
                double mB = sumB / wB, mF = (sumAll - sumB) / wF;
                double between = wB * wF * (mB - mF) * (mB - mF);
                if (between > bestVar) { bestVar = between; best = t; }
            }
            return best / 255f;
        }

        private const int ClassCount = 9;
        private const int Pool = 4;                       // 48 px classified, pooled to 12x12 per class

        /// <summary>Takes the already cropped, <see cref="Size"/>-square tile.</summary>
        private static float[] SemanticVector(Image<Rgb24> small, float blackMax)
        {
            int grid = Size / Pool;
            var v = new float[ClassCount * grid * grid];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int k = Classify(small[x, y], blackMax);
                    v[k * grid * grid + (y / Pool) * grid + (x / Pool)] += 1f / (Pool * Pool);
                }
            return v;
        }

        /// <summary>0 black, 1 gray (white is folded in), 2 unused, 3 red, 4 orange, 5 yellow, 6 green, 7 blue, 8 purple.</summary>
        private static int Classify(Rgb24 p, float blackMax)
        {
            float r = p.R / 255f, g = p.G / 255f, b = p.B / 255f;
            float max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
            float delta = max - min;
            float sat = max <= 0 ? 0 : delta / max;
            if (max < blackMax) return 0;
            if (sat < 0.28f) return 1;
            float hue;
            if (max == r) hue = 60 * (((g - b) / delta) % 6);
            else if (max == g) hue = 60 * ((b - r) / delta + 2);
            else hue = 60 * ((r - g) / delta + 4);
            if (hue < 0) hue += 360;
            if (hue < 18 || hue >= 340) return 3;
            if (hue < 45) return 4;
            if (hue < 72) return 5;
            if (hue < 170) return 6;
            if (hue < 262) return 7;
            return 8;
        }

        private static float[] ColorVector(Image<Rgb24> image)
        {
            // R, G and B planes one after another, so each channel is correlated separately.
            var vector = new float[Size * Size * 3];
            int plane = Size * Size;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var p = image[x, y];
                    int i = y * Size + x;
                    vector[i] = p.R;
                    vector[plane + i] = p.G;
                    vector[2 * plane + i] = p.B;
                }
            return vector;
        }

        private static float[] GrayVector(Image<Rgb24> image)
        {
            var vector = new float[Size * Size];
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    var p = image[x, y];
                    vector[y * Size + x] = 0.299f * p.R + 0.587f * p.G + 0.114f * p.B;
                }
            return vector;
        }

        /// <summary>Sobel gradient magnitude. Border pixels are left at zero.</summary>
        private static float[] EdgeVector(float[] gray)
        {
            var edges = new float[gray.Length];
            float At(int x, int y) => gray[y * Size + x];
            for (int y = 1; y < Size - 1; y++)
                for (int x = 1; x < Size - 1; x++)
                {
                    float gx = At(x + 1, y - 1) + 2 * At(x + 1, y) + At(x + 1, y + 1)
                             - At(x - 1, y - 1) - 2 * At(x - 1, y) - At(x - 1, y + 1);
                    float gy = At(x - 1, y + 1) + 2 * At(x, y + 1) + At(x + 1, y + 1)
                             - At(x - 1, y - 1) - 2 * At(x, y - 1) - At(x + 1, y - 1);
                    edges[y * Size + x] = MathF.Sqrt(gx * gx + gy * gy);
                }
            return edges;
        }

        private static void Normalize(float[] vector)
        {
            float mean = vector.Average();
            float sumSquares = 0;
            for (int i = 0; i < vector.Length; i++)
            {
                vector[i] -= mean;
                sumSquares += vector[i] * vector[i];
            }
            // A perfectly flat image has no structure to correlate; leave it as zeros so it
            // scores 0 against everything rather than dividing by zero.
            if (sumSquares <= 0) return;
            float scale = 1 / MathF.Sqrt(sumSquares);
            for (int i = 0; i < vector.Length; i++) vector[i] *= scale;
        }
    }
}
