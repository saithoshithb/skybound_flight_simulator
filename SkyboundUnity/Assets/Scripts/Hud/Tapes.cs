using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    public struct SpeedBands
    {
        public float stallKt;
        /// <summary>Top of normal operating range (green).</summary>
        public float normalMaxKt;
        /// <summary>Never exceed.</summary>
        public float vneKt;
    }

    /// <summary>
    /// Glass-cockpit HUD instruments: airspeed and altitude tapes, a heading ribbon, a vertical-speed
    /// scale and a compact attitude ball. Pure drawing functions; values come straight from the
    /// simulation. The browser fades a tape's ends by masking the finished canvas; nothing here can
    /// mask, so each thing drawn takes its own alpha from where it sits, through Faded.
    /// </summary>
    public static class Tapes
    {
        const float D2R = Mathf.PI / 180;
        const float COMMA_EM = 0.2f;
        /// <summary>A tick's number closer to the pointer box than this would show through it.</summary>
        const float BOX_CLEAR = 24;
        const float BACKING_ALPHA = 0.55f;
        const int HEADING_COLUMNS = 96;
        const float FADE = 0.16f;
        const int FADE_SLICES = 6;

        /// <summary>Opacity at a fraction along the tape: fades out toward the ends.</summary>
        public static float FadeAt(float t)
        {
            if (t < 0 || t > 1) return 0;
            return t < FADE ? t / FADE : t > 1 - FADE ? (1 - t) / FADE : 1;
        }

        static Color Faded(Color color, float t)
        {
            color.a *= FadeAt(t);
            return color;
        }

        /// <summary>A vertical strip whose alpha follows the fade: solid in the middle, in small steps at the ends.</summary>
        static void FadedStrip(Ctx c, float x, float w, float y0, float y1, float h, Color color)
        {
            var solidFrom = Mathf.Clamp(h * FADE, y0, y1);
            var solidTo = Mathf.Clamp(h * (1 - FADE), y0, y1);
            Slices(c, x, w, y0, solidFrom, h, color);
            if (solidTo > solidFrom)
            {
                c.FillStyle(color);
                c.FillRect(x, solidFrom, w, solidTo - solidFrom);
            }
            Slices(c, x, w, solidTo, y1, h, color);
        }

        static void Slices(Ctx c, float x, float w, float y0, float y1, float h, Color color)
        {
            if (y1 <= y0) return;
            var step = (y1 - y0) / FADE_SLICES;
            for (var i = 0; i < FADE_SLICES; i++)
            {
                var y = y0 + i * step;
                c.FillStyle(Faded(color, (y + step / 2) / h));
                c.FillRect(x, y, w, step);
            }
        }

        /// <summary>Dark translucent backing that fades out toward the tape ends. It never changes, so it's drawn once on a layer of its own.</summary>
        public static void TapeBacking(Ctx c, float x, float w, float h)
        {
            var mid = WithAlpha(HudTheme.Backing, BACKING_ALPHA);
            // Solid in the middle, out to nothing by 18% from each end. The mask then fades it too.
            var edge = h * 0.18f;
            for (var i = 0; i < FADE_SLICES; i++)
            {
                var y0 = edge * i / FADE_SLICES;
                var y1 = edge * (i + 1) / FADE_SLICES;
                var a0 = mid.a * (y0 / edge) * FadeAt(y0 / h);
                var a1 = mid.a * (y1 / edge) * FadeAt(y1 / h);
                c.FillLinear(WithAlpha(mid, a0), WithAlpha(mid, a1), x, y0, x, y1);
                c.FillRect(x, y0, w, y1 - y0);
                c.FillLinear(WithAlpha(mid, a1), WithAlpha(mid, a0), x, h - y1, x, h - y0);
                c.FillRect(x, h - y1, w, y1 - y0);
            }
            c.FillStyle(mid);
            c.FillRect(x, edge, w, h - edge * 2);
        }

        static Color WithAlpha(Color color, float a)
        {
            color.a = a;
            return color;
        }

        static void Text(Ctx c, string s, float x, float y, float size, Color color, TextAlign align = TextAlign.Center)
        {
            c.Text(s, x, y, size, color, align, Face.Display);
        }

        /// <summary>A number centred on x whose digits sit in cells of one width, so it doesn't jitter as it counts.</summary>
        static void Tabular(Ctx c, string number, float x, float y, float size, Color color)
        {
            var width = 0f;
            foreach (var ch in number) width += ch == ',' ? size * COMMA_EM : size * TabularLabel.DIGIT_EM;
            var left = x - width / 2;
            foreach (var ch in number)
            {
                var cell = ch == ',' ? size * COMMA_EM : size * TabularLabel.DIGIT_EM;
                c.Text(NumberText.Char(ch), left + cell / 2, y, size, color, TextAlign.Center, Face.Display);
                left += cell;
            }
        }

        /// <summary>Pointer box: rectangle with an arrow toward the side, cyan outline.</summary>
        static void PointerBox(Ctx c, float x, float y, float w, float h, bool arrowRight)
        {
            const float a = 8;
            c.BeginPath();
            if (arrowRight)
            {
                c.MoveTo(x, y - h / 2);
                c.LineTo(x + w, y - h / 2);
                c.LineTo(x + w, y - 5);
                c.LineTo(x + w + a, y);
                c.LineTo(x + w, y + 5);
                c.LineTo(x + w, y + h / 2);
                c.LineTo(x, y + h / 2);
            }
            else
            {
                c.MoveTo(x + w, y - h / 2);
                c.LineTo(x, y - h / 2);
                c.LineTo(x, y - 5);
                c.LineTo(x - a, y);
                c.LineTo(x, y + 5);
                c.LineTo(x, y + h / 2);
                c.LineTo(x + w, y + h / 2);
            }
            c.ClosePath();
            c.FillStyle(HudTheme.Box);
            c.Fill();
            c.StrokeStyle(HudTheme.Accent);
            c.lineWidth = 1.2f;
            c.Stroke();
        }

        // ---------------------------------------------------------------- airspeed
        public static void DrawSpeedTape(Ctx c, float w, float h, float ias, float? targetKt, SpeedBands bands, bool airborne)
        {
            var tapeW = w - 10;
            var pxPerKt = h / 64;
            float YOf(float kt) => h / 2 - (kt - ias) * pxPerKt;
            var right = tapeW;

            // Speed bands on the right edge.
            void Band(float from, float to, Color color)
            {
                var y0 = Mathf.Min(h, Mathf.Max(0, YOf(to)));
                var y1 = Mathf.Min(h, Mathf.Max(0, YOf(from)));
                if (y1 > y0) FadedStrip(c, right - 4, 4, y0, y1, h, color);
            }
            // The low-speed band only means something in flight; on the runway it would just glare.
            if (airborne) Band(-50, bands.stallKt, HudTheme.BandLow);
            Band(bands.stallKt, bands.normalMaxKt, HudTheme.BandNormal);
            Band(bands.normalMaxKt, bands.vneKt, HudTheme.BandHigh);
            Band(bands.vneKt, 400, HudTheme.BandLow);

            var lo = Mathf.FloorToInt((ias - 40) / 5) * 5;
            for (var kt = Mathf.Max(0, lo); kt <= ias + 40; kt += 5)
            {
                var y = YOf(kt);
                var t = y / h;
                var major = kt % 10 == 0;
                c.StrokeStyle(Faded(major ? HudTheme.Ink : HudTheme.Dim, t));
                c.lineWidth = 1.2f;
                c.BeginPath();
                c.MoveTo(right - 6 - (major ? 10 : 6), y);
                c.LineTo(right - 6, y);
                c.Stroke();
                if (major && Mathf.Abs(y - h / 2) > BOX_CLEAR) Text(c, NumberText.Whole(kt), right - 22, y, 15, Faded(HudTheme.Ink, t), TextAlign.Right);
            }

            // Target-speed bug (Easy flight auto-throttle).
            if (targetKt.HasValue)
            {
                var y = Mathf.Min(h - 8, Mathf.Max(8, YOf(targetKt.Value)));
                c.FillStyle(Faded(HudTheme.Accent, y / h));
                c.BeginPath();
                c.MoveTo(right - 1, y);
                c.LineTo(right + 8, y - 6);
                c.LineTo(right + 8, y + 6);
                c.ClosePath();
                c.Fill();
            }

            PointerBox(c, 2, h / 2, tapeW - 16, 30, true);
            Tabular(c, NumberText.Whole(Mathf.Max(0, Mathf.RoundToInt(ias))), 2 + (tapeW - 16) / 2, h / 2 + 1, 22, HudTheme.Ink);
        }

        // ---------------------------------------------------------------- altitude
        public static void DrawAltitudeTape(Ctx c, float w, float h, float altFt, float groundFt)
        {
            var pxPerFt = h / 640;
            float YOf(float ft) => h / 2 - (ft - altFt) * pxPerFt;

            // Terrain below: hatched strip from ground level down.
            var gy = YOf(groundFt);
            if (gy < h)
            {
                FadedStrip(c, 0, 6, Mathf.Max(0, gy), h, h, HudTheme.Ground);
                c.StrokeStyle(Faded(HudTheme.BandHigh, gy / h));
                c.lineWidth = 1.5f;
                c.BeginPath();
                c.MoveTo(0, gy);
                c.LineTo(w, gy);
                c.Stroke();
            }

            var lo = Mathf.FloorToInt((altFt - 360) / 20) * 20;
            for (var ft = lo; ft <= altFt + 360; ft += 20)
            {
                if (ft < groundFt - 10) continue; // nothing to read below the ground
                var y = YOf(ft);
                var t = y / h;
                var major = ft % 100 == 0;
                c.StrokeStyle(Faded(major ? HudTheme.Ink : HudTheme.Dim, t));
                c.lineWidth = 1.2f;
                c.BeginPath();
                c.MoveTo(8, y);
                c.LineTo(8 + (major ? 12 : 6), y);
                c.Stroke();
                if (major && Mathf.Abs(y - h / 2) > BOX_CLEAR) Text(c, NumberText.Thousands(ft), 26, y, 14, Faded(HudTheme.Ink, t), TextAlign.Left);
            }

            PointerBox(c, 12, h / 2, w - 14, 30, false);
            var shown = Mathf.RoundToInt(altFt / 10) * 10;
            Tabular(c, NumberText.Thousands(shown), 12 + (w - 14) / 2, h / 2 + 1, 21, HudTheme.Ink);
        }

        // ---------------------------------------------------------------- vertical speed
        /// <summary>Non-linear scale: fine near zero, ±2,000 fpm at the ends.</summary>
        public static float VsiPos(float fpm) => Mathf.Sign(fpm) * (fpm == 0 ? 0 : Mathf.Sqrt(Mathf.Min(2000, Mathf.Abs(fpm)) / 2000));

        static readonly float[] VSI_MARKS = { -2000, -1000, -500, 0, 500, 1000, 2000 };
        static readonly int[] BALL_LADDER = { -20, -10, 10, 20 };
        static readonly int[] BALL_BANKS = { -45, -30, -20, -10, 0, 10, 20, 30, 45 };

        public static void DrawVsi(Ctx c, float w, float h, float fpm, float? targetFpm)
        {
            var mid = h / 2;
            var half = h / 2 - 10;
            c.FillStyle(WithAlpha(HudTheme.Backing, 0.35f));
            c.FillRect(0, 10, w, h - 20);
            foreach (var v in VSI_MARKS)
            {
                var yy = mid - VsiPos(v) * half;
                c.StrokeStyle(v == 0 ? HudTheme.Ink : HudTheme.Dim);
                c.lineWidth = v == 0 ? 1.5f : 1;
                c.BeginPath();
                c.MoveTo(w - (v == 0 ? 10 : 6), yy);
                c.LineTo(w, yy);
                c.Stroke();
            }
            if (targetFpm.HasValue)
            {
                var ty = mid - VsiPos(targetFpm.Value) * half;
                c.StrokeStyle(WithAlpha(HudTheme.Accent, 0.55f));
                c.lineWidth = 2;
                c.BeginPath();
                c.MoveTo(0, ty);
                c.LineTo(w - 2, ty);
                c.Stroke();
            }
            var y = mid - VsiPos(fpm) * half;
            c.StrokeStyle(HudTheme.Accent);
            c.lineWidth = 3;
            c.lineCap = LineCap.Round;
            c.BeginPath();
            c.MoveTo(w - 1, mid);
            c.LineTo(2, y);
            c.Stroke();
            c.lineCap = LineCap.Butt;
        }

        // ---------------------------------------------------------------- heading
        /// <summary>A vertical backing faded left and right: columns, each its own gradient. Dark enough at the top to read against a bright sky. Drawn once.</summary>
        public static void DrawHeadingBacking(Ctx c, float w, float h)
        {
            var bottom = h - 6;
            for (var i = 0; i < HEADING_COLUMNS; i++)
            {
                var x0 = w * i / HEADING_COLUMNS;
                var x1 = w * (i + 1) / HEADING_COLUMNS;
                var fade = FadeAt((x0 + x1) / 2 / w);
                c.FillLinear(WithAlpha(HudTheme.Backing, 0.35f * fade), WithAlpha(HudTheme.Backing, 0.6f * fade), x0, 0, x0, h);
                c.FillRect(x0, 0, x1 - x0, h);
                c.FillStyle(WithAlpha(HudTheme.Ink, 0.25f * fade));
                c.FillRect(x0, bottom, x1 - x0, 1);
            }
        }

        public static void DrawHeadingTape(Ctx c, float w, float h, float hdg)
        {
            var pxPerDeg = w / 70;
            float XOf(float d) => w / 2 + d * pxPerDeg;
            var bottom = h - 6;
            var start = Mathf.FloorToInt((hdg - 40) / 5) * 5;
            for (var d = start; d <= hdg + 40; d += 5)
            {
                var x = XOf(d - hdg);
                var t = x / w;
                var deg = ((d % 360) + 360) % 360;
                var major = deg % 10 == 0;
                c.StrokeStyle(Faded(major ? HudTheme.Ink : HudTheme.Dim, t));
                c.lineWidth = 1.2f;
                c.BeginPath();
                c.MoveTo(x, bottom);
                c.LineTo(x, bottom - (major ? 10 : 5));
                c.Stroke();
                if (deg % 30 == 0)
                {
                    var card = deg == 0 ? "N" : deg == 90 ? "E" : deg == 180 ? "S" : deg == 270 ? "W" : NumberText.TwoDigit(deg / 10);
                    Text(c, card, x, bottom - 20, card.Length == 1 ? 16 : 14, Faded(card.Length == 1 ? HudTheme.Accent : HudTheme.Ink, t));
                }
            }
            // Lubber pointer.
            c.FillStyle(HudTheme.Accent);
            c.BeginPath();
            c.MoveTo(w / 2, bottom + 2);
            c.LineTo(w / 2 - 5, h);
            c.LineTo(w / 2 + 5, h);
            c.ClosePath();
            c.Fill();
        }

        // ---------------------------------------------------------------- attitude ball
        public static void DrawAttitudeBall(Ctx c, float size, float pitchDeg, float rollDeg)
        {
            var r = size / 2 - 2;
            var cx = size / 2;
            var cy = size / 2;
            c.Save();
            c.Translate(cx, cy);
            c.Rotate(-rollDeg * D2R);
            var pxPerDeg = r / 26;
            var hy = Mathf.Max(-r * 1.8f, Mathf.Min(r * 1.8f, pitchDeg * pxPerDeg));
            // Flat colours: a Painter2D gradient allocates each time it's made, and this redraws every frame.
            c.FillStyle(HudTheme.Sky);
            c.BeginPath();
            c.Arc(0, 0, r, 0, Mathf.PI * 2);
            c.Fill();
            c.FillStyle(HudTheme.Earth);
            Gauges.FillBelowChord(c, r, hy);
            c.StrokeStyle(HudTheme.Ink);
            c.lineWidth = 1.5f;
            Gauges.LineInCircle(c, r, hy, r * 2);
            c.lineWidth = 1;
            foreach (var p in BALL_LADDER)
            {
                var y = hy - p * pxPerDeg;
                var half = Math.Abs(p) == 10 ? r * 0.28f : r * 0.42f;
                c.StrokeStyle(WithAlpha(HudTheme.Ink, 0.8f));
                Gauges.LineInCircle(c, r, y, half);
            }
            c.Restore();
            // Bank scale (fixed) and pointer (rotates).
            foreach (var b in BALL_BANKS)
            {
                var a = (-90 + b) * D2R;
                var big = b == 0 || Math.Abs(b) == 30;
                c.StrokeStyle(WithAlpha(HudTheme.Ink, 0.85f));
                c.lineWidth = big ? 1.6f : 1;
                c.BeginPath();
                c.MoveTo(cx + Mathf.Cos(a) * (r - (big ? 9 : 6)), cy + Mathf.Sin(a) * (r - (big ? 9 : 6)));
                c.LineTo(cx + Mathf.Cos(a) * (r - 1), cy + Mathf.Sin(a) * (r - 1));
                c.Stroke();
            }
            c.Save();
            c.Translate(cx, cy);
            c.Rotate(-rollDeg * D2R);
            c.FillStyle(HudTheme.Accent);
            c.BeginPath();
            c.MoveTo(0, -r + 10);
            c.LineTo(-5, -r + 18);
            c.LineTo(5, -r + 18);
            c.ClosePath();
            c.Fill();
            c.Restore();
            // Fixed aircraft symbol.
            c.StrokeStyle(HudTheme.Accent);
            c.lineWidth = 2.6f;
            c.lineCap = LineCap.Round;
            c.BeginPath();
            c.MoveTo(cx - r * 0.5f, cy);
            c.LineTo(cx - r * 0.16f, cy);
            c.LineTo(cx - r * 0.08f, cy + r * 0.08f);
            c.MoveTo(cx + r * 0.5f, cy);
            c.LineTo(cx + r * 0.16f, cy);
            c.LineTo(cx + r * 0.08f, cy + r * 0.08f);
            c.Stroke();
            c.lineCap = LineCap.Butt;
            c.FillStyle(HudTheme.Accent);
            c.BeginPath();
            c.Arc(cx, cy, 2.2f, 0, Mathf.PI * 2);
            c.Fill();
            // Thin bezel.
            c.StrokeStyle(HudTheme.Bezel);
            c.lineWidth = 1;
            c.BeginPath();
            c.Arc(cx, cy, r + 1, 0, Mathf.PI * 2);
            c.Stroke();
        }
    }
}
