using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>
    /// The colours of what the HUD draws in code. They live in Skybound.uss with all the other
    /// colours, in the .hud-theme rule, so a restyle means editing that one file. The values here are
    /// only what shows until the stylesheet has been applied.
    /// </summary>
    public static class HudTheme
    {
        public static Color Ink = Three.Hex("#eef3f8");
        public static Color Dim = new Color(238 / 255f, 243 / 255f, 248 / 255f, 0.55f);
        public static Color Accent = Three.Hex("#4fd6ff");
        public static Color Box = new Color(6 / 255f, 11 / 255f, 19 / 255f, 0.86f);
        public static Color Backing = new Color(6 / 255f, 10 / 255f, 18 / 255f, 1);
        public static Color BandLow = new Color(255 / 255f, 90 / 255f, 79 / 255f, 0.85f);
        public static Color BandNormal = new Color(79 / 255f, 209 / 255f, 139 / 255f, 0.75f);
        public static Color BandHigh = new Color(242 / 255f, 177 / 255f, 61 / 255f, 0.85f);
        public static Color Ground = new Color(120 / 255f, 84 / 255f, 48 / 255f, 0.45f);
        public static Color Sky = Three.Hex("#2a6db5");
        public static Color Earth = Three.Hex("#6b4a2b");
        public static Color Bezel = new Color(120 / 255f, 190 / 255f, 1, 0.35f);
        public static Color MapLow = Three.Hex("#1d3a33");
        public static Color MapHigh = Three.Hex("#5f6a58");
        public static Color MapContour = Three.Hex("#7f9a8c");

        const int COLOUR_COUNT = 15;

        static int found;

        static void Read(ICustomStyle style, string name, ref Color colour)
        {
            if (!style.TryGetValue(new CustomStyleProperty<Color>(name), out var value)) return;
            colour = value;
            found++;
        }

        /// <summary>True when the stylesheet supplied every colour.</summary>
        public static bool Apply(ICustomStyle style)
        {
            found = 0;
            Read(style, "--hud-ink", ref Ink);
            Read(style, "--hud-dim", ref Dim);
            Read(style, "--hud-accent", ref Accent);
            Read(style, "--hud-box", ref Box);
            Read(style, "--hud-backing", ref Backing);
            Read(style, "--hud-band-low", ref BandLow);
            Read(style, "--hud-band-normal", ref BandNormal);
            Read(style, "--hud-band-high", ref BandHigh);
            Read(style, "--hud-ground", ref Ground);
            Read(style, "--hud-sky", ref Sky);
            Read(style, "--hud-earth", ref Earth);
            Read(style, "--hud-bezel", ref Bezel);
            Read(style, "--hud-map-low", ref MapLow);
            Read(style, "--hud-map-high", ref MapHigh);
            Read(style, "--hud-map-contour", ref MapContour);
            return found == COLOUR_COUNT;
        }
    }
}
