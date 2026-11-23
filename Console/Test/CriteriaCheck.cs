// CriteriaCheck.cs — 프롬프트별 정답 기준의 타당성 검증 (실험 전에 1번 실행)
//  B0 통과율: 낮아야 한다 (LLM 없이 우연히 통과하면 기준이 너무 느슨함)
//  참고 파라미터 통과율: 높아야 한다 (올바르게 해석해도 통과 못하면 기준이 달성 불가능)
using System; using System.Collections.Generic; using System.IO; using System.Linq; using ObstacleWFC;
class CriteriaCheck {
  static GenerationParams FromObject(Dictionary<string, object> o) {
    var p = GenerationParams.Default();
    foreach (var kv in (Dictionary<string, object>)o["tag_multipliers"]) p.TagMultipliers[kv.Key] = (double)kv.Value;
    foreach (var kv in (Dictionary<string, object>)o["region_density"]) p.RegionDensity[kv.Key] = (string)kv.Value;
    p.PathRequired = (bool)((Dictionary<string, object>)o["hard_constraints"])["path_required"];
    return p;
  }
  static void Main(string[] args) {
    var map = BaseMap.Load(args.Length > 0 ? args[0] : "Maps/base_map_01.txt");
    var root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(args.Length > 1 ? args[1] : "Experiment/prompts.json"));
    int n = args.Length > 2 ? int.Parse(args[2]) : 100;
    var gen = new ObstacleGenerator(map);
    var b0 = new List<Dictionary<string, double>>();
    for (int s = 0; s < n; s++) b0.Add(FidelityMetrics.Compute(map, gen.Generate(GenerationParams.Default(), 100000 + s).Obstacles));
    int bad = 0;
    Console.WriteLine("id   B0통과  참고통과  기준식");
    foreach (Dictionary<string, object> pr in (List<object>)root["prompts"]) {
      string id = (string)pr["id"], crit = (string)pr["criteria"];
      var unknown = Criteria.UnknownNames(crit, b0[0]);
      if (unknown.Count > 0) { Console.WriteLine(id + " 알 수 없는 지표: " + string.Join(",", unknown)); bad++; continue; }
      double b0pass = b0.Count(m => Criteria.Evaluate(crit, m)) / (double)n;
      var p = FromObject((Dictionary<string, object>)pr["reference_params"]);
      int refPass = 0; var fails = new Dictionary<string, int>();
      for (int s = 0; s < n; s++) {
        var m = FidelityMetrics.Compute(map, gen.Generate(p, 200000 + s).Obstacles);
        List<CriterionResult> d;
        if (Criteria.Evaluate(crit, m, out d)) refPass++;
        else foreach (var c in d.Where(c => !c.Pass)) { fails[c.Clause] = fails.ContainsKey(c.Clause) ? fails[c.Clause] + 1 : 1; }
      }
      double rp = refPass / (double)n;
      bool ok = b0pass <= 0.05 && rp >= 0.90;
      if (!ok) bad++;
      Console.WriteLine("{0,-4} {1,5:P0}  {2,6:P0}   {3}{4}{5}", id, b0pass, rp, crit, ok ? "" : "   ← 확인 필요",
        fails.Count > 0 ? "   (참고 실패: " + string.Join(", ", fails.Select(f => f.Key + " ×" + f.Value)) + ")" : "");
    }
    Console.WriteLine("\n기준 미달 {0}건 (B0 ≤ 5%, 참고 ≥ 90%)", bad);
  }
}
