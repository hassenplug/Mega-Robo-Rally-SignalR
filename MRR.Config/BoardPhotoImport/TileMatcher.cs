using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MRR.Config.BoardPhotoImport
{
    /// <summary>
    /// The catalog tile a cell looks most like, and how much (normalized cross-correlation, 1 = identical).
    /// <paramref name="Margin"/> is how far that is ahead of the best tile of a different type or
    /// number (turned copies of the same tile do not count): a small margin means the cell could
    /// as well be something else. <paramref name="RunnerUp"/> names that other tile.
    /// </summary>
    public record TileMatch(TileTemplate Template, float Score, float Margin = float.PositiveInfinity, string? RunnerUp = null);

    /// <summary>Finds the closest catalog tile for each photographed cell.</summary>
    public class TileMatcher
    {
        private readonly TileTemplateCatalog _catalog;

        public TileMatcher(TileTemplateCatalog catalog)
        {
            _catalog = catalog;
        }

        /// <summary>Best match over every tile at every orientation.</summary>
        public TileMatch Match(Image<Rgb24> cell, MatchMode mode, float blackMax = TileFeatures.DefaultBlackMax)
        {
            var features = TileFeatures.Compute(cell, mode, blackMax);
            TileTemplate? best = null;
            float bestScore = float.NegativeInfinity;
            // Best score and file name per type and number, for the margin to the runner-up.
            var byKey = new Dictionary<(int, int), (float Score, string File)>();
            foreach (var template in _catalog.GetTemplates(mode))
            {
                float score = TileFeatures.Score(features, template.Features);
                if (score > bestScore) { bestScore = score; best = template; }
                var key = (template.TypeId, template.Parameter);
                if (!byKey.TryGetValue(key, out var seen) || score > seen.Score) byKey[key] = (score, template.FileName);
            }
            float runnerScore = float.NegativeInfinity;
            string? runnerUp = null;
            foreach (var (key, v) in byKey)
                if (key != (best!.TypeId, best.Parameter) && v.Score > runnerScore) { runnerScore = v.Score; runnerUp = v.File; }
            if (best != null && IsBelt(best.TypeId) && BeltRotationMode is { } rotationMode)
                best = PickBeltRotation(cell, mode, rotationMode, blackMax, best);
            return new TileMatch(best!, bestScore, bestScore - runnerScore, runnerUp);
        }

        /// <summary>
        /// Mode used to pick which way a belt points once its type is known. The first pass's
        /// Semantic class maps are coarse (12x12) and lose an arrow's direction. Null keeps the
        /// first pass's rotation. Tried on board 6: Edges +0, Color +3, Grayscale +4 belt cells.
        /// </summary>
        public static MatchMode? BeltRotationMode { get; set; } = MatchMode.Grayscale;

        private TileTemplate PickBeltRotation(Image<Rgb24> cell, MatchMode firstMode, MatchMode rotationMode,
            float blackMax, TileTemplate best)
        {
            var features = TileFeatures.Compute(cell, rotationMode, blackMax);
            TileTemplate? pick = null;
            float pickScore = float.NegativeInfinity;
            foreach (var t in _catalog.GetTemplates(rotationMode))
            {
                if (t.TypeId != best.TypeId || t.Parameter != best.Parameter) continue;
                float sc = TileFeatures.Score(features, t.Features);
                if (sc > pickScore) { pickScore = sc; pick = t; }
            }
            if (pick == null || pick.Rotation == best.Rotation) return best;
            // Hand back the first-pass catalog entry so the template instance stays the same kind.
            return _catalog.GetTemplates(firstMode).First(t =>
                t.TypeId == best.TypeId && t.Parameter == best.Parameter && t.Rotation == pick.Rotation);
        }

        private static bool IsBelt(int typeId) => typeId is (>= 10 and <= 16) or (>= 20 and <= 26);
    }
}
