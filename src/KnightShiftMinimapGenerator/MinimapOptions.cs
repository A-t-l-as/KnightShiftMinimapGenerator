namespace KnightShiftMinimapGenerator
{
   public class MinimapOptions
   {
      /// <summary>Minimap scaling factor (nearest neighbour). 1 = one pixel per map vertex, like the game.</summary>
      public int Scale { get; set; } = 1;

      /// <summary>Draw the 1 px black frame that the game minimap has.</summary>
      public bool DrawFrame { get; set; } = true;

      /// <summary>Render starting positions (from the MIS file) as circled numbers.</summary>
      public bool MarkStartingPositions { get; set; } = true;

      /// <summary>Relief shading strength, 1 = like the game, 0 = flat colors.</summary>
      public double ShadingStrength { get; set; } = 1.0;

      /// <summary>Palette used when the map's terrain set is not known.</summary>
      public TerrainPalette FallbackPalette { get; set; }
   }
}
