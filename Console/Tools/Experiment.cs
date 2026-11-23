// Experiment.cs — 목표 맵 기반 실험 (교수님 피드백 반영판)
//   targets : 정답 파라미터를 숨긴 목표 맵과 명시형 명령어를 만든다 → Experiment/v2/targets.json, grids/*.txt
//   jobs    : targets.json(서술형 명령어 포함)으로 Unity가 보낼 LLM 요청 목록을 만든다 → jobs.json
//   eval    : Unity가 저장한 응답으로 조건별 맵을 만들고 지표를 계산한다 → results.csv, consistency.csv
using System; using System.Collections.Generic; using System.Globalization; using System.IO; using System.Linq; using System.Text;
using ObstacleWFC;

static class Experiment
{
    static BaseMap Map; static ObstacleGenerator Gen; static string Dir;
    static readonly string[] Reg = Regions.Names;
    static readonly Dictionary<string, string> RegKo = new Dictionary<string, string> {
        {"NW","북서"},{"N","북"},{"NE","북동"},{"W","서"},{"C","중앙"},{"E","동"},{"SW","남서"},{"S","남"},{"SE","남동"} };
    static readonly string[] TagKo = { "", "바위", "벽", "상자", "수풀" };
    static CultureInfo Inv = CultureInfo.InvariantCulture;

    // 구역 패턴 (NW N NE / W C E / SW S SE 순서, H/M/L)
    static readonly Dictionary<string, string> Pat = new Dictionary<string, string> {
        {"P01","HHHMMMLLL"},{"P02","LLLMMMHHH"},{"P03","HMLHMLHML"},{"P04","LMHLMHLMH"},{"P05","LLLLHLLLL"},
        {"P06","HHHHLHHHH"},{"P07","MMHMMMMMM"},{"P08","HHHHHHLHH"},{"P09","HLLLHLLLH"},{"P10","HHHHHHHHH"},
        {"P11","LLLLLLLLL"},{"P12","HLHLLLHLH"},{"P13","LHLLHLLHL"},{"P14","LLLHHHLLL"},{"P15","LLHLHLHLL"},
        {"P16","HHMHMMMMM"},{"P17","LLLLLHLHH"},{"P18","MMMMMMMMM"},{"P19","LLLMHMHHH"},{"P20","MMHMMHMMH"},
        {"F01","HHMHMMMML"},{"F02","LLLMMMMMM"},{"F03","MMMMMMMMM"} };
    // 종류 배수 프로필 (rock, wall, crate, bush)
    static readonly Dictionary<string, double[]> Tag = new Dictionary<string, double[]> {
        {"T0",new[]{1.0,1,1,1}},{"T1",new[]{4.0,1,1,1}},{"T2",new[]{0.3,3,3,0.3}},{"T3",new[]{2.0,0.2,0.2,3}},
        {"T4",new[]{1.0,1,4,1}},{"T5",new[]{1.0,0,1,1}},{"T6",new[]{1.0,1,1,4}},{"T7",new[]{1.0,4,1,1}},
        {"TF1",new[]{3.0,1,1,1}},{"TF2",new[]{0.3,3,3,0.3}},{"TF3",new[]{0.0,1,1,0}} };
    // 시험용 목표 20개 + few-shot 예시용 3개 (예시는 시험용과 패턴·종류 조합이 다름)
    static readonly string[][] Targets = {
        new[]{"T01","P01","T1"},new[]{"T02","P02","T4"},new[]{"T03","P03","T3"},new[]{"T04","P04","T2"},new[]{"T05","P05","T7"},
        new[]{"T06","P06","T2"},new[]{"T07","P07","T0"},new[]{"T08","P08","T3"},new[]{"T09","P09","T6"},new[]{"T10","P10","T3"},
        new[]{"T11","P11","T7"},new[]{"T12","P12","T1"},new[]{"T13","P13","T5"},new[]{"T14","P14","T0"},new[]{"T15","P15","T4"},
        new[]{"T16","P16","T6"},new[]{"T17","P17","T2"},new[]{"T18","P18","T1"},new[]{"T19","P19","T5"},new[]{"T20","P20","T6"},
        new[]{"FS1","F01","TF1"},new[]{"FS2","F02","TF2"},new[]{"FS3","F03","TF3"} };

    static GenerationParams Hidden(string pat, string tag)
    {
        var p = new GenerationParams();
        string[] names = { "rock", "wall", "crate", "bush" };
        for (int i = 0; i < 4; i++) p.TagMultipliers[names[i]] = Tag[tag][i];
        for (int i = 0; i < 9; i++) p.RegionDensity[Reg[i]] = Pat[pat][i] == 'H' ? "high" : Pat[pat][i] == 'L' ? "low" : "mid";
        p.PathRequired = true;
        return p;
    }

    static double[] RegionVec(int[,] g) { var r = MapMetrics.RegionRatios(Map, g, GenerationParams.Default()); return Reg.Select(k => r[k]).ToArray(); }
    static double[] Shares(int[,] g)
    {
        var c = MapMetrics.TagCounts(g); double tot = c.Skip(1).Sum();
        return Enumerable.Range(1, 4).Select(t => tot == 0 ? 0 : c[t] / tot).ToArray();
    }
    static string GridText(int[,] g)
    {
        var sb = new StringBuilder();
        for (int y = 0; y < Map.Height; y++)
        {
            for (int x = 0; x < Map.Width; x++)
            {
                var b = Map.Tiles[x, y];
                char c = b == BaseTile.Water ? '~' : b == BaseTile.Road ? '=' : b == BaseTile.Spawn ? 'S' : b == BaseTile.Goal ? 'G' : DirectGrid.Symbols[g[x, y]];
                sb.Append(c);
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }
    static string R2(double v) { return (Math.Round(v * 20) / 20).ToString("0.00", Inv); }

    static string ExplicitPrompt(int[,] g)
    {
        var rv = RegionVec(g); var sh = Shares(g);
        var sb = new StringBuilder("구역별 장애물 밀도(장애물을 놓을 수 있는 칸 중 장애물 비율)를 ");
        sb.Append(string.Join(", ", Enumerable.Range(0, 9).Select(i => RegKo[Reg[i]] + " " + R2(rv[i]))));
        sb.Append("로 맞춰줘. 장애물 종류 비율은 ");
        sb.Append(string.Join(", ", Enumerable.Range(0, 4).Select(i => TagKo[i + 1] + " " + (Math.Round(sh[i] * 20) * 5).ToString(Inv) + "%")));
        sb.Append("로 해줘. 시작점에서 목표 지점까지 길은 남겨줘.");
        return sb.ToString();
    }

    static void BuildTargets()
    {
        Directory.CreateDirectory(Path.Combine(Dir, "grids"));
        var sb = new StringBuilder("{\"map\":\"Maps/base_map_01.txt\",\"targets\":[\n");
        for (int i = 0; i < Targets.Length; i++)
        {
            string id = Targets[i][0]; var hp = Hidden(Targets[i][1], Targets[i][2]);
            int seed = (id.StartsWith("FS") ? 6000 : 5000) + i;
            var res = Gen.Generate(hp, seed);
            var g = res.Obstacles;
            File.WriteAllText(Path.Combine(Dir, "grids", id + ".txt"), GridText(g));
            var rv = RegionVec(g); var sh = Shares(g);
            sb.Append("{\"id\":").Append(MiniJson.Quote(id))
              .Append(",\"role\":").Append(MiniJson.Quote(id.StartsWith("FS") ? "fewshot" : "test"))
              .Append(",\"pattern\":").Append(MiniJson.Quote(Targets[i][1])).Append(",\"tags\":").Append(MiniJson.Quote(Targets[i][2]))
              .Append(",\"seed\":").Append(seed)
              .Append(",\"hidden\":").Append(ParamSchema.ToJson(hp))
              .Append(",\"density\":").Append(MiniJson.Num(Math.Round(MapMetrics.ObstacleRatio(Map, g, hp), 4)))
              .Append(",\"region\":[").Append(string.Join(",", rv.Select(v => MiniJson.Num(Math.Round(v, 4))))).Append("]")
              .Append(",\"shares\":[").Append(string.Join(",", sh.Select(v => MiniJson.Num(Math.Round(v, 4))))).Append("]")
              .Append(",\"explicit\":").Append(MiniJson.Quote(ExplicitPrompt(g)))
              .Append(",\"descriptive\":\"\"}").Append(i < Targets.Length - 1 ? ",\n" : "\n");
        }
        sb.Append("]}\n");
        File.WriteAllText(Path.Combine(Dir, "targets.json"), sb.ToString());
        Console.WriteLine("targets.json, grids/ 작성: " + Targets.Length + "개");
    }

    static List<Dictionary<string, object>> LoadTargets()
    {
        var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(Path.Combine(Dir, "targets.json")));
        return ((List<object>)root["targets"]).Cast<Dictionary<string, object>>().ToList();
    }
    static string S(Dictionary<string, object> d, string k) { return d[k] as string; }

    static void BuildJobs(int reps)
    {
        var ts = LoadTargets();
        var pool = ts.Where(t => S(t, "role") == "fewshot").ToList();
        var ex = new List<string[]>();
        foreach (var t in pool)
        {
            string hj = MiniJson.Quote("x"); // placeholder
            var hidden = (Dictionary<string, object>)t["hidden"];
            string json = HiddenJson(hidden);
            if (string.IsNullOrEmpty(S(t, "descriptive"))) throw new Exception(S(t, "id") + " 서술형 명령어 없음");
            ex.Add(new[] { S(t, "descriptive"), json });
            ex.Add(new[] { S(t, "explicit"), json });
        }
        var sb = new StringBuilder("{\"version\":\"v2\",\"created\":").Append(MiniJson.Quote(DateTime.Now.ToString("yyyy-MM-dd HH:mm")))
            .Append(",\"systems\":{\"ZS\":").Append(MiniJson.Quote(Prompts.ParamsSystem(Map)))
            .Append(",\"FS\":").Append(MiniJson.Quote(Prompts.ParamsSystemFewShot(Map, ex.ToArray())))
            .Append(",\"B1\":").Append(MiniJson.Quote(Prompts.DirectSystem(Map))).Append("},\"jobs\":[\n");
        var jobs = new List<string>();
        // 순서: 반복(rep)을 가장 바깥에 두어, 중간에 한도가 끝나도 모든 목표가 최소 1회씩 먼저 채워지게 한다
        for (int r = 1; r <= reps; r++)
            foreach (var t in ts.Where(t => S(t, "role") == "test"))
                foreach (var pt in new[] { "D", "X" })
                    foreach (var c in new[] { "ZS", "FS", "B1" })
                    {
                        if (c == "B1" && pt == "X") continue;
                        string user = pt == "D" ? S(t, "descriptive") : S(t, "explicit");
                        if (string.IsNullOrEmpty(user)) throw new Exception(S(t, "id") + " 명령어 없음");
                        string id = S(t, "id") + "_" + pt + "_" + c + "_r" + r;
                        jobs.Add("{\"id\":" + MiniJson.Quote(id) + ",\"system\":" + MiniJson.Quote(c) + ",\"user\":" + MiniJson.Quote(user) + "}");
                    }
        sb.Append(string.Join(",\n", jobs)).Append("\n]}\n");
        File.WriteAllText(Path.Combine(Dir, "jobs.json"), sb.ToString());
        Console.WriteLine("jobs.json 작성: 요청 " + jobs.Count + "개");
    }

    static string HiddenJson(Dictionary<string, object> h)
    {
        var p = ToParams(h);
        return ParamSchema.ToJson(p);
    }
    static GenerationParams ToParams(Dictionary<string, object> h)
    {
        var rep = ParamSchema.Parse(ToJsonObj(h));
        return rep.Params;
    }
    static string ToJsonObj(object o)
    {
        if (o is Dictionary<string, object>) return "{" + string.Join(",", ((Dictionary<string, object>)o).Select(kv => MiniJson.Quote(kv.Key) + ":" + ToJsonObj(kv.Value))) + "}";
        if (o is List<object>) return "[" + string.Join(",", ((List<object>)o).Select(ToJsonObj)) + "]";
        if (o is string) return MiniJson.Quote((string)o);
        if (o is bool) return (bool)o ? "true" : "false";
        if (o == null) return "null";
        return MiniJson.Num(Convert.ToDouble(o, Inv));
    }

    // ───────── eval ─────────
    class MapRes { public int[,] G; public List<int[]> Path; public double[] Rv; public double[] Sh; }
    static MapRes Info(int[,] g) { return new MapRes { G = g, Path = PathMetrics.ShortestPath(Map, g), Rv = RegionVec(g), Sh = Shares(g) }; }
    static string F(double v) { return double.IsNaN(v) ? "" : v.ToString("0.####", Inv); }

    static void Eval(string respDir, int nSeeds)
    {
        var ts = LoadTargets().Where(t => S(t, "role") == "test").ToList();
        var outR = new StringBuilder("target,ptype,cond,rep,seed,json_found,pure_json,schema_valid,format_valid,region_corr,region_mae,density_err,type_l1,adj_viol,forbid_viol,path_ok,path_len,pathcorr_offset,pathcorr_xy,path_dist,level_agree,top_tag_agree\n");
        var outC = new StringBuilder("target,ptype,cond,kind,a,b,region_corr,pathcorr_offset,pathcorr_xy,path_dist,level_agree\n");
        int[] seeds = Enumerable.Range(0, nSeeds).Select(s => 100 + s).ToArray();
        int missing = 0;
        foreach (var t in ts)
        {
            string tid = S(t, "id");
            var tg = ReadGrid(Path.Combine(Dir, "grids", tid + ".txt"));
            var T = Info(tg); var hp = ToParams((Dictionary<string, object>)t["hidden"]);
            // 기준선: B0(LLM 없음), UB(정답 파라미터 = 상한)
            foreach (var cond in new[] { "B0", "UB" })
            {
                var p = cond == "B0" ? GenerationParams.Default() : hp;
                var maps = seeds.Select(s => Info(Gen.Generate(p, s).Obstacles)).ToList();
                for (int i = 0; i < seeds.Length; i++) outR.Append(Row(tid, "-", cond, 0, seeds[i], null, maps[i], T, p, hp));
                SeedPairs(outC, tid, "-", cond, maps);
            }
            foreach (var pt in new[] { "D", "X" })
            {
                foreach (var cond in new[] { "ZS", "FS" })
                {
                    var perRep = new List<List<MapRes>>(); var repParams = new List<GenerationParams>();
                    for (int r = 1; ; r++)
                    {
                        string f = Path.Combine(respDir, tid + "_" + pt + "_" + cond + "_r" + r + ".txt");
                        if (!File.Exists(f)) { if (r <= 3) missing++; if (r > 3) break; else continue; }
                        var rep = ParamSchema.Parse(File.ReadAllText(f));
                        var p = rep.JsonFound ? rep.Params : GenerationParams.Default();
                        repParams.Add(p);
                        var maps = seeds.Select(s => Info(Gen.Generate(p, s).Obstacles)).ToList();
                        perRep.Add(maps);
                        for (int i = 0; i < seeds.Length; i++) outR.Append(Row(tid, pt, cond, r, seeds[i], rep, maps[i], T, p, hp));
                        if (cond == "ZS")
                        {   // A1: 구역 밀도 제거 (추가 호출 없음)
                            var pa = Conditions.ParamsFor(Condition.A1_NoRegion, p);
                            for (int i = 0; i < seeds.Length; i++) { var m = Info(Gen.Generate(pa, seeds[i]).Obstacles); outR.Append(Row(tid, pt, "A1", r, seeds[i], rep, m, T, pa, hp)); }
                        }
                    }
                    // 일관성: 같은 seed에서 반복(rep)끼리 비교 → LLM 해석의 변동만 남는다
                    for (int a = 0; a < perRep.Count; a++)
                        for (int b = a + 1; b < perRep.Count; b++)
                        {
                            double la = Reg.Count(k => Lv(repParams[a], k) == Lv(repParams[b], k)) / 9.0;
                            for (int i = 0; i < seeds.Length; i++)
                            {
                                var c = PathMetrics.Compare(Map, perRep[a][i].Path, perRep[b][i].Path);
                                outC.Append(string.Join(",", tid, pt, cond, "rep", a + 1, b + 1, F(PathMetrics.Pearson(perRep[a][i].Rv, perRep[b][i].Rv)), F(c[0]), F(c[1]), F(c[2]), F(la))).Append('\n');
                            }
                        }
                    if (perRep.Count > 0) SeedPairs(outC, tid, pt, cond, perRep[0]);
                }
                if (pt == "D")
                {
                    for (int r = 1; ; r++)
                    {
                        string f = Path.Combine(respDir, tid + "_D_B1_r" + r + ".txt");
                        if (!File.Exists(f)) { if (r <= 3) { missing++; continue; } break; }
                        var dr = DirectGrid.Parse(File.ReadAllText(f), Map.Width, Map.Height);
                        if (!dr.FormatValid) { outR.Append(string.Join(",", tid, pt, "B1", r, "-", "", "", "", "false")).Append(",,,,,,,,,,,,,\n"); continue; }
                        outR.Append(Row(tid, pt, "B1", r, 0, null, Info(dr.Obstacles), T, GenerationParams.Default(), hp, true));
                    }
                }
            }
        }
        File.WriteAllText(Path.Combine(Dir, "results.csv"), outR.ToString(), new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(Dir, "consistency.csv"), outC.ToString(), new UTF8Encoding(false));
        Console.WriteLine("results.csv, consistency.csv 작성. 없는 응답 파일 " + missing + "개");
    }

    static void SeedPairs(StringBuilder outC, string tid, string pt, string cond, List<MapRes> maps)
    {   // 같은 파라미터에서 seed끼리 비교 → WFC 자체의 변동
        for (int a = 0; a < maps.Count; a++)
            for (int b = a + 1; b < maps.Count; b++)
            {
                var c = PathMetrics.Compare(Map, maps[a].Path, maps[b].Path);
                outC.Append(string.Join(",", tid, pt, cond, "seed", a, b, F(PathMetrics.Pearson(maps[a].Rv, maps[b].Rv)), F(c[0]), F(c[1]), F(c[2]), "")).Append('\n');
            }
    }

    static string Lv(GenerationParams p, string r) { string v; return p.RegionDensity.TryGetValue(r, out v) ? v : "mid"; }
    static string TopTag(GenerationParams p)
    {
        string[] n = { "rock", "wall", "crate", "bush" };
        var vals = n.Select(k => { double v; return p.TagMultipliers.TryGetValue(k, out v) ? v : 1.0; }).ToArray();
        double mx = vals.Max(); if (vals.All(v => Math.Abs(v - mx) < 1e-9)) return "none";
        return string.Join("+", Enumerable.Range(0, 4).Where(i => Math.Abs(vals[i] - mx) < 1e-9).Select(i => n[i]));
    }

    static string Row(string tid, string pt, string cond, int rep, int seed, ParseReport pr, MapRes m, MapRes T, GenerationParams p, GenerationParams hp, bool direct = false)
    {
        if ((seed == 100 || direct) && rep <= 1)
        {   // 3D 비교 화면용 대표 맵 (seed 100, 첫 번째 응답)
            string md = Path.Combine(Dir, "maps"); Directory.CreateDirectory(md);
            File.WriteAllText(Path.Combine(md, tid + "_" + (pt == "-" ? "" : pt + "_") + cond + ".txt"), GridText(m.G));
        }
        var c = PathMetrics.Compare(Map, m.Path, T.Path);
        double dens = MapMetrics.ObstacleRatio(Map, m.G, GenerationParams.Default()), tdens = MapMetrics.ObstacleRatio(Map, T.G, GenerationParams.Default());
        double mae = Enumerable.Range(0, 9).Average(i => Math.Abs(m.Rv[i] - T.Rv[i]));
        double l1 = Enumerable.Range(0, 4).Sum(i => Math.Abs(m.Sh[i] - T.Sh[i]));
        string la = "", tt = "";
        if (cond == "ZS" || cond == "FS") { la = F(Reg.Count(k => Lv(p, k) == Lv(hp, k)) / 9.0); tt = TopTag(p) == TopTag(hp) ? "1" : "0"; }
        return string.Join(",", tid, pt, cond, rep, seed,
            pr == null ? "" : (pr.JsonFound ? "1" : "0"), pr == null ? "" : (pr.PureJson ? "1" : "0"), pr == null ? "" : (pr.SchemaValid ? "1" : "0"),
            direct ? "true" : "",
            F(PathMetrics.Pearson(m.Rv, T.Rv)), F(mae), F(Math.Abs(dens - tdens)), F(l1),
            MapMetrics.AdjacencyViolations(m.G), MapMetrics.ForbiddenViolations(Map, m.G, GenerationParams.Default()),
            m.Path != null ? "1" : "0", m.Path != null ? m.Path.Count.ToString() : "",
            F(c[0]), F(c[1]), F(c[2]), la, tt) + "\n";
    }

    static int[,] ReadGrid(string f)
    {
        var lines = File.ReadAllLines(f).Where(l => l.Trim().Length > 0).ToArray();
        var g = new int[Map.Width, Map.Height];
        for (int y = 0; y < Map.Height; y++)
            for (int x = 0; x < Map.Width; x++) { int k = DirectGrid.Symbols.IndexOf(lines[y][x]); g[x, y] = k < 0 ? 0 : k; }
        return g;
    }

    static void Main(string[] a)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        string mode = a.Length > 0 ? a[0] : "";
        Dir = a.Length > 1 ? a[1] : "Experiment/v2";
        string mapPath = a.Length > 2 ? a[2] : "Maps/base_map_01.txt";
        Map = BaseMap.Parse(File.ReadAllText(mapPath)); Gen = new ObstacleGenerator(Map);
        if (mode == "targets") BuildTargets();
        else if (mode == "jobs") BuildJobs(a.Length > 3 ? int.Parse(a[3]) : 3);
        else if (mode == "eval") Eval(a.Length > 3 ? a[3] : Path.Combine(Dir, "responses"), a.Length > 4 ? int.Parse(a[4]) : 10);
        else Console.WriteLine("사용법: experiment.exe targets|jobs|eval [폴더] [맵] [jobs: 반복 수 | eval: 응답 폴더] [eval: seed 수]");
    }
}
