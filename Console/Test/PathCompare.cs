// 경로 확보 방식 비교: 무작위 비용 다익스트라(현재) vs A*(균일 비용) vs A*(무작위 비용)
using System; using System.Collections.Generic; using System.IO; using System.Linq; using ObstacleWFC;
static class PathCompare {
  static int W,H; static BaseMap M; static readonly int[] DX={1,-1,0,0}, DY={0,0,1,-1};
  // useH: A* 휴리스틱 사용 여부, tieRng: 동점 무작위 처리
  static List<int> Search(double[] cost, bool useH, Random tieRng, out int expanded){
    int n=W*H; var g=new double[n]; var prev=new int[n]; var done=new bool[n]; var tie=new double[n];
    for(int i=0;i<n;i++){g[i]=double.MaxValue;prev[i]=-1;tie[i]=tieRng==null?0:tieRng.NextDouble()*1e-6;}
    int s=M.SpawnX*H+M.SpawnY, t=M.GoalX*H+M.GoalY; g[s]=0; expanded=0;
    while(true){
      int u=-1; double best=double.MaxValue;
      for(int i=0;i<n;i++) if(!done[i]&&g[i]<double.MaxValue){
        double h=useH? Math.Abs(i/H-M.GoalX)+Math.Abs(i%H-M.GoalY):0; double f=g[i]+h+tie[i];
        if(f<best){best=f;u=i;} }
      if(u<0) return null; if(u==t) break; done[u]=true; expanded++;
      int ux=u/H, uy=u%H;
      for(int d=0;d<4;d++){int nx=ux+DX[d],ny=uy+DY[d]; if(nx<0||ny<0||nx>=W||ny>=H) continue;
        if(M.Tiles[nx,ny]==BaseTile.Water) continue; int v=nx*H+ny; double nd=g[u]+cost[v];
        if(nd<g[v]){g[v]=nd;prev[v]=u;}}
    }
    var p=new List<int>(); for(int v=t;v>=0;v=prev[v]) p.Add(v); p.Reverse(); return p;
  }
  static double Jac(HashSet<int> a,HashSet<int> b){int i=a.Count(x=>b.Contains(x)); return (double)i/(a.Count+b.Count-i);}
  static void Main(string[] a){
    M=BaseMap.Parse(File.ReadAllText(a[0])); W=M.Width; H=M.Height; int N=200;
    int placeable=0; for(int x=0;x<W;x++)for(int y=0;y<H;y++){var b=M.Tiles[x,y]; if(b==BaseTile.Ground) placeable++;}
    var ones=Enumerable.Repeat(1.0,W*H).ToArray();
    var methods=new[]{"A* 균일 비용, 동점 고정","A* 균일 비용, 동점 무작위","다익스트라 무작위 비용(현재)","A* 무작위 비용"};
    var paths=new List<List<int>>[4]; var exp=new double[4];
    for(int m=0;m<4;m++){paths[m]=new List<List<int>>();}
    bool allSame=true;
    for(int s=0;s<N;s++){
      int e;
      paths[0].Add(Search(ones,true,null,out e)); exp[0]+=e;
      paths[1].Add(Search(ones,true,new Random(s),out e)); exp[1]+=e;
      var r=new Random(s); var c=new double[W*H]; for(int i=0;i<c.Length;i++) c[i]=1.0+r.NextDouble()*3.0;
      var pd=Search(c,false,null,out e); paths[2].Add(pd); exp[2]+=e;
      var pa=Search(c,true,null,out e); paths[3].Add(pa); exp[3]+=e;
      if(!pd.SequenceEqual(pa)) allSame=false;
    }
    int manh=Math.Abs(M.SpawnX-M.GoalX)+Math.Abs(M.SpawnY-M.GoalY);
    Console.WriteLine("seed "+N+"개, 맨해튼 거리 "+manh+", 장애물 가능 칸(땅) "+placeable);
    for(int m=0;m<4;m++){
      var sets=paths[m].Select(p=>new HashSet<int>(p.Skip(1).Take(p.Count-2))).ToList();
      double jac=0; int k=0; for(int i=0;i<N;i++)for(int j=i+1;j<N;j++){jac+=Jac(sets[i],sets[j]);k++;}
      int distinct=paths[m].Select(p=>string.Join(",",p)).Distinct().Count();
      double len=paths[m].Average(p=>p.Count), mx=paths[m].Max(p=>p.Count), mn=paths[m].Min(p=>p.Count);
      var cover=new HashSet<int>(sets.SelectMany(x=>x));
      Console.WriteLine(string.Format("{0}: 길이 평균 {1:F1}칸 (최소 {2}, 최대 {3}), 서로 다른 경로 {4}개, 경로끼리 겹침(Jaccard) 평균 {5:F2}, 한 번이라도 경로가 된 칸 {6}개, 탐색한 칸 평균 {7:F0}",
        methods[m],len,mn,mx,distinct,jac/k,cover.Count,exp[m]/N));
    }
    Console.WriteLine("다익스트라와 A*(같은 무작위 비용) 경로 완전 일치: "+allSame);
  }
}
