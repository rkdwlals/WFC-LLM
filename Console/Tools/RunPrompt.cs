// RunPrompt.cs — 콘솔에서 프롬프트 하나를 넣어 4개 조건(B0, A1, 제안, B1)을 한 번에 돌려보는 도구.
// LLM 원문 응답은 logs/ 폴더에 저장하고, --replay-* 옵션으로 API 호출 없이 다시 돌릴 수 있다.
//
// 빌드:  mcs -out:runprompt.exe -r:System.Net.Http Core/*.cs Tools/RunPrompt.cs
// 실행:  export LLM_API_KEY=...   (Windows: set LLM_API_KEY=...)
//        mono runprompt.exe --provider anthropic --model 모델이름 --prompt "북쪽에 장애물을 몰아줘"

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using ObstacleWFC;

class RunPrompt
{
    static int Main(string[] args)
    {
        Console.OutputEncoding = new UTF8Encoding(false); // BOM 없이
        try { return Run(args); }
        catch (Exception e)
        {
            var inner = e is AggregateException && e.InnerException != null ? e.InnerException : e;
            Console.WriteLine("오류: " + inner.Message);
            return 1;
        }
    }

    static int Run(string[] args)
    {
        var opt = ParseArgs(args);
        string prompt = Get(opt, "prompt", null);
        if (prompt == null) { Console.WriteLine("사용법: --prompt \"...\" [--provider anthropic|openai] [--model 이름] [--base-url URL] [--temperature 0.7|none] [--seed 0] [--map 경로] [--replay-params 파일] [--replay-direct 파일] [--skip-direct]"); return 1; }

        var map = BaseMap.Load(Get(opt, "map", "Assets/Maps/base_map_01.txt"));
        int seed = int.Parse(Get(opt, "seed", "0"), CultureInfo.InvariantCulture);
        var cfg = new LlmConfig
        {
            Provider = Get(opt, "provider", "anthropic") == "openai" ? LlmProvider.OpenAICompatible : LlmProvider.Anthropic,
            Model = Get(opt, "model", ""),
            BaseUrl = Get(opt, "base-url", ""),
            ApiKey = LlmApi.ResolveApiKey()
        };
        string temp = Get(opt, "temperature", "0.7");
        cfg.Temperature = temp == "none" ? (double?)null : double.Parse(temp, CultureInfo.InvariantCulture);

        Directory.CreateDirectory("logs");
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

        // ── 제안 방법: 파라미터 JSON
        string paramsText = opt.ContainsKey("replay-params")
            ? File.ReadAllText(opt["replay-params"])
            : CallLlm(cfg, Prompts.ParamsSystem(map), prompt);
        if (!opt.ContainsKey("replay-params")) File.WriteAllText("logs/" + stamp + "_params.txt", paramsText);

        var rep = ParamSchema.Parse(paramsText);
        Console.WriteLine("■ LLM 파라미터 응답");
        Console.WriteLine(paramsText.Trim());
        Console.WriteLine("  JSON 찾음 {0} | 순수 JSON {1} | 스키마 준수 {2}", rep.JsonFound, rep.PureJson, rep.SchemaValid);
        foreach (var e in rep.Errors) Console.WriteLine("  - " + e);
        if (rep.JsonFound) Console.WriteLine("  적용 파라미터: " + ParamSchema.ToJson(rep.Params));

        // ── B1: 직접 배치
        DirectReport direct = null;
        if (!opt.ContainsKey("skip-direct"))
        {
            string directText = opt.ContainsKey("replay-direct")
                ? File.ReadAllText(opt["replay-direct"])
                : CallLlm(cfg, Prompts.DirectSystem(map), prompt);
            if (!opt.ContainsKey("replay-direct")) File.WriteAllText("logs/" + stamp + "_direct.txt", directText);
            direct = DirectGrid.Parse(directText, map.Width, map.Height);
        }

        File.WriteAllText("logs/" + stamp + "_meta.json",
            "{\"prompt\":" + MiniJson.Quote(prompt) + ",\"provider\":" + MiniJson.Quote(cfg.Provider.ToString()) +
            ",\"model\":" + MiniJson.Quote(cfg.Model) + ",\"temperature\":" + (cfg.Temperature.HasValue ? MiniJson.Num(cfg.Temperature.Value) : "null") +
            ",\"seed\":" + seed + "}");

        // ── 조건별 생성과 지표
        var gen = new ObstacleGenerator(map);
        var shown = new List<KeyValuePair<string, int[,]>>();
        Console.WriteLine("\n■ 조건별 결과 (seed {0})", seed);
        foreach (Condition c in new[] { Condition.B0_Default, Condition.A1_NoRegion, Condition.Proposed })
        {
            if (c != Condition.B0_Default && !rep.JsonFound) { Console.WriteLine("  {0}: LLM 응답에서 JSON을 못 찾아 생성 불가", c); continue; }
            var p = Conditions.ParamsFor(c, rep.Params ?? GenerationParams.Default());
            var res = gen.Generate(p, seed);
            PrintMetrics(c.ToString(), map, res.Success ? res.Obstacles : null, p, res.Success);
            if (c == Condition.Proposed && res.Success) shown.Add(new KeyValuePair<string, int[,]>("제안 방법", res.Obstacles));
        }
        if (direct != null)
        {
            if (!direct.FormatValid)
            {
                Console.WriteLine("  B1_Direct: 형식 오류 → " + string.Join(" / ", direct.Errors.Take(3)));
            }
            else
            {
                PrintMetrics("B1_Direct", map, direct.Obstacles, GenerationParams.Default(), true);
                shown.Add(new KeyValuePair<string, int[,]>("B1 LLM 직접 배치", direct.Obstacles));
            }
        }

        foreach (var kv in shown) { Console.WriteLine("\n■ " + kv.Key); Console.Write(Render(map, kv.Value)); }
        Console.WriteLine("\n원문 응답 저장: logs/" + stamp + "_*.txt");
        return 0;
    }

    static string CallLlm(LlmConfig cfg, string system, string user)
    {
        if (string.IsNullOrEmpty(cfg.ApiKey)) throw new InvalidOperationException("API 키가 없습니다 (환경 변수 LLM_API_KEY 또는 사용자 폴더의 .llm_api_key 파일).");
        string keyProblem = LlmApi.DiagnoseKey(cfg.ApiKey);
        if (keyProblem != null) throw new InvalidOperationException(keyProblem);
        var req = LlmApi.Build(cfg, system, user);
        using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(120) })
        {
            HttpResponseMessage resp; string body;
            for (int attempt = 0; ; attempt++)
            {
                var msg = new HttpRequestMessage(HttpMethod.Post, req.Url);
                foreach (var h in req.Headers)
                    if (h.Key != "content-type") msg.Headers.TryAddWithoutValidation(h.Key, h.Value);
                msg.Content = new StringContent(req.Body, Encoding.UTF8, "application/json");
                resp = http.SendAsync(msg).Result;
                body = resp.Content.ReadAsStringAsync().Result;
                if (resp.IsSuccessStatusCode || !LlmApi.IsRetryable((int)resp.StatusCode) || attempt >= LlmApi.RetryDelaysSeconds.Length) break;
                int wait = LlmApi.RetryDelaysSeconds[attempt];
                Console.WriteLine("  서버 응답 {0} → {1}초 뒤 재시도 ({2}/{3})", (int)resp.StatusCode, wait, attempt + 1, LlmApi.RetryDelaysSeconds.Length);
                System.Threading.Thread.Sleep(wait * 1000);
            }
            if (!resp.IsSuccessStatusCode)
            {
                try { LlmApi.ExtractText(cfg, body); } catch (InvalidOperationException e) { throw new InvalidOperationException("HTTP " + (int)resp.StatusCode + " " + e.Message); }
                throw new InvalidOperationException("HTTP " + (int)resp.StatusCode + ": " + body);
            }
            return LlmApi.ExtractText(cfg, body);
        }
    }

    static void PrintMetrics(string name, BaseMap map, int[,] g, GenerationParams p, bool ok)
    {
        if (!ok || g == null) { Console.WriteLine("  {0}: 생성 실패", name); return; }
        var rr = MapMetrics.RegionRatios(map, g, p);
        var tc = MapMetrics.TagCounts(g);
        int obs = tc.Skip(1).Sum();
        Console.WriteLine("  {0}: 장애물 {1:P1} | 경로 {2} | 금지 위반 {3} | 인접 위반 {4}",
            name, MapMetrics.ObstacleRatio(map, g, p), ObstacleGenerator.PathExists(map, g) ? "OK" : "막힘",
            MapMetrics.ForbiddenViolations(map, g, p), MapMetrics.AdjacencyViolations(g));
        Console.WriteLine("      구역 밀도  NW {0:P0} N {1:P0} NE {2:P0} | W {3:P0} C {4:P0} E {5:P0} | SW {6:P0} S {7:P0} SE {8:P0}",
            rr["NW"], rr["N"], rr["NE"], rr["W"], rr["C"], rr["E"], rr["SW"], rr["S"], rr["SE"]);
        Console.WriteLine("      구성 비율  바위 {0:P0} 벽 {1:P0} 상자 {2:P0} 수풀 {3:P0}",
            Share(tc, 1, obs), Share(tc, 2, obs), Share(tc, 3, obs), Share(tc, 4, obs));
    }

    static double Share(int[] tc, int i, int total) { return total == 0 ? 0 : (double)tc[i] / total; }

    public static string Render(BaseMap map, int[,] g)
    {
        var sb = new StringBuilder();
        for (int y = 0; y < map.Height; y++)
        {
            for (int x = 0; x < map.Width; x++)
            {
                if (g[x, y] != 0) { sb.Append(DirectGrid.Symbols[g[x, y]]); continue; }
                switch (map.Tiles[x, y])
                {
                    case BaseTile.Water: sb.Append('~'); break;
                    case BaseTile.Road: sb.Append('='); break;
                    case BaseTile.Spawn: sb.Append('S'); break;
                    case BaseTile.Goal: sb.Append('G'); break;
                    default: sb.Append('.'); break;
                }
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static Dictionary<string, string> ParseArgs(string[] a)
    {
        var d = new Dictionary<string, string>();
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].StartsWith("--")) continue;
            string k = a[i].Substring(2);
            if (i + 1 < a.Length && !a[i + 1].StartsWith("--")) d[k] = a[++i]; else d[k] = "";
        }
        return d;
    }

    static string Get(Dictionary<string, string> d, string k, string def) { string v; return d.TryGetValue(k, out v) ? v : def; }
}
