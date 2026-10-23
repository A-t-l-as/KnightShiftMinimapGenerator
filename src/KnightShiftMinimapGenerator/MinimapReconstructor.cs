using System;
using System.Collections.Generic;
using System.Linq;

namespace KnightShiftMinimapGenerator
{
   public class ReconstructOptions
   {
      /// <summary>Curvature smoothing of the recovered terrain. Higher = smoother hills, lower = more (noisy) detail.</summary>
      public double Smoothing { get; set; } = 5e-7;
      /// <summary>Median terrain height of the land (game units).</summary>
      public double BaseHeight { get; set; } = 2500;
      /// <summary>Number of texture/height refinement passes.</summary>
      public int Passes { get; set; } = 2;
      public Action<string> Log { get; set; }
   }

   /// <summary>
   /// Rebuilds terrain heights, textures and water of an LND file from a minimap image (inverse of <see cref="MinimapGenerator"/>).
   ///
   /// The minimap only contains shading, not heights, so the result is an approximation:
   ///  * water:    exact (blue pixels), water depth is invented,
   ///  * textures: nearest palette color after removing the shading,
   ///  * heights:  "shape from shading" - heights are optimized (Adam, coarse-to-fine) so that the
   ///              lighting model reproduces the minimap brightness, with a curvature smoothness term.
   ///              Slopes towards/away from the light are recovered well, absolute heights of plateaus are not.
   /// </summary>
   public static class MinimapReconstructor
   {
      private const double HeightScale = 255.79;
      private static readonly double[] Light = Normalize(-0.9238, -0.4578, 1.0);
      private const double Ambient = 0.1906;

      public static void Reconstruct(RgbImage minimap, TerrainPalette palette, LndFile target, ReconstructOptions options = null)
      {
         options ??= new ReconstructOptions();
         int w = minimap.Width, h = minimap.Height;
         if (w != target.Width || h != target.Height)
            throw new ArgumentException($"Minimap is {w}x{h}, map is {target.Width}x{target.Height}.");
         var log = options.Log ?? (_ => { });

         int n = w * h;
         var img = new double[n * 3];
         for (int i = 0; i < n * 3; i++) img[i] = minimap.Pixels[i];

         var water = new bool[n];
         var mask = new double[n];
         for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
               int i = y * w + x;
               var (r, g, b) = minimap.GetPixel(x, y);
               water[i] = r == MinimapGenerator.WaterColor[0] && g == MinimapGenerator.WaterColor[1] && b == MinimapGenerator.WaterColor[2];
               bool frame = x == 0 || y == 0 || x == w - 1 || y == h - 1;
               mask[i] = water[i] || frame ? 0 : (r == 0 && g == 0 && b == 0 ? 0.3 : 1.0);
            }

         var keys = palette.Colors.Keys.OrderBy(k => k).ToArray();
         if (keys.Length == 0) throw new ArgumentException("Palette is empty.");
         var pal = keys.Select(k => palette.Colors[k].Select(v => (double)v).ToArray()).ToArray();

         log("Estimating textures...");
         var tex = PickTexturesByChroma(img, w, h, keys, pal);
         var H = new double[n];
         Array.Fill(H, options.BaseHeight);

         for (int pass = 0; pass < options.Passes; pass++)
         {
            var sobs = ObservedShading(img, tex, palette, w, h);
            var levels = pass == 0 ? new[] { 8, 4, 2, 1 } : new[] { 4, 2, 1 };
            int fullIters = pass == 0 ? 600 : 300;
            foreach (int f in levels)
            {
               log($"Pass {pass + 1}/{options.Passes}: solving heights at 1/{f} resolution...");
               if (f == 1)
                  H = Optimize(sobs, mask, H, w, h, HeightScale, fullIters, 10.0, options.Smoothing);
               else
               {
                  int cw = (w + f - 1) / f, ch = (h + f - 1) / f;
                  var hc = Optimize(Down(sobs, w, h, f), Down(mask, w, h, f), Down(H, w, h, f), cw, ch,
                                    HeightScale / f, 300, 10.0 * f, options.Smoothing * Math.Pow(f, 4));
                  H = Up(hc, cw, ch, f, w, h);
               }
            }
            var s = Shade(H, w, h, HeightScale, out _, out _);
            tex = PickTextures(img, s, w, h, keys, pal);
         }

         // absolute height: median of land = BaseHeight
         var landHeights = Enumerable.Range(0, n).Where(i => !water[i]).Select(i => H[i]).OrderBy(v => v).ToArray();
         double median = landHeights.Length > 0 ? landHeights[landHeights.Length / 2] : 0;
         for (int i = 0; i < n; i++) H[i] += options.BaseHeight - median;

         log("Placing water...");
         var waterLevel = PlaceWater(H, water, w, h);

         for (int i = 0; i < n; i++)
         {
            target.TerrainHeight[i] = (ushort)Math.Clamp(Math.Round(H[i]), 0, 65535);
            target.Texture[i] = (byte)tex[i];
            target.WaterHeight[i] = (ushort)Math.Clamp(Math.Round(waterLevel[i]), 0, 65535);
         }
      }

      // ---------------- textures ----------------

      /// <summary>Average of the 4 pixels that a vertex contributes to.</summary>
      private static double[] VertexColor(double[] img, double[] shade, int w, int h, int x, int y)
      {
         var c = new double[3];
         for (int dy = -1; dy <= 0; dy++)
            for (int dx = -1; dx <= 0; dx++)
            {
               int px = Math.Clamp(x + dx, 0, w - 1), py = Math.Clamp(y + dy, 0, h - 1);
               int i = py * w + px;
               double s = shade == null ? 1.0 : Math.Max(shade[i], 0.25);
               for (int k = 0; k < 3; k++) c[k] += img[i * 3 + k] / s * 0.25;
            }
         return c;
      }

      private static int[] PickTexturesByChroma(double[] img, int w, int h, int[] keys, double[][] pal)
      {
         var tex = new int[w * h];
         for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
               var o = VertexColor(img, null, w, h, x, y);
               double best = double.MaxValue; int bestKey = keys[0];
               for (int t = 0; t < keys.Length; t++)
               {
                  var p = pal[t];
                  double pp = p[0] * p[0] + p[1] * p[1] + p[2] * p[2];
                  double sc = (o[0] * p[0] + o[1] * p[1] + o[2] * p[2]) / Math.Max(pp, 1);
                  double res = 0;
                  for (int k = 0; k < 3; k++) res += (o[k] - sc * p[k]) * (o[k] - sc * p[k]);
                  double ls = Math.Log(Math.Max(sc, 0.05));
                  res += 400 * ls * ls;
                  if (res < best) { best = res; bestKey = keys[t]; }
               }
               tex[y * w + x] = bestKey;
            }
         return tex;
      }

      private static int[] PickTextures(double[] img, double[] shade, int w, int h, int[] keys, double[][] pal)
      {
         var tex = new int[w * h];
         for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
               var o = VertexColor(img, shade, w, h, x, y);
               double best = double.MaxValue; int bestKey = keys[0];
               for (int t = 0; t < keys.Length; t++)
               {
                  double d = 0;
                  for (int k = 0; k < 3; k++) d += (o[k] - pal[t][k]) * (o[k] - pal[t][k]);
                  if (d < best) { best = d; bestKey = keys[t]; }
               }
               tex[y * w + x] = bestKey;
            }
         return tex;
      }

      /// <summary>Brightness of every pixel relative to its (quad averaged) texture color.</summary>
      private static double[] ObservedShading(double[] img, int[] tex, TerrainPalette palette, int w, int h)
      {
         var s = new double[w * h];
         for (int y = 0; y < h; y++)
         {
            int y1 = Math.Min(y + 1, h - 1);
            for (int x = 0; x < w; x++)
            {
               int x1 = Math.Min(x + 1, w - 1);
               var c0 = palette.GetColor((byte)tex[y * w + x]);
               var c1 = palette.GetColor((byte)tex[y * w + x1]);
               var c2 = palette.GetColor((byte)tex[y1 * w + x]);
               var c3 = palette.GetColor((byte)tex[y1 * w + x1]);
               double num = 0, den = 0;
               int i = y * w + x;
               for (int k = 0; k < 3; k++)
               {
                  double b = (c0[k] + c1[k] + c2[k] + c3[k]) * 0.25;
                  num += img[i * 3 + k] * b;
                  den += b * b;
               }
               s[i] = num / Math.Max(den, 1);
            }
         }
         return s;
      }

      // ---------------- heights ----------------

      /// <summary>Lighting model of the minimap (same as MinimapGenerator) + derivatives w.r.t. the central differences.</summary>
      private static double[] Shade(double[] H, int w, int h, double k, out double[] dsdx, out double[] dsdy)
      {
         var s = new double[w * h];
         dsdx = new double[w * h];
         dsdy = new double[w * h];
         double flat = Light[2] + Ambient;
         for (int y = 0; y < h; y++)
         {
            int ym = Math.Max(y - 1, 0), yp = Math.Min(y + 1, h - 1);
            for (int x = 0; x < w; x++)
            {
               int xm = Math.Max(x - 1, 0), xp = Math.Min(x + 1, w - 1);
               int i = y * w + x;
               double dx = H[y * w + xp] - H[y * w + xm];
               double dy = H[yp * w + x] - H[ym * w + x];
               double nx = -dx / k, ny = -dy / k;
               double len = Math.Sqrt(nx * nx + ny * ny + 1);
               double num = nx * Light[0] + ny * Light[1] + Light[2];
               double v = (num / len + Ambient) / flat;
               if (v > 0)
               {
                  s[i] = v;
                  double len3 = len * len * len;
                  dsdx[i] = (Light[0] / len - num * nx / len3) * (-1 / k) / flat;
                  dsdy[i] = (Light[1] / len - num * ny / len3) * (-1 / k) / flat;
               }
            }
         }
         return s;
      }

      private static double[] Laplacian(double[] a, int w, int h)
      {
         var r = new double[w * h];
         for (int y = 0; y < h; y++)
         {
            int ym = Math.Max(y - 1, 0), yp = Math.Min(y + 1, h - 1);
            for (int x = 0; x < w; x++)
            {
               int xm = Math.Max(x - 1, 0), xp = Math.Min(x + 1, w - 1);
               r[y * w + x] = a[y * w + xp] + a[y * w + xm] + a[yp * w + x] + a[ym * w + x] - 4 * a[y * w + x];
            }
         }
         return r;
      }

      /// <summary>Adam optimization of sum(mask*(shade(H)-sobs)^2) + lambda*sum(laplacian(H)^2).</summary>
      private static double[] Optimize(double[] sobs, double[] mask, double[] H0, int w, int h, double k, int iters, double lr, double lambda)
      {
         int n = w * h;
         var H = (double[])H0.Clone();
         var m = new double[n];
         var v = new double[n];
         var g = new double[n];
         const double b1 = 0.9, b2 = 0.999;
         for (int it = 1; it <= iters; it++)
         {
            var s = Shade(H, w, h, k, out var ax, out var ay);
            Array.Clear(g);
            for (int y = 0; y < h; y++)
            {
               int ym = Math.Max(y - 1, 0), yp = Math.Min(y + 1, h - 1);
               for (int x = 0; x < w; x++)
               {
                  int i = y * w + x;
                  double r = (s[i] - sobs[i]) * mask[i];
                  if (r == 0) continue;
                  int xm = Math.Max(x - 1, 0), xp = Math.Min(x + 1, w - 1);
                  double gx = 2 * r * ax[i], gy = 2 * r * ay[i];
                  g[y * w + xp] += gx; g[y * w + xm] -= gx;
                  g[yp * w + x] += gy; g[ym * w + x] -= gy;
               }
            }
            if (lambda > 0)
            {
               var l2 = Laplacian(Laplacian(H, w, h), w, h);
               for (int i = 0; i < n; i++) g[i] += lambda * 2 * l2[i];
            }
            double c1 = 1 - Math.Pow(b1, it), c2 = 1 - Math.Pow(b2, it);
            for (int i = 0; i < n; i++)
            {
               m[i] = b1 * m[i] + (1 - b1) * g[i];
               v[i] = b2 * v[i] + (1 - b2) * g[i] * g[i];
               H[i] -= lr * (m[i] / c1) / (Math.Sqrt(v[i] / c2) + 1e-12);
            }
         }
         return H;
      }

      private static double[] Down(double[] a, int w, int h, int f)
      {
         int cw = (w + f - 1) / f, ch = (h + f - 1) / f;
         var r = new double[cw * ch];
         var cnt = new int[cw * ch];
         for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
               int i = (y / f) * cw + x / f;
               r[i] += a[y * w + x];
               cnt[i]++;
            }
         for (int i = 0; i < r.Length; i++) r[i] /= cnt[i];
         return r;
      }

      private static double[] Up(double[] a, int cw, int ch, int f, int w, int h)
      {
         var r = new double[w * h];
         for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
               r[y * w + x] = a[(y / f) * cw + x / f];
         return r;
      }

      // ---------------- water ----------------

      /// <summary>
      /// Sets a flat level for every lake/river (20th percentile of its shore heights) and pushes the terrain under
      /// the water below it, deeper further from the shore. Returns the water height layer.
      /// </summary>
      private static double[] PlaceWater(double[] H, bool[] water, int w, int h)
      {
         int n = w * h;
         var level = new double[n];
         var label = new int[n];
         int labels = 0;
         var dist = WaterDistance(water, w, h);
         var queue = new Queue<int>();
         for (int start = 0; start < n; start++)
         {
            if (!water[start] || label[start] != 0) continue;
            labels++;
            var body = new List<int>();
            var shore = new HashSet<int>();
            label[start] = labels;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
               int i = queue.Dequeue();
               body.Add(i);
               int x = i % w, y = i / w;
               foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
               {
                  if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                  int j = ny * w + nx;
                  if (water[j])
                  {
                     if (label[j] == 0) { label[j] = labels; queue.Enqueue(j); }
                  }
                  else shore.Add(j);
               }
            }
            double lvl;
            if (shore.Count > 0)
            {
               var sh = shore.Select(j => H[j]).OrderBy(v => v).ToArray();
               lvl = sh[(int)(0.2 * (sh.Length - 1))];
            }
            else
            {
               var bh = body.Select(j => H[j]).OrderBy(v => v).ToArray();
               lvl = bh[bh.Length / 2];
            }
            foreach (int i in body) level[i] = lvl;
         }

         // lower the 4 corners of every water cell below the water level
         var cornerLevel = new double[n];
         var cornerDist = new double[n];
         var isCorner = new bool[n];
         for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
               int i = y * w + x;
               if (!water[i]) continue;
               for (int dy = 0; dy <= 1; dy++)
                  for (int dx = 0; dx <= 1; dx++)
                  {
                     int j = Math.Min(y + dy, h - 1) * w + Math.Min(x + dx, w - 1);
                     isCorner[j] = true;
                     cornerLevel[j] = Math.Max(cornerLevel[j], level[i]);
                     cornerDist[j] = Math.Max(cornerDist[j], dist[i]);
                  }
            }
         for (int i = 0; i < n; i++)
            if (isCorner[i])
               H[i] = Math.Min(H[i], cornerLevel[i] - (16 + Math.Min(300, 30 * cornerDist[i])));
         return level;
      }

      /// <summary>Distance (4-neighbour steps) from every water pixel to the nearest land pixel.</summary>
      private static double[] WaterDistance(bool[] water, int w, int h)
      {
         int n = w * h;
         var d = new double[n];
         var q = new Queue<int>();
         for (int i = 0; i < n; i++)
         {
            if (!water[i]) { d[i] = 0; q.Enqueue(i); }
            else d[i] = double.MaxValue;
         }
         if (q.Count == 0) { Array.Fill(d, 1000); return d; }
         while (q.Count > 0)
         {
            int i = q.Dequeue();
            int x = i % w, y = i / w;
            foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
            {
               if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
               int j = ny * w + nx;
               if (d[j] > d[i] + 1) { d[j] = d[i] + 1; q.Enqueue(j); }
            }
         }
         return d;
      }

      private static double[] Normalize(double x, double y, double z)
      {
         double l = Math.Sqrt(x * x + y * y + z * z);
         return new[] { x / l, y / l, z / l };
      }
   }
}
