using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MRR.Config.BoardPhotoImport
{
    /// <summary>
    /// Snaps hand-placed board corners onto the board's real grid. The tiles are separated by thin
    /// dark seams that stay sharp in a phone photo, so a rough corner placement (within about a
    /// third of a cell) can be corrected by rectifying with a margin and sliding/stretching a
    /// regular grid until its lines sit on the seams. This fixes the shift and scale errors; it
    /// does not fix a corner that is off by more than that.
    /// </summary>
    public static class GridRefiner
    {
        private const int CellPx = 40;
        private const float Margin = 0.5f;   // cells of the photo included outside the corners
        private const int Gap = 4;           // px either side of a seam where the tile surface is sampled

        public static PointF[] Refine(Image<Rgb24> photo, IReadOnlyList<PointF> corners, int cols, int rows, int iterations = 4)
        {
            var current = corners.ToArray();
            for (int i = 0; i < iterations; i++)
            {
                var h0 = PerspectiveRectifier.SolveHomography(
                    [new PointF(0, 0), new PointF(cols, 0), new PointF(cols, rows), new PointF(0, rows)], current);
                var gray = RectifyGray(photo, h0, cols, rows, out int w, out int hgt);

                var (vProfile, hProfile) = SeamProfiles(gray, w, hgt);
                var (dx, sx) = FitAxis(vProfile, cols);
                var (dy, sy) = FitAxis(hProfile, rows);

                float x0 = dx, x1 = dx + cols * sx, y0 = dy, y1 = dy + rows * sy;
                current =
                [
                    PerspectiveRectifier.Apply(h0, x0, y0), PerspectiveRectifier.Apply(h0, x1, y0),
                    PerspectiveRectifier.Apply(h0, x1, y1), PerspectiveRectifier.Apply(h0, x0, y1),
                ];
            }
            return current;
        }

        private static float[] RectifyGray(Image<Rgb24> photo, double[] h0, int cols, int rows, out int w, out int h)
        {
            w = (int)((cols + 2 * Margin) * CellPx);
            h = (int)((rows + 2 * Margin) * CellPx);
            var gray = new float[w * h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    var p = PerspectiveRectifier.Apply(h0, (x + 0.5f) / CellPx - Margin, (y + 0.5f) / CellPx - Margin);
                    var c = PerspectiveRectifier.SampleBilinear(photo, p.X - 0.5f, p.Y - 0.5f);
                    gray[y * w + x] = 0.299f * c.R + 0.587f * c.G + 0.114f * c.B;
                }
            return gray;
        }

        /// <summary>
        /// For each column (row), how much dark thin line there is: a pixel counts when it is darker
        /// than both neighbours <see cref="Gap"/> px away and those neighbours are light, which keeps
        /// out the black belts and their arrows.
        /// </summary>
        private static (float[] Vertical, float[] Horizontal) SeamProfiles(float[] g, int w, int h)
        {
            var v = new float[w];
            var hor = new float[h];
            for (int y = Gap; y < h - Gap; y++)
                for (int x = Gap; x < w - Gap; x++)
                {
                    float c = g[y * w + x];
                    float a = g[y * w + x - Gap], b = g[y * w + x + Gap];
                    if (a > 100 && b > 100) v[x] += Math.Max(0, (a + b) / 2 - c);
                    a = g[(y - Gap) * w + x]; b = g[(y + Gap) * w + x];
                    if (a > 100 && b > 100) hor[y] += Math.Max(0, (a + b) / 2 - c);
                }
            return (Smooth(v), Smooth(hor));
        }

        private static float[] Smooth(float[] p)
        {
            var o = new float[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                float s = 0; int n = 0;
                for (int k = -1; k <= 1; k++)
                    if (i + k >= 0 && i + k < p.Length) { s += p[i + k]; n++; }
                o[i] = s / n;
            }
            return o;
        }

        /// <summary>Best (shift, scale) in cell units so lines at shift + i*scale, i = 0..count, hit the profile's peaks.</summary>
        private static (float Shift, float Scale) FitAxis(float[] profile, int count)
        {
            float GridScore(float shift, float scale)
            {
                float score = 0;
                for (int i = 0; i <= count; i++)
                {
                    int p = (int)MathF.Round(CellPx * (Margin + shift + i * scale));
                    if (p < 0 || p >= profile.Length) return float.NegativeInfinity;
                    score += profile[p];
                }
                return score;
            }

            float bestScore = float.NegativeInfinity, bestShift = 0, bestScale = 1;
            for (float scale = 0.90f; scale <= 1.10f; scale += 0.002f)
                for (float shift = -0.45f; shift <= 0.45f; shift += 0.01f)
                {
                    float score = GridScore(shift, scale);
                    if (score > bestScore) { bestScore = score; bestShift = shift; bestScale = scale; }
                }

            // Seams make the best grid score far above the same grid moved half a cell onto the
            // tile centres. A photo with no seams (or a synthetic image) scores about the same
            // either way; then the corners are left alone rather than moved on noise.
            float offSeam = GridScore(bestShift + 0.5f, bestScale);
            if (bestScore <= 0 || float.IsNegativeInfinity(bestScore) || bestScore < 1.5f * Math.Max(offSeam, 0)) return (0, 1);
            return (bestShift, bestScale);
        }
    }
}
