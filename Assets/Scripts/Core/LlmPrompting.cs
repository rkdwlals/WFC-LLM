// LlmPrompting.cs
// LLM에 보낼 프롬프트를 만들고, LLM 응답을 파싱·검사하는 코드.
//  - 제안 방법: 프롬프트 → JSON 파라미터(ParamSchema) → WFC
//  - B1(LLM 직접 배치): 프롬프트 → 장애물 격자 텍스트(DirectGrid) → 그대로 맵
// 프롬프트에는 출력 형식 설명만 있고 예시 출력은 없다 (Zero-shot 유지).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ObstacleWFC
{
    // ─────────────────────────────────────────────
    // 비교 조건
    // ─────────────────────────────────────────────
    public enum Condition
    {
        B0_Default,     // LLM 없이 기본 파라미터
        A1_NoRegion,    // LLM 파라미터에서 구역 밀도만 제거 (구역 제약이 필요한지 검증)
        Proposed,       // LLM 파라미터 전체
        B1_Direct       // LLM이 격자를 직접 출력 (WFC 없음) — ParamsFor가 아니라 DirectGrid 사용
    }

    public static class Conditions
    {
        public static GenerationParams ParamsFor(Condition c, GenerationParams llm)
        {
            switch (c)
            {
                case Condition.B0_Default: return GenerationParams.Default();
                case Condition.A1_NoRegion:
                    var p = Clone(llm); p.RegionDensity.Clear(); return p;
                case Condition.Proposed: return Clone(llm);
                default: throw new ArgumentException("B1은 WFC 파라미터를 쓰지 않습니다.");
            }
        }

        public static GenerationParams Clone(GenerationParams src)
        {
            var p = new GenerationParams();
            p.TagMultipliers = new Dictionary<string, double>(src.TagMultipliers);
            p.RegionDensity = new Dictionary<string, string>(src.RegionDensity);
            p.PathRequired = src.PathRequired;
            p.ForbiddenBase = new List<BaseTile>(src.ForbiddenBase);
            return p;
        }
    }

    // ─────────────────────────────────────────────
    // 프롬프트
    // ─────────────────────────────────────────────
    public static class Prompts
    {
        // 베이스 맵을 구역별로 요약 (LLM이 "강 근처", "시작점 주변" 같은 요청을 해석할 수 있게)
        public static string BaseSummary(BaseMap map)
        {
            var total = new Dictionary<string, int>(); var water = new Dictionary<string, int>(); var road = new Dictionary<string, int>();
            foreach (var r in Regions.Names) { total[r] = 0; water[r] = 0; road[r] = 0; }
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    string r = Regions.Of(x, y, map.Width, map.Height);
                    total[r]++;
                    if (map.Tiles[x, y] == BaseTile.Water) water[r]++;
                    if (map.Tiles[x, y] == BaseTile.Road) road[r]++;
                }
            var sb = new StringBuilder();
            foreach (var r in Regions.Names)
            {
                var parts = new List<string>();
                if (water[r] > 0) parts.Add("물 " + (100 * water[r] / total[r]) + "%");
                if (road[r] > 0) parts.Add("길(다리) 있음");
                if (map.SpawnX >= 0 && Regions.Of(map.SpawnX, map.SpawnY, map.Width, map.Height) == r) parts.Add("시작점");
                if (map.GoalX >= 0 && Regions.Of(map.GoalX, map.GoalY, map.Width, map.Height) == r) parts.Add("목표 지점");
                sb.Append("- ").Append(r).Append(": ").Append(parts.Count == 0 ? "땅" : string.Join(", ", parts)).Append('\n');
            }
            return sb.ToString();
        }

        const string Common =
            "맵은 {W}×{H} 격자의 2D 탑다운 게임 맵이고, 위쪽이 북쪽이다.\n" +
            "바닥(베이스 맵)은 고정되어 있고, 장애물만 새로 배치한다.\n" +
            "맵은 다음 3×3 구역으로 나뉜다.\n" +
            "NW N NE\nW  C  E\nSW S SE\n\n" +
            "구역별 베이스 맵 요약:\n{SUMMARY}\n" +
            "장애물은 rock(바위), wall(벽), crate(상자), bush(수풀) 네 종류다.\n" +
            "벽과 상자는 서로 붙을 수 있고, 바위와 수풀은 서로 붙을 수 있다. 두 묶음(벽·상자 / 바위·수풀)은 상하좌우로 붙을 수 없다.\n" +
            "물, 길, 시작점, 목표 지점 칸에는 장애물을 놓을 수 없다.\n\n";

        // 제안 방법(및 A1)용 시스템 프롬프트
        // calibration=false: 수치 보정 힌트(배수·밀도 → 실제 비율) 두 문장을 뺀 버전 (어블레이션 A3용)
        public static string ParamsSystem(BaseMap map, bool calibration = true)
        {
            return Fill(map, Common) +
                "사용자 요청을 읽고 아래 형식의 JSON 객체 하나만 출력하라. 설명 문장이나 코드 블록 표시(```)는 쓰지 마라.\n" +
                "{\n" +
                "  \"tag_multipliers\": { \"rock\": 숫자, \"wall\": 숫자, \"crate\": 숫자, \"bush\": 숫자 },\n" +
                "  \"region_density\": { \"구역 이름\": \"low\" 또는 \"mid\" 또는 \"high\" },\n" +
                "  \"hard_constraints\": { \"path_required\": true 또는 false }\n" +
                "}\n\n" +
                "각 항목의 뜻:\n" +
                "- tag_multipliers: 장애물 종류의 구성 비율을 정한다. 1.0이 기본이고 0 이상 10 이하. 전체 장애물의 양은 바꾸지 않는다.\n" +
                (calibration ? "  배수가 클수록 그 종류의 비율이 커진다. 모두 1이면 각 종류가 약 25%, rock 2이면 바위 약 45%, rock 3이면 약 55%, rock 4이면 약 65%가 된다.\n" : "  배수가 클수록 그 종류의 비율이 커진다.\n") +
                "- region_density: 구역별 장애물의 양을 정한다. 구역 이름은 NW, N, NE, W, C, E, SW, S, SE만 쓴다. 적지 않은 구역은 mid로 처리된다.\n" +
                (calibration ? "  구역에서 장애물이 차지하는 대략적인 비율: low 약 0.1, mid 약 0.3, high 약 0.5.\n" : "") +
                "- path_required: 시작점에서 목표 지점까지 막히지 않은 길을 반드시 남길지 정한다.\n" +
                "요청에서 언급하지 않은 항목은 기본값(배수 1.0, 밀도 mid, path_required true)으로 둔다.\n";
        }

        // Few-shot: 제안 방법 시스템 프롬프트 뒤에 (요청 → 정답 JSON) 예시를 붙인다.
        // examples[i][0] = 요청 문장, examples[i][1] = 정답 JSON 한 줄. 예시는 시험용 목표 맵과 겹치지 않아야 한다.
        public static string ParamsSystemFewShot(BaseMap map, string[][] examples)
        {
            var sb = new StringBuilder(ParamsSystem(map));
            sb.Append("\n아래는 요청과 올바른 출력의 예시다.\n");
            for (int i = 0; i < examples.Length; i++)
            {
                sb.Append("\n예시 ").Append(i + 1).Append('\n');
                sb.Append("요청: ").Append(examples[i][0]).Append('\n');
                sb.Append("출력: ").Append(examples[i][1]).Append('\n');
            }
            return sb.ToString();
        }

        // B1(LLM 직접 배치)용 시스템 프롬프트
        public static string DirectSystem(BaseMap map)
        {
            var baseText = new StringBuilder();
            for (int y = 0; y < map.Height; y++)
            {
                for (int x = 0; x < map.Width; x++)
                {
                    switch (map.Tiles[x, y])
                    {
                        case BaseTile.Water: baseText.Append('~'); break;
                        case BaseTile.Road: baseText.Append('='); break;
                        case BaseTile.Spawn: baseText.Append('S'); break;
                        case BaseTile.Goal: baseText.Append('G'); break;
                        default: baseText.Append('.'); break;
                    }
                }
                baseText.Append('\n');
            }
            return Fill(map, Common) +
                "베이스 맵 전체(첫 줄이 북쪽, 기호: . 땅, ~ 물, = 길, S 시작점, G 목표 지점):\n" + baseText + "\n" +
                "사용자 요청에 맞게 장애물을 배치한 격자 하나만 출력하라. 설명 문장이나 코드 블록 표시(```)는 쓰지 마라.\n" +
                "격자는 정확히 {H}줄이고, 각 줄은 정확히 {W}글자다. 첫 줄이 북쪽이고, 베이스 맵과 같은 위치에 대응한다.\n" +
                "기호: . 빈 칸, R 바위, W 벽, C 상자, B 수풀\n" +
                "요청에서 길을 막으라는 말이 없으면 시작점에서 목표 지점까지 막히지 않은 길을 남긴다.\n";
        }

        static string Fill(BaseMap map, string t)
        {
            return t.Replace("{W}", map.Width.ToString(CultureInfo.InvariantCulture))
                    .Replace("{H}", map.Height.ToString(CultureInfo.InvariantCulture))
                    .Replace("{SUMMARY}", BaseSummary(map));
        }
    }

    // ─────────────────────────────────────────────
    // 제안 방법: JSON 응답 파싱 + 스키마 검사 (RQ1)
    // ─────────────────────────────────────────────
    public class ParseReport
    {
        public bool JsonFound;        // 응답에서 JSON 객체를 찾아 파싱했는가
        public bool PureJson;         // 응답 전체가 JSON 객체 하나뿐인가 (앞뒤 설명·코드블록 없음)
        public bool SchemaValid;      // 스키마 위반이 하나도 없는가
        public List<string> Errors = new List<string>();
        public GenerationParams Params;  // JsonFound일 때: 위반 항목을 기본값으로 대체한 결과
        public string ExtractedJson;
    }

    public static class ParamSchema
    {
        public static readonly string[] Levels = { "low", "mid", "high" };
        public const double MinMult = 0, MaxMult = 10;
        static readonly string[] TopKeys = { "tag_multipliers", "region_density", "hard_constraints" };

        public static ParseReport Parse(string llmText)
        {
            var rep = new ParseReport();
            string json = ExtractJsonObject(llmText ?? "");
            if (json == null) { rep.Errors.Add("응답에서 JSON 객체를 찾지 못함"); return rep; }
            rep.ExtractedJson = json;
            rep.PureJson = llmText.Trim() == json;

            Dictionary<string, object> root;
            try { root = MiniJson.Parse(json) as Dictionary<string, object>; }
            catch (FormatException e) { rep.Errors.Add("JSON 문법 오류: " + e.Message); return rep; }
            if (root == null) { rep.Errors.Add("최상위가 객체가 아님"); return rep; }
            rep.JsonFound = true;

            var p = GenerationParams.Default();
            rep.Params = p;

            foreach (var k in root.Keys)
                if (Array.IndexOf(TopKeys, k) < 0) rep.Errors.Add("알 수 없는 최상위 키: " + k);
            foreach (var k in TopKeys)
                if (!root.ContainsKey(k)) rep.Errors.Add("필수 키 없음: " + k);

            // tag_multipliers
            object v;
            if (root.TryGetValue("tag_multipliers", out v))
            {
                var d = v as Dictionary<string, object>;
                if (d == null) rep.Errors.Add("tag_multipliers가 객체가 아님");
                else foreach (var kv in d)
                    {
                        int idx = ObstacleSet.IndexOf(kv.Key);
                        if (idx <= 0) { rep.Errors.Add("알 수 없는 장애물 태그: " + kv.Key); continue; }
                        if (!(kv.Value is double)) { rep.Errors.Add("배수가 숫자가 아님: " + kv.Key); continue; }
                        double m = (double)kv.Value;
                        if (m < MinMult || m > MaxMult || double.IsNaN(m))
                        {
                            rep.Errors.Add("배수 범위(0~10) 벗어남: " + kv.Key + "=" + MiniJson.Num(m));
                            m = Math.Max(MinMult, Math.Min(MaxMult, double.IsNaN(m) ? 1.0 : m));
                        }
                        p.TagMultipliers[kv.Key] = m;
                    }
            }

            // region_density
            if (root.TryGetValue("region_density", out v))
            {
                var d = v as Dictionary<string, object>;
                if (d == null) rep.Errors.Add("region_density가 객체가 아님");
                else foreach (var kv in d)
                    {
                        if (Array.IndexOf(Regions.Names, kv.Key) < 0) { rep.Errors.Add("알 수 없는 구역: " + kv.Key); continue; }
                        var s = kv.Value as string;
                        if (s == null || Array.IndexOf(Levels, s) < 0) { rep.Errors.Add("밀도 값 오류: " + kv.Key + "=" + kv.Value); continue; }
                        p.RegionDensity[kv.Key] = s;
                    }
            }

            // hard_constraints
            if (root.TryGetValue("hard_constraints", out v))
            {
                var d = v as Dictionary<string, object>;
                if (d == null) rep.Errors.Add("hard_constraints가 객체가 아님");
                else
                {
                    foreach (var k in d.Keys)
                        if (k != "path_required") rep.Errors.Add("알 수 없는 hard_constraints 키: " + k);
                    object pr;
                    if (!d.TryGetValue("path_required", out pr)) rep.Errors.Add("필수 키 없음: path_required");
                    else if (!(pr is bool)) rep.Errors.Add("path_required가 true/false가 아님");
                    else p.PathRequired = (bool)pr;
                }
            }

            rep.SchemaValid = rep.Errors.Count == 0;
            return rep;
        }

        // 텍스트에서 첫 번째 '{'부터 짝이 맞는 '}'까지 잘라낸다 (문자열 안의 괄호는 무시)
        public static string ExtractJsonObject(string text)
        {
            int start = text.IndexOf('{');
            while (start >= 0)
            {
                int depth = 0; bool inStr = false, esc = false;
                for (int i = start; i < text.Length; i++)
                {
                    char c = text[i];
                    if (inStr)
                    {
                        if (esc) esc = false;
                        else if (c == '\\') esc = true;
                        else if (c == '"') inStr = false;
                        continue;
                    }
                    if (c == '"') inStr = true;
                    else if (c == '{') depth++;
                    else if (c == '}' && --depth == 0) return text.Substring(start, i - start + 1);
                }
                start = text.IndexOf('{', start + 1);
            }
            return null;
        }

        // 파라미터를 JSON으로 (로그·논문 부록용)
        public static string ToJson(GenerationParams p)
        {
            var sb = new StringBuilder("{\"tag_multipliers\":{");
            sb.Append(string.Join(",", p.TagMultipliers.Select(kv => MiniJson.Quote(kv.Key) + ":" + MiniJson.Num(kv.Value))));
            sb.Append("},\"region_density\":{");
            sb.Append(string.Join(",", p.RegionDensity.Select(kv => MiniJson.Quote(kv.Key) + ":" + MiniJson.Quote(kv.Value))));
            sb.Append("},\"hard_constraints\":{\"path_required\":").Append(p.PathRequired ? "true" : "false").Append("}}");
            return sb.ToString();
        }
    }

    // ─────────────────────────────────────────────
    // B1: LLM 직접 배치 격자 파싱
    // ─────────────────────────────────────────────
    public class DirectReport
    {
        public bool FormatValid;      // 정확히 H줄 × W글자, 허용 기호만
        public List<string> Errors = new List<string>();
        public int[,] Obstacles;      // FormatValid일 때만
    }

    public static class DirectGrid
    {
        public const string Symbols = ".RWCB"; // ObstacleSet.Tags 순서와 같다 (empty, rock, wall, crate, bush)

        public static DirectReport Parse(string llmText, int w, int h)
        {
            var rep = new DirectReport();
            var rows = new List<string>();
            foreach (var raw in (llmText ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("```")) continue;
                if (line.All(c => Symbols.IndexOf(c) >= 0)) rows.Add(line);
            }
            if (rows.Count != h) rep.Errors.Add("격자 줄 수 " + rows.Count + " (필요: " + h + ")");
            for (int y = 0; y < rows.Count; y++)
                if (rows[y].Length != w) rep.Errors.Add((y + 1) + "번째 줄 길이 " + rows[y].Length + " (필요: " + w + ")");
            if (rep.Errors.Count > 0) return rep;

            rep.Obstacles = new int[w, h];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    rep.Obstacles[x, y] = Symbols.IndexOf(rows[y][x]);
            rep.FormatValid = true;
            return rep;
        }

        public static string ToText(int[,] g)
        {
            int w = g.GetLength(0), h = g.GetLength(1);
            var sb = new StringBuilder();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) sb.Append(Symbols[g[x, y]]);
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
