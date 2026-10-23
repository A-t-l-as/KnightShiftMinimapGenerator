using System;
using System.Collections.Generic;
using System.Linq;

namespace KnightShiftMinimapGenerator
{
   /// <summary>
   /// Recreates the KnightShift minimap from an LND file.
   ///
   /// The model was fitted against minimaps taken from the game (mean error ~4-5 / 255 per channel):
   ///  * water:   vertex (x,y) is water when waterHeight[x,y] &gt; max(terrainHeight) of the cell (x..x+1, y..y+1),
   ///             drawn with a solid color (14,46,255) - exact match with the game,
   ///  * land:    average texture color of the 4 cell corners (palette depends on the terrain set),
   ///             multiplied by Lambert lighting computed from central height differences,
   ///  * frame:   1 px black frame around the image.
   /// Starting positions from the MIS file can be drawn on top (like the Earth 2150 version).
   /// The painted color layers of the LND are not used by the game minimap.
   /// </summary>
   public static class MinimapGenerator
   {
      public static readonly byte[] WaterColor = { 14, 46, 255 };

      // fitted lighting parameters
      private const double HeightScale = 255.79;          // height units per one map cell (horizontal)
      private static readonly double[] Light = Normalize(-0.9238, -0.4578, 1.0);
      private const double Ambient = 0.1906;

      public static RgbImage Generate(LndFile lnd, TerrainPalette palette, MinimapOptions options = null, MisFile mis = null)
      {
         options ??= new MinimapOptions();
         palette ??= options.FallbackPalette ?? new TerrainPalette { Name = "fallback" };

         int w = lnd.Width, h = lnd.Height;
         var img = new RgbImage(w, h);
         var shade = ComputeShading(lnd, options.ShadingStrength);

         for (int y = 0; y < h; y++)
         {
            int y1 = Math.Min(y + 1, h - 1);
            for (int x = 0; x < w; x++)
            {
               if (options.DrawFrame && (x == 0 || y == 0 || x == w - 1 || y == h - 1))
               {
                  img.SetPixel(x, y, 0, 0, 0);
                  continue;
               }
               int x1 = Math.Min(x + 1, w - 1);
               int i00 = y * w + x, i01 = y * w + x1, i10 = y1 * w + x, i11 = y1 * w + x1;

               int maxH = Math.Max(Math.Max(lnd.TerrainHeight[i00], lnd.TerrainHeight[i01]),
                                   Math.Max(lnd.TerrainHeight[i10], lnd.TerrainHeight[i11]));
               if (lnd.WaterHeight[i00] > maxH)
               {
                  img.SetPixel(x, y, WaterColor[0], WaterColor[1], WaterColor[2]);
                  continue;
               }

               var c0 = palette.GetColor(lnd.Texture[i00]);
               var c1 = palette.GetColor(lnd.Texture[i01]);
               var c2 = palette.GetColor(lnd.Texture[i10]);
               var c3 = palette.GetColor(lnd.Texture[i11]);
               double s = shade[i00];
               img.SetPixel(x, y,
                  ToByte((c0[0] + c1[0] + c2[0] + c3[0]) * 0.25 * s),
                  ToByte((c0[1] + c1[1] + c2[1] + c3[1]) * 0.25 * s),
                  ToByte((c0[2] + c1[2] + c2[2] + c3[2]) * 0.25 * s));
            }
         }
         var result = img.Scale(options.Scale);
         if (mis != null && options.MarkStartingPositions)
            PlayerMarkers.Draw(result, mis.Players, options.Scale);
         return result;
      }

      /// <summary>Brightness multiplier per vertex (1.0 = flat terrain).</summary>
      public static double[] ComputeShading(LndFile lnd, double strength = 1.0)
      {
         int w = lnd.Width, h = lnd.Height;
         var H = lnd.TerrainHeight;
         var res = new double[w * h];
         double flat = (Light[2] + Ambient);
         for (int y = 0; y < h; y++)
         {
            int ym = Math.Max(y - 1, 0), yp = Math.Min(y + 1, h - 1);
            for (int x = 0; x < w; x++)
            {
               int xm = Math.Max(x - 1, 0), xp = Math.Min(x + 1, w - 1);
               double dx = H[y * w + xp] - H[y * w + xm];
               double dy = H[yp * w + x] - H[ym * w + x];
               double nx = -dx / HeightScale, ny = -dy / HeightScale;
               double len = Math.Sqrt(nx * nx + ny * ny + 1.0);
               double dot = (nx * Light[0] + ny * Light[1] + Light[2]) / len;
               double s = Math.Max(0.0, (dot + Ambient) / flat);
               res[y * w + x] = 1.0 + (s - 1.0) * strength;
            }
         }
         return res;
      }

      /// <summary>
      /// Builds a palette for the map's terrain set from a minimap image taken from the game
      /// (median of reference color / shading for every texture index).
      /// </summary>
      public static TerrainPalette Calibrate(LndFile lnd, RgbImage reference, string name = null)
      {
         if (reference.Width != lnd.Width || reference.Height != lnd.Height)
            throw new ArgumentException($"Reference image is {reference.Width}x{reference.Height}, map is {lnd.Width}x{lnd.Height}.");

         int w = lnd.Width, h = lnd.Height;
         var shade = ComputeShading(lnd);
         var samples = new Dictionary<int, List<double>[]>();
         for (int y = 2; y < h - 2; y++)
            for (int x = 2; x < w - 2; x++)
            {
               int i = y * w + x;
               int maxH = Math.Max(Math.Max(lnd.TerrainHeight[i], lnd.TerrainHeight[i + 1]),
                                   Math.Max(lnd.TerrainHeight[i + w], lnd.TerrainHeight[i + w + 1]));
               if (lnd.WaterHeight[i] > maxH || shade[i] < 0.3) continue;
               int t = lnd.Texture[i] & 0x7F;
               if (!samples.TryGetValue(t, out var lists))
                  samples[t] = lists = new[] { new List<double>(), new List<double>(), new List<double>() };
               var (r, g, b) = reference.GetPixel(x, y);
               lists[0].Add(r / shade[i]); lists[1].Add(g / shade[i]); lists[2].Add(b / shade[i]);
            }

         var palette = new TerrainPalette
         {
            TerrainId = lnd.TerrainIdHex,
            Name = name ?? $"Terrain {lnd.TerrainIdHex} (from map '{lnd.Title}')"
         };
         foreach (var kv in samples.Where(kv => kv.Value[0].Count >= 20).OrderBy(kv => kv.Key))
            palette.Colors[kv.Key] = kv.Value.Select(l => ToByte(Median(l))).ToArray();
         return palette;
      }

      /// <summary>Mean absolute difference per channel between two images of the same size.</summary>
      public static double MeanAbsoluteError(RgbImage a, RgbImage b)
      {
         long sum = 0;
         for (int i = 0; i < a.Pixels.Length; i++) sum += Math.Abs(a.Pixels[i] - b.Pixels[i]);
         return (double)sum / a.Pixels.Length;
      }

      private static double Median(List<double> l)
      {
         l.Sort();
         int n = l.Count;
         return n % 2 == 1 ? l[n / 2] : (l[n / 2 - 1] + l[n / 2]) / 2;
      }

      private static byte ToByte(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);

      private static double[] Normalize(double x, double y, double z)
      {
         double l = Math.Sqrt(x * x + y * y + z * z);
         return new[] { x / l, y / l, z / l };
      }
   }
}
