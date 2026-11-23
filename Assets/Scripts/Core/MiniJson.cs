// MiniJson.cs
// 외부 라이브러리 없이 쓰는 작은 JSON 파서/작성기.
// Unity의 JsonUtility는 Dictionary를 못 다루고, Newtonsoft는 콘솔(Mono)에 기본 포함이 아니라서 직접 만든다.
// 결과 타입: Dictionary<string, object>, List<object>, string, double, bool, null

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ObstacleWFC
{
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.SkipWs();
            object v = p.Value();
            p.SkipWs();
            if (!p.End) throw new FormatException("JSON 뒤에 남는 문자가 있습니다 (위치 " + p.Pos + ")");
            return v;
        }

        // 문자열을 JSON 문자열 리터럴로 (따옴표 포함)
        public static string Quote(string s)
        {
            var sb = new StringBuilder(s.Length + 2);
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
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Num(double d) { return d.ToString("R", CultureInfo.InvariantCulture); }

        class Parser
        {
            readonly string s; int i;
            public Parser(string text) { s = text; }
            public int Pos { get { return i; } }
            public bool End { get { return i >= s.Length; } }

            public void SkipWs() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }

            Exception Err(string msg) { return new FormatException(msg + " (위치 " + i + ")"); }

            public object Value()
            {
                SkipWs();
                if (End) throw Err("값이 없습니다");
                char c = s[i];
                if (c == '{') return Obj();
                if (c == '[') return Arr();
                if (c == '"') return Str();
                if (c == 't') return Lit("true", true);
                if (c == 'f') return Lit("false", false);
                if (c == 'n') return Lit("null", null);
                if (c == '-' || (c >= '0' && c <= '9')) return Number();
                throw Err("예상하지 못한 문자 '" + c + "'");
            }

            object Lit(string word, object val)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) throw Err("잘못된 값");
                i += word.Length; return val;
            }

            Dictionary<string, object> Obj()
            {
                var d = new Dictionary<string, object>();
                i++; SkipWs();
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    if (End || s[i] != '"') throw Err("키는 문자열이어야 합니다");
                    string k = Str();
                    SkipWs();
                    if (End || s[i] != ':') throw Err("':'가 필요합니다");
                    i++;
                    d[k] = Value();
                    SkipWs();
                    if (End) throw Err("'}'가 없습니다");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw Err("',' 또는 '}'가 필요합니다");
                }
            }

            List<object> Arr()
            {
                var l = new List<object>();
                i++; SkipWs();
                if (i < s.Length && s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(Value());
                    SkipWs();
                    if (End) throw Err("']'가 없습니다");
                    if (s[i] == ',') { i++; continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw Err("',' 또는 ']'가 필요합니다");
                }
            }

            string Str()
            {
                i++;
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw Err("문자열이 끝나지 않았습니다");
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (End) throw Err("잘못된 이스케이프");
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw Err("잘못된 \\u 이스케이프");
                            sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16));
                            i += 4; break;
                        default: throw Err("잘못된 이스케이프 \\" + e);
                    }
                }
            }

            double Number()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && "0123456789.eE+-".IndexOf(s[i]) >= 0) i++;
                double d;
                if (!double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                    throw Err("잘못된 숫자");
                return d;
            }
        }
    }
}
