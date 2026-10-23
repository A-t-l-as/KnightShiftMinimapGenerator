using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace KnightShiftMinimapGenerator
{
   /// <summary>
   /// Average minimap color of every terrain texture of one terrain set (summer, winter...).
   /// Key = texture index from the LND texture layer (value &amp; 0x7F); missing entries fall back to (index &amp; 7).
   /// </summary>
   public sealed class TerrainPalette
   {
      public string TerrainId { get; set; }
      public string Name { get; set; }
      public Dictionary<int, byte[]> Colors { get; set; } = new Dictionary<int, byte[]>();

      private static readonly byte[] Fallback = { 128, 128, 128 };

      public byte[] GetColor(byte texture)
      {
         int t = texture & 0x7F;
         if (Colors.TryGetValue(t, out var c)) return c;
         if (Colors.TryGetValue(t & 7, out c)) return c;
         return Fallback;
      }
   }

   /// <summary>Built-in palettes + palettes loaded from a JSON file (created by the 'calibrate' mode).</summary>
   public sealed class PaletteStore
   {
      private readonly Dictionary<string, TerrainPalette> _palettes = new Dictionary<string, TerrainPalette>(StringComparer.OrdinalIgnoreCase);

      public IEnumerable<TerrainPalette> All => _palettes.Values;

      public static PaletteStore CreateDefault()
      {
         var s = new PaletteStore();
         foreach (var p in BuiltIn()) s.Add(p);
         return s;
      }

      public void Add(TerrainPalette p) => _palettes[p.TerrainId] = p;

      public TerrainPalette Find(string terrainId) =>
         terrainId != null && _palettes.TryGetValue(terrainId, out var p) ? p : null;

      public void LoadJson(string path)
      {
         var list = JsonSerializer.Deserialize<List<PaletteDto>>(File.ReadAllText(path));
         foreach (var d in list) Add(d.ToPalette());
      }

      public void SaveJson(string path, IEnumerable<TerrainPalette> palettes)
      {
         var list = palettes.Select(PaletteDto.From).ToList();
         File.WriteAllText(path, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
      }

      /// <summary>Loads existing JSON (if any), adds/replaces the palette and saves it back.</summary>
      public static void MergeIntoJson(string path, TerrainPalette palette)
      {
         var s = new PaletteStore();
         if (File.Exists(path)) s.LoadJson(path);
         s.Add(palette);
         s.SaveJson(path, s.All);
      }

      private class PaletteDto
      {
         public string TerrainId { get; set; }
         public string Name { get; set; }
         public Dictionary<string, int[]> Colors { get; set; }

         public TerrainPalette ToPalette() => new TerrainPalette
         {
            TerrainId = TerrainId,
            Name = Name,
            Colors = Colors.ToDictionary(kv => int.Parse(kv.Key), kv => kv.Value.Select(v => (byte)Math.Clamp(v, 0, 255)).ToArray())
         };

         public static PaletteDto From(TerrainPalette p) => new PaletteDto
         {
            TerrainId = p.TerrainId,
            Name = p.Name,
            Colors = p.Colors.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value.Select(v => (int)v).ToArray())
         };
      }

      // Calibrated from minimaps dumped from the game (see README).
      private static IEnumerable<TerrainPalette> BuiltIn()
      {
         yield return Make("eaeaeaea502150210001000000000000", "Terrain 00 (summer; maps: aaa, RPG_GE08_0)", new[]
         {
            (0, 98, 98, 69), (1, 94, 96, 66), (2, 100, 107, 69), (3, 83, 102, 56),
            (4, 90, 87, 64), (5, 93, 91, 65), (6, 108, 104, 68), (7, 109, 101, 76),
            (8, 100, 98, 71), (9, 94, 94, 66), (10, 104, 112, 73), (11, 79, 103, 52),
            (12, 86, 81, 62), (13, 96, 87, 66), (14, 110, 102, 68), (15, 108, 99, 74)
         });
         yield return Make("eaeaeaea502150210001000000000001", "Terrain 01 (summer; maps: Twisted Canyons II, RPG_GE08_1)", new[]
         {
            (0, 108, 97, 65), (1, 88, 89, 54), (2, 59, 84, 38), (3, 66, 91, 42),
            (4, 88, 84, 58), (5, 97, 92, 61), (6, 139, 125, 92), (7, 104, 99, 73),
            (8, 107, 96, 62), (11, 60, 85, 36), (12, 81, 78, 54), (14, 146, 130, 98),
            (15, 100, 96, 71)
         });
         yield return Make("eaeaeaea502150210001000000000002", "Terrain 02 (map: RPG_GE08_2)", new[]
         {
            (0, 110, 108, 86), (1, 95, 94, 70), (2, 94, 96, 57), (3, 89, 90, 56),
            (4, 88, 86, 66), (5, 101, 100, 78), (6, 111, 107, 85), (7, 117, 113, 96),
            (8, 116, 113, 93), (11, 81, 84, 48), (12, 77, 76, 59), (14, 115, 110, 88),
            (15, 120, 116, 98)
         });
         yield return Make("eaeaeaea502150210001000000000003", "Terrain 03 (map: RPG_GE12_0 'The Den')", new[]
         {
            (0, 118, 114, 114), (1, 103, 102, 81), (2, 78, 92, 42), (3, 72, 91, 38),
            (4, 89, 86, 60), (5, 91, 85, 49), (6, 94, 83, 45), (7, 69, 73, 65),
            (8, 118, 115, 116), (9, 100, 100, 80), (10, 76, 89, 43), (11, 67, 88, 38),
            (12, 81, 77, 54), (13, 93, 84, 50), (14, 94, 76, 44)
         });
         yield return Make("eaeaeaea502150210001000000000004", "Terrain 04 (winter; maps: Mage's war II, RPG_GE08_3)", new[]
         {
            (0, 175, 178, 179), (1, 139, 141, 138), (2, 122, 120, 101), (3, 116, 112, 86),
            (4, 133, 135, 133), (5, 164, 167, 168), (6, 187, 187, 187), (7, 135, 135, 132),
            (8, 176, 179, 180), (9, 130, 132, 129), (11, 104, 98, 65), (12, 119, 120, 117),
            (13, 169, 172, 173), (14, 188, 188, 189), (15, 132, 131, 129)
         });
      }

      private static TerrainPalette Make(string id, string name, (int i, int r, int g, int b)[] cols) => new TerrainPalette
      {
         TerrainId = id,
         Name = name,
         Colors = cols.ToDictionary(c => c.i, c => new[] { (byte)c.r, (byte)c.g, (byte)c.b })
      };
   }
}
