using KnightShiftMinimapGenerator;
using System;
using System.IO;
using System.Linq;

namespace KnightShiftMinimapGenerator.Runner
{
   class Program
   {
      static int Main(string[] args)
      {
         CommandLineOptions o;
         try
         {
            o = CommandLineOptions.Parse(args);
         }
         catch (Exception e)
         {
            Console.WriteLine(e.Message);
            Console.WriteLine(CommandLineOptions.Help);
            return -2;
         }
         if (o == null || args.Length == 0)
         {
            Console.WriteLine(CommandLineOptions.Help);
            return 0;
         }

         try
         {
            var palettes = LoadPalettes(o.Palettes);
            switch (o.Mode)
            {
               case "single":
                  CreateMinimapFile(o.Lnd, o.Mis, o.OutputPath, palettes, o);
                  break;
               case "multiple":
                  CreateMultipleMinimapFiles(o.InputPath, o.OutputPath, palettes, o);
                  break;
               case "calibrate":
                  Calibrate(o);
                  break;
               case "info":
                  Info(o, palettes);
                  break;
               case "reconstruct":
                  Reconstruct(o, palettes);
                  break;
               default:
                  Console.WriteLine($"Invalid mode value {o.Mode}");
                  return -1;
            }
            return 0;
         }
         catch (Exception e)
         {
            Console.WriteLine("Unhandled error:");
            Console.WriteLine(e.ToString());
            return -1;
         }
      }

      private static string DefaultPalettesPath => Path.Combine(AppContext.BaseDirectory, "palettes.json");

      private static PaletteStore LoadPalettes(string path)
      {
         var store = PaletteStore.CreateDefault();
         path ??= File.Exists(DefaultPalettesPath) ? DefaultPalettesPath : null;
         if (path != null && File.Exists(path))
            store.LoadJson(path);
         return store;
      }

      private static void CreateMultipleMinimapFiles(string inputPath, string outputPath, PaletteStore palettes, CommandLineOptions o)
      {
         if (string.IsNullOrEmpty(inputPath))
            throw new ArgumentException("--i option is required in multiple mode.");
         if (string.IsNullOrEmpty(outputPath))
            throw new ArgumentException("--o option is required.");
         foreach (var lnd in Directory.GetFiles(inputPath, "*.lnd"))
         {
            try
            {
               CreateMinimapFile(lnd, null, Path.Combine(outputPath, Path.GetFileNameWithoutExtension(lnd) + "." + o.Format), palettes, o);
            }
            catch (Exception e)
            {
               Console.WriteLine($" Error while parsing {Path.GetFileNameWithoutExtension(lnd)} level: {e.Message}");
            }
         }
      }

      private static void CreateMinimapFile(string lndFilePath, string misFilePath, string outputPath, PaletteStore palettes, CommandLineOptions o)
      {
         if (string.IsNullOrEmpty(lndFilePath))
            throw new ArgumentException("--lnd option is required in single mode.");
         if (string.IsNullOrEmpty(outputPath))
            throw new ArgumentException("--o option is required.");

         var lnd = LndFile.Read(lndFilePath);
         MisFile mis = null;
         if (!o.HideStartingPositions)
         {
            misFilePath ??= Path.ChangeExtension(lndFilePath, ".mis");
            if (File.Exists(misFilePath))
            {
               try
               {
                  mis = MisFile.Read(misFilePath);
               }
               catch (Exception e)
               {
                  Console.WriteLine($" Warning: {Path.GetFileName(misFilePath)}: {e.Message} Starting positions not drawn.");
               }
            }
            else if (o.Mis != null)
               throw new FileNotFoundException("MIS file not found.", misFilePath);
         }
         var palette = palettes.Find(lnd.TerrainIdHex);
         if (palette == null)
         {
            Console.WriteLine($" Warning: {Path.GetFileName(lndFilePath)}: unknown terrain set {lnd.TerrainIdHex}, using fallback palette." +
                              " Use --mode calibrate with a minimap from the game to add it.");
            palette = palettes.All.First();
         }
         MinimapGenerator.Generate(lnd, palette, new MinimapOptions
         {
            Scale = o.Scale,
            DrawFrame = !o.NoFrame,
            ShadingStrength = o.Shading,
            MarkStartingPositions = !o.HideStartingPositions
         }, mis).Save(outputPath);
         Console.WriteLine($"{Path.GetFileName(lndFilePath)} -> {outputPath}");
      }

      private static void Calibrate(CommandLineOptions o)
      {
         if (string.IsNullOrEmpty(o.Lnd) || string.IsNullOrEmpty(o.Reference))
            throw new ArgumentException("--lnd and --ref options are required in calibrate mode.");
         var lnd = LndFile.Read(o.Lnd);
         var reference = RgbImage.LoadBmp(o.Reference);
         var palette = MinimapGenerator.Calibrate(lnd, reference, o.Name);
         var path = o.Palettes ?? DefaultPalettesPath;
         PaletteStore.MergeIntoJson(path, palette);

         var check = MinimapGenerator.Generate(lnd, palette, new MinimapOptions());
         Console.WriteLine($"Terrain {palette.TerrainId}: {palette.Colors.Count} texture colors saved to {path}");
         Console.WriteLine($"Mean error vs reference: {MinimapGenerator.MeanAbsoluteError(check, reference):0.00} / 255");
      }

      private const string KnownTerrainPrefix = "eaeaeaea5021502100010000000000";

      private static void Reconstruct(CommandLineOptions o, PaletteStore palettes)
      {
         if (string.IsNullOrEmpty(o.Reference) || string.IsNullOrEmpty(o.OutputPath))
            throw new ArgumentException("--ref and --o options are required in reconstruct mode.");
         var minimap = RgbImage.LoadBmp(o.Reference);

         LndFile lnd;
         if (!string.IsNullOrEmpty(o.Template))
         {
            lnd = LndFile.Read(o.Template);
            if (lnd.Width != minimap.Width || lnd.Height != minimap.Height)
               throw new ArgumentException($"Template map is {lnd.Width}x{lnd.Height}, minimap is {minimap.Width}x{minimap.Height}. " +
                                           "Create an empty map of the same size in the editor.");
            if (Path.GetFullPath(o.Template) == Path.GetFullPath(o.OutputPath))
            {
               File.Copy(o.Template, o.Template + ".bak", true);
               Console.WriteLine($"Backup of the template saved as {o.Template}.bak");
            }
         }
         else
         {
            if (string.IsNullOrEmpty(o.Terrain))
               throw new ArgumentException("--template or --terrain option is required in reconstruct mode.");
            string id = o.Terrain.Length == 2 ? KnownTerrainPrefix + o.Terrain : o.Terrain;
            if (id.Length != 32) throw new ArgumentException("--terrain must be 2 hex digits (e.g. 01) or a 32-digit terrain id.");
            var idBytes = Enumerable.Range(0, 16).Select(i => Convert.ToByte(id.Substring(i * 2, 2), 16)).ToArray();
            string name = Path.GetFileNameWithoutExtension(o.OutputPath);
            lnd = LndFile.CreateEmpty(minimap.Width, minimap.Height, name, o.Name ?? name, idBytes);
         }
         if (o.Name != null) lnd.Title = o.Name;
         lnd.Container.InternalName = Path.GetFileNameWithoutExtension(o.OutputPath);

         var palette = palettes.Find(lnd.TerrainIdHex)
            ?? throw new ArgumentException($"No palette for terrain set {lnd.TerrainIdHex}. Add one with --mode calibrate first.");
         Console.WriteLine($"Reconstructing {minimap.Width}x{minimap.Height} map, terrain: {palette.Name}");

         var sw = System.Diagnostics.Stopwatch.StartNew();
         MinimapReconstructor.Reconstruct(minimap, palette, lnd, new ReconstructOptions
         {
            Smoothing = o.Smoothing,
            Log = msg => Console.WriteLine("  " + msg)
         });
         lnd.Write(o.OutputPath);

         var check = MinimapGenerator.Generate(LndFile.Read(o.OutputPath), palette, new MinimapOptions());
         Console.WriteLine($"Saved {o.OutputPath} ({sw.Elapsed.TotalSeconds:0.0} s)");
         Console.WriteLine($"Minimap of the reconstructed map vs input: mean error {MinimapGenerator.MeanAbsoluteError(check, minimap):0.00} / 255");
         if (!string.IsNullOrEmpty(o.Preview))
         {
            check.Scale(o.Scale).Save(o.Preview);
            Console.WriteLine($"Preview saved to {o.Preview}");
         }
      }

      private static void Info(CommandLineOptions o, PaletteStore palettes)
      {
         var files = !string.IsNullOrEmpty(o.Lnd) ? new[] { o.Lnd }
                   : !string.IsNullOrEmpty(o.InputPath) ? Directory.GetFiles(o.InputPath, "*.lnd")
                   : throw new ArgumentException("--lnd or --i option is required in info mode.");
         foreach (var f in files)
         {
            try
            {
               var lnd = LndFile.Read(f);
               var pal = palettes.Find(lnd.TerrainIdHex);
               Console.WriteLine($"{Path.GetFileName(f)}: \"{lnd.Title}\" {lnd.Width}x{lnd.Height} border={lnd.Border} " +
                                 $"lnd v{lnd.Container.Version} terrain={lnd.TerrainIdHex} palette={(pal?.Name ?? "UNKNOWN")}");
               var misPath = Path.ChangeExtension(f, ".mis");
               if (File.Exists(misPath))
               {
                  var mis = MisFile.Read(misPath);
                  var starts = mis.Players.Where(p => p.IsActive).Select(p => $"{p.Index + 1}:({p.StartPositionX},{p.StartPositionY})");
                  Console.WriteLine($"   mis v{mis.Container.Version}, starting positions: {string.Join(" ", starts)}");
               }
            }
            catch (Exception e)
            {
               Console.WriteLine($"{Path.GetFileName(f)}: error {e.Message}");
            }
         }
      }
   }
}
