using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MRR.Config.BoardPhotoImport
{
    /// <summary>The catalog tile a cell looks most like, and how much (normalized cross-correlation, 1 = identical).</summary>
    public record TileMatch(TileTemplate Template, float Score);

    /// <summary>Finds the closest catalog tile for each photographed cell.</summary>
    public class TileMatcher
    {
        private readonly TileTemplateCatalog _catalog;

        public TileMatcher(TileTemplateCatalog catalog)
        {
            _catalog = catalog;
        }

        /// <summary>Best match over every tile at every orientation.</summary>
        public TileMatch Match(Image<Rgb24> cell, MatchMode mode)
        {
            var features = TileFeatures.Compute(cell, mode);
            TileTemplate? best = null;
            float bestScore = float.NegativeInfinity;
            foreach (var template in _catalog.GetTemplates(mode))
            {
                float score = TileFeatures.Score(features, template.Features);
                if (score > bestScore) { bestScore = score; best = template; }
            }
            return new TileMatch(best!, bestScore);
        }
    }
}
