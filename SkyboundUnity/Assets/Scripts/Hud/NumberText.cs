using System;
using System.Collections.Generic;
using System.Globalization;
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

        static readonly Dictionary<int, string> whole = new Dictionary<int, string>();
        static readonly Dictionary<int, string> thousands = new Dictionary<int, string>();
        static readonly string[] twoDigit = new string[TWO_DIGITS];

        /// <summary>1250 → "1250".</summary>
        public static string Whole(int v) => Lookup(whole, v, "0");

        /// <summary>1250 → "1,250".</summary>
        public static string Thousands(int v) => Lookup(thousands, v, "N0");

        /// <summary>9 → "09". Headings on the ribbon.</summary>
        public static string TwoDigit(int v)
        {
            return twoDigit[v] ?? (twoDigit[v] = v.ToString("00", CultureInfo.InvariantCulture));
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

    /// <summary>A label whose text is rebuilt only when the whole number behind it changes.</summary>
    public sealed class IntLabel
    {
        readonly Label label;
        readonly Func<int, string> format;
        int shown = int.MinValue;

        public IntLabel(Label target, Func<int, string> toText)
        {
            label = target;
            format = toText;
        }

        public void Set(int value)
        {
            if (value == shown) return;
            shown = value;
            Ui.SetText(label, format(value));
        }
    }
}
