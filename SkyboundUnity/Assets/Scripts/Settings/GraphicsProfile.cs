namespace Skybound
{
    public enum GraphicsQuality
    {
        Low,
        Medium,
        High,
    }

    /// <summary>
    /// What each graphics tier costs. Everything visual that scales with GPU or CPU load is
    /// read from here, so a tier change is one switch. The browser's dpr range becomes the
    /// render scale in each tier's URP asset.
    /// </summary>
    public sealed class GraphicsProfile
    {
        public bool antialias;
        public bool shadows;
        public int shadowMapSize;
        /// <summary>Terrain mesh grid (segments per side). Physics always uses the exact terrain function.</summary>
        public int terrainSegments;
        public int trees;
        public int clouds;
        public float fogFar;
        /// <summary>Bloom and vignette allowed (also needs the Effects setting).</summary>
        public bool postFx;

        public static readonly GraphicsProfile Low = new GraphicsProfile
        {
            antialias = false,
            shadows = false,
            shadowMapSize = 512,
            terrainSegments = 160,
            trees = 900,
            clouds = 16,
            fogFar = 14000,
            postFx = false,
        };

        public static readonly GraphicsProfile Medium = new GraphicsProfile
        {
            antialias = true,
            shadows = true,
            shadowMapSize = 1024,
            terrainSegments = 280,
            trees = 1800,
            clouds = 28,
            fogFar = 19000,
            postFx = false,
        };

        public static readonly GraphicsProfile High = new GraphicsProfile
        {
            antialias = true,
            shadows = true,
            shadowMapSize = 2048,
            terrainSegments = 400,
            trees = 2600,
            clouds = 38,
            fogFar = 22000,
            postFx = true,
        };

        public static GraphicsProfile For(GraphicsQuality quality)
        {
            switch (quality)
            {
                case GraphicsQuality.Low: return Low;
                case GraphicsQuality.High: return High;
                default: return Medium;
            }
        }
    }
}
