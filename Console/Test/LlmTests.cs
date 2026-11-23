// LlmTests.cs — API 키 없이 가짜 LLM 응답으로 LLM 연동 코드를 검사한다.
using System;
using System.Collections.Generic;
using System.Linq;
using ObstacleWFC;

class LlmTests
{
    static int fails = 0;
    static void Check(bool ok, string msg) { Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + msg); if (!ok) fails++; }

    static void Main(string[] args)
    {
        var map = BaseMap.Load(args.Length > 0 ? args[0] : "Maps/base_map_01.txt");

        // 1) 정상 응답 (JSON만)
        string pure = "{\"tag_multipliers\":{\"rock\":3,\"wall\":0.5,\"crate\":1,\"bush\":1},\"region_density\":{\"NW\":\"high\",\"N\":\"high\",\"NE\":\"high\",\"SW\":\"low\",\"S\":\"low\",\"SE\":\"low\"},\"hard_constraints\":{\"path_required\":true}}";
        var r1 = ParamSchema.Parse(pure);
        Check(r1.JsonFound && r1.PureJson && r1.SchemaValid, "정상 JSON → 찾음·순수·스키마 준수");
        Check(r1.Params.TagMultipliers["rock"] == 3 && r1.Params.RegionDensity["N"] == "high" && r1.Params.PathRequired, "정상 JSON → 값이 그대로 들어감");

        // 2) 설명 + 코드 블록으로 감싼 응답
        var r2 = ParamSchema.Parse("요청을 반영했습니다.\n```json\n" + pure + "\n```\n북쪽이 빽빽합니다.");
        Check(r2.JsonFound && !r2.PureJson && r2.SchemaValid, "코드 블록 응답 → 찾음·순수 아님·스키마 준수");

        // 3) 잘못된 값들
        string bad = "{\"tag_multipliers\":{\"rock\":-3,\"tree\":2,\"wall\":\"많이\"},\"region_density\":{\"north\":\"high\",\"C\":\"very high\",\"S\":\"low\"},\"extra\":1}";
        var r3 = ParamSchema.Parse(bad);
        Check(r3.JsonFound && !r3.SchemaValid, "잘못된 값 → 스키마 위반으로 판정");
        foreach (var e in r3.Errors) Console.WriteLine("       · " + e);
        Check(r3.Errors.Count == 7, "잘못된 값 → 위반 7건 (음수 배수, 없는 태그, 문자 배수, 없는 구역, 없는 밀도 값, 모르는 키, hard_constraints 없음)");
        Check(r3.Params.TagMultipliers["rock"] == 0 && r3.Params.RegionDensity.Count == 1 && r3.Params.RegionDensity["S"] == "low" && r3.Params.PathRequired,
              "잘못된 값 → 범위 밖은 잘라내고, 틀린 항목은 버리고, 나머지는 기본값");

        // 4) JSON 없음 / 문법 오류
        Check(!ParamSchema.Parse("죄송하지만 이해하지 못했습니다.").JsonFound, "JSON 없는 응답 → 찾지 못함");
        Check(!ParamSchema.Parse("{\"tag_multipliers\": {\"rock\": 2,}}").JsonFound, "문법 오류(쉼표) → 파싱 실패");

        // 5) 문자열 안의 괄호
        var r5 = ParamSchema.Parse("{\"tag_multipliers\":{},\"region_density\":{},\"hard_constraints\":{\"path_required\":false},\"note\":\"a}b{\"}");
        Check(r5.JsonFound && r5.Errors.Count == 1 && !r5.Params.PathRequired, "문자열 안의 괄호 → 올바르게 잘라냄");

        // 6) 요청 본문: 한글·따옴표·줄바꿈이 깨지지 않는지 (본문을 다시 파싱해서 비교)
        string sys = Prompts.ParamsSystem(map);
        string user = "북쪽에 \"바위\"를\n몰아줘 \\ 탭\t끝";
        var ca = new LlmConfig { Provider = LlmProvider.Anthropic, Model = "test-model", ApiKey = "k", Temperature = 0.7 };
        var reqA = LlmApi.Build(ca, sys, user);
        var bodyA = (Dictionary<string, object>)MiniJson.Parse(reqA.Body);
        var msgA = (Dictionary<string, object>)((List<object>)bodyA["messages"])[0];
        Check(reqA.Url == "https://api.anthropic.com/v1/messages" && reqA.Headers["x-api-key"] == "k" && reqA.Headers.ContainsKey("anthropic-version"), "Anthropic 요청 → 주소·헤더");
        Check((string)bodyA["system"] == sys && (string)msgA["content"] == user && (double)bodyA["temperature"] == 0.7 && bodyA.ContainsKey("max_tokens"), "Anthropic 요청 → 본문 왕복 일치");

        var co = new LlmConfig { Provider = LlmProvider.OpenAICompatible, Model = "m", ApiKey = "k", Temperature = null, BaseUrl = "http://localhost:11434/v1/" };
        var reqO = LlmApi.Build(co, sys, user);
        var bodyO = (Dictionary<string, object>)MiniJson.Parse(reqO.Body);
        var msgs = (List<object>)bodyO["messages"];
        Check(reqO.Url == "http://localhost:11434/v1/chat/completions" && reqO.Headers["Authorization"] == "Bearer k", "OpenAI 형식 요청 → 주소(BaseUrl 변경)·헤더");
        Check(msgs.Count == 2 && (string)((Dictionary<string, object>)msgs[1])["content"] == user && !bodyO.ContainsKey("temperature"), "OpenAI 형식 요청 → 본문 일치, temperature 생략");

        // 7) 응답에서 텍스트 꺼내기
        string respA = "{\"id\":\"x\",\"type\":\"message\",\"content\":[{\"type\":\"text\",\"text\":" + MiniJson.Quote(pure) + "}],\"stop_reason\":\"end_turn\"}";
        Check(LlmApi.ExtractText(ca, respA) == pure, "Anthropic 응답 → 텍스트 추출");
        string respO = "{\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" + MiniJson.Quote(pure) + "}}]}";
        Check(LlmApi.ExtractText(co, respO) == pure, "OpenAI 형식 응답 → 텍스트 추출");
        bool threw = false;
        try { LlmApi.ExtractText(ca, "{\"type\":\"error\",\"error\":{\"type\":\"authentication_error\",\"message\":\"invalid x-api-key\"}}"); }
        catch (InvalidOperationException e) { threw = e.Message.Contains("invalid x-api-key"); }
        Check(threw, "API 오류 응답 → 오류 메시지로 예외");

        // 8) 프롬프트 내용
        Check(sys.Contains("SW: 시작점") && sys.Contains("NE: 목표 지점") && sys.Contains("W: 물"), "시스템 프롬프트 → 베이스 요약(시작 SW, 목표 NE, 물) 포함");
        Check(!sys.Contains("\"rock\": 3") && !sys.Contains("예시"), "시스템 프롬프트 → 예시 출력 없음 (Zero-shot)");
        Console.WriteLine("\n----- 제안 방법 시스템 프롬프트 -----\n" + sys + "-------------------------------------\n");

        // 9) B1 격자 파싱
        var gen = new ObstacleGenerator(map);
        var real = gen.Generate(GenerationParams.Default(), 7).Obstacles;
        var d1 = DirectGrid.Parse("배치 결과입니다:\n```\n" + DirectGrid.ToText(real) + "```", map.Width, map.Height);
        Check(d1.FormatValid && MapMetrics.Hamming(d1.Obstacles, real) == 0, "B1 격자 → 코드 블록·설명 섞여도 정확히 복원");
        var lines = DirectGrid.ToText(real).Split('\n').Where(l => l.Length > 0).ToList();
        Check(!DirectGrid.Parse(string.Join("\n", lines.Skip(1)), map.Width, map.Height).FormatValid, "B1 격자 → 31줄이면 형식 오류");
        lines[3] = lines[3].Substring(1);
        Check(!DirectGrid.Parse(string.Join("\n", lines), map.Width, map.Height).FormatValid, "B1 격자 → 한 줄이 31글자면 형식 오류");

        // B1: LLM이 물 위·규칙 위반으로 배치한 경우 지표가 잡아내는지
        var bad1 = new int[map.Width, map.Height];
        bad1[5, 6] = ObstacleSet.IndexOf("rock");   // 서쪽 연못 (물)
        bad1[10, 10] = ObstacleSet.IndexOf("wall"); bad1[11, 10] = ObstacleSet.IndexOf("rock"); // 벽-바위 인접
        var dp = GenerationParams.Default();
        Check(MapMetrics.ForbiddenViolations(map, bad1, dp) == 1 && MapMetrics.AdjacencyViolations(bad1) == 1, "B1 위반 → 금지 위치 1, 인접 위반 1 검출");

        // 10) 전체 흐름: 프롬프트 응답 → 조건별 파라미터 → 생성
        var pNorth = r1.Params;
        double north = 0, south = 0, northA1 = 0, southA1 = 0, rockP = 0, rockB0 = 0; int n = 50;
        for (int s = 0; s < n; s++)
        {
            var pp = Conditions.ParamsFor(Condition.Proposed, pNorth);
            var ga = gen.Generate(pp, s).Obstacles; var rr = MapMetrics.RegionRatios(map, ga, pp);
            north += rr["N"] + rr["NW"] + rr["NE"]; south += rr["S"] + rr["SW"] + rr["SE"];
            var tc = MapMetrics.TagCounts(ga); rockP += (double)tc[1] / tc.Skip(1).Sum();

            var pa = Conditions.ParamsFor(Condition.A1_NoRegion, pNorth);
            var gb = gen.Generate(pa, s).Obstacles; var ra = MapMetrics.RegionRatios(map, gb, pa);
            northA1 += ra["N"] + ra["NW"] + ra["NE"]; southA1 += ra["S"] + ra["SW"] + ra["SE"];

            var p0 = Conditions.ParamsFor(Condition.B0_Default, pNorth);
            var tc0 = MapMetrics.TagCounts(gen.Generate(p0, s).Obstacles); rockB0 += (double)tc0[1] / tc0.Skip(1).Sum();
        }
        Console.WriteLine("  제안: 북쪽 {0:P1} / 남쪽 {1:P1}, 바위 비중 {2:P1} | A1: 북쪽 {3:P1} / 남쪽 {4:P1} | B0 바위 비중 {5:P1}",
            north / (3 * n), south / (3 * n), rockP / n, northA1 / (3 * n), southA1 / (3 * n), rockB0 / n);
        Check(north > 2 * south, "제안 방법 → 북쪽 밀도가 남쪽의 2배 초과");
        Check(Math.Abs(northA1 - southA1) / (3 * n) < 0.05, "A1(구역 제약 제거) → 북·남 밀도 차이 5%p 미만");
        Check(rockP > rockB0 + 0.1, "제안 방법 → 바위 비중이 B0보다 10%p 이상 높음");
        Check(Conditions.ParamsFor(Condition.A1_NoRegion, pNorth).RegionDensity.Count == 0 && pNorth.RegionDensity.Count == 6, "A1 → 원본 파라미터는 건드리지 않음");

        Console.WriteLine("\n실패 {0}건", fails);
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
