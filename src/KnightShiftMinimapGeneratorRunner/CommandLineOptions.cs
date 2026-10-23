using System;
using System.Collections.Generic;

namespace KnightShiftMinimapGenerator.Runner
{
   public class CommandLineOptions
   {
      public string Mode { get; set; } = "single";
      public string Lnd { get; set; }
      public string Mis { get; set; }
      public bool HideStartingPositions { get; set; }
      public string InputPath { get; set; }
      public string OutputPath { get; set; }
      public int Scale { get; set; } = 1;
      public bool NoFrame { get; set; }
      public double Shading { get; set; } = 1.0;
      public string Palettes { get; set; }
      public string Reference { get; set; }
      public string Name { get; set; }
      public string Format { get; set; } = "png";
      public string Template { get; set; }
      public string Terrain { get; set; }
      public double Smoothing { get; set; } = 5e-7;
      public string Preview { get; set; }

      public const string Help =
@"KnightShift minimap generator

Modes (--mode):
  single     one map:           --lnd <file.lnd> [--mis <file.mis>] --o <output.png|.bmp>
  multiple   whole folder:      --i <folder with .lnd/.mis files> --o <output folder> [--format png|bmp]
  calibrate  new terrain palette from a minimap taken from the game:
                                --lnd <file.lnd> --ref <minimap.bmp> [--palettes palettes.json] [--name ""Winter 2""]
  info       map information:   --lnd <file.lnd>  (or --i <folder>)
  reconstruct  rebuild an .lnd file from a minimap taken from the game (approximation, see README):
                                --ref <minimap.bmp> --o <output.lnd> --template <empty map.lnd>
                                (or instead of --template: --terrain <00..04 | 32-digit id> [--name ""Title""])
                                [--smoothing 5e-7] [--preview <preview.png>]

Options:
  --scale <n>         scale factor (default 1 = one pixel per map vertex, like the game)
  --format png|bmp    output format in 'multiple' mode (default png)
  --noframe           do not draw the 1 px black frame
  --hidestartingpositions
                      do not mark player starting positions (read from the .mis file
                      with the same name as the .lnd file, or from --mis)
  --shading <0..1>    relief shading strength (default 1)
  --palettes <file>   additional terrain palettes (JSON); default: palettes.json next to the executable, if it exists
";

      public static CommandLineOptions Parse(string[] args)
      {
         var o = new CommandLineOptions();
         for (int i = 0; i < args.Length; i++)
         {
            string a = args[i].TrimStart('-').ToLowerInvariant();
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {args[i]}");
            switch (a)
            {
               case "mode": case "m": o.Mode = Next().ToLowerInvariant(); break;
               case "lnd": case "l": o.Lnd = Next(); break;
               case "i": case "input": case "inputpath": o.InputPath = Next(); break;
               case "o": case "output": case "outputpath": o.OutputPath = Next(); break;
               case "scale": case "s": o.Scale = int.Parse(Next()); break;
               case "noframe": o.NoFrame = true; break;
               case "shading": o.Shading = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
               case "palettes": case "p": o.Palettes = Next(); break;
               case "ref": case "reference": o.Reference = Next(); break;
               case "name": o.Name = Next(); break;
               case "format": case "f": o.Format = Next().TrimStart('.').ToLowerInvariant(); break;
               case "mis": o.Mis = Next(); break;
               case "hidestartingpositions": case "nopositions": o.HideStartingPositions = true; break;
               case "template": case "t": o.Template = Next(); break;
               case "terrain": o.Terrain = Next(); break;
               case "smoothing": o.Smoothing = double.Parse(Next(), System.Globalization.CultureInfo.InvariantCulture); break;
               case "preview": o.Preview = Next(); break;
               case "h": case "help": case "?": return null;
               default: throw new ArgumentException($"Unknown option {args[i]}");
            }
         }
         return o;
      }
   }
}
