using System;
using System.IO;
using System.Text;

namespace KnightShiftMinimapGenerator
{
   /// <summary>
   /// KnightShift terrain file (.lnd). Layout reverse engineered from the editor save function (FUN_005d2610)
   /// and the level header reader (FUN_005d6d20):
   /// <code>
   /// int32  width
   /// int32  height
   /// int32  border            (non playable margin, 16 on all known maps)
   /// int32  titleLength, char[titleLength] title
   /// byte[16] terrainId       (only for lnd version &gt;= 4; identifies the terrain set: summer, winter...)
   /// uint16 height[w*h]       terrain height per vertex
   /// byte   layerA[w*h]       (unknown, usually 64)
   /// byte   texture[w*h]     terrain texture index (low bits) + flags (0x80)
   /// uint32 colorA[w*h]       ARGB tint painted in the editor (not used by the minimap)
   /// uint32 colorB[w*h]       ARGB tint painted in the editor (not used by the minimap)
   /// byte   layerF[w*h]       (unknown, 0 or 138)
   /// int32  unknown           (2048 on all known maps)
   /// uint16 waterHeight[w*h]  water level per vertex, 0 = no water
   /// </code>
   /// </summary>
   public sealed class LndFile
   {
      public KsContainer Container { get; set; }
      public int Width { get; set; }
      public int Height { get; set; }
      public int Border { get; set; }
      public string Title { get; set; }
      public byte[] TerrainId { get; set; }
      public string TerrainIdHex => TerrainId == null ? "" : BitConverter.ToString(TerrainId).Replace("-", "").ToLowerInvariant();

      public ushort[] TerrainHeight { get; set; }
      public byte[] LayerA { get; set; }
      public byte[] Texture { get; set; }
      public uint[] ColorA { get; set; }
      public uint[] ColorB { get; set; }
      public byte[] LayerF { get; set; }
      public int Unknown { get; set; }
      public ushort[] WaterHeight { get; set; }

      public static LndFile Read(string path) => Read(KsContainer.Read(path));

      public static LndFile Read(KsContainer container)
      {
         if (container.Signature != "LD")
            throw new InvalidDataException($"Not an LND file (signature '{container.Signature}').");

         var f = new LndFile { Container = container };
         using (var r = new BinaryReader(new MemoryStream(container.Data)))
         {
            f.Width = r.ReadInt32();
            f.Height = r.ReadInt32();
            if (f.Width <= 0 || f.Height <= 0 || f.Width > 4096 || f.Height > 4096)
               throw new InvalidDataException($"Invalid map size {f.Width}x{f.Height}.");
            f.Border = r.ReadInt32();
            int titleLen = r.ReadInt32();
            f.Title = titleLen > 0 ? Encoding.Latin1.GetString(r.ReadBytes(titleLen)) : "";
            if (container.Version >= 4)
               f.TerrainId = r.ReadBytes(16);

            int n = f.Width * f.Height;
            f.TerrainHeight = ReadUShorts(r, n);
            f.LayerA = r.ReadBytes(n);
            f.Texture = r.ReadBytes(n);
            f.ColorA = ReadUInts(r, n);
            f.ColorB = ReadUInts(r, n);
            f.LayerF = r.ReadBytes(n);
            f.Unknown = r.ReadInt32();
            f.WaterHeight = ReadUShorts(r, n);
         }
         return f;
      }

      /// <summary>Creates an empty map with the same default layer values the editor uses.</summary>
      public static LndFile CreateEmpty(int width, int height, string internalName, string title, byte[] terrainId, ushort baseHeight = 2500)
      {
         int n = width * height;
         var f = new LndFile
         {
            Container = new KsContainer { InternalName = internalName, FormatId = 0x0600444C, Guid = Guid.NewGuid() },
            Width = width,
            Height = height,
            Border = 16,
            Title = title ?? "",
            TerrainId = terrainId ?? new byte[16],
            TerrainHeight = new ushort[n],
            LayerA = new byte[n],
            Texture = new byte[n],
            ColorA = new uint[n],
            ColorB = new uint[n],
            LayerF = new byte[n],
            Unknown = 2048,
            WaterHeight = new ushort[n]
         };
         Array.Fill(f.TerrainHeight, baseHeight);
         Array.Fill(f.LayerA, (byte)64);
         Array.Fill(f.ColorA, 0xFFFFFFFFu);
         Array.Fill(f.ColorB, 0xFFFFFFFFu);
         return f;
      }

      /// <summary>Serializes the map data in the layout written by the editor (FUN_005d2610).</summary>
      public byte[] SerializeData()
      {
         int n = Width * Height;
         if (TerrainHeight.Length != n || LayerA.Length != n || Texture.Length != n || ColorA.Length != n ||
             ColorB.Length != n || LayerF.Length != n || WaterHeight.Length != n)
            throw new InvalidDataException("Layer sizes do not match the map size.");
         using (var ms = new MemoryStream())
         using (var w = new BinaryWriter(ms))
         {
            w.Write(Width);
            w.Write(Height);
            w.Write(Border);
            var title = Encoding.Latin1.GetBytes(Title ?? "");
            w.Write(title.Length);
            w.Write(title);
            if (Container.Version >= 4)
               w.Write(TerrainId ?? new byte[16]);
            WriteArray(w, TerrainHeight, 2);
            w.Write(LayerA);
            w.Write(Texture);
            WriteArray(w, ColorA, 4);
            WriteArray(w, ColorB, 4);
            w.Write(LayerF);
            w.Write(Unknown);
            WriteArray(w, WaterHeight, 2);
            w.Flush();
            return ms.ToArray();
         }
      }

      public void Write(string path)
      {
         Container.Data = SerializeData();
         Container.Write(path);
      }

      private static void WriteArray(BinaryWriter w, Array a, int elementSize)
      {
         var bytes = new byte[a.Length * elementSize];
         Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length);
         w.Write(bytes);
      }

      private static ushort[] ReadUShorts(BinaryReader r, int n)
      {
         var bytes = r.ReadBytes(n * 2);
         if (bytes.Length != n * 2) throw new EndOfStreamException("LND file is truncated.");
         var a = new ushort[n];
         Buffer.BlockCopy(bytes, 0, a, 0, bytes.Length);
         return a;
      }

      private static uint[] ReadUInts(BinaryReader r, int n)
      {
         var bytes = r.ReadBytes(n * 4);
         if (bytes.Length != n * 4) throw new EndOfStreamException("LND file is truncated.");
         var a = new uint[n];
         Buffer.BlockCopy(bytes, 0, a, 0, bytes.Length);
         return a;
      }
   }
}
