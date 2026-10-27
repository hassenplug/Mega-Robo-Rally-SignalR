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
        /// Resizes and converts an image to a zero-mean, unit-length vector, so the dot
        /// product of two vectors is their normalized cross-correlation (1 = identical,
        /// 0 = unrelated). That makes the score independent of overall brightness and
        /// contrast, which a phone photo under room lighting will not match.
        /// </summary>
        public static float[] Compute(Image<Rgb24> tile, MatchMode mode)
        {
            // No crop of the cell edges, though in a photo they hold the gaps between tiles:
            // a wall (element200) is only a bar along one edge, about 4% of the tile, and a
            // crop of that size makes every wall match Blank.
            using var small = tile.Clone(ctx => ctx.Resize(Size, Size));

            float[] vector = mode switch
            {
                MatchMode.Color => ColorVector(small),
                MatchMode.Grayscale => GrayVector(small),
                MatchMode.Edges => EdgeVector(GrayVector(small)),
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
