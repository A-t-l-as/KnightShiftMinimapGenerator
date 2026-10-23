using System;
using System.Collections.Generic;

namespace KnightShiftMinimapGenerator
{
   /// <summary>
   /// Draws starting positions as numbered white circles with a black outline (number = player slot + 1).
   /// Players whose markers would overlap (e.g. RPG maps where all players start in one spot)
   /// are drawn as one marker with the lowest slot number.
   /// </summary>
   public static class PlayerMarkers
   {
      // 3x5 pixel digits, one row per string, '#' = ink
      private static readonly string[][] Digits =
      {
         new[] { "###", "#.#", "#.#", "#.#", "###" },
         new[] { ".#.", "##.", ".#.", ".#.", "###" },
         new[] { "###", "..#", "###", "#..", "###" },
         new[] { "###", "..#", "###", "..#", "###" },
         new[] { "#.#", "#.#", "###", "..#", "..#" },
         new[] { "###", "#..", "###", "..#", "###" },
         new[] { "###", "#..", "###", "#.#", "###" },
         new[] { "###", "..#", "..#", ".#.", ".#." },
         new[] { "###", "#.#", "###", "#.#", "###" },
         new[] { "###", "#.#", "###", "..#", "###" },
      };

      /// <summary>
      /// Draws the markers on an already scaled minimap.
      /// </summary>
      /// <param name="scale">Scale the image was enlarged with (map coordinates are multiplied by it).</param>
      public static void Draw(RgbImage img, IEnumerable<PlayerData> players, int scale)
      {
         scale = Math.Max(1, scale);
         int font = scale;                                  // digit pixel size
         int outline = Math.Max(1, (font + 1) / 2);
         int maxRadius = Radius(7 * font, 5 * font, font);   // radius of a two-digit marker
         var drawn = new List<(int X, int Y)>();
         foreach (var p in players)
         {
            if (!p.IsActive) continue;
            int cx = p.StartPositionX * scale + scale / 2;
            int cy = p.StartPositionY * scale + scale / 2;
            bool overlaps = false;
            foreach (var d in drawn)
               if ((d.X - cx) * (d.X - cx) + (d.Y - cy) * (d.Y - cy) < (2 * maxRadius + outline) * (2 * maxRadius + outline))
                  overlaps = true;
            if (overlaps) continue;
            drawn.Add((cx, cy));

            string text = (p.Index + 1).ToString();
            int textW = (text.Length * 4 - 1) * font;
            int textH = 5 * font;
            int radius = Radius(textW, textH, font);

            for (int y = cy - radius - outline; y <= cy + radius + outline; y++)
               for (int x = cx - radius - outline; x <= cx + radius + outline; x++)
               {
                  if (x < 0 || y < 0 || x >= img.Width || y >= img.Height) continue;
                  double d = Math.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
                  if (d <= radius) img.SetPixel(x, y, 255, 255, 255);
                  else if (d <= radius + outline) img.SetPixel(x, y, 0, 0, 0);
               }

            int tx = cx - textW / 2, ty = cy - textH / 2;
            foreach (char ch in text)
            {
               var glyph = Digits[ch - '0'];
               for (int gy = 0; gy < 5; gy++)
                  for (int gx = 0; gx < 3; gx++)
                     if (glyph[gy][gx] == '#')
                        for (int py = 0; py < font; py++)
                           for (int px = 0; px < font; px++)
                           {
                              int x = tx + gx * font + px, y = ty + gy * font + py;
                              if (x >= 0 && y >= 0 && x < img.Width && y < img.Height)
                                 img.SetPixel(x, y, 0, 0, 0);
                           }
               tx += 4 * font;
            }
         }
      }

      private static int Radius(int textW, int textH, int font) =>
         (int)Math.Ceiling(Math.Sqrt(textW * textW + textH * textH) / 2.0) + font;
   }
}
