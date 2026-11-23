// TestRunner.cs — 콘솔에서 핵심 코드가 의도대로 동작하는지 확인하는 테스트
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using ObstacleWFC;

class TestRunner
{
    static int fails = 0;
    static void Check(bool ok, string msg) { Console.WriteLine((ok ? "[PASS] " : "[FAIL] ") + msg); if (!ok) fails++; }

    static void Main(string[] args)
    {
        var map = BaseMap.Load(args.Length > 0 ? args[0] : "Maps/base_map_01.txt");
        var gen = new ObstacleGenerator(map);
        Console.WriteLine("베이스 맵 {0}x{1}, 시작 ({2},{3}), 목표 ({4},{5})\n", map.Width, map.Height, map.SpawnX, map.SpawnY, map.GoalX, map.GoalY);

        // 1) seed 재현성
        var p0 = GenerationParams.Default();
        var a = gen.Generate(p0, 42); var b = gen.Generate(p0, 42);
        Check(MapMetrics.Hamming(a.Obstacles, b.Obstacles) == 0, "같은 seed → 같은 맵");
        var c = gen.Generate(p0, 43);
        Check(MapMetrics.Hamming(a.Obstacles, c.Obstacles) > 0, "다른 seed → 다른 맵");

        // 2) 조건별 배치 실행
        var north = GenerationParams.Default();
        foreach (var r in new[] { "NW", "N", "NE" }) north.RegionDensity[r] = "high";
        foreach (var r in new[] { "SW", "S", "SE" }) north.RegionDensity[r] = "low";

        var rocky = GenerationParams.Default();
        rocky.TagMultipliers["rock"] = 5.0;

        var dense = GenerationParams.Default();
        foreach (var r in Regions.Names) dense.RegionDensity[r] = "high";
        var sparse = GenerationParams.Default();
        foreach (var r in Regions.Names) sparse.RegionDensity[r] = "low";

        var sets = new List<KeyValuePair<string, GenerationParams>>
        {
            new KeyValuePair<string, GenerationParams>("기본", p0),
            new KeyValuePair<string, GenerationParams>("북쪽 몰기", north),
            new KeyValuePair<string, GenerationParams>("바위 ×5", rocky),
            new KeyValuePair<string, GenerationParams>("전체 high", dense),
            new KeyValuePair<string, GenerationParams>("전체 low", sparse),
        };

        const int N = 200;
        var summary = new Dictionary<string, double[]>();
        foreach (var kv in sets)
        {
            var p = kv.Value;
            int forb = 0, adj = 0, pathFail = 0, genFail = 0, attempts = 0, contra = 0;
            double ratio = 0, northR = 0, southR = 0, rockShare = 0;
            var maps = new List<int[,]>();
            var sw = Stopwatch.StartNew();
            for (int s = 0; s < N; s++)
            {
                var res = gen.Generate(p, s);
                attempts += res.Attempts; contra += res.Contradictions;
                if (!res.Success) { genFail++; continue; }
                if (!res.PathOk) pathFail++;
                forb += MapMetrics.ForbiddenViolations(map, res.Obstacles, p);
                adj += MapMetrics.AdjacencyViolations(res.Obstacles);
                ratio += MapMetrics.ObstacleRatio(map, res.Obstacles, p);
                var rr = MapMetrics.RegionRatios(map, res.Obstacles, p);
                northR += (rr["NW"] + rr["N"] + rr["NE"]) / 3; southR += (rr["SW"] + rr["S"] + rr["SE"]) / 3;
                var tc = MapMetrics.TagCounts(res.Obstacles);
                int obs = tc.Skip(1).Sum();
                rockShare += obs == 0 ? 0 : (double)tc[ObstacleSet.IndexOf("rock")] / obs;
                if (maps.Count < 30) maps.Add(res.Obstacles);
            }
            sw.Stop();
            int ok = N - genFail;
            double div = 0; int pairs = 0;
            for (int i = 0; i < maps.Count; i++) for (int j = i + 1; j < maps.Count; j++) { div += MapMetrics.Hamming(maps[i], maps[j]); pairs++; }

            Console.WriteLine("── {0} ({1}개 seed)", kv.Key, N);
            Console.WriteLine("  생성 실패 {0}, 경로 실패 {1}, 평균 시도 {2:F2}회, 모순 {3}회", genFail, pathFail, (double)attempts / N, contra);
            Console.WriteLine("  금지 위치 위반 {0}, 인접 규칙 위반 {1}", forb, adj);
            Console.WriteLine("  장애물 비율 {0:P1} | 북쪽 {1:P1} / 남쪽 {2:P1} | 바위 비중 {3:P1} | 다양성(해밍) {4:F3} | 맵당 {5:F1} ms",
                ratio / ok, northR / ok, southR / ok, rockShare / ok, div / Math.Max(1, pairs), sw.Elapsed.TotalMilliseconds / N);
            summary[kv.Key] = new[] { ratio / ok, northR / ok, southR / ok, rockShare / ok, forb, adj, pathFail, genFail };
        }

        Console.WriteLine();
        foreach (var kv in summary) { Check(kv.Value[4] == 0, kv.Key + ": 금지 위치 위반 0"); Check(kv.Value[5] == 0, kv.Key + ": 인접 규칙 위반 0"); Check(kv.Value[7] == 0, kv.Key + ": 생성 실패 0"); }
        Check(summary["북쪽 몰기"][1] > summary["북쪽 몰기"][2] * 2, "북쪽 몰기: 북쪽 밀도가 남쪽의 2배 초과");
        Check(summary["바위 ×5"][3] > summary["기본"][3], "바위 ×5: 바위 비중이 기본보다 높음");
        Check(Math.Abs(summary["바위 ×5"][0] - summary["기본"][0]) < 0.05, "바위 ×5: 전체 장애물 비율은 기본과 5%p 이내 (구성만 변경)");
        Check(summary["전체 high"][0] > summary["기본"][0] && summary["기본"][0] > summary["전체 low"][0], "밀도: high > 기본 > low");

        // 3) 예시 맵 출력 (북쪽 몰기, seed 0)
        var ex = gen.Generate(north, 0);
        Console.WriteLine("\n예시: 북쪽 몰기 seed 0  (.땅 ~물 =길 S시작 G목표 / R바위 W벽 C상자 B수풀)");
        string sym = ".RWCB";
        for (int y = 0; y < map.Height; y++)
        {
            var line = new char[map.Width];
            for (int x = 0; x < map.Width; x++)
            {
                int t = ex.Obstacles[x, y];
                if (t != 0) line[x] = sym[t];
                else switch (map.Tiles[x, y]) { case BaseTile.Water: line[x] = '~'; break; case BaseTile.Road: line[x] = '='; break; case BaseTile.Spawn: line[x] = 'S'; break; case BaseTile.Goal: line[x] = 'G'; break; default: line[x] = '.'; break; }
            }
            Console.WriteLine(new string(line));
        }

        Console.WriteLine("\n실패 {0}건", fails);
        Environment.Exit(fails == 0 ? 0 : 1);
    }
}
