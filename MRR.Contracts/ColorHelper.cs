using System;
using System.Globalization;

namespace MRR
{
    public static class ColorHelper
    {
        public static (int r, int g, int b) ParseHex(string hex, int dr = 0, int dg = 0, int db = 0)
        {
            try
            {
                if (hex.Length >= 6)
                    return (int.Parse(hex[..2], NumberStyles.HexNumber),
                            int.Parse(hex[2..4], NumberStyles.HexNumber),
                            int.Parse(hex[4..6], NumberStyles.HexNumber));
            }
            catch { }
            return (dr, dg, db);
        }
    }
}
