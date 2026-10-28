using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MRR.Config.BoardPhotoImport
{
    /// <summary>
    /// Turns a photo of the board taken at an angle into a flat, square-on image, and cuts it
    /// into one image per cell. ImageSharp has no homography transform, so the 3×3 solve and
    /// per-pixel sampling are done here.
    /// </summary>
    public static class PerspectiveRectifier
    {
        /// <summary>
        /// Maps the four board corners in the photo -- top-left, top-right, bottom-right,
        /// bottom-left, in that order -- onto a cols×cellPx by rows×cellPx image. "Top-left"
        /// is the board's square (0,0); getting the order wrong imports the board rotated or
        /// mirrored.
        /// </summary>
        public static Image<Rgb24> Rectify(Image<Rgb24> photo, IReadOnlyList<PointF> corners, int cols, int rows, int cellPx)
        {
            if (corners.Count != 4) throw new ArgumentException("Exactly four corners are required.", nameof(corners));

            int width = cols * cellPx;
            int height = rows * cellPx;
            // Solve for the map from the output image to the photo, so each output pixel can
            // look up where it comes from (inverse mapping leaves no holes).
            var h = SolveHomography(
                [new PointF(0, 0), new PointF(width, 0), new PointF(width, height), new PointF(0, height)],
                corners);

            var output = new Image<Rgb24>(width, height);
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // Sample at the pixel centre.
                    var source = Apply(h, x + 0.5f, y + 0.5f);
                    output[x, y] = SampleBilinear(photo, source.X - 0.5f, source.Y - 0.5f);
                }
            return output;
        }

        /// <summary>Cuts a rectified board into cells. Result is indexed [row, col]; dispose each one.</summary>
        public static Image<Rgb24>[,] SliceCells(Image<Rgb24> rectified, int cols, int rows)
        {
            int cellW = rectified.Width / cols;
            int cellH = rectified.Height / rows;
            var cells = new Image<Rgb24>[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    var area = new Rectangle(c * cellW, r * cellH, cellW, cellH);
                    cells[r, c] = rectified.Clone(ctx => ctx.Crop(area));
                }
            return cells;
        }

        /// <summary>
        /// The 3×3 projective transform taking each <paramref name="from"/> point to the
        /// matching <paramref name="to"/> point, as 9 row-major values with h[8] = 1. Standard
        /// direct linear transform: two equations per point pair give an 8×8 system.
        /// </summary>
        public static double[] SolveHomography(IReadOnlyList<PointF> from, IReadOnlyList<PointF> to)
        {
            var a = new double[8, 9];
            for (int i = 0; i < 4; i++)
            {
                double x = from[i].X, y = from[i].Y, u = to[i].X, v = to[i].Y;
                int r = 2 * i;
                double[] row1 = [x, y, 1, 0, 0, 0, -u * x, -u * y, u];
                double[] row2 = [0, 0, 0, x, y, 1, -v * x, -v * y, v];
                for (int k = 0; k < 9; k++) { a[r, k] = row1[k]; a[r + 1, k] = row2[k]; }
            }

            // Gaussian elimination with partial pivoting on the augmented matrix.
            for (int col = 0; col < 8; col++)
            {
                int pivot = col;
                for (int r = col + 1; r < 8; r++)
                    if (Math.Abs(a[r, col]) > Math.Abs(a[pivot, col])) pivot = r;
                if (Math.Abs(a[pivot, col]) < 1e-12)
                    throw new ArgumentException("Corner points are degenerate (three in a line, or repeated).");
                if (pivot != col)
                    for (int k = 0; k < 9; k++) (a[col, k], a[pivot, k]) = (a[pivot, k], a[col, k]);

                for (int r = 0; r < 8; r++)
                {
                    if (r == col) continue;
                    double factor = a[r, col] / a[col, col];
                    for (int k = col; k < 9; k++) a[r, k] -= factor * a[col, k];
                }
            }

            var h = new double[9];
            for (int i = 0; i < 8; i++) h[i] = a[i, 8] / a[i, i];
            h[8] = 1;
            return h;
        }

        /// <summary>Applies a homography from <see cref="SolveHomography"/> to a point.</summary>
        public static PointF Apply(double[] h, double x, double y)
        {
            double w = h[6] * x + h[7] * y + h[8];
            return new PointF(
                (float)((h[0] * x + h[1] * y + h[2]) / w),
                (float)((h[3] * x + h[4] * y + h[5]) / w));
        }

        /// <summary>Bilinear sample in pixel-index coordinates, clamped to the image edge.</summary>
        private static Rgb24 SampleBilinear(Image<Rgb24> image, float x, float y)
        {
            x = Math.Clamp(x, 0, image.Width - 1);
            y = Math.Clamp(y, 0, image.Height - 1);
            int x0 = (int)x, y0 = (int)y;
            int x1 = Math.Min(x0 + 1, image.Width - 1), y1 = Math.Min(y0 + 1, image.Height - 1);
            float fx = x - x0, fy = y - y0;

            Rgb24 p00 = image[x0, y0], p10 = image[x1, y0], p01 = image[x0, y1], p11 = image[x1, y1];
            byte Mix(byte c00, byte c10, byte c01, byte c11) => (byte)Math.Round(
                (c00 * (1 - fx) + c10 * fx) * (1 - fy) + (c01 * (1 - fx) + c11 * fx) * fy);
            return new Rgb24(
                Mix(p00.R, p10.R, p01.R, p11.R),
                Mix(p00.G, p10.G, p01.G, p11.G),
                Mix(p00.B, p10.B, p01.B, p11.B));
        }
    }
}
