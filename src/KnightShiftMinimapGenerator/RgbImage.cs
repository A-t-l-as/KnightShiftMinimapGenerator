using System;
using System.IO;
using System.IO.Compression;

namespace KnightShiftMinimapGenerator
{
   /// <summary>Simple 24-bit RGB image with dependency-free PNG/BMP writers and a BMP reader.</summary>
   public sealed class RgbImage
   {
      public int Width { get; }
      public int Height { get; }
      /// <summary>Top-down rows, 3 bytes per pixel (R,G,B).</summary>
      public byte[] Pixels { get; }

      public RgbImage(int width, int height)
      {
         Width = width;
         Height = height;
         Pixels = new byte[width * height * 3];
      }

      public void SetPixel(int x, int y, byte r, byte g, byte b)
      {
         int i = (y * Width + x) * 3;
         Pixels[i] = r; Pixels[i + 1] = g; Pixels[i + 2] = b;
      }

      public (byte R, byte G, byte B) GetPixel(int x, int y)
      {
         int i = (y * Width + x) * 3;
         return (Pixels[i], Pixels[i + 1], Pixels[i + 2]);
      }

      public RgbImage Scale(int factor)
      {
         if (factor <= 1) return this;
         var res = new RgbImage(Width * factor, Height * factor);
         for (int y = 0; y < res.Height; y++)
            for (int x = 0; x < res.Width; x++)
               Array.Copy(Pixels, ((y / factor) * Width + x / factor) * 3, res.Pixels, (y * res.Width + x) * 3, 3);
         return res;
      }

      public void Save(string path)
      {
         var dir = Path.GetDirectoryName(Path.GetFullPath(path));
         if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
         if (Path.GetExtension(path).Equals(".bmp", StringComparison.OrdinalIgnoreCase))
            File.WriteAllBytes(path, ToBmp());
         else
            File.WriteAllBytes(path, ToPng());
      }

      // ---------------- BMP ----------------

      public byte[] ToBmp()
      {
         int stride = (Width * 3 + 3) & ~3;
         int size = 54 + stride * Height;
         var ms = new MemoryStream(size);
         var w = new BinaryWriter(ms);
         w.Write((byte)'B'); w.Write((byte)'M'); w.Write(size); w.Write(0); w.Write(54);
         w.Write(40); w.Write(Width); w.Write(Height); w.Write((short)1); w.Write((short)24);
         w.Write(0); w.Write(0); w.Write(100); w.Write(100); w.Write(0); w.Write(0);
         var row = new byte[stride];
         for (int y = Height - 1; y >= 0; y--)
         {
            for (int x = 0; x < Width; x++)
            {
               int i = (y * Width + x) * 3;
               row[x * 3] = Pixels[i + 2]; row[x * 3 + 1] = Pixels[i + 1]; row[x * 3 + 2] = Pixels[i];
            }
            w.Write(row);
         }
         return ms.ToArray();
      }

      /// <summary>Reads an uncompressed 24 or 32-bit BMP (e.g. a minimap dumped from the game).</summary>
      public static RgbImage LoadBmp(string path)
      {
         var b = File.ReadAllBytes(path);
         if (b.Length < 54 || b[0] != 'B' || b[1] != 'M') throw new InvalidDataException("Not a BMP file.");
         int offset = BitConverter.ToInt32(b, 10);
         int w = BitConverter.ToInt32(b, 18);
         int h = BitConverter.ToInt32(b, 22);
         int bpp = BitConverter.ToInt16(b, 28);
         int compression = BitConverter.ToInt32(b, 30);
         if ((bpp != 24 && bpp != 32) || (compression != 0 && compression != 3))
            throw new InvalidDataException("Only uncompressed 24/32-bit BMP files are supported.");
         bool bottomUp = h > 0; h = Math.Abs(h);
         int bytesPP = bpp / 8;
         int stride = (w * bytesPP + 3) & ~3;
         var img = new RgbImage(w, h);
         for (int y = 0; y < h; y++)
         {
            int src = offset + (bottomUp ? h - 1 - y : y) * stride;
            for (int x = 0; x < w; x++)
            {
               int p = src + x * bytesPP;
               img.SetPixel(x, y, b[p + 2], b[p + 1], b[p]);
            }
         }
         return img;
      }

      // ---------------- PNG ----------------

      public byte[] ToPng()
      {
         var raw = new byte[(Width * 3 + 1) * Height];
         for (int y = 0; y < Height; y++)
         {
            raw[y * (Width * 3 + 1)] = 0; // filter: none
            Array.Copy(Pixels, y * Width * 3, raw, y * (Width * 3 + 1) + 1, Width * 3);
         }
         byte[] idat;
         using (var ms = new MemoryStream())
         {
            ms.WriteByte(0x78); ms.WriteByte(0x9C);
            using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, true))
               ds.Write(raw, 0, raw.Length);
            uint adler = Adler32(raw);
            ms.WriteByte((byte)(adler >> 24)); ms.WriteByte((byte)(adler >> 16));
            ms.WriteByte((byte)(adler >> 8)); ms.WriteByte((byte)adler);
            idat = ms.ToArray();
         }
         using (var png = new MemoryStream())
         {
            png.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);
            var ihdr = new byte[13];
            WriteBE(ihdr, 0, (uint)Width); WriteBE(ihdr, 4, (uint)Height);
            ihdr[8] = 8; ihdr[9] = 2; // 8-bit RGB
            WriteChunk(png, "IHDR", ihdr);
            WriteChunk(png, "IDAT", idat);
            WriteChunk(png, "IEND", new byte[0]);
            return png.ToArray();
         }
      }

      private static void WriteChunk(Stream s, string type, byte[] data)
      {
         var len = new byte[4]; WriteBE(len, 0, (uint)data.Length); s.Write(len, 0, 4);
         var typeAndData = new byte[4 + data.Length];
         for (int i = 0; i < 4; i++) typeAndData[i] = (byte)type[i];
         Array.Copy(data, 0, typeAndData, 4, data.Length);
         s.Write(typeAndData, 0, typeAndData.Length);
         var crc = new byte[4]; WriteBE(crc, 0, Crc32(typeAndData)); s.Write(crc, 0, 4);
      }

      private static void WriteBE(byte[] b, int o, uint v)
      {
         b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v;
      }

      private static uint[] _crcTable;
      private static uint Crc32(byte[] data)
      {
         if (_crcTable == null)
         {
            var t = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
               uint c = n;
               for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
               t[n] = c;
            }
            _crcTable = t;
         }
         uint crc = 0xFFFFFFFF;
         foreach (var x in data) crc = _crcTable[(crc ^ x) & 0xFF] ^ (crc >> 8);
         return crc ^ 0xFFFFFFFF;
      }

      private static uint Adler32(byte[] data)
      {
         uint a = 1, b = 0;
         foreach (var x in data) { a = (a + x) % 65521; b = (b + a) % 65521; }
         return (b << 16) | a;
      }
   }
}
