// ObstacleWfc.cs
// 고정 베이스 맵 위에서 장애물 레이어만 생성하는 WFC(Wave Function Collapse) 핵심 코드.
// UnityEngine에 의존하지 않으므로 Unity 안에서도, 콘솔(배치 실험)에서도 그대로 쓸 수 있다.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ObstacleWFC
{
    // ─────────────────────────────────────────────
    // 1. 베이스 맵 (고정 레이어)
    // ─────────────────────────────────────────────
    public enum BaseTile { Ground, Water, Road, Spawn, Goal }

    public class BaseMap
    {
        public int Width, Height;
        public BaseTile[,] Tiles;          // [x, y], y = 0 이 맵의 맨 위(북쪽)
        public int SpawnX = -1, SpawnY = -1, GoalX = -1, GoalY = -1;

        // 텍스트 형식: '.' 땅, '~' 물, '=' 길, 'S' 시작점, 'G' 목표 지점. 첫 줄이 북쪽.
        public static BaseMap Parse(string text)
        {
            var lines = new List<string>();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
                if (raw.Trim().Length > 0) lines.Add(raw.Trim());

            var map = new BaseMap { Height = lines.Count, Width = lines[0].Length };
            map.Tiles = new BaseTile[map.Width, map.Height];
            for (int y = 0; y < map.Height; y++)
            {
                if (lines[y].Length != map.Width)
                    throw new FormatException("베이스 맵 " + (y + 1) + "번째 줄 길이가 다릅니다.");
                for (int x = 0; x < map.Width; x++)
                {
                    char c = lines[y][x];
                    BaseTile t;
                    switch (c)
                    {
                        case '.': t = BaseTile.Ground; break;
                        case '~': t = BaseTile.Water; break;
                        case '=': t = BaseTile.Road; break;
                        case 'S': t = BaseTile.Spawn; map.SpawnX = x; map.SpawnY = y; break;
                        case 'G': t = BaseTile.Goal; map.GoalX = x; map.GoalY = y; break;
                        default: throw new FormatException("알 수 없는 베이스 타일 문자: '" + c + "'");
                    }
                    map.Tiles[x, y] = t;
                }
            }
            return map;
        }

        public static BaseMap Load(string path) { return Parse(File.ReadAllText(path)); }
    }

    // ─────────────────────────────────────────────
    // 2. 장애물 타일 세트와 인접 규칙
    // ─────────────────────────────────────────────
    public static class ObstacleSet
    {
        // 인덱스 0은 반드시 '빈 칸'
        public static readonly string[] Tags = { "empty", "rock", "wall", "crate", "bush" };
        public const int Empty = 0;
        public static int Count { get { return Tags.Length; } }

        // 기본 가중치 (자리 표시용 값 — 베이스 맵 기본 분포를 보고 조정할 것)
        public static readonly double[] BaseWeights = { 6.0, 1.0, 1.0, 1.0, 1.0 };

        // 인접 규칙: Allowed[a, b] = a 옆(상하좌우)에 b가 올 수 있는가. 대칭.
        // 빈 칸은 모든 타일과 인접 가능.
        // 벽·상자 묶음 / 바위·수풀 묶음 → 두 묶음은 서로 붙을 수 없다.
        public static readonly bool[,] Allowed = BuildRules();

        static bool[,] BuildRules()
        {
            int n = Tags.Length;
            var a = new bool[n, n];
            Action<string, string> allow = (p, q) =>
            {
                int i = Array.IndexOf(Tags, p), j = Array.IndexOf(Tags, q);
                a[i, j] = true; a[j, i] = true;
            };
            foreach (var t in Tags) allow("empty", t);
            allow("rock", "rock"); allow("rock", "bush"); allow("bush", "bush");
            allow("wall", "wall"); allow("wall", "crate"); allow("crate", "crate");
            return a;
        }

        public static int IndexOf(string tag) { return Array.IndexOf(Tags, tag); }
    }

    // ─────────────────────────────────────────────
    // 3. 생성 파라미터 (LLM이 출력할 JSON 스키마와 1:1 대응)
    // ─────────────────────────────────────────────
    public class GenerationParams
    {
        // tag_multipliers: 장애물 태그별 가중치 배수. 없는 태그는 1.0
        public Dictionary<string, double> TagMultipliers = new Dictionary<string, double>();

        // region_density: 3×3 구역(NW,N,NE,W,C,E,SW,S,SE) → "low" | "mid" | "high". 없는 구역은 "mid"
        public Dictionary<string, string> RegionDensity = new Dictionary<string, string>();

        // hard_constraints
        public bool PathRequired = true;

        // 사용자 지정 동선 (2026-11-16 추가, E3): 비워 두면(null) 기존과 똑같이 무작위 경로를 쓴다.
        //   값이 있으면 이 칸들을 생성 전에 '빈 칸'으로 고정한다. 각 원소는 {x, y}.
        //   주의: LlmPrompting.cs의 Conditions.Clone은 이 필드를 복사하지 않는다.
        public List<int[]> FixedCorridor = null;
        public List<BaseTile> ForbiddenBase = new List<BaseTile> { BaseTile.Water, BaseTile.Road };

        // 구역 밀도 수준 → 장애물 가중치 배수 (자리 표시용 값)
        public static double DensityFactor(string level)
        {
            switch (level)
            {
                case "low": return 0.25;
                case "high": return 4.0;
                default: return 1.0;
            }
        }

        public static GenerationParams Default() { return new GenerationParams(); }
    }

    public static class Regions
    {
        public static readonly string[] Names = { "NW", "N", "NE", "W", "C", "E", "SW", "S", "SE" };

        public static string Of(int x, int y, int w, int h)
        {
            int rx = Math.Min(2, x * 3 / w), ry = Math.Min(2, y * 3 / h);
            return Names[ry * 3 + rx];
        }
    }

    // ─────────────────────────────────────────────
    // 4. 생성 결과
    // ─────────────────────────────────────────────
    public class GenerationResult
    {
        public int[,] Obstacles;      // [x, y] 장애물 타일 인덱스
        public bool Success;          // WFC가 모순 없이 끝났는가
        public bool PathOk;           // 경로 조건 만족 여부 (조건이 없으면 true)
        public int Attempts;          // 사용한 시도 횟수 (재시도 포함)
        public int Contradictions;    // 시도 중 모순 발생 횟수
        public int UsedSeed;          // 최종 결과를 만든 seed
    }

    // ─────────────────────────────────────────────
    // 5. WFC 생성기
    // ─────────────────────────────────────────────
    public class ObstacleGenerator
    {
        readonly BaseMap baseMap;
        readonly int W, H, T;

        public ObstacleGenerator(BaseMap map)
        {
            baseMap = map; W = map.Width; H = map.Height; T = ObstacleSet.Count;
        }

        // 경로 확보 방식:
        //  - PathRequired이면 생성 전에 시작점→목표 지점 사이의 무작위 경로(corridor)를 '빈 칸'으로 미리 고정한다.
        //    (밀도가 높은 구역에 목표가 있으면 '생성 후 검사 + 재시도' 방식은 대부분 실패했기 때문)
        //  - 모순이 나면 seed를 바꿔 maxAttempts번까지 재시도한다.
        // 두 규칙 모두 Validity 수치에 영향을 주므로 논문에 반드시 명시할 것.
        public GenerationResult Generate(GenerationParams p, int seed, int maxAttempts = 20)
        {
            GenerationResult last = null;
            int contradictions = 0;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                int s = unchecked(seed * 1000003 + attempt);
                int[,] grid = RunOnce(p, s);
                if (grid == null) { contradictions++; continue; }

                bool pathOk = !p.PathRequired || PathExists(baseMap, grid);
                last = new GenerationResult
                {
                    Obstacles = grid, Success = true, PathOk = pathOk,
                    Attempts = attempt + 1, Contradictions = contradictions, UsedSeed = s
                };
                if (pathOk) return last;
            }
            if (last == null)
                last = new GenerationResult { Success = false, PathOk = false, Attempts = maxAttempts, Contradictions = contradictions };
            return last;
        }

        // WFC 1회 실행. 모순이면 null.
        public int[,] RunOnce(GenerationParams p, int seed)
        {
            var rng = new Random(seed);
            var possible = new bool[W, H, T];
            var count = new int[W, H];
            var weights = BuildWeights(p);
            var wlogw = new double[W, H, T];
            var sumW = new double[W, H];
            var sumWLog = new double[W, H];
            bool[,] corridor = p.FixedCorridor != null ? CorridorFromCells(p.FixedCorridor)
                             : p.PathRequired ? RandomCorridor(rng) : null;

            // 초기화 + pre-constraint: 금지 베이스 타일, 시작점, 목표 지점, 경로 corridor는 '빈 칸'만 허용
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    BaseTile b = baseMap.Tiles[x, y];
                    bool onlyEmpty = b == BaseTile.Spawn || b == BaseTile.Goal || p.ForbiddenBase.Contains(b)
                                     || (corridor != null && corridor[x, y]);
                    for (int t = 0; t < T; t++)
                    {
                        possible[x, y, t] = onlyEmpty ? t == ObstacleSet.Empty : true;
                        wlogw[x, y, t] = weights[x, y, t] * Math.Log(weights[x, y, t]);
                        if (possible[x, y, t]) { sumW[x, y] += weights[x, y, t]; sumWLog[x, y] += wlogw[x, y, t]; }
                    }
                    count[x, y] = onlyEmpty ? 1 : T;
                }

            // 후보를 지울 때 엔트로피 계산용 합계도 같이 갱신한다 (매 단계 전체 재계산을 피해서 빠르게)
            Action<int, int, int> remove = (x, y, t) =>
            {
                possible[x, y, t] = false; count[x, y]--;
                sumW[x, y] -= weights[x, y, t]; sumWLog[x, y] -= wlogw[x, y, t];
            };

            // 사전 고정 칸에서부터 전파
            var stack = new Stack<int>();
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    if (count[x, y] < T) stack.Push(x * H + y);
            if (!Propagate(possible, count, stack, remove)) return null;

            while (true)
            {
                // 엔트로피가 가장 낮은 미확정 칸 선택
                int bx = -1, by = -1; double best = double.MaxValue;
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                    {
                        if (count[x, y] <= 1) continue;
                        double e = Math.Log(sumW[x, y]) - sumWLog[x, y] / sumW[x, y] + rng.NextDouble() * 1e-6;
                        if (e < best) { best = e; bx = x; by = y; }
                    }
                if (bx < 0) break; // 모두 확정

                // 가중치 기반 무작위 선택(collapse)
                double r = rng.NextDouble() * sumW[bx, by];
                int chosen = -1;
                for (int t = 0; t < T; t++)
                {
                    if (!possible[bx, by, t]) continue;
                    chosen = t;
                    r -= weights[bx, by, t];
                    if (r <= 0) break;
                }
                for (int t = 0; t < T; t++)
                    if (t != chosen && possible[bx, by, t]) remove(bx, by, t);

                stack.Push(bx * H + by);
                if (!Propagate(possible, count, stack, remove)) return null;
            }

            var grid = new int[W, H];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                    for (int t = 0; t < T; t++)
                        if (possible[x, y, t]) { grid[x, y] = t; break; }
            return grid;
        }

        // 시작점 → 목표 지점 무작위 경로. 칸마다 무작위 비용(1~4)을 주고 최단 경로를 찾는다.
        // 물은 지나갈 수 없다. seed가 같으면 같은 경로가 나온다.
        // 사용자 지정 동선 → 고정 칸 표시 (2026-11-16 추가, E3)
        bool[,] CorridorFromCells(List<int[]> cells)
        {
            var c = new bool[W, H];
            foreach (var xy in cells)
                if (xy[0] >= 0 && xy[1] >= 0 && xy[0] < W && xy[1] < H) c[xy[0], xy[1]] = true;
            return c;
        }

        // 동선 검사 (2026-11-16 추가, E3): 칸이 모두 맵 안이고 물이 아니며, 상하좌우로 이어져 시작점과 목표를 잇는지.
        public static string ValidateCorridor(BaseMap map, List<int[]> cells)
        {
            if (cells == null || cells.Count == 0) return "동선이 비어 있음";
            var set = new HashSet<int>();
            foreach (var xy in cells)
            {
                if (xy[0] < 0 || xy[1] < 0 || xy[0] >= map.Width || xy[1] >= map.Height) return "맵 밖 칸: " + xy[0] + "," + xy[1];
                if (map.Tiles[xy[0], xy[1]] == BaseTile.Water) return "물 위의 칸: " + xy[0] + "," + xy[1];
                set.Add(xy[0] * map.Height + xy[1]);
            }
            int s = map.SpawnX * map.Height + map.SpawnY, g = map.GoalX * map.Height + map.GoalY;
            if (!set.Contains(s)) return "시작점이 동선에 없음";
            if (!set.Contains(g)) return "목표 지점이 동선에 없음";
            var seen = new HashSet<int> { s }; var q = new Queue<int>(); q.Enqueue(s);
            while (q.Count > 0)
            {
                int u = q.Dequeue(), ux = u / map.Height, uy = u % map.Height;
                for (int d = 0; d < 4; d++)
                {
                    int nx = ux + DX[d], ny = uy + DY[d], v = nx * map.Height + ny;
                    if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height || !set.Contains(v) || seen.Contains(v)) continue;
                    seen.Add(v); q.Enqueue(v);
                }
            }
            return seen.Contains(g) ? null : "동선이 시작점과 목표를 잇지 않음";
        }

        bool[,] RandomCorridor(Random rng)
        {
            if (baseMap.SpawnX < 0 || baseMap.GoalX < 0) return null;
            int n = W * H;
            var cost = new double[n];
            for (int i = 0; i < n; i++) cost[i] = 1.0 + rng.NextDouble() * 3.0;
            var dist = new double[n]; var prev = new int[n]; var done = new bool[n];
            for (int i = 0; i < n; i++) { dist[i] = double.MaxValue; prev[i] = -1; }
            int start = baseMap.SpawnX * H + baseMap.SpawnY, goal = baseMap.GoalX * H + baseMap.GoalY;
            dist[start] = 0;
            for (int iter = 0; iter < n; iter++)
            {
                int u = -1; double du = double.MaxValue;
                for (int i = 0; i < n; i++) if (!done[i] && dist[i] < du) { du = dist[i]; u = i; }
                if (u < 0 || u == goal) break;
                done[u] = true;
                int ux = u / H, uy = u % H;
                for (int d = 0; d < 4; d++)
                {
                    int nx = ux + DX[d], ny = uy + DY[d];
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    if (baseMap.Tiles[nx, ny] == BaseTile.Water) continue;
                    int v = nx * H + ny;
                    double nd = du + cost[v];
                    if (nd < dist[v]) { dist[v] = nd; prev[v] = u; }
                }
            }
            if (dist[goal] == double.MaxValue) return null; // 베이스 맵 자체에 길이 없음
            var c = new bool[W, H];
            for (int v = goal; v >= 0; v = prev[v]) c[v / H, v % H] = true;
            return c;
        }

        // 칸별 가중치 = 기본 가중치 × 태그 배수(정규화) × 구역 밀도 배수(장애물에만)
        // 정규화: 태그 배수를 곱한 뒤 장애물 가중치의 합이 원래 합과 같도록 맞춘다.
        //   → 태그 배수는 장애물 '구성'만 바꾸고, '양'(밀도)은 구역 밀도가 전담한다.
        double[,,] BuildWeights(GenerationParams p)
        {
            var tagW = new double[T];
            double baseObs = 0, multObs = 0;
            for (int t = 0; t < T; t++)
            {
                double m;
                double mult = p.TagMultipliers.TryGetValue(ObstacleSet.Tags[t], out m) ? m : 1.0;
                if (mult < 0) mult = 0;
                tagW[t] = ObstacleSet.BaseWeights[t] * mult;
                if (t != ObstacleSet.Empty) { baseObs += ObstacleSet.BaseWeights[t]; multObs += tagW[t]; }
            }
            tagW[ObstacleSet.Empty] = ObstacleSet.BaseWeights[ObstacleSet.Empty];
            if (multObs > 0)
                for (int t = 0; t < T; t++)
                    if (t != ObstacleSet.Empty) tagW[t] *= baseObs / multObs;

            var w = new double[W, H, T];
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    string region = Regions.Of(x, y, W, H);
                    string level;
                    double dens = p.RegionDensity.TryGetValue(region, out level)
                        ? GenerationParams.DensityFactor(level) : 1.0;
                    for (int t = 0; t < T; t++)
                    {
                        double v = tagW[t];
                        if (t != ObstacleSet.Empty) v *= dens;
                        w[x, y, t] = Math.Max(v, 1e-9);
                    }
                }
            return w;
        }

        static readonly int[] DX = { 1, -1, 0, 0 };
        static readonly int[] DY = { 0, 0, 1, -1 };

        // 확정된 칸의 영향을 이웃으로 전파. 어떤 칸의 후보가 0개가 되면 모순(false).
        bool Propagate(bool[,,] possible, int[,] count, Stack<int> stack, Action<int, int, int> remove)
        {
            while (stack.Count > 0)
            {
                int id = stack.Pop();
                int cx = id / H, cy = id % H;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + DX[d], ny = cy + DY[d];
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    bool changed = false;
                    for (int t = 0; t < T; t++)
                    {
                        if (!possible[nx, ny, t]) continue;
                        bool supported = false;
                        for (int s = 0; s < T; s++)
                            if (possible[cx, cy, s] && ObstacleSet.Allowed[s, t]) { supported = true; break; }
                        if (!supported) { remove(nx, ny, t); changed = true; }
                    }
                    if (count[nx, ny] == 0) return false;
                    if (changed) stack.Push(nx * H + ny);
                }
            }
            return true;
        }

        // 시작점 → 목표 지점 BFS. 물은 못 지나가고 장애물이 있는 칸도 못 지나간다.
        public static bool PathExists(BaseMap map, int[,] obstacles)
        {
            if (map.SpawnX < 0 || map.GoalX < 0) return true;
            int w = map.Width, h = map.Height;
            var seen = new bool[w, h];
            var q = new Queue<int>();
            q.Enqueue(map.SpawnX * h + map.SpawnY); seen[map.SpawnX, map.SpawnY] = true;
            while (q.Count > 0)
            {
                int id = q.Dequeue(); int x = id / h, y = id % h;
                if (x == map.GoalX && y == map.GoalY) return true;
                for (int d = 0; d < 4; d++)
                {
                    int nx = x + DX[d], ny = y + DY[d];
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h || seen[nx, ny]) continue;
                    if (map.Tiles[nx, ny] == BaseTile.Water) continue;
                    if (obstacles[nx, ny] != ObstacleSet.Empty) continue;
                    seen[nx, ny] = true; q.Enqueue(nx * h + ny);
                }
            }
            return false;
        }
    }

    // ─────────────────────────────────────────────
    // 6. 평가 지표 (빠른 확인용. 본 실험 분석은 CSV로 내보내 Python에서 한다)
    // ─────────────────────────────────────────────
    public static class MapMetrics
    {
        // 장애물을 둘 수 있는 칸(금지 베이스·시작·목표 제외) 중 장애물 비율
        public static double ObstacleRatio(BaseMap map, int[,] g, GenerationParams p)
        {
            int placeable = 0, filled = 0;
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    var b = map.Tiles[x, y];
                    if (b == BaseTile.Spawn || b == BaseTile.Goal || p.ForbiddenBase.Contains(b)) continue;
                    placeable++;
                    if (g[x, y] != ObstacleSet.Empty) filled++;
                }
            return placeable == 0 ? 0 : (double)filled / placeable;
        }

        // 구역별 장애물 비율 (구역 이름 → 비율)
        public static Dictionary<string, double> RegionRatios(BaseMap map, int[,] g, GenerationParams p)
        {
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
            var res = new Dictionary<string, double>();
            foreach (var r in Regions.Names) res[r] = total[r] == 0 ? 0 : (double)filled[r] / total[r];
            return res;
        }

        // 태그별 장애물 개수
        public static int[] TagCounts(int[,] g)
        {
            var c = new int[ObstacleSet.Count];
            foreach (int t in g) c[t]++;
            return c;
        }

        // 금지 베이스 타일(+시작·목표) 위에 놓인 장애물 수. 정상이면 항상 0.
        public static int ForbiddenViolations(BaseMap map, int[,] g, GenerationParams p)
        {
            int v = 0;
            for (int x = 0; x < map.Width; x++)
                for (int y = 0; y < map.Height; y++)
                {
                    var b = map.Tiles[x, y];
                    bool forbidden = b == BaseTile.Spawn || b == BaseTile.Goal || p.ForbiddenBase.Contains(b);
                    if (forbidden && g[x, y] != ObstacleSet.Empty) v++;
                }
            return v;
        }

        // 인접 규칙 위반 수. WFC 결과라면 항상 0이어야 한다. (LLM 직접 배치 조건 B1 평가에 사용)
        public static int AdjacencyViolations(int[,] g)
        {
            int w = g.GetLength(0), h = g.GetLength(1), v = 0;
            for (int x = 0; x < w; x++)
                for (int y = 0; y < h; y++)
                {
                    if (x + 1 < w && !ObstacleSet.Allowed[g[x, y], g[x + 1, y]]) v++;
                    if (y + 1 < h && !ObstacleSet.Allowed[g[x, y], g[x, y + 1]]) v++;
                }
            return v;
        }

        // 두 맵의 해밍 거리(서로 다른 칸의 비율). 다양성 계산용.
        public static double Hamming(int[,] a, int[,] b)
        {
            int w = a.GetLength(0), h = a.GetLength(1), diff = 0;
            for (int x = 0; x < w; x++) for (int y = 0; y < h; y++) if (a[x, y] != b[x, y]) diff++;
            return (double)diff / (w * h);
        }

        // CSV: 한 줄 = 맵의 한 행(북쪽부터), 값 = 장애물 태그 인덱스
        public static string ToCsv(int[,] g)
        {
            int w = g.GetLength(0), h = g.GetLength(1);
            var sb = new StringBuilder();
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++) { if (x > 0) sb.Append(','); sb.Append(g[x, y]); }
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
