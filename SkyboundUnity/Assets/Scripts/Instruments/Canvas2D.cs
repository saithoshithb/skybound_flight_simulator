using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    public enum TextAlign
    {
        Left,
        Center,
        Right,
    }

    public enum Face
    {
        Body,
        Bold,
        Display,
    }

    /// <summary>
    /// The parts of the browser's Canvas 2D that the instruments use, so gauges.ts and tapes.ts port
    /// line by line. Painter2D can only be used while an element repaints and can't rotate or clip,
    /// so calls are recorded with their transform applied and replayed at repaint. Text goes to
    /// pooled Labels, because rotated text is something only an element's transform can do. Clipping
    /// isn't here at all: the two round instruments work out their shapes inside the circle.
    /// </summary>
    public sealed class Ctx
    {
        enum SegmentKind
        {
            Move,
            Line,
            Arc,
            Bezier,
            Close,
        }

        struct Segment
        {
            public SegmentKind kind;
            public Vector2 point;
            public float radius, from, to;
            public Vector2 control1, control2;
        }

        struct Paint
        {
            public bool stroke;
            public int firstSegment, lastSegment;
            public Color color;
            public float width;
            public LineCap cap;
            public bool hasGradient;
            public FillGradient gradient;
        }

        struct Transform2D
        {
            public Vector2 origin;
            public float angle;
        }

        readonly List<Segment> segments = new List<Segment>();
        readonly List<Paint> paints = new List<Paint>();
        readonly Stack<Transform2D> stack = new Stack<Transform2D>();
        readonly CanvasElement owner;
        Transform2D t;
        int pathStart;
        Color fill = Color.black;
        Color stroke = Color.black;
        bool hasGradient;
        FillGradient gradient;

        /// <summary>Drawing units per pixel-free unit: icons and silhouettes are written on their own small grids.</summary>
        public float unit = 1;
        public float lineWidth = 1;
        public LineCap lineCap = LineCap.Butt;

        public Ctx(CanvasElement element)
        {
            owner = element;
        }

        public void Reset()
        {
            segments.Clear();
            paints.Clear();
            stack.Clear();
            t = default;
            pathStart = 0;
            lineWidth = 1;
            lineCap = LineCap.Butt;
            unit = 1;
            hasGradient = false;
        }

        public Vector2 ToScreen(float x, float y)
        {
            var cos = Mathf.Cos(t.angle);
            var sin = Mathf.Sin(t.angle);
            x *= unit;
            y *= unit;
            return new Vector2(t.origin.x + x * cos - y * sin, t.origin.y + x * sin + y * cos);
        }

        public void Save() => stack.Push(t);
        public void Restore() => t = stack.Pop();
        public void Translate(float x, float y) => t.origin = ToScreen(x, y);
        public void Rotate(float radians) => t.angle += radians;

        public void FillStyle(Color color)
        {
            fill = color;
            hasGradient = false;
        }

        /// <summary>The two-colour gradient calls drop alpha, so anything translucent goes through a Gradient object.</summary>
        static Gradient WithAlpha(Color from, Color to)
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(from, 0), new GradientColorKey(to, 1) },
                new[] { new GradientAlphaKey(from.a, 0), new GradientAlphaKey(to.a, 1) });
            return g;
        }

        public void FillLinear(Color from, Color to, float x0, float y0, float x1, float y1)
        {
            hasGradient = true;
            gradient = from.a >= 1 && to.a >= 1
                ? FillGradient.MakeLinearGradient(from, to, ToScreen(x0, y0), ToScreen(x1, y1), AddressMode.Clamp)
                : FillGradient.MakeLinearGradient(WithAlpha(from, to), ToScreen(x0, y0), ToScreen(x1, y1), AddressMode.Clamp);
        }

        public void FillRadial(Color from, Color to, float x, float y, float radius, float focusX, float focusY)
        {
            hasGradient = true;
            gradient = from.a >= 1 && to.a >= 1
                ? FillGradient.MakeRadialGradient(from, to, ToScreen(x, y), radius * unit, ToScreen(focusX, focusY), AddressMode.Clamp)
                : FillGradient.MakeRadialGradient(WithAlpha(from, to), ToScreen(x, y), radius * unit, ToScreen(focusX, focusY), AddressMode.Clamp);
        }

        public void StrokeStyle(Color color) => stroke = color;

        public void BeginPath() => pathStart = segments.Count;

        public void MoveTo(float x, float y) => segments.Add(new Segment { kind = SegmentKind.Move, point = ToScreen(x, y) });
        public void LineTo(float x, float y) => segments.Add(new Segment { kind = SegmentKind.Line, point = ToScreen(x, y) });
        public void ClosePath() => segments.Add(new Segment { kind = SegmentKind.Close });

        /// <summary>Angles in radians, clockwise on screen, as in Canvas 2D.</summary>
        public void Arc(float x, float y, float radius, float from, float to)
        {
            segments.Add(new Segment { kind = SegmentKind.Arc, point = ToScreen(x, y), radius = radius * unit, from = from + t.angle, to = to + t.angle });
        }

        public void BezierCurveTo(float c1x, float c1y, float c2x, float c2y, float x, float y)
        {
            segments.Add(new Segment { kind = SegmentKind.Bezier, control1 = ToScreen(c1x, c1y), control2 = ToScreen(c2x, c2y), point = ToScreen(x, y) });
        }

        /// <summary>The browser's roundRect, for icons.</summary>
        public void RoundRect(float x, float y, float w, float h, float r)
        {
            MoveTo(x + r, y);
            LineTo(x + w - r, y);
            Arc(x + w - r, y + r, r, -Mathf.PI / 2, 0);
            LineTo(x + w, y + h - r);
            Arc(x + w - r, y + h - r, r, 0, Mathf.PI / 2);
            LineTo(x + r, y + h);
            Arc(x + r, y + h - r, r, Mathf.PI / 2, Mathf.PI);
            LineTo(x, y + r);
            Arc(x + r, y + r, r, Mathf.PI, Mathf.PI * 1.5f);
            ClosePath();
        }

        public void Fill()
        {
            paints.Add(new Paint { firstSegment = pathStart, lastSegment = segments.Count, color = fill, hasGradient = hasGradient, gradient = gradient });
        }

        public void Stroke()
        {
            paints.Add(new Paint { stroke = true, firstSegment = pathStart, lastSegment = segments.Count, color = stroke, width = lineWidth, cap = lineCap });
        }

        public void FillRect(float x, float y, float w, float h)
        {
            BeginPath();
            MoveTo(x, y);
            LineTo(x + w, y);
            LineTo(x + w, y + h);
            LineTo(x, y + h);
            ClosePath();
            Fill();
        }

        public void StrokeRect(float x, float y, float w, float h)
        {
            BeginPath();
            MoveTo(x, y);
            LineTo(x + w, y);
            LineTo(x + w, y + h);
            LineTo(x, y + h);
            ClosePath();
            Stroke();
        }

        public void Text(string text, float x, float y, float size, Color color, TextAlign align = TextAlign.Center, Face face = Face.Bold)
        {
            owner.PlaceText(text, ToScreen(x, y), size, color, align, t.angle * Mathf.Rad2Deg, face);
        }

        public void Replay(Painter2D p)
        {
            foreach (var paint in paints)
            {
                if (paint.stroke)
                {
                    p.strokeColor = paint.color;
                    p.lineWidth = paint.width;
                    p.lineCap = paint.cap;
                }
                else if (paint.hasGradient) p.fillGradient = paint.gradient;
                else
                {
                    p.fillGradient = default;
                    p.fillColor = paint.color;
                }
                p.BeginPath();
                for (var i = paint.firstSegment; i < paint.lastSegment; i++)
                {
                    var s = segments[i];
                    switch (s.kind)
                    {
                        case SegmentKind.Move: p.MoveTo(s.point); break;
                        case SegmentKind.Line: p.LineTo(s.point); break;
                        case SegmentKind.Arc: p.Arc(s.point, s.radius, Angle.Radians(s.from), Angle.Radians(s.to)); break;
                        case SegmentKind.Bezier: p.BezierCurveTo(s.control1, s.control2, s.point); break;
                        default: p.ClosePath(); break;
                    }
                }
                if (paint.stroke) p.Stroke();
                else p.Fill();
            }
        }
    }

    /// <summary>
    /// One browser canvas. Begin hands back the context and End shows what was drawn on it, so a
    /// face drawn once costs nothing afterwards, like the browser's cached face canvas.
    /// </summary>
    public class CanvasElement : VisualElement
    {
        const float TEXT_BOX_WIDTH = 240;

        sealed class TextSlot
        {
            public Label label;
            public string text;
            public float size = -1, x = float.NaN, y = float.NaN, angle = float.NaN;
            public Color color;
            public TextAlign align;
            public bool shown;
            public Face? face;
        }

        readonly Ctx ctx;
        readonly List<TextSlot> slots = new List<TextSlot>();
        int used;
        int inFlight;

        public CanvasElement()
        {
            ctx = new Ctx(this);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += m => ctx.Replay(m.painter2D);
        }

        public Ctx Begin()
        {
            ctx.Reset();
            used = 0;
            inFlight++;
            return ctx;
        }

        public void End()
        {
            for (var i = used; i < slots.Count; i++)
            {
                if (!slots[i].shown) continue;
                slots[i].shown = false;
                slots[i].label.style.display = DisplayStyle.None;
            }
            MarkDirtyRepaint();
        }

        internal void PlaceText(string text, Vector2 at, float size, Color color, TextAlign align, float angleDeg, Face face)
        {
            if (used == slots.Count)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.AddToClassList("canvas-text");
                label.style.width = TEXT_BOX_WIDTH;
                Add(label);
                slots.Add(new TextSlot { label = label });
            }
            var slot = slots[used++];
            var l = slot.label;
            if (!slot.shown)
            {
                slot.shown = true;
                l.style.display = DisplayStyle.Flex;
            }
            if (slot.text != text)
            {
                slot.text = text;
                l.text = text;
            }
            if (slot.size != size)
            {
                slot.size = size;
                l.style.fontSize = size;
                l.style.height = size * 1.5f;
                slot.x = float.NaN;
            }
            if (slot.color != color)
            {
                slot.color = color;
                l.style.color = color;
            }
            if (slot.face != face)
            {
                slot.face = face;
                Fonts.Apply(l, face);
            }
            if (slot.align != align || slot.x != at.x || slot.y != at.y)
            {
                slot.align = align;
                slot.x = at.x;
                slot.y = at.y;
                l.style.unityTextAlign = align == TextAlign.Left ? TextAnchor.MiddleLeft : align == TextAlign.Right ? TextAnchor.MiddleRight : TextAnchor.MiddleCenter;
                var left = align == TextAlign.Left ? at.x : align == TextAlign.Right ? at.x - TEXT_BOX_WIDTH : at.x - TEXT_BOX_WIDTH / 2;
                l.style.translate = new Translate(left, at.y - size * 0.75f);
            }
            if (slot.angle != angleDeg)
            {
                slot.angle = angleDeg;
                l.style.rotate = new Rotate(Angle.Degrees(angleDeg));
            }
        }
    }

    /// <summary>
    /// Barlow Condensed ships in Resources. Inter is the browser's first choice for body text, but
    /// its package only holds a compressed web font Unity can't read, so body text uses Segoe UI, the
    /// browser's second choice, from Windows itself. Its semibold weight is bold here: a second font
    /// from the system mixes up text between elements in UI Toolkit's text cache.
    /// </summary>
    public static class Fonts
    {
        static FontDefinition? body, display;

        public static FontDefinition Body => body ??= FontDefinition.FromFont(Font.CreateDynamicFontFromOSFont(new[] { "Segoe UI", "Arial" }, 16));
        public static FontDefinition Display => display ??= FontDefinition.FromFont(Resources.Load<Font>("Fonts/BarlowCondensed-SemiBold"));

        public static void Apply(VisualElement el, Face face)
        {
            el.style.unityFontDefinition = face == Face.Display ? Display : Body;
            el.style.unityFontStyleAndWeight = face == Face.Bold ? FontStyle.Bold : FontStyle.Normal;
        }
    }
}
