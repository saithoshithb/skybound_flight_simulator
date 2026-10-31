using UnityEngine;

namespace Skybound
{
    /// <summary>The curve in Resources/Shaders/ThreeColor.hlsl, for checking rendered colours in tests.</summary>
    public static class ThreeToneMapping
    {
        const float EXPOSURE = 0.95f;

        static float Fit(float v)
        {
            return (v * (v + 0.0245786f) - 0.000090537f) / (v * (0.983729f * v + 0.4329510f) + 0.238081f);
        }

        /// <summary>The scene colour that tone-maps to this display colour, for things three.js leaves out of tone mapping.</summary>
        public static Color Inverse(Color display)
        {
            var c = display;
            for (var i = 0; i < 32; i++)
            {
                var mapped = Aces(c);
                c = new Color(c.r * display.r / Mathf.Max(mapped.r, 1e-4f), c.g * display.g / Mathf.Max(mapped.g, 1e-4f), c.b * display.b / Mathf.Max(mapped.b, 1e-4f));
            }
            return c;
        }

        /// <summary>Linear scene colour to linear display colour.</summary>
        public static Color Aces(Color c)
        {
            var scale = EXPOSURE / 0.6f;
            var r = c.r * scale;
            var g = c.g * scale;
            var b = c.b * scale;
            var ir = Fit(0.59719f * r + 0.35458f * g + 0.04823f * b);
            var ig = Fit(0.07600f * r + 0.90834f * g + 0.01566f * b);
            var ib = Fit(0.02840f * r + 0.13383f * g + 0.83777f * b);
            return new Color(
                Mathf.Clamp01(1.60475f * ir - 0.53108f * ig - 0.07367f * ib),
                Mathf.Clamp01(-0.10208f * ir + 1.10813f * ig - 0.00605f * ib),
                Mathf.Clamp01(-0.00327f * ir - 0.07276f * ig + 1.07602f * ib));
        }
    }
}
