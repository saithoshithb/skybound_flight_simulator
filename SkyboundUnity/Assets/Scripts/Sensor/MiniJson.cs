using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Skybound
{
    /// <summary>
    /// A strict JSON reader that behaves like the browser's JSON.parse, plus the two bits of
    /// JavaScript text formatting the protocol's error messages rely on. JsonUtility can't
    /// replace it here: it can't tell a missing or mistyped field from a zero, and it accepts
    /// malformed text, and the protocol has to reject both.
    /// Objects become dictionaries, arrays lists, numbers doubles.
    /// </summary>
    public static class MiniJson
    {
        /// <summary>What reading an absent key gives, JavaScript's undefined.</summary>
        public static readonly object Undefined = new object();

        public static object Get(Dictionary<string, object> o, string key)
        {
            return o.TryGetValue(key, out var v) ? v : Undefined;
        }

        /// <exception cref="FormatException">The text isn't valid JSON.</exception>
        public static object Parse(string text)
        {
            var at = 0;
            var value = ReadValue(text, ref at);
            SkipSpace(text, ref at);
            if (at != text.Length) throw new FormatException("trailing text");
            return value;
        }

        static void SkipSpace(string s, ref int at)
        {
            while (at < s.Length && (s[at] == ' ' || s[at] == '\t' || s[at] == '\n' || s[at] == '\r')) at++;
        }

        static object ReadValue(string s, ref int at)
        {
            SkipSpace(s, ref at);
            if (at >= s.Length) throw new FormatException("unexpected end");
            switch (s[at])
            {
                case '{': return ReadObject(s, ref at);
                case '[': return ReadArray(s, ref at);
                case '"': return ReadString(s, ref at);
                case 't': return ReadWord(s, ref at, "true", true);
                case 'f': return ReadWord(s, ref at, "false", false);
                case 'n': return ReadWord(s, ref at, "null", null);
                default: return ReadNumber(s, ref at);
            }
        }

        static object ReadWord(string s, ref int at, string word, object value)
        {
            if (string.CompareOrdinal(s, at, word, 0, word.Length) != 0) throw new FormatException("unexpected word");
            at += word.Length;
            return value;
        }

        static Dictionary<string, object> ReadObject(string s, ref int at)
        {
            var o = new Dictionary<string, object>();
            at++;
            SkipSpace(s, ref at);
            if (at < s.Length && s[at] == '}')
            {
                at++;
                return o;
            }
            while (true)
            {
                SkipSpace(s, ref at);
                if (at >= s.Length || s[at] != '"') throw new FormatException("expected a key");
                var key = ReadString(s, ref at);
                SkipSpace(s, ref at);
                if (at >= s.Length || s[at] != ':') throw new FormatException("expected :");
                at++;
                o[key] = ReadValue(s, ref at); // a repeated key: the last one wins
                SkipSpace(s, ref at);
                if (at >= s.Length) throw new FormatException("unexpected end");
                if (s[at] == '}')
                {
                    at++;
                    return o;
                }
                if (s[at] != ',') throw new FormatException("expected ,");
                at++;
            }
        }

        static List<object> ReadArray(string s, ref int at)
        {
            var list = new List<object>();
            at++;
            SkipSpace(s, ref at);
            if (at < s.Length && s[at] == ']')
            {
                at++;
                return list;
            }
            while (true)
            {
                list.Add(ReadValue(s, ref at));
                SkipSpace(s, ref at);
                if (at >= s.Length) throw new FormatException("unexpected end");
                if (s[at] == ']')
                {
                    at++;
                    return list;
                }
                if (s[at] != ',') throw new FormatException("expected ,");
                at++;
            }
        }

        static string ReadString(string s, ref int at)
        {
            var text = new StringBuilder();
            at++;
            while (true)
            {
                if (at >= s.Length) throw new FormatException("unterminated string");
                var c = s[at++];
                if (c == '"') return text.ToString();
                if (c < 0x20) throw new FormatException("control character in string");
                if (c != '\\')
                {
                    text.Append(c);
                    continue;
                }
                if (at >= s.Length) throw new FormatException("unterminated string");
                var escape = s[at++];
                switch (escape)
                {
                    case '"': text.Append('"'); break;
                    case '\\': text.Append('\\'); break;
                    case '/': text.Append('/'); break;
                    case 'b': text.Append('\b'); break;
                    case 'f': text.Append('\f'); break;
                    case 'n': text.Append('\n'); break;
                    case 'r': text.Append('\r'); break;
                    case 't': text.Append('\t'); break;
                    case 'u':
                        if (at + 4 > s.Length) throw new FormatException("bad \\u escape");
                        var code = 0;
                        for (var i = 0; i < 4; i++)
                        {
                            var digit = HexDigit(s[at++]);
                            if (digit < 0) throw new FormatException("bad \\u escape");
                            code = code * 16 + digit;
                        }
                        text.Append((char)code);
                        break;
                    default:
                        throw new FormatException("bad escape");
                }
            }
        }

        static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        static bool IsDigit(string s, int at) => at < s.Length && s[at] >= '0' && s[at] <= '9';

        static object ReadNumber(string s, ref int at)
        {
            var start = at;
            if (s[at] == '-') at++;
            if (!IsDigit(s, at)) throw new FormatException("expected a number");
            if (s[at] == '0') at++;
            else while (IsDigit(s, at)) at++;
            if (at < s.Length && s[at] == '.')
            {
                at++;
                if (!IsDigit(s, at)) throw new FormatException("expected a digit");
                while (IsDigit(s, at)) at++;
            }
            if (at < s.Length && (s[at] == 'e' || s[at] == 'E'))
            {
                at++;
                if (at < s.Length && (s[at] == '+' || s[at] == '-')) at++;
                if (!IsDigit(s, at)) throw new FormatException("expected a digit");
                while (IsDigit(s, at)) at++;
            }
            var text = s.Substring(start, at - start);
            try
            {
                return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
            }
            catch (OverflowException)
            {
                // JSON.parse("1e999") is Infinity; the validator then rejects it as not finite.
                return text[0] == '-' ? double.NegativeInfinity : double.PositiveInfinity;
            }
        }

        /// <summary>String(value) in JavaScript, for error messages.</summary>
        public static string JsString(object value)
        {
            if (value == Undefined) return "undefined";
            switch (value)
            {
                case null: return "null";
                case string s: return s;
                case bool b: return b ? "true" : "false";
                case double d: return JsNumber(d);
                case List<object> list: return string.Join(",", list.Select(v => v == null ? "" : JsString(v)));
                default: return "[object Object]";
            }
        }

        /// <summary>The text JavaScript prints for a number: shortest round-trip digits, its own rules for exponents.</summary>
        public static string JsNumber(double v)
        {
            if (double.IsNaN(v)) return "NaN";
            if (double.IsPositiveInfinity(v)) return "Infinity";
            if (double.IsNegativeInfinity(v)) return "-Infinity";
            if (v == 0) return "0";

            // The fewest digits that read back as the same number. Mono's "R" can give one too many.
            var abs = Math.Abs(v);
            var r = "";
            for (var precision = 0; precision <= 16; precision++)
            {
                r = abs.ToString("E" + precision, CultureInfo.InvariantCulture);
                if (ReadsBackAs(r, abs)) break;
            }
            var e = r.IndexOf('E');
            var exponent = int.Parse(r.Substring(e + 1), CultureInfo.InvariantCulture);
            var mantissa = r.Substring(0, e);
            var point = mantissa.IndexOf('.');
            var digits = mantissa.Replace(".", "");
            // value = 0.digits × 10^n
            var n = (point < 0 ? mantissa.Length : point) + exponent;
            var trimmed = digits.TrimStart('0');
            n -= digits.Length - trimmed.Length;
            digits = trimmed.TrimEnd('0');

            var k = digits.Length;
            string text;
            if (k <= n && n <= 21) text = digits + new string('0', n - k);
            else if (0 < n && n <= 21) text = digits.Substring(0, n) + "." + digits.Substring(n);
            else if (-6 < n && n <= 0) text = "0." + new string('0', -n) + digits;
            else
            {
                var power = n - 1;
                var sign = power < 0 ? "-" : "+";
                var size = Math.Abs(power).ToString(CultureInfo.InvariantCulture);
                text = k == 1 ? digits + "e" + sign + size : digits.Substring(0, 1) + "." + digits.Substring(1) + "e" + sign + size;
            }
            return v < 0 ? "-" + text : text;
        }

        static bool ReadsBackAs(string text, double value)
        {
            try
            {
                return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture) == value;
            }
            catch (OverflowException)
            {
                return false; // the largest doubles round up past the limit at low precision
            }
        }

        /// <summary>
        /// Number.prototype.toFixed for values under 1e21. It rounds the number's exact binary
        /// value, so 1.005 gives "1.00"; C#'s "F2" rounds 15 significant digits and gives "1.01".
        /// </summary>
        public static string JsToFixed(double v, int digits)
        {
            var bits = BitConverter.DoubleToInt64Bits(Math.Abs(v));
            var biased = (int)(bits >> 52);
            var fraction = bits & 0xFFFFFFFFFFFFFL;
            var mantissa = new BigInteger(biased == 0 ? fraction : fraction | (1L << 52));
            var exponent = (biased == 0 ? 1 : biased) - 1075;

            var scaled = mantissa * BigInteger.Pow(10, digits);
            // Nearest whole number, an exact tie going up.
            var n = exponent >= 0
                ? scaled * BigInteger.Pow(2, exponent)
                : (scaled * 2 + BigInteger.Pow(2, -exponent)) / BigInteger.Pow(2, 1 - exponent);

            var text = n.ToString(CultureInfo.InvariantCulture).PadLeft(digits + 1, '0');
            if (digits > 0) text = text.Substring(0, text.Length - digits) + "." + text.Substring(text.Length - digits);
            return v < 0 ? "-" + text : text;
        }

        /// <summary>String.prototype.trim. C#'s Trim differs on two characters: U+FEFF and U+0085.</summary>
        public static string JsTrim(string s)
        {
            var start = 0;
            var end = s.Length;
            while (start < end && IsJsSpace(s[start])) start++;
            while (end > start && IsJsSpace(s[end - 1])) end--;
            return s.Substring(start, end - start);
        }

        static bool IsJsSpace(char c) => c == '﻿' || (c != '\u0085' && char.IsWhiteSpace(c));
    }
}
