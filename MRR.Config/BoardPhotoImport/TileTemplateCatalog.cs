using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace MRR.Config.BoardPhotoImport
{
    /// <summary>One tile image at one orientation, ready to compare against a photographed cell.</summary>
    /// <param name="TypeId">SquareType value from the file name.</param>
    /// <param name="Parameter">
    /// Number from the file name: the flag number (100), start number (110) or respawn letter
    /// as 1=A, 2=B, ... (120). 0 for tiles with one image per type.
    /// </param>
    /// <param name="Rotation">
    /// Which way the artwork points: Up is the image as drawn, then each step is 90°
    /// clockwise, the same as the editor's rotationToDeg.
    /// </param>
    public record TileTemplate(int TypeId, int Parameter, Direction Rotation, string FileName, float[] Features);

    /// <summary>
    /// Every tile image in wwwroot/images (element{typeId}[-{parameter}]_top.png) at all four
    /// orientations. There are no per-rotation image files -- the editor rotates with CSS -- so
    /// the rotations are made here.
    ///
    /// Images only. The actions each tile carries come from the BoardID 0 template at import
    /// time (BoardPhotoImportService), because that can change when the template is re-seeded
    /// and the images cannot.
    /// </summary>
    public class TileTemplateCatalog
    {
        private static readonly Regex FileNamePattern =
            new(@"^element(?<type>\d+)(-(?<param>[0-9]+|[A-Z]))?_top\.png$", RegexOptions.IgnoreCase);

        private readonly string _imageDirectory;
        private readonly ConcurrentDictionary<MatchMode, IReadOnlyList<TileTemplate>> _byMode = new();

        public TileTemplateCatalog(string imageDirectory)
        {
            _imageDirectory = imageDirectory;
        }

        /// <summary>The templates for a match mode, built on first use and then cached.</summary>
        public IReadOnlyList<TileTemplate> GetTemplates(MatchMode mode) => _byMode.GetOrAdd(mode, Build);

        /// <summary>
        /// Parses element{typeId}[-{parameter}]_top.png. Respawn letters become numbers
        /// (A=1), matching respawnLetter() in board-editor.html. Returns false for other files.
        /// </summary>
        public static bool TryParseFileName(string fileName, out int typeId, out int parameter)
        {
            typeId = 0;
            parameter = 0;
            var match = FileNamePattern.Match(fileName);
            if (!match.Success) return false;

            typeId = int.Parse(match.Groups["type"].Value);
            var param = match.Groups["param"].Value;
            if (param.Length == 0) return true;
            parameter = char.IsLetter(param[0]) ? char.ToUpperInvariant(param[0]) - 'A' + 1 : int.Parse(param);
            return true;
        }

        private IReadOnlyList<TileTemplate> Build(MatchMode mode)
        {
            var templates = new List<TileTemplate>();
            foreach (var path in Directory.EnumerateFiles(_imageDirectory, "element*_top.png").Order())
            {
                var fileName = Path.GetFileName(path);
                if (!TryParseFileName(fileName, out int typeId, out int parameter)) continue;

                using var image = Image.Load<Rgb24>(path);
                // Every tile gets all four orientations, even ones the game treats as
                // unrotatable (a flag, a pit): the physical tile can still be laid down
                // turned, and the photo has to match it either way.
                foreach (var rotation in new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left })
                {
                    using var rotated = image.Clone(ctx => ctx.Rotate((RotateMode)(((int)rotation - 1) * 90)));
                    templates.Add(new TileTemplate(typeId, parameter, rotation, fileName,
                        TileFeatures.Compute(rotated, mode)));
                }
            }
            if (templates.Count == 0)
                throw new InvalidOperationException($"No element*_top.png tile images found in {_imageDirectory}");
            return templates;
        }
    }
}
