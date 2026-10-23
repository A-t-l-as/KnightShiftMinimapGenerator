using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace KnightShiftMinimapGenerator
{
   /// <summary>
   /// KnightShift level files (.lnd / .mis) are made of two zlib streams written one after another:
   ///  1) a small header: magic 0x39D0A1FF, file name (byte-length prefixed), format id (e.g. "LD\0\x06"), GUID,
   ///  2) the actual data.
   /// </summary>
   public sealed class KsContainer
   {
      public const uint Magic = 0x39D0A1FF;

      public string InternalName { get; set; }
      /// <summary>Raw format id, e.g. 0x0600444C = 'L','D', version 6 (lnd) / 0x0C00444D = 'M','D', version 12 (mis).</summary>
      public uint FormatId { get; set; }
      public int Version => (int)(FormatId >> 24);
      public string Signature => Encoding.ASCII.GetString(new[] { (byte)(FormatId & 0xFF), (byte)((FormatId >> 8) & 0xFF) });
      public Guid Guid { get; set; }
      public byte[] Data { get; set; }

      public static KsContainer Read(string path) => Read(File.ReadAllBytes(path));

      public static KsContainer Read(byte[] file)
      {
         if (file.Length < 4 || file[0] != 0x78)
            throw new InvalidDataException("Not a KnightShift level file (zlib header not found).");

         var header = Inflate(file, 0);
         int dataOffset = FindNextStream(file, header);
         var data = Inflate(file, dataOffset);

         var c = new KsContainer { Data = data };
         using (var r = new BinaryReader(new MemoryStream(header)))
         {
            uint magic = r.ReadUInt32();
            if (magic != Magic)
               throw new InvalidDataException($"Bad header magic 0x{magic:X8}.");
            int nameLen = r.ReadByte();
            c.InternalName = Encoding.Latin1.GetString(r.ReadBytes(nameLen));
            c.FormatId = r.ReadUInt32();
            if (r.BaseStream.Length - r.BaseStream.Position >= 16)
               c.Guid = new Guid(r.ReadBytes(16));
         }
         return c;
      }

      /// <summary>Serializes the container (header stream + data stream), like the game editor does.</summary>
      public byte[] ToBytes()
      {
         var name = Encoding.Latin1.GetBytes(InternalName ?? "");
         if (name.Length > 255) throw new InvalidDataException("Internal name is too long.");
         byte[] header;
         using (var ms = new MemoryStream())
         using (var w = new BinaryWriter(ms))
         {
            w.Write(Magic);
            w.Write((byte)name.Length);
            w.Write(name);
            w.Write(FormatId);
            w.Write(Guid.ToByteArray());
            w.Flush();
            header = ms.ToArray();
         }
         using (var output = new MemoryStream())
         {
            Deflate(output, header);
            Deflate(output, Data);
            return output.ToArray();
         }
      }

      public void Write(string path)
      {
         var dir = Path.GetDirectoryName(Path.GetFullPath(path));
         if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
         File.WriteAllBytes(path, ToBytes());
      }

      private static void Deflate(Stream output, byte[] data)
      {
         using (var z = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
            z.Write(data, 0, data.Length);
      }

      private static byte[] Inflate(byte[] file, int offset)
      {
         // skip the 2-byte zlib header, DeflateStream handles the raw deflate data and stops at the final block
         using (var input = new MemoryStream(file, offset + 2, file.Length - offset - 2))
         using (var ds = new DeflateStream(input, CompressionMode.Decompress))
         using (var output = new MemoryStream())
         {
            ds.CopyTo(output);
            return output.ToArray();
         }
      }

      /// <summary>
      /// Finds where the second zlib stream starts: the first one ends with the big-endian Adler-32 of its content.
      /// </summary>
      private static int FindNextStream(byte[] file, byte[] firstContent)
      {
         uint adler = Adler32(firstContent);
         byte a0 = (byte)(adler >> 24), a1 = (byte)(adler >> 16), a2 = (byte)(adler >> 8), a3 = (byte)adler;
         for (int i = 2; i + 5 < file.Length; i++)
         {
            if (file[i] == a0 && file[i + 1] == a1 && file[i + 2] == a2 && file[i + 3] == a3
                && file[i + 4] == 0x78 && ((file[i + 4] << 8) | file[i + 5]) % 31 == 0)
               return i + 4;
         }
         throw new InvalidDataException("Data stream not found in level file.");
      }

      private static uint Adler32(byte[] data)
      {
         uint a = 1, b = 0;
         foreach (var x in data)
         {
            a = (a + x) % 65521;
            b = (b + a) % 65521;
         }
         return (b << 16) | a;
      }
   }
}
