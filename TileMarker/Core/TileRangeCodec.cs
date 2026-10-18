using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Xna.Framework;

namespace TileMarker.Core
{
    /// <summary>
    /// Reads and writes the same "x,y" / "x1-x2,y1-y2" tile range string format already used by
    /// Lots of Kisses' VisionIgnoredTiles config, so any mod consuming this API can reuse its
    /// existing parser without changes.
    /// </summary>
    internal static class TileRangeCodec
    {
        /// <summary>Expands a list of range strings into the full set of individual tiles.</summary>
        public static HashSet<Point> Expand(IEnumerable<string> entries)
        {
            var tiles = new HashSet<Point>();
            if (entries == null)
                return tiles;

            foreach (string entry in entries)
            {
                if (!TryParseEntry(entry, out int minX, out int maxX, out int minY, out int maxY))
                    continue;

                for (int x = minX; x <= maxX; x++)
                {
                    for (int y = minY; y <= maxY; y++)
                        tiles.Add(new Point(x, y));
                }
            }

            return tiles;
        }

        /// <summary>Parses a single "x,y" or "x1-x2,y1-y2" entry. Invalid entries return false.</summary>
        public static bool TryParseEntry(string entry, out int minX, out int maxX, out int minY, out int maxY)
        {
            minX = maxX = minY = maxY = 0;

            if (string.IsNullOrWhiteSpace(entry))
                return false;

            string[] axes = entry.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (axes.Length != 2)
                return false;

            return TryParseInclusiveRange(axes[0], out minX, out maxX)
                && TryParseInclusiveRange(axes[1], out minY, out maxY);
        }

        private static bool TryParseInclusiveRange(string value, out int min, out int max)
        {
            min = 0;
            max = 0;

            string[] bounds = value.Split('-', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (bounds.Length == 1 && int.TryParse(bounds[0], out int coordinate))
            {
                min = max = coordinate;
                return true;
            }

            if (bounds.Length != 2
                || !int.TryParse(bounds[0], out int first)
                || !int.TryParse(bounds[1], out int second))
                return false;

            min = Math.Min(first, second);
            max = Math.Max(first, second);
            return true;
        }

        /// <summary>
        /// Compresses a set of individual tiles into row-based range strings ("x1-x2,y" or "x,y").
        /// Only merges consecutive X values on the same row — it does not attempt full 2D rectangle
        /// decomposition, which keeps this simple and keeps the saved file human-readable.
        /// </summary>
        public static List<string> Compress(IEnumerable<Point> tiles)
        {
            var result = new List<string>();
            if (tiles == null)
                return result;

            var byRow = tiles
                .GroupBy(p => p.Y)
                .OrderBy(g => g.Key);

            foreach (var row in byRow)
            {
                List<int> xs = row.Select(p => p.X).Distinct().OrderBy(x => x).ToList();

                int rangeStart = xs[0];
                int previous = xs[0];

                for (int i = 1; i <= xs.Count; i++)
                {
                    bool atEnd = i == xs.Count;
                    int current = atEnd ? int.MinValue : xs[i];

                    if (!atEnd && current == previous + 1)
                    {
                        previous = current;
                        continue;
                    }

                    result.Add(FormatEntry(rangeStart, previous, row.Key));

                    if (!atEnd)
                    {
                        rangeStart = current;
                        previous = current;
                    }
                }
            }

            return result;
        }

        private static string FormatEntry(int xStart, int xEnd, int y)
        {
            var sb = new StringBuilder();
            sb.Append(xStart == xEnd ? xStart.ToString() : $"{xStart}-{xEnd}");
            sb.Append(',');
            sb.Append(y);
            return sb.ToString();
        }
    }
}
