// RuleBaseline.cs — B2: LLM 없이 규칙만으로 명령어를 WFC 파라미터로 바꾸는 기준선.
// 목적: "LLM이 아니라 간단한 규칙으로도 되지 않나?"에 답하기 위함.
// 주의: 사전(키워드 목록)은 실험 명령어를 본 뒤에 작성됨 → 기준선에 유리한 쪽으로 편향될 수 있음(보수적 비교).
//       정답 파라미터(hidden)를 보고 조정하지 않음. 한 번 작성 후 고정.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ObstacleWFC;

// RuleBaselineM.cs (2026-11-19) — 새 베이스 맵 실험용 B2.
// RuleBaseline_frozen.cs(md5 c0cefdbf...)와 언어 사전(강도어·종류어·일반 방향어)은 완전히 같고,
// 맵에 따라 가리키는 구역이 달라지는 위치어(시작점, 목표, 다리, 강 기준 방향)만 SetAnchors(map)로 현재 맵에 맞춘다.
// 고정본에서 이 위치어들은 base_map_01 기준(시작점 SW, 목표 NE, 다리 C, 가로 강)으로 하드코딩되어 있었다.
public static class RuleBaselineM
{
    static readonly string[] All = { "NW", "N", "NE", "W", "C", "E", "SW", "S", "SE" };

    // ── 명시형: 숫자를 정규식으로 읽음
    // 구역 밀도 → 수준: 시스템 프롬프트의 보정값(low 0.1, mid 0.3, high 0.5)의 중간점 0.2, 0.4를 경계로 사용
    // 종류 비율 → 배수: 비율에 비례(모두 25%면 1.0) → 배수 = 비율 × 4, 0~10으로 자름
    public static GenerationParams Explicit(string text)
    {
        var p = new GenerationParams();
        var regMap = new Dictionary<string, string> { { "북서", "NW" }, { "북동", "NE" }, { "남서", "SW" }, { "남동", "SE" }, { "중앙", "C" }, { "북", "N" }, { "남", "S" }, { "서", "W" }, { "동", "E" } };
        foreach (Match m in Regex.Matches(text, @"(북서|북동|남서|남동|중앙|북|남|서|동)\s*([0-9]+(?:\.[0-9]+)?)"))
        {
            double v = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            p.RegionDensity[regMap[m.Groups[1].Value]] = v < 0.2 ? "low" : v >= 0.4 ? "high" : "mid";
        }
        var tagMap = new Dictionary<string, string> { { "바위", "rock" }, { "벽", "wall" }, { "상자", "crate" }, { "수풀", "bush" } };
        foreach (Match m in Regex.Matches(text, @"(바위|벽|상자|수풀)\s*([0-9]+(?:\.[0-9]+)?)\s*%"))
            p.TagMultipliers[tagMap[m.Groups[1].Value]] = Math.Min(10, double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) / 100.0 * 4);
        return p;
    }

    // ── 서술형: 방향어 + 강도어 사전
    // 방향어(긴 표현 먼저 찾고 지움)
    static List<KeyValuePair<string, string[]>> Dir;

    // 맵에서 위치어가 가리키는 구역을 계산한다.
    //  - 시작점/목표: 해당 칸이 속한 구역
    //  - 다리: 길(=) 칸이 있는 구역들
    //  - 강: 물 칸이 한 행(또는 열) 전체의 80% 이상인 행(열)들을 강으로 보고, 강의 방향(가로/세로)에 따라
    //        "강 북쪽/위쪽/남쪽/아래쪽"(가로 강) 또는 "강 서쪽/왼쪽/동쪽/오른쪽"(세로 강)이 가리키는 구역을 정한다.
    //        구역의 칸 과반이 강의 그쪽에 있으면 포함한다.
    public static void SetAnchors(BaseMap map)
    {
        int W = map.Width, H = map.Height;
        Func<int, int, string> reg = (x, y) => Regions.Of(x, y, W, H);
        var bridge = new HashSet<string>();
        for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) if (map.Tiles[x, y] == BaseTile.Road) bridge.Add(reg(x, y));
        var riverRows = Enumerable.Range(0, H).Where(y => Enumerable.Range(0, W).Count(x => map.Tiles[x, y] == BaseTile.Water || map.Tiles[x, y] == BaseTile.Road) >= 0.8 * W).ToList();
        var riverCols = Enumerable.Range(0, W).Where(x => Enumerable.Range(0, H).Count(y => map.Tiles[x, y] == BaseTile.Water || map.Tiles[x, y] == BaseTile.Road) >= 0.8 * H).ToList();
        Func<Func<int, int, bool>, string[]> side = pred =>
            All.Where(r => {
                int tot = 0, hit = 0;
                for (int x = 0; x < W; x++) for (int y = 0; y < H; y++) if (reg(x, y) == r) { tot++; if (pred(x, y)) hit++; }
                return hit * 2 > tot; }).ToArray();
        var list = new List<KeyValuePair<string, string[]>>(); Action<string, string[]> add = (k, v) => list.Add(new KeyValuePair<string, string[]>(k, v));
        add("서쪽 절반", new[] { "NW", "W", "SW" }); add("동쪽 절반", new[] { "NE", "E", "SE" });
        if (riverRows.Count > 0)
        {
            int top = riverRows.Min(), bot = riverRows.Max();
            var n = side((x, y) => y < top); var s = side((x, y) => y > bot);
            add("강 북쪽", n); add("강 위쪽", n); add("강 남쪽", s); add("강 아래쪽", s);
        }
        if (riverCols.Count > 0)
        {
            int l = riverCols.Min(), rr = riverCols.Max();
            var w = side((x, y) => x < l); var e = side((x, y) => x > rr);
            add("강 서쪽", w); add("강 왼쪽", w); add("강 동쪽", e); add("강 오른쪽", e);
        }
        add("북서", new[] { "NW" }); add("북동", new[] { "NE" }); add("남서", new[] { "SW" }); add("남동", new[] { "SE" });
        add("목표", new[] { reg(map.GoalX, map.GoalY) }); add("시작점", new[] { reg(map.SpawnX, map.SpawnY) });
        add("중앙", new[] { "C" }); add("가운데", new[] { "C" }); add("다리", bridge.Count > 0 ? bridge.ToArray() : new string[0]);
        add("북쪽", new[] { "N" }); add("남쪽", new[] { "S" }); add("서쪽", new[] { "W" }); add("동쪽", new[] { "E" });
        add("서편", new[] { "W" }); add("동편", new[] { "E" });
        Dir = list;
    }
    public static string AnchorsText() { return string.Join("; ", Dir.Select(d => d.Key + "=" + string.Join("/", d.Value))); }
    static readonly string[] High = { "빽빽", "촘촘", "빼곡", "밀집", "몰려", "몰린", "몰아", "뭉쳐", "뭉친", "뒤덮", "뒤엉", "덩어리", "무리", "무더기", "군집", "가득", "복잡", "답답", "막고", "요새", "숲" };
    static readonly string[] Low = { "비어", "비워", "비운", "트여", "트인", "열린", "열려", "열어", "한산", "듬성듬성", "드문드문", "여유", "시원", "적게", "적은", "적고", "줄어", "널찍", "넓게" };
    static readonly string[] Global = { "전체적으로", "맵 전체", "전체가", "사방", "전반" };

    static readonly (string pat, string tag)[] Tags = { ("바위", "rock"), ("돌무더기", "rock"), ("벽", "wall"), ("상자", "crate"), ("수풀", "bush"), ("덤불", "bush") };
    static readonly string[] TagMany = { "주로", "주된", "주인공", "가장 많", "가장 눈에", "대부분", "압도적", "많은", "많아", "많고", "많이", "지배적" };
    static readonly string[] TagFew = { "드물", "조금", "적게", "소수", "한두", "하나씩", "포인트", "살짝", "몇 개", "작은 무리", "작은 군집" };
    static readonly string[] TagAlmostNone = { "거의 없" };
    static readonly string[] TagNone = { "전혀 없", "없어요", "없음", "없는", "없이" };

    public static GenerationParams Descriptive(string text)
    {
        var p = new GenerationParams();
        var clauses = Regex.Split(text, @"[.,]|(?<=[가-힣])고\s|(?<=[가-힣])며\s|(?<=[가-힣])는데\s").Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
        var specific = new Dictionary<string, string>(); string global = null;
        var onlyTags = new List<string>();
        foreach (var cl in clauses)
        {
            // 강도
            int hi = High.Count(w => cl.Contains(w)), lo = Low.Count(w => cl.Contains(w));
            string lv = cl.Contains("중간 밀도") ? "mid" : hi > lo ? "high" : lo > hi ? "low" : null;
            // 방향
            string rest = cl; var regs = new HashSet<string>();
            foreach (var d in Dir) if (rest.Contains(d.Key)) { foreach (var r in d.Value) regs.Add(r); rest = rest.Replace(d.Key, " "); }
            if (lv != null)
            {
                if (regs.Count > 0) foreach (var r in regs) specific[r] = lv;
                else if (Global.Any(g => cl.Contains(g)) || cl.Contains("맵이에요") || cl.Contains("맵으로") || cl.Contains("필드예요")) global = lv;
                else if (cl.Contains("나머지")) foreach (var r in All) if (!specific.ContainsKey(r)) specific[r] = lv;
            }
            // 종류
            var tags = Tags.Where(t => cl.Contains(t.pat)).Select(t => t.tag).Distinct().ToList();
            if (tags.Count == 0) continue;
            double? m = null;
            if (cl.Contains("만으로") || cl.Contains("만 쓰") || cl.Contains("만 있")) { onlyTags.AddRange(tags); m = 3; }
            else if (TagNone.Any(w => cl.Contains(w)) && !TagAlmostNone.Any(w => cl.Contains(w))) m = 0;
            else if (TagAlmostNone.Any(w => cl.Contains(w))) m = 0.2;
            else if (TagFew.Any(w => cl.Contains(w))) m = 0.4;
            else if (TagMany.Any(w => cl.Contains(w))) m = 3;
            if (m.HasValue) foreach (var t in tags) if (!p.TagMultipliers.ContainsKey(t)) p.TagMultipliers[t] = m.Value;
        }
        if (onlyTags.Count > 0) foreach (var t in new[] { "rock", "wall", "crate", "bush" }) if (!onlyTags.Contains(t)) p.TagMultipliers[t] = 0;
        foreach (var r in All)
        {
            string v = specific.ContainsKey(r) ? specific[r] : global;
            if (v != null) p.RegionDensity[r] = v;
        }
        return p;
    }
}
