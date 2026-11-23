// PathMetrics.cs — 시작점→목표 지점 이동 경로 관련 지표 (UnityEngine 비의존)
// 교수님 피드백: 조건별로 실제 이동 경로가 얼마나 달라지는지를 상관계수로 비교.
using System;
using System.Collections.Generic;

namespace ObstacleWFC
{
    public static class PathMetrics
    {
        static readonly int[] DX = { 1, -1, 0, 0 }, DY = { 0, 0, 1, -1 };

        // BFS 최단 경로 (물·장애물 통과 불가, 4방향). 없으면 null. 좌표는 {x, y}.
        public static List<int[]> ShortestPath(BaseMap map, int[,] obstacles)
        {
            int W = map.Width, H = map.Height;
            var prev = new int[W * H]; for (int i = 0; i < prev.Length; i++) prev[i] = -2;
            int s = map.SpawnX * H + map.SpawnY, t = map.GoalX * H + map.GoalY;
            var q = new Queue<int>(); q.Enqueue(s); prev[s] = -1;
            while (q.Count > 0)
            {
                int u = q.Dequeue(); if (u == t) break;
                int ux = u / H, uy = u % H;
                for (int d = 0; d < 4; d++)
                {
                    int nx = ux + DX[d], ny = uy + DY[d];
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                    int v = nx * H + ny;
                    if (prev[v] != -2) continue;
                    if (map.Tiles[nx, ny] == BaseTile.Water || obstacles[nx, ny] != ObstacleSet.Empty) continue;
                    prev[v] = u; q.Enqueue(v);
                }
            }
            if (prev[t] == -2) return null;
            var path = new List<int[]>();
            for (int v = t; v != -1; v = prev[v]) path.Add(new[] { v / H, v % H });
            path.Reverse();
            return path;
        }

        // 경로를 진행도(0~1) 기준으로 k개 점으로 다시 뽑는다 (길이가 다른 경로끼리 비교하기 위함)
        public static double[][] Resample(List<int[]> path, int k)
        {
            var xs = new double[k]; var ys = new double[k];
            for (int i = 0; i < k; i++)
            {
                double pos = (path.Count - 1) * (double)i / (k - 1);
                int a = (int)Math.Floor(pos), b = Math.Min(a + 1, path.Count - 1); double f = pos - a;
                xs[i] = path[a][0] * (1 - f) + path[b][0] * f;
                ys[i] = path[a][1] * (1 - f) + path[b][1] * f;
            }
            return new[] { xs, ys };
        }

        // 시작점→목표 직선에서 옆으로 벗어난 거리(부호 있음). 두 경로 모두 같은 시작·끝을 가지므로
        // x, y 좌표를 그대로 상관 내면 '둘 다 시작에서 끝으로 간다'는 추세 때문에 항상 높게 나온다.
        // 그래서 추세를 뺀 '옆으로 휜 모양'을 비교한다.
        public static double[] Offset(BaseMap map, double[][] rs)
        {
            double sx = map.SpawnX, sy = map.SpawnY, gx = map.GoalX, gy = map.GoalY;
            double len = Math.Sqrt((gx - sx) * (gx - sx) + (gy - sy) * (gy - sy));
            var o = new double[rs[0].Length];
            for (int i = 0; i < o.Length; i++)
                o[i] = ((gx - sx) * (rs[1][i] - sy) - (gy - sy) * (rs[0][i] - sx)) / len;
            return o;
        }

        public static double Pearson(double[] a, double[] b)
        {
            int n = a.Length; double ma = 0, mb = 0;
            for (int i = 0; i < n; i++) { ma += a[i]; mb += b[i]; }
            ma /= n; mb /= n;
            double sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < n; i++) { sab += (a[i] - ma) * (b[i] - mb); saa += (a[i] - ma) * (a[i] - ma); sbb += (b[i] - mb) * (b[i] - mb); }
            if (saa < 1e-12 || sbb < 1e-12) return double.NaN;   // 한쪽이 일정하면 상관계수 정의 안 됨
            return sab / Math.Sqrt(saa * sbb);
        }

        // 두 경로 비교: [0] 휜 모양 상관(Offset), [1] x·y 좌표 상관 평균, [2] 같은 진행도 점 사이 평균 거리(칸)
        public static double[] Compare(BaseMap map, List<int[]> p, List<int[]> q, int k = 50)
        {
            if (p == null || q == null) return new[] { double.NaN, double.NaN, double.NaN };
            var a = Resample(p, k); var b = Resample(q, k);
            double rOff = Pearson(Offset(map, a), Offset(map, b));
            double rxy = (Pearson(a[0], b[0]) + Pearson(a[1], b[1])) / 2;
            double dist = 0;
            for (int i = 0; i < k; i++) dist += Math.Sqrt(Math.Pow(a[0][i] - b[0][i], 2) + Math.Pow(a[1][i] - b[1][i], 2));
            return new[] { rOff, rxy, dist / k };
        }
    }
}
