using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>
    /// Round-dial flight instruments. Each draw function takes the gauge centre and radius plus the
    /// value(s) it shows. Static parts (faces, scales) are drawn once; only needles and moving cards
    /// redraw. Markings follow the conventions of a light trainer's "six-pack".
    /// </summary>
    public static class Gauges
    {
        static readonly Color FACE = Three.Hex("#141516");
        static readonly Color FACE_GLOW = Three.Hex("#1d1f20");
        static readonly Color BEZEL = Three.Hex("#2b2d2f");
        static readonly Color SCREW = Three.Hex("#4a4c4e");
        static readonly Color INK = Three.Hex("#ecebe6");
        static readonly Color DIM = Three.Hex("#9a9b97");
        static readonly Color NEEDLE = Three.Hex("#f4f3ee");
        static readonly Color HUB = Three.Hex("#3a3c3e");
        static readonly Color SKY = Three.Hex("#3d76b0");
        static readonly Color EARTH = Three.Hex("#7a5532");
        static readonly Color AMBER = Three.Hex("#f0a43a");
        static readonly Color WHITE_ARC = Three.Hex("#e8e8e2");
        static readonly Color GREEN = Three.Hex("#3f9a52");
        static readonly Color YELLOW = Three.Hex("#d6b23c");
        static readonly Color RED = Three.Hex("#d24a3a");
        const float D2R = Mathf.PI / 180;
        const float TWO_PI = Mathf.PI * 2;

        public const float WHITE_LOW = 45, WHITE_HIGH = 85, GREEN_LOW = 51, GREEN_HIGH = 129, RED_LINE = 163;

        static readonly float[] SCREWS = { 45, 135, 225, 315 };
        static readonly int[] BANK_MARKS = { -60, -45, -30, -20, -10, 0, 10, 20, 30, 45, 60 };

        static void Face(Ctx c, float x, float y, float r)
        {
            c.FillStyle(BEZEL);
            c.BeginPath();
            c.Arc(x, y, r, 0, TWO_PI);
            c.Fill();
            c.FillRadial(FACE_GLOW, FACE, x, y, r * 0.92f, x, y - r * 0.3f);
            c.BeginPath();
            c.Arc(x, y, r * 0.9f, 0, TWO_PI);
            c.Fill();
            // mounting screws
            c.FillStyle(SCREW);
            foreach (var a in SCREWS)
            {
                c.BeginPath();
                c.Arc(x + Mathf.Cos(a * D2R) * r * 0.965f, y + Mathf.Sin(a * D2R) * r * 0.965f, r * 0.025f, 0, TWO_PI);
                c.Fill();
            }
        }

        static void Tick(Ctx c, float x, float y, float r0, float r1, float angle, float width, Color? color = null)
        {
            c.StrokeStyle(color ?? INK);
            c.lineWidth = width;
            c.BeginPath();
            c.MoveTo(x + Mathf.Cos(angle) * r0, y + Mathf.Sin(angle) * r0);
            c.LineTo(x + Mathf.Cos(angle) * r1, y + Mathf.Sin(angle) * r1);
            c.Stroke();
        }

        static void Label(Ctx c, string text, float x, float y, float size, Color? color = null, bool semibold = true)
        {
            c.Text(text, x, y, size, color ?? INK, TextAlign.Center, semibold ? Skybound.Face.Bold : Skybound.Face.Body);
        }

        static void Needle(Ctx c, float x, float y, float angle, float length, float width, float tail = 0.18f)
        {
            c.Save();
            c.Translate(x, y);
            c.Rotate(angle);
            c.FillStyle(NEEDLE);
            c.BeginPath();
            c.MoveTo(-length * tail, -width * 0.6f);
            c.LineTo(length * 0.82f, -width * 0.5f);
            c.LineTo(length, 0);
            c.LineTo(length * 0.82f, width * 0.5f);
            c.LineTo(-length * tail, width * 0.6f);
            c.ClosePath();
            c.Fill();
            c.Restore();
            c.FillStyle(HUB);
            c.BeginPath();
            c.Arc(x, y, width * 1.3f, 0, TWO_PI);
            c.Fill();
        }

        static void ArcStroke(Ctx c, float x, float y, float r, float a0, float a1, float width, Color color)
        {
            c.StrokeStyle(color);
            c.lineWidth = width;
            c.BeginPath();
            c.Arc(x, y, r, a0, a1);
            c.Stroke();
        }

        // ---------------------------------------------------------------- airspeed
        const float ASI_MIN = 40;
        const float ASI_MAX = 200;

        /// <summary>Needle angle for a speed: 40 kt at 12 o'clock, sweeping 330° clockwise.</summary>
        public static float AsiAngle(float kt) => (-90 + (Mathf.Min(Mathf.Max(kt, ASI_MIN - 6), ASI_MAX) - ASI_MIN) * (330 / (ASI_MAX - ASI_MIN))) * D2R;

        public static void DrawAirspeedFace(Ctx c, float x, float y, float r)
        {
            Face(c, x, y, r);
            var R = r * 0.82f;
            ArcStroke(c, x, y, R * 0.93f, AsiAngle(WHITE_LOW), AsiAngle(WHITE_HIGH), r * 0.035f, WHITE_ARC);
            ArcStroke(c, x, y, R, AsiAngle(GREEN_LOW), AsiAngle(GREEN_HIGH), r * 0.05f, GREEN);
            ArcStroke(c, x, y, R, AsiAngle(GREEN_HIGH), AsiAngle(RED_LINE), r * 0.05f, YELLOW);
            Tick(c, x, y, R * 0.9f, R * 1.04f, AsiAngle(RED_LINE), r * 0.03f, RED);
            for (var kt = (int)ASI_MIN; kt <= ASI_MAX; kt += 10)
            {
                var big = kt % 20 == 0;
                Tick(c, x, y, R * (big ? 0.82f : 0.88f), R * 0.97f, AsiAngle(kt), big ? r * 0.022f : r * 0.012f);
                if (big) Label(c, kt.ToString(), x + Mathf.Cos(AsiAngle(kt)) * R * 0.66f, y + Mathf.Sin(AsiAngle(kt)) * R * 0.66f, r * 0.13f);
            }
            Label(c, "AIRSPEED", x, y - r * 0.28f, r * 0.08f, DIM, false);
            Label(c, "KNOTS", x, y + r * 0.3f, r * 0.08f, DIM, false);
        }

        public static void DrawAirspeedNeedle(Ctx c, float x, float y, float r, float kt)
        {
            Needle(c, x, y, AsiAngle(kt), r * 0.74f, r * 0.06f);
        }

        // ---------------------------------------------------------------- attitude
        /// <summary>The part of a circle below a horizontal line, in the current rotated frame.</summary>
        public static void FillBelowChord(Ctx c, float radius, float lineY)
        {
            if (lineY >= radius) return;
            c.BeginPath();
            if (lineY <= -radius)
            {
                c.Arc(0, 0, radius, 0, TWO_PI);
            }
            else
            {
                var a = Mathf.Asin(lineY / radius);
                c.Arc(0, 0, radius, a, Mathf.PI - a);
                c.ClosePath();
            }
            c.Fill();
        }

        /// <summary>A horizontal line trimmed to the circle, where the browser game clips.</summary>
        public static void LineInCircle(Ctx c, float radius, float lineY, float halfWidth)
        {
            if (Mathf.Abs(lineY) >= radius) return;
            var half = Mathf.Min(halfWidth, Mathf.Sqrt(radius * radius - lineY * lineY));
            c.BeginPath();
            c.MoveTo(-half, lineY);
            c.LineTo(half, lineY);
            c.Stroke();
        }

        public static void DrawAttitude(Ctx c, float x, float y, float r, float pitchDeg, float rollDeg)
        {
            Face(c, x, y, r);
            var R = r * 0.88f;
            var pxPerDeg = R / 28;
            c.Save();
            c.Translate(x, y);
            c.Rotate(-rollDeg * D2R);
            var horizon = Mathf.Max(-R * 1.6f, Mathf.Min(R * 1.6f, pitchDeg * pxPerDeg));
            c.FillStyle(SKY);
            c.BeginPath();
            c.Arc(0, 0, R, 0, TWO_PI);
            c.Fill();
            c.FillStyle(EARTH);
            FillBelowChord(c, R, horizon);
            c.StrokeStyle(INK);
            c.lineWidth = r * 0.018f;
            LineInCircle(c, R, horizon, R * 2);
            // pitch ladder every 5°, labelled every 10°
            for (var p = -30; p <= 30; p += 5)
            {
                if (p == 0) continue;
                var yy = horizon - p * pxPerDeg;
                if (Mathf.Abs(yy) > R * 0.75f) continue;
                var w = p % 10 == 0 ? R * 0.28f : R * 0.14f;
                c.lineWidth = r * 0.012f;
                c.BeginPath();
                c.MoveTo(-w, yy);
                c.LineTo(w, yy);
                c.Stroke();
                if (p % 10 == 0)
                {
                    var text = NumberText.Whole(Mathf.Abs(p));
                    Label(c, text, -w - R * 0.1f, yy, r * 0.075f);
                    Label(c, text, w + R * 0.1f, yy, r * 0.075f);
                }
            }
            c.Restore();
            // fixed bank scale and pointer
            foreach (var b in BANK_MARKS)
            {
                var a = (-90 + b) * D2R;
                var big = b == 0 || Mathf.Abs(b) == 30 || Mathf.Abs(b) == 60;
                Tick(c, x, y, R * (big ? 0.84f : 0.89f), R * 0.98f, a, big ? r * 0.024f : r * 0.014f);
            }
            c.Save();
            c.Translate(x, y);
            c.Rotate(-rollDeg * D2R);
            c.FillStyle(AMBER);
            c.BeginPath();
            c.MoveTo(0, -R * 0.83f);
            c.LineTo(-R * 0.05f, -R * 0.74f);
            c.LineTo(R * 0.05f, -R * 0.74f);
            c.ClosePath();
            c.Fill();
            c.Restore();
            // miniature aircraft (fixed)
            c.StrokeStyle(AMBER);
            c.lineWidth = r * 0.04f;
            c.lineCap = LineCap.Round;
            c.BeginPath();
            c.MoveTo(x - R * 0.42f, y);
            c.LineTo(x - R * 0.14f, y);
            c.LineTo(x - R * 0.07f, y + R * 0.07f);
            c.MoveTo(x + R * 0.42f, y);
            c.LineTo(x + R * 0.14f, y);
            c.LineTo(x + R * 0.07f, y + R * 0.07f);
            c.Stroke();
            c.FillStyle(AMBER);
            c.BeginPath();
            c.Arc(x, y, r * 0.03f, 0, TWO_PI);
            c.Fill();
            c.lineCap = LineCap.Butt;
        }

        // ---------------------------------------------------------------- altimeter
        public static void DrawAltimeterFace(Ctx c, float x, float y, float r)
        {
            Face(c, x, y, r);
            var R = r * 0.82f;
            for (var i = 0; i < 50; i++)
            {
                var a = (-90 + i * 7.2f) * D2R;
                var big = i % 5 == 0;
                Tick(c, x, y, R * (big ? 0.82f : 0.9f), R * 0.98f, a, big ? r * 0.022f : r * 0.011f);
                if (big) Label(c, (i / 5).ToString(), x + Mathf.Cos(a) * R * 0.66f, y + Mathf.Sin(a) * R * 0.66f, r * 0.15f);
            }
            Label(c, "ALT", x, y - r * 0.3f, r * 0.08f, DIM, false);
            Label(c, "FEET", x, y + r * 0.34f, r * 0.075f, DIM, false);
            // Digital counter for readability, like a modern altimeter's drum.
            c.FillStyle(Three.Hex("#0b0c0c"));
            c.FillRect(x + r * 0.14f, y - r * 0.09f, r * 0.5f, r * 0.18f);
        }

        /// <summary>Drawn on a layer above the counter's digits, which are text and always top their own layer.</summary>
        public static void DrawAltimeterCounter(Ctx c, float x, float y, float r, float feet)
        {
            var ft = Mathf.Max(0, feet);
            Label(c, NumberText.Whole(Mathf.RoundToInt(ft / 10) * 10), x + r * 0.39f, y + r * 0.005f, r * 0.12f);
        }

        public static void DrawAltimeterNeedles(Ctx c, float x, float y, float r, float feet)
        {
            var ft = Mathf.Max(0, feet);
            Needle(c, x, y, (-90 + ((ft / 10000) % 1) * 360) * D2R, r * 0.46f, r * 0.075f, 0.1f);
            Needle(c, x, y, (-90 + ((ft / 1000) % 1) * 360) * D2R, r * 0.76f, r * 0.05f);
        }

        // ------------------------------------------------------- turn coordinator
        public static void DrawTurnCoordinatorFace(Ctx c, float x, float y, float r)
        {
            Face(c, x, y, r);
            var R = r * 0.82f;
            // standard-rate (3°/s) marks at ±20° below the wings-level marks
            foreach (var a in new[] { 180f, 0, 160, 20 }) Tick(c, x, y, R * 0.78f, R * 0.95f, a * D2R, r * 0.03f);
            Label(c, "L", x - R * 0.72f, y + R * 0.42f, r * 0.12f);
            Label(c, "R", x + R * 0.72f, y + R * 0.42f, r * 0.12f);
            Label(c, "TURN COORDINATOR", x, y - r * 0.42f, r * 0.065f, DIM, false);
            Label(c, "2 MIN", x, y + r * 0.66f, r * 0.07f, DIM, false);
            // ball tube
            ArcStroke(c, x, y - R * 1.15f, R * 1.55f, (90 - 16) * D2R, (90 + 16) * D2R, r * 0.14f, Three.Hex("#3c3e40"));
            c.StrokeStyle(INK);
            c.lineWidth = r * 0.012f;
            foreach (var dx in new[] { -0.08f, 0.08f })
            {
                c.BeginPath();
                c.MoveTo(x + R * dx, y + R * 0.3f);
                c.LineTo(x + R * dx, y + R * 0.47f);
                c.Stroke();
            }
        }

        public static void DrawTurnCoordinatorDynamic(Ctx c, float x, float y, float r, float turnRateDps, float lateralG)
        {
            var R = r * 0.82f;
            // Aircraft symbol: 3°/s turn = 20° deflection; limited at ~2× standard rate.
            var deflect = Mathf.Max(-40, Mathf.Min(40, (turnRateDps / 3) * 20)) * D2R;
            c.Save();
            c.Translate(x, y);
            c.Rotate(deflect);
            c.StrokeStyle(NEEDLE);
            c.FillStyle(NEEDLE);
            c.lineWidth = r * 0.05f;
            c.lineCap = LineCap.Round;
            c.BeginPath();
            c.MoveTo(-R * 0.72f, 0);
            c.LineTo(R * 0.72f, 0);
            c.MoveTo(0, -R * 0.16f);
            c.LineTo(0, 0);
            c.MoveTo(-R * 0.18f, R * 0.12f);
            c.LineTo(R * 0.18f, R * 0.12f);
            c.Stroke();
            c.BeginPath();
            c.Arc(0, 0, r * 0.07f, 0, TWO_PI);
            c.Fill();
            c.Restore();
            c.lineCap = LineCap.Butt;
            // Slip ball: thrown opposite to the lateral acceleration.
            var off = Mathf.Max(-1, Mathf.Min(1, -lateralG / 0.15f));
            var a = (90 - off * 13) * D2R;
            c.FillStyle(Three.Hex("#0a0a0a"));
            c.BeginPath();
            c.Arc(x + Mathf.Cos(a) * R * 1.55f, y - R * 1.15f + Mathf.Sin(a) * R * 1.55f, r * 0.06f, 0, TWO_PI);
            c.Fill();
            c.StrokeStyle(Three.Hex("#6a6c6e"));
            c.lineWidth = 1;
            c.Stroke();
        }

        // ------------------------------------------------------- heading indicator
        public static void DrawHeading(Ctx c, float x, float y, float r, float headingDeg)
        {
            Face(c, x, y, r);
            var R = r * 0.84f;
            c.Save();
            c.Translate(x, y);
            c.Rotate(-headingDeg * D2R);
            for (var d = 0; d < 360; d += 5)
            {
                var a = (d - 90) * D2R;
                var big = d % 30 == 0;
                var mid = d % 10 == 0;
                Tick(c, 0, 0, R * (big ? 0.8f : mid ? 0.85f : 0.9f), R * 0.98f, a, big ? r * 0.022f : r * 0.011f);
                if (big)
                {
                    var cardinal = d % 90 == 0;
                    var txt = d == 0 ? "N" : d == 90 ? "E" : d == 180 ? "S" : d == 270 ? "W" : NumberText.Whole(d / 10);
                    c.Save();
                    c.Rotate(d * D2R);
                    Label(c, txt, 0, -R * 0.64f, r * (cardinal ? 0.15f : 0.12f), cardinal ? AMBER : INK);
                    c.Restore();
                }
            }
            c.Restore();
            // lubber line and aircraft silhouette (fixed)
            c.FillStyle(AMBER);
            c.BeginPath();
            c.MoveTo(x, y - R * 0.99f);
            c.LineTo(x - r * 0.05f, y - R * 1.08f);
            c.LineTo(x + r * 0.05f, y - R * 1.08f);
            c.ClosePath();
            c.Fill();
            c.StrokeStyle(AMBER);
            c.lineWidth = r * 0.035f;
            c.lineCap = LineCap.Round;
            c.BeginPath();
            c.MoveTo(x, y - R * 0.3f);
            c.LineTo(x, y + R * 0.3f);
            c.MoveTo(x - R * 0.28f, y - R * 0.02f);
            c.LineTo(x + R * 0.28f, y - R * 0.02f);
            c.MoveTo(x - R * 0.11f, y + R * 0.25f);
            c.LineTo(x + R * 0.11f, y + R * 0.25f);
            c.Stroke();
            c.lineCap = LineCap.Butt;
        }

        // ------------------------------------------------------- vertical speed
        const float VSI_MAX = 2000;

        /// <summary>0 fpm at 9 o'clock; climb swings the needle up (clockwise), descent down.</summary>
        public static float VsiAngle(float fpm) => (180 + (Mathf.Max(-VSI_MAX, Mathf.Min(VSI_MAX, fpm)) / VSI_MAX) * 170) * D2R;

        public static void DrawVsiFace(Ctx c, float x, float y, float r)
        {
            Face(c, x, y, r);
            var R = r * 0.82f;
            for (var v = -2000; v <= 2000; v += 100)
            {
                var big = v % 500 == 0;
                Tick(c, x, y, R * (big ? 0.82f : 0.9f), R * 0.98f, VsiAngle(v), big ? r * 0.022f : r * 0.011f);
                if (big && v % 1000 == 0 && v != 0) Label(c, (Mathf.Abs(v) / 100).ToString(), x + Mathf.Cos(VsiAngle(v)) * R * 0.66f, y + Mathf.Sin(VsiAngle(v)) * R * 0.66f, r * 0.14f);
                if (big && Mathf.Abs(v) == 500) Label(c, "5", x + Mathf.Cos(VsiAngle(v)) * R * 0.66f, y + Mathf.Sin(VsiAngle(v)) * R * 0.66f, r * 0.12f);
            }
            Label(c, "0", x - R * 0.66f, y, r * 0.14f);
            Label(c, "UP", x - R * 0.25f, y - R * 0.32f, r * 0.08f, DIM, false);
            Label(c, "DOWN", x - R * 0.25f, y + R * 0.32f, r * 0.08f, DIM, false);
            Label(c, "VERTICAL SPEED", x + R * 0.2f, y - r * 0.12f, r * 0.062f, DIM, false);
            Label(c, "100 FT/MIN", x + R * 0.2f, y + r * 0.12f, r * 0.062f, DIM, false);
        }

        public static void DrawVsiNeedle(Ctx c, float x, float y, float r, float fpm)
        {
            Needle(c, x, y, VsiAngle(fpm), r * 0.74f, r * 0.06f);
        }

        // ------------------------------------------------------- tachometer
        public static float TachAngle(float rpm) => (135 + (Mathf.Max(0, Mathf.Min(3000, rpm)) / 3000) * 270) * D2R;

        public static void DrawTachFace(Ctx c, float x, float y, float r)
        {
            Face(c, x, y, r);
            var R = r * 0.82f;
            ArcStroke(c, x, y, R, TachAngle(2100), TachAngle(2700), r * 0.05f, GREEN);
            Tick(c, x, y, R * 0.88f, R * 1.03f, TachAngle(2700), r * 0.03f, RED);
            for (var rpm = 0; rpm <= 3000; rpm += 100)
            {
                var big = rpm % 500 == 0;
                Tick(c, x, y, R * (big ? 0.82f : 0.9f), R * 0.98f, TachAngle(rpm), big ? r * 0.022f : r * 0.011f);
                if (rpm % 500 == 0) Label(c, (rpm / 100).ToString(), x + Mathf.Cos(TachAngle(rpm)) * R * 0.66f, y + Mathf.Sin(TachAngle(rpm)) * R * 0.66f, r * 0.12f);
            }
            Label(c, "RPM", x, y - r * 0.28f, r * 0.08f, DIM, false);
            Label(c, "×100", x, y + r * 0.42f, r * 0.075f, DIM, false);
        }

        public static void DrawTachNeedle(Ctx c, float x, float y, float r, float rpm)
        {
            Needle(c, x, y, TachAngle(rpm), r * 0.74f, r * 0.06f);
        }
    }
}
