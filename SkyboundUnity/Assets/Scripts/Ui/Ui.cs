using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>
    /// What React's re-rendering does in the browser game: everything a screen shows registers here
    /// and is brought up to date once a frame. Text and classes are only touched when they differ,
    /// so an idle screen costs almost nothing.
    /// </summary>
    public sealed class Scope
    {
        readonly List<Action> frame = new List<Action>();

        public void On(Action a) => frame.Add(a);

        public void Frame()
        {
            foreach (var a in frame) a();
        }

        public void Text(TextElement el, Func<string> text) => On(() => Ui.SetText(el, text()));
        public void Show(VisualElement el, Func<bool> shown) => On(() => Ui.SetShown(el, shown()));
        public void Toggle(VisualElement el, string cls, Func<bool> on) => On(() => el.EnableInClassList(cls, on()));
    }

    /// <summary>The browser game's ui/primitives, as functions that build UI Toolkit elements.</summary>
    public static class Ui
    {
        public static readonly Color INK = Three.Hex("#eef3f8");
        public static readonly Color INK_2 = Three.Hex("#a7b4c4");
        public static readonly Color INK_3 = Three.Hex("#66768a");
        public static readonly Color ACCENT = Three.Hex("#4fd6ff");
        public static readonly Color LINE = Three.Hex("#1f2b3d");

        public static void SetText(TextElement el, string text)
        {
            if (el.text != text) el.text = text;
        }

        public static void SetShown(VisualElement el, bool shown)
        {
            var want = shown ? DisplayStyle.Flex : DisplayStyle.None;
            if (el.style.display != want) el.style.display = want;
        }

        public static VisualElement Div(params string[] classes)
        {
            var el = new VisualElement();
            foreach (var c in classes) el.AddToClassList(c);
            return el;
        }

        public static T With<T>(this T parent, params VisualElement[] children) where T : VisualElement
        {
            foreach (var child in children) parent.Add(child);
            return parent;
        }

        /// <summary>A Label. The classes num, label and display use Barlow Condensed, as in the browser's index.css.</summary>
        public static Label Txt(string text, params string[] classes)
        {
            var el = new Label(text);
            foreach (var c in classes)
            {
                el.AddToClassList(c);
                if (c == "num" || c == "label" || c == "display") Fonts.Apply(el, Face.Display);
                if (c == "semi") Fonts.Apply(el, Face.Bold);
            }
            return el;
        }

        /// <summary>Small uppercase section label.</summary>
        public static Label SectionLabel(string text) => Txt(text.ToUpperInvariant(), "label");

        public static VisualElement Dot(string tone)
        {
            return Div("dot", "dot--" + tone);
        }

        public static Button Btn(string text, Action click, string variant = "outline", string size = "md")
        {
            var button = new Button(click) { text = text, focusable = false };
            button.AddToClassList("btn");
            button.AddToClassList("btn--" + variant);
            button.AddToClassList("btn--" + size);
            Fonts.Apply(button, Face.Bold);
            return button;
        }

        /// <summary>Glass card with a title row.</summary>
        public static VisualElement PanelBox(string title, VisualElement aside, VisualElement content)
        {
            var header = Div("panel-header").With(SectionLabel(title));
            if (aside != null) header.Add(aside);
            return Div("glass", "rounded-lg", "panel-box").With(header, Div("panel-body").With(content));
        }

        public static VisualElement SliderRow(Scope scope, string label, Func<float> value, Func<string> display, float min, float max, float step, Action<float> onChange, string hint = null)
        {
            var labelEl = Txt(label, "slider-row__label");
            if (hint != null) labelEl.tooltip = hint;
            var valueEl = Txt("", "num", "slider-row__value");
            scope.Text(valueEl, display);
            var slider = new SliderEl(min, max, step, onChange) { name = label };
            scope.On(() => slider.Value = value());
            return Div("slider-row").With(Div("row-between").With(labelEl, valueEl), slider);
        }

        /// <summary>A label and a switch on one line.</summary>
        public static VisualElement SwitchLine(Scope scope, string label, Func<bool> value, Action<bool> onChange)
        {
            return Div("row-between", "switch-line").With(Txt(label, "switch-line__label"), new SwitchEl(scope, value, onChange));
        }

        public static ScrollView Scroller(params string[] classes)
        {
            var scroll = new ScrollView(ScrollViewMode.Vertical)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Hidden,
                verticalScrollerVisibility = ScrollerVisibility.Hidden,
                focusable = false,
            };
            foreach (var c in classes) scroll.AddToClassList(c);
            return scroll;
        }

        /// <summary>Brief fade or rise as an element appears: starts in the "from" class, which comes off a frame later.</summary>
        public static T Appear<T>(this T el, string kind = "rise") where T : VisualElement
        {
            el.AddToClassList("anim-" + kind);
            el.AddToClassList("anim-" + kind + "--from");
            el.schedule.Execute(() => el.RemoveFromClassList("anim-" + kind + "--from")).ExecuteLater(16);
            return el;
        }
    }

    /// <summary>The on/off switch.</summary>
    public class SwitchEl : VisualElement
    {
        readonly Action<bool> onChange;
        readonly Func<bool> value;
        readonly Func<bool> disabled;

        public SwitchEl(Scope scope, Func<bool> current, Action<bool> changed, Func<bool> isDisabled = null)
        {
            value = current;
            onChange = changed;
            disabled = isDisabled;
            AddToClassList("switch");
            Add(Ui.Div("switch__thumb"));
            this.AddManipulator(new Clickable(Press));
            scope.On(() =>
            {
                EnableInClassList("switch--on", value());
                EnableInClassList("switch--disabled", disabled != null && disabled());
            });
        }

        public void Press()
        {
            if (disabled == null || !disabled()) onChange(!value());
        }
    }

    /// <summary>The slider: a track, a filled part and a round thumb, dragged with the pointer.</summary>
    public class SliderEl : VisualElement
    {
        readonly float min, max, step;
        readonly Action<float> onChange;
        readonly VisualElement fill;
        readonly VisualElement thumb;
        bool dragging;
        float shown = float.NaN;

        public SliderEl(float minimum, float maximum, float stepSize, Action<float> changed)
        {
            min = minimum;
            max = maximum;
            step = stepSize;
            onChange = changed;
            AddToClassList("slider");
            fill = Ui.Div("slider__fill");
            thumb = Ui.Div("slider__thumb");
            Add(Ui.Div("slider__track").With(fill));
            Add(thumb);
            RegisterCallback<PointerDownEvent>(e =>
            {
                dragging = true;
                this.CapturePointer(e.pointerId);
                SetFromPointer(e.localPosition.x);
            });
            RegisterCallback<PointerMoveEvent>(e =>
            {
                if (dragging) SetFromPointer(e.localPosition.x);
            });
            RegisterCallback<PointerUpEvent>(e =>
            {
                dragging = false;
                this.ReleasePointer(e.pointerId);
            });
        }

        /// <summary>Quantised to the step, as Radix's slider does.</summary>
        public static float Quantise(float raw, float min, float max, float step)
        {
            return Mathf.Clamp(min + Mathf.Round((raw - min) / step) * step, min, max);
        }

        public void SetFromPointer(float x)
        {
            var width = resolvedStyle.width;
            if (width <= 0) return;
            onChange(Quantise(Mathf.Lerp(min, max, Mathf.Clamp01(x / width)), min, max, step));
        }

        public float Value
        {
            set
            {
                if (value == shown) return;
                shown = value;
                var at = Mathf.InverseLerp(min, max, value);
                fill.style.width = Length.Percent(at * 100);
                thumb.style.left = Length.Percent(at * 100);
            }
        }
    }

    /// <summary>A row of mutually exclusive buttons.</summary>
    public class SegmentedEl<T> : VisualElement
    {
        readonly List<(T value, Button button)> options = new List<(T, Button)>();

        public SegmentedEl(Scope scope, Func<T> current, Action<T> changed, params (T value, string label)[] choices)
        {
            AddToClassList("segmented");
            foreach (var (value, label) in choices)
            {
                var chosen = value;
                var button = new Button(() => changed(chosen)) { text = label, focusable = false };
                button.AddToClassList("segmented__option");
                Fonts.Apply(button, Face.Bold);
                Add(button);
                options.Add((value, button));
            }
            scope.On(() =>
            {
                var now = current();
                foreach (var (value, button) in options) button.EnableInClassList("segmented__option--on", EqualityComparer<T>.Default.Equals(value, now));
            });
        }
    }

    public enum Icon
    {
        Pause,
        Play,
        Camera,
        Gauge,
        Help,
        Settings,
        Recenter,
        Plus,
        Minus,
        Restart,
        Approach,
        Home,
        Clipboard,
        Plane,
    }

    /// <summary>Small stroke icon set for the flight UI, on a 24×24 grid.</summary>
    public class IconEl : CanvasElement
    {
        const float GRID = 24;
        const float STROKE = 1.7f;

        readonly Icon icon;
        readonly float size;
        Color color;

        public IconEl(Icon which, float pixels, Color tint)
        {
            icon = which;
            size = pixels;
            style.width = pixels;
            style.height = pixels;
            Tint = tint;
        }

        public Color Tint
        {
            set
            {
                if (color == value) return;
                color = value;
                var c = Begin();
                c.unit = size / GRID;
                c.StrokeStyle(color);
                c.lineWidth = STROKE * c.unit;
                c.lineCap = LineCap.Round;
                DrawIcon(c, icon);
                End();
            }
        }

        static void Line(Ctx c, float x0, float y0, float x1, float y1)
        {
            c.BeginPath();
            c.MoveTo(x0, y0);
            c.LineTo(x1, y1);
            c.Stroke();
        }

        static void Circle(Ctx c, float x, float y, float r)
        {
            c.BeginPath();
            c.Arc(x, y, r, 0, Mathf.PI * 2);
            c.Stroke();
        }

        static void Poly(Ctx c, bool close, params float[] xy)
        {
            c.BeginPath();
            c.MoveTo(xy[0], xy[1]);
            for (var i = 2; i < xy.Length; i += 2) c.LineTo(xy[i], xy[i + 1]);
            if (close) c.ClosePath();
            c.Stroke();
        }

        static void Rounded(Ctx c, float x, float y, float w, float h, float r)
        {
            c.BeginPath();
            c.RoundRect(x, y, w, h, r);
            c.Stroke();
        }

        static void DrawIcon(Ctx c, Icon icon)
        {
            const float D2R = Mathf.PI / 180;
            switch (icon)
            {
                case Icon.Pause:
                    Line(c, 9, 5, 9, 19);
                    Line(c, 15, 5, 15, 19);
                    break;
                case Icon.Play:
                    Poly(c, true, 8, 5.5f, 8, 18.5f, 18.5f, 12);
                    break;
                case Icon.Camera:
                    Poly(c, true, 3, 8.5f, 4.5f, 7, 7.5f, 7, 9, 5, 15, 5, 16.5f, 7, 19.5f, 7, 21, 8.5f, 21, 17.5f, 19.5f, 19, 4.5f, 19, 3, 17.5f);
                    Circle(c, 12, 13, 3.5f);
                    break;
                case Icon.Gauge:
                    c.BeginPath();
                    c.Arc(12, 13, 8, 150 * D2R, 390 * D2R);
                    c.Stroke();
                    Line(c, 12, 13, 16, 9);
                    Circle(c, 12, 13, 1.2f);
                    break;
                case Icon.Help:
                    Rounded(c, 3, 6, 18, 12, 2);
                    Line(c, 7, 10, 8, 10);
                    Line(c, 11, 10, 12, 10);
                    Line(c, 15, 10, 17, 10);
                    Line(c, 7, 14, 17, 14);
                    break;
                case Icon.Settings:
                    Line(c, 4, 7, 14, 7);
                    Line(c, 18, 7, 20, 7);
                    Line(c, 4, 17, 8, 17);
                    Line(c, 12, 17, 20, 17);
                    Circle(c, 16, 7, 2);
                    Circle(c, 10, 17, 2);
                    break;
                case Icon.Recenter:
                    Circle(c, 12, 12, 7);
                    Circle(c, 12, 12, 1.5f);
                    Line(c, 12, 2, 12, 5);
                    Line(c, 12, 19, 12, 22);
                    Line(c, 2, 12, 5, 12);
                    Line(c, 19, 12, 22, 12);
                    break;
                case Icon.Plus:
                    Line(c, 12, 5, 12, 19);
                    Line(c, 5, 12, 19, 12);
                    break;
                case Icon.Minus:
                    Line(c, 5, 12, 19, 12);
                    break;
                case Icon.Restart:
                    c.BeginPath();
                    c.Arc(12, 12, 8, 180 * D2R, 498 * D2R);
                    c.Stroke();
                    Poly(c, false, 4, 4, 4, 8, 8, 8);
                    break;
                case Icon.Approach:
                    Line(c, 3, 6, 14, 14);
                    Line(c, 12, 19, 21, 19);
                    Line(c, 15, 21, 18, 17);
                    break;
                case Icon.Home:
                    Poly(c, true, 4, 11, 12, 5, 20, 11, 20, 19, 19, 20, 15, 20, 15, 15, 9, 15, 9, 20, 5, 20, 4, 19);
                    break;
                case Icon.Clipboard:
                    Rounded(c, 6, 5, 12, 16, 2);
                    Poly(c, false, 9, 5, 9, 3.5f, 15, 3.5f, 15, 5);
                    Line(c, 9, 11, 15, 11);
                    Line(c, 9, 15, 13, 15);
                    break;
                case Icon.Plane:
                    Line(c, 12, 3, 12, 21);
                    Poly(c, false, 4, 11, 12, 9, 20, 11);
                    Poly(c, false, 9, 19, 12, 18, 15, 19);
                    break;
            }
        }
    }

    /// <summary>
    /// Square icon button for toolbars. Active marks a toggle that is on; the label doubles as tooltip.
    /// Its colour is drawn in, so hovering has to redraw it, where the browser uses currentColor.
    /// </summary>
    public class IconButton : VisualElement
    {
        readonly IconEl icon;
        readonly VisualElement activeBar = Ui.Div("icon-btn__bar");
        readonly Action press;
        bool hover;
        bool active;

        public IconButton(Icon which, string label, string shortcut, Action click)
        {
            press = click;
            AddToClassList("glass");
            AddToClassList("icon-btn");
            tooltip = shortcut != null ? $"{label} ({shortcut})" : label;
            name = label;
            icon = new IconEl(which, 18, Ui.INK_2);
            Add(icon);
            Add(activeBar);
            Ui.SetShown(activeBar, false);
            this.AddManipulator(new Clickable(click));
            RegisterCallback<PointerEnterEvent>(_ => { hover = true; Refresh(); });
            RegisterCallback<PointerLeaveEvent>(_ => { hover = false; Refresh(); });
        }

        public void Press() => press();

        public bool Active
        {
            set
            {
                if (active == value) return;
                active = value;
                EnableInClassList("icon-btn--active", active);
                Ui.SetShown(activeBar, active);
                Refresh();
            }
        }

        void Refresh() => icon.Tint = active ? Ui.ACCENT : hover ? Ui.INK : Ui.INK_2;
    }

    /// <summary>Full-size gradient background: stops of a vertical gradient plus an optional soft glow.</summary>
    public class BackdropEl : CanvasElement
    {
        readonly Color[] stops;
        readonly float[] at;
        readonly Color glow;
        readonly Vector2 glowAt;
        readonly float glowRadius;

        public BackdropEl(Color[] colours, float[] positions, Color glowColour, Vector2 glowCentre, float glowSize)
        {
            stops = colours;
            at = positions;
            glow = glowColour;
            glowAt = glowCentre;
            glowRadius = glowSize;
            AddToClassList("fill");
            RegisterCallback<GeometryChangedEvent>(e => Paint(e.newRect.width, e.newRect.height));
        }

        void Paint(float w, float h)
        {
            var c = Begin();
            for (var i = 0; i + 1 < stops.Length; i++)
            {
                c.FillLinear(stops[i], stops[i + 1], 0, at[i] * h, 0, at[i + 1] * h);
                c.FillRect(0, at[i] * h, w, (at[i + 1] - at[i]) * h);
            }
            var clear = glow;
            clear.a = 0;
            var centre = new Vector2(glowAt.x * w, glowAt.y * h);
            var radius = glowRadius * w;
            c.FillRadial(glow, clear, centre.x, centre.y, radius, centre.x, centre.y);
            c.FillRect(0, 0, w, h);
            End();
        }
    }
}
