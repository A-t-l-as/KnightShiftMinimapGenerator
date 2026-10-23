using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace KnightShiftMinimapGenerator
{
   public sealed class PlayerData
   {
      /// <summary>Slot index (0-15).</summary>
      public int Index { get; set; }
      /// <summary>Player flags (bit 0 = slot used). 0 = empty slot.</summary>
      public int Flags { get; set; }
      public int StartPositionX { get; set; }
      public int StartPositionY { get; set; }
      public byte Extra1 { get; set; }
      public byte Extra2 { get; set; }

      public bool IsActive => Flags != 0 && !(StartPositionX == 255 && StartPositionY == 255);
   }

   /// <summary>
   /// KnightShift mission file (.mis). Only the header is parsed, layout taken from FUN_005e2950:
   /// <code>
   /// string  name                           (int32 length + chars)
   /// [v6]    string lndName                 / [v7+] byte[16] lndGuid
   /// byte    unknown
   /// int32   unknown
   /// uint16  width, height
   /// int32   unknown
   /// [v9+]   int32 unknown, [v11+] int32 unknown
   /// [v12]   string x3
   /// 16 x player: int32 flags, byte startX, byte startY, [v8+] byte, byte
   /// ... (objects, scripts - not parsed)
   /// </code>
   /// </summary>
   public sealed class MisFile
   {
      public const int MaxPlayers = 16;

      public KsContainer Container { get; private set; }
      public string Name { get; private set; }
      public Guid LndGuid { get; private set; }
      public int Width { get; private set; }
      public int Height { get; private set; }
      public IReadOnlyList<PlayerData> Players { get; private set; }

      public static MisFile Read(string path) => Read(KsContainer.Read(path));

      public static MisFile Read(KsContainer container)
      {
         if (container.Signature != "MD")
            throw new InvalidDataException($"Not a MIS file (signature '{container.Signature}').");
         int v = container.Version;
         if (v < 6 || v > 12)
            throw new InvalidDataException($"Unsupported MIS version {v}.");

         var m = new MisFile { Container = container };
         using (var r = new BinaryReader(new MemoryStream(container.Data)))
         {
            m.Name = ReadString(r);
            if (v == 6)
               ReadString(r); // lnd name, the game resolves it to a GUID
            else
               m.LndGuid = new Guid(r.ReadBytes(16));
            r.ReadByte();
            r.ReadInt32();
            m.Width = r.ReadUInt16();
            m.Height = r.ReadUInt16();
            r.ReadInt32();
            if (v >= 9) r.ReadInt32();
            if (v >= 11) r.ReadInt32();
            if (v >= 12)
            {
               ReadString(r);
               ReadString(r);
               ReadString(r);
            }

            var players = new List<PlayerData>();
            for (int i = 0; i < MaxPlayers; i++)
            {
               var p = new PlayerData { Index = i };
               int flags = r.ReadInt32();
               p.StartPositionX = r.ReadByte();
               p.StartPositionY = r.ReadByte();
               if (v >= 8)
               {
                  p.Extra1 = r.ReadByte();
                  p.Extra2 = r.ReadByte();
               }
               else
               {
                  // old flag layout is converted by the game
                  flags = ((flags & 0xC) * 2 | flags & 2) * 2 | flags & 1;
                  p.Extra1 = p.Extra2 = 0xFF;
               }
               p.Flags = flags;
               players.Add(p);
            }
            m.Players = players;
         }
         return m;
      }

      private static string ReadString(BinaryReader r)
      {
         int len = r.ReadInt32();
         if (len < 0 || len > 1 << 20) throw new InvalidDataException("Invalid string in MIS file.");
         return len == 0 ? "" : Encoding.Latin1.GetString(r.ReadBytes(len));
      }
   }
}
