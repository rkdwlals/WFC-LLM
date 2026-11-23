// Criteria.cs
// Fidelity(프롬프트 충실도) 평가용 지표 계산과 기준식 해석.
// 기준식 예: "north >= 2*south & share_rock >= 0.5 & path == 1"
//   - '&'로 여러 조건을 잇는다 (모두 만족해야 통과)
//   - 비교: >=, <=, >, <, ==
//   - 양변: '+'로 더하고 '*'로 곱한 식 (숫자와 지표 이름)

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace ObstacleWFC
{
    public static class FidelityMetrics
    {
        // 지표 이름 목록 (논문 표에 그대로 쓴다)
        //  density              장애물을 둘 수 있는 칸 중 장애물 비율
        //  north/south/west/east 3×3 구역 중 해당 줄(3구역)의 장애물 비율
        //  center / outer       가운데 구역(C) / 나머지 8구역의 장애물 비율
        //  r_NW … r_SE          구역 하나의 장애물 비율
        //  share_rock …         장애물 중 해당 종류의 비중
        //  path                 시작점→목표 지점 도달 가능 1, 불가 0
        public static Dictionary<string, double> Compute(BaseMap map, int[,] g)
        {
            var p = GenerationParams.Default(); // 장애물을 둘 수 있는 칸의 정의(물·길·시작·목표 제외)는 모든 조건에서 같게
            var m = new Dictionary<string, double>();
            var total = new Dictionary<string, int>(); var filled = new Dictionary<string, int>();
            foreach (var r in Regions.Names) { total[r] = 0; filled[r] = 0; }
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    var b = map.Tiles[x, y];
                    if (b == BaseTile.Spawn || b == BaseTile.Goal || p.ForbiddenBase.Contains(b)) continue;
                    string r = Regions.Of(x, y, map.Width, map.Height);
                    total[r]++;
                    if (g[x, y] != ObstacleSet.Empty) filled[r]++;
                }
            Func<string[], double> ratio = rs =>
            {
                int t = rs.Sum(r => total[r]), f = rs.Sum(r => filled[r]);
                return t == 0 ? 0 : (double)f / t;
            };
            m["density"] = ratio(Regions.Names);
            m["north"] = ratio(new[] { "NW", "N", "NE" });
            m["south"] = ratio(new[] { "SW", "S", "SE" });
            m["west"] = ratio(new[] { "NW", "W", "SW" });
            m["east"] = ratio(new[] { "NE", "E", "SE" });
            m["center"] = ratio(new[] { "C" });
            m["outer"] = ratio(Regions.Names.Where(r => r != "C").ToArray());
            foreach (var r in Regions.Names) m["r_" + r] = ratio(new[] { r });

            var tc = MapMetrics.TagCounts(g);
            int obs = tc.Skip(1).Sum();
            for (int t = 1; t < ObstacleSet.Count; t++)
                m["share_" + ObstacleSet.Tags[t]] = obs == 0 ? 0 : (double)tc[t] / obs;

            m["path"] = ObstacleGenerator.PathExists(map, g) ? 1 : 0;
            m["forbidden_violations"] = MapMetrics.ForbiddenViolations(map, g, p);
            m["adjacency_violations"] = MapMetrics.AdjacencyViolations(g);
            return m;
        }
    }

    public class CriterionResult
    {
        public string Clause;
        public double Left, Right;
        public bool Pass;
    }

    public static class Criteria
    {
        static readonly string[] Ops = { ">=", "<=", "==", ">", "<" };

        public static bool Evaluate(string expr, Dictionary<string, double> m, out List<CriterionResult> details)
        {
            details = new List<CriterionResult>();
            bool all = true;
            foreach (var raw in expr.Split('&'))
            {
                string clause = raw.Trim();
                if (clause.Length == 0) continue;
                string op = null; int at = -1;
                foreach (var o in Ops) { at = clause.IndexOf(o, StringComparison.Ordinal); if (at >= 0) { op = o; break; } }
                if (op == null) throw new FormatException("비교 연산자가 없는 조건: " + clause);
                double l = Side(clause.Substring(0, at), m), r = Side(clause.Substring(at + op.Length), m);
                bool pass;
                switch (op)
                {
                    case ">=": pass = l >= r; break;
                    case "<=": pass = l <= r; break;
                    case ">": pass = l > r; break;
                    case "<": pass = l < r; break;
                    default: pass = Math.Abs(l - r) < 1e-9; break;
                }
                details.Add(new CriterionResult { Clause = clause, Left = l, Right = r, Pass = pass });
                all &= pass;
            }
            return all;
        }

        public static bool Evaluate(string expr, Dictionary<string, double> m)
        {
            List<CriterionResult> d; return Evaluate(expr, m, out d);
        }

        // 기준식에 쓰인 지표 이름이 모두 존재하는지 검사 (오타 방지)
        public static List<string> UnknownNames(string expr, Dictionary<string, double> m)
        {
            var bad = new List<string>();
            foreach (var tok in expr.Split(new[] { '&', '+', '*', '>', '<', '=', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                double d;
                if (double.TryParse(tok, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) continue;
                if (!m.ContainsKey(tok) && !bad.Contains(tok)) bad.Add(tok);
            }
            return bad;
        }

        static double Side(string s, Dictionary<string, double> m)
        {
            double sum = 0;
            foreach (var term in s.Split('+'))
            {
                double prod = 1;
                foreach (var f in term.Split('*'))
                {
                    string t = f.Trim();
                    double v;
                    if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out v)) prod *= v;
                    else if (m.TryGetValue(t, out v)) prod *= v;
                    else throw new FormatException("알 수 없는 지표 이름: " + t);
                }
                sum += prod;
            }
            return sum;
        }
    }
}
