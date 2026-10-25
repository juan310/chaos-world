using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace FH.Core
{
    /// <summary>Small JSON reader/writer producing Dictionary/List/double/string/bool/null trees.
    /// Kept engine-independent so save migration can operate on the raw tree before mapping.</summary>
    public static class Json
    {
        public static object Parse(string text)
        {
            if (text == null) throw new FormatException("JSON text is null");
            var p = new Parser(text);
            p.SkipWs();
            var value = p.ReadValue();
            p.SkipWs();
            if (!p.End) throw new FormatException("Unexpected trailing characters at " + p.Pos);
            return value;
        }

        public static string Write(object value, bool pretty = true)
        {
            var sb = new StringBuilder();
            WriteValue(sb, value, pretty, 0);
            return sb.ToString();
        }

        static void Indent(StringBuilder sb, bool pretty, int depth)
        {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        static void WriteValue(StringBuilder sb, object v, bool pretty, int depth)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(FormatNumber(d)); break;
                case float f: sb.Append(FormatNumber(f)); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object> map:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in map)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        Indent(sb, pretty, depth + 1);
                        WriteString(sb, kv.Key);
                        sb.Append(pretty ? ": " : ":");
                        WriteValue(sb, kv.Value, pretty, depth + 1);
                    }
                    if (!first) Indent(sb, pretty, depth);
                    sb.Append('}');
                    break;
                case IList list:
                    sb.Append('[');
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        Indent(sb, pretty, depth + 1);
                        WriteValue(sb, list[i], pretty, depth + 1);
                    }
                    if (list.Count > 0) Indent(sb, pretty, depth);
                    sb.Append(']');
                    break;
                default:
                    throw new FormatException("Unsupported JSON value " + v.GetType().Name);
            }
        }

        static string FormatNumber(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "0";
            if (Math.Abs(d - Math.Round(d)) < 1e-9 && Math.Abs(d) < 1e15) return ((long)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
            return d.ToString("R", CultureInfo.InvariantCulture);
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        class Parser
        {
            readonly string s;
            public int Pos;
            public Parser(string text) { s = text; }
            public bool End => Pos >= s.Length;

            public void SkipWs()
            {
                while (Pos < s.Length)
                {
                    char c = s[Pos];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r' || c == '﻿') Pos++;
                    else if (c == '/' && Pos + 1 < s.Length && s[Pos + 1] == '/')
                    {
                        while (Pos < s.Length && s[Pos] != '\n') Pos++;
                    }
                    else break;
                }
            }

            char Next()
            {
                if (Pos >= s.Length) throw new FormatException("Unexpected end of JSON");
                return s[Pos++];
            }

            public object ReadValue()
            {
                SkipWs();
                if (End) throw new FormatException("Unexpected end of JSON");
                char c = s[Pos];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't') { Expect("true"); return true; }
                if (c == 'f') { Expect("false"); return false; }
                if (c == 'n') { Expect("null"); return null; }
                if (c == '-' || (c >= '0' && c <= '9')) return ReadNumber();
                throw new FormatException("Unexpected character '" + c + "' at " + Pos);
            }

            void Expect(string word)
            {
                if (string.CompareOrdinal(s, Pos, word, 0, word.Length) != 0) throw new FormatException("Expected " + word + " at " + Pos);
                Pos += word.Length;
            }

            Dictionary<string, object> ReadObject()
            {
                var map = new Dictionary<string, object>();
                Pos++;
                SkipWs();
                if (!End && s[Pos] == '}') { Pos++; return map; }
                while (true)
                {
                    SkipWs();
                    if (End || s[Pos] != '"') throw new FormatException("Expected key at " + Pos);
                    string key = ReadString();
                    SkipWs();
                    if (Next() != ':') throw new FormatException("Expected ':' at " + Pos);
                    map[key] = ReadValue();
                    SkipWs();
                    char c = Next();
                    if (c == '}') return map;
                    if (c != ',') throw new FormatException("Expected ',' or '}' at " + Pos);
                }
            }

            List<object> ReadArray()
            {
                var list = new List<object>();
                Pos++;
                SkipWs();
                if (!End && s[Pos] == ']') { Pos++; return list; }
                while (true)
                {
                    list.Add(ReadValue());
                    SkipWs();
                    char c = Next();
                    if (c == ']') return list;
                    if (c != ',') throw new FormatException("Expected ',' or ']' at " + Pos);
                }
            }

            string ReadString()
            {
                var sb = new StringBuilder();
                Pos++;
                while (true)
                {
                    char c = Next();
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    char e = Next();
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (Pos + 4 > s.Length) throw new FormatException("Bad unicode escape");
                            sb.Append((char)int.Parse(s.Substring(Pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            Pos += 4;
                            break;
                        default: throw new FormatException("Bad escape at " + Pos);
                    }
                }
            }

            double ReadNumber()
            {
                int start = Pos;
                while (Pos < s.Length && "+-0123456789.eE".IndexOf(s[Pos]) >= 0) Pos++;
                if (!double.TryParse(s.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double d))
                    throw new FormatException("Bad number at " + start);
                return d;
            }
        }
    }

    /// <summary>Maps JSON trees onto plain classes with public fields (and back).
    /// Supports primitives, enums (as names), List&lt;T&gt;, and nested classes. Unknown keys are ignored.</summary>
    public static class JsonMapper
    {
        public static T To<T>(object tree) where T : new() => (T)ToType(tree, typeof(T));

        public static object ToType(object tree, Type t)
        {
            if (tree == null) return t.IsValueType ? Activator.CreateInstance(t) : (t == typeof(string) ? null : null);
            if (t == typeof(string)) return tree is string s ? s : Convert.ToString(tree, CultureInfo.InvariantCulture);
            if (t == typeof(int)) return Convert.ToInt32(Num(tree));
            if (t == typeof(long)) return Convert.ToInt64(Num(tree));
            if (t == typeof(float)) return (float)Num(tree);
            if (t == typeof(double)) return Num(tree);
            if (t == typeof(bool)) return tree is bool b ? b : Num(tree) != 0;
            if (t.IsEnum)
            {
                if (tree is string es) return Enum.Parse(t, es, true);
                return Enum.ToObject(t, Convert.ToInt32(Num(tree)));
            }
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>))
            {
                var list = (IList)Activator.CreateInstance(t);
                var et = t.GetGenericArguments()[0];
                if (tree is IList src) foreach (var item in src) list.Add(ToType(item, et));
                return list;
            }
            if (tree is IDictionary<string, object> map)
            {
                var obj = Activator.CreateInstance(t);
                foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (f.IsInitOnly || f.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                    if (map.TryGetValue(f.Name, out var v)) f.SetValue(obj, ToType(v, f.FieldType));
                }
                return obj;
            }
            throw new FormatException("Cannot map " + tree.GetType().Name + " to " + t.Name);
        }

        static double Num(object o)
        {
            if (o is double d) return d;
            if (o is bool b) return b ? 1 : 0;
            if (o is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return p;
            return Convert.ToDouble(o, CultureInfo.InvariantCulture);
        }

        public static object ToTree(object obj)
        {
            if (obj == null) return null;
            var t = obj.GetType();
            if (obj is string || obj is bool || obj is double) return obj;
            if (obj is int i) return (double)i;
            if (obj is long l) return (double)l;
            if (obj is float f) return (double)f;
            if (t.IsEnum) return obj.ToString();
            if (obj is IList list)
            {
                var result = new List<object>();
                foreach (var item in list) result.Add(ToTree(item));
                return result;
            }
            var map = new Dictionary<string, object>();
            foreach (var field in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.IsInitOnly || field.IsDefined(typeof(NonSerializedAttribute), false)) continue;
                map[field.Name] = ToTree(field.GetValue(obj));
            }
            return map;
        }
    }
}
