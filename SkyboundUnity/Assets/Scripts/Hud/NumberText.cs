using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Skybound
{
    /// <summary>
    /// Number strings for what redraws every frame. The tapes show the same few tick labels over
    /// and over, so each is made once. The tables only hold what's been shown, and start again if a
    /// long flight ever fills them.
    /// </summary>
    public static class NumberText
    {
        const int MAX_ENTRIES = 8192;
        const int TWO_DIGITS = 100;
        const int ASCII = 128;

        static readonly Dictionary<int, string> whole = new Dictionary<int, string>();
        static readonly Dictionary<int, string> thousands = new Dictionary<int, string>();
        static readonly string[] twoDigit = new string[TWO_DIGITS];
        static readonly string[] ascii = new string[ASCII];
        static readonly Dictionary<char, string> otherChars = new Dictionary<char, string>();

        /// <summary>1250 → "1250".</summary>
        public static string Whole(int v) => Lookup(whole, v, "0");

        /// <summary>1250 → "1,250".</summary>
        public static string Thousands(int v) => Lookup(thousands, v, "N0");

        /// <summary>9 → "09". Headings on the ribbon.</summary>
        public static string TwoDigit(int v)
        {
            return twoDigit[v] ?? (twoDigit[v] = v.ToString("00", CultureInfo.InvariantCulture));
        }

        /// <summary>A one-letter string, made once.</summary>
        public static string Char(char c)
        {
            if (c < ASCII) return ascii[c] ?? (ascii[c] = c.ToString());
            if (otherChars.TryGetValue(c, out var text)) return text;
            text = c.ToString();
            otherChars[c] = text;
            return text;
        }

        static string Lookup(Dictionary<int, string> table, int v, string format)
        {
            if (table.TryGetValue(v, out var text)) return text;
            if (table.Count >= MAX_ENTRIES) table.Clear();
            text = v.ToString(format, CultureInfo.InvariantCulture);
            table[v] = text;
            return text;
        }
    }

    /// <summary>
    /// A readout whose digits don't jitter. Barlow's digits differ in width (a 1 is 60% of a 0) and
    /// Unity's text can't ask a font for tabular figures, so each character gets its own label, and
    /// the digits and signs a cell of the same width.
    /// </summary>
    public sealed class TabularLabel : VisualElement
    {
        /// <summary>Barlow Condensed's widest digit, as a share of the font size.</summary>
        public const float DIGIT_EM = 0.46f;

        readonly List<Label> cells = new List<Label>();
        readonly float fontSize;
        string shown = "";

        public string Text => shown;

        public TabularLabel(float size, params string[] classes)
        {
            fontSize = size;
            AddToClassList("tabular");
            foreach (var c in classes) AddToClassList(c);
            pickingMode = PickingMode.Ignore;
        }

        public void Set(string text)
        {
            if (text == shown) return;
            for (var i = 0; i < text.Length; i++)
            {
                if (i == cells.Count)
                {
                    var cell = Ui.Txt("", "num", "tabular__cell");
                    cell.style.fontSize = fontSize;
                    cell.pickingMode = PickingMode.Ignore;
                    cells.Add(cell);
                    Add(cell);
                }
                if (i < shown.Length && shown[i] == text[i]) continue;
                var ch = text[i];
                var fixedWidth = ch >= '0' && ch <= '9' || ch == '+' || ch == '−';
                cells[i].text = NumberText.Char(ch);
                cells[i].style.width = fixedWidth ? new StyleLength(fontSize * DIGIT_EM) : new StyleLength(StyleKeyword.Auto);
                Ui.SetShown(cells[i], true);
            }
            for (var i = text.Length; i < shown.Length; i++) Ui.SetShown(cells[i], false);
            shown = text;
        }
    }

    /// <summary>
    /// A readout whose text is rebuilt only when the whole number behind it changes, and made only
    /// the first time that number comes up.
    /// </summary>
    public sealed class IntLabel
    {
        const int MAX_ENTRIES = 4096;

        readonly Action<string> show;
        readonly Func<int, string> format;
        readonly Dictionary<int, string> made = new Dictionary<int, string>();
        int shown = int.MinValue;

        public IntLabel(Label target, Func<int, string> toText)
        {
            show = text => Ui.SetText(target, text);
            format = toText;
        }

        public IntLabel(TabularLabel target, Func<int, string> toText)
        {
            show = target.Set;
            format = toText;
        }

        public void Set(int value)
        {
            if (value == shown) return;
            shown = value;
            if (!made.TryGetValue(value, out var text))
            {
                if (made.Count >= MAX_ENTRIES) made.Clear();
                text = format(value);
                made[value] = text;
            }
            show(text);
        }
    }
}
