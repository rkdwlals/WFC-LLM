// MapView3D.cs — 격자 텍스트 맵을 3D로 보여 주고, 두 맵을 나란히 비교한다 (교수님 피드백 3번).
// 사용법: 빈 씬에 빈 GameObject를 만들고 붙인 뒤 Play.
//  - 왼쪽/오른쪽 파일: 프로젝트 폴더 기준 경로 (예: Experiment/v2/maps/T01_B0.txt, Experiment/v2/maps/T01_D_ZS.txt)
//  - 기호: . 땅, ~ 물, = 길(다리), S 시작점, G 목표 지점, R 바위, W 벽, C 상자, B 수풀
//  - 프리팹을 넣으면 그 모델을 쓰고, 비워 두면 기본 도형(구·상자)으로 표시한다.
//  - 이동 경로(시작점→목표 최단 경로)를 선으로 표시한다.
//  - 화면 왼쪽 위 칸에서 파일을 바꾸고 "다시 그리기". "스크린샷" 버튼은 프로젝트 폴더/Screenshots/ 에 저장.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using ObstacleWFC;

public class MapView3D : MonoBehaviour
{
    public string leftFile = "Experiment/v2/maps/T01_B0.txt";
    public string rightFile = "Experiment/v2/maps/T01_D_ZS.txt";
    public string leftLabel = "LLM 없음 (B0)";
    public string rightLabel = "LLM 조건 적용";

    [Header("프리팹 (비우면 기본 도형)")]
    public GameObject rockPrefab, wallPrefab, cratePrefab, bushPrefab;

    [Header("표시")]
    public bool showPath = true;
    public float gap = 6f;

    readonly List<GameObject> spawned = new List<GameObject>();
    Material mGround, mWater, mRoad, mRock, mWall, mCrate, mBush, mStart, mGoal, mPath;

    void Start()
    {
        mGround = Mat(new Color(0.80f, 0.76f, 0.62f)); mWater = Mat(new Color(0.30f, 0.55f, 0.85f));
        mRoad = Mat(new Color(0.62f, 0.52f, 0.38f)); mRock = Mat(new Color(0.50f, 0.50f, 0.52f));
        mWall = Mat(new Color(0.40f, 0.26f, 0.16f)); mCrate = Mat(new Color(0.90f, 0.57f, 0.18f));
        mBush = Mat(new Color(0.22f, 0.58f, 0.24f)); mStart = Mat(Color.white); mGoal = Mat(new Color(0.9f, 0.15f, 0.15f));
        mPath = Mat(new Color(1f, 0.95f, 0.2f));
        Rebuild();
    }

    Material Mat(Color c)
    {
        var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var m = new Material(sh); m.color = c; return m;
    }

    string Root { get { return Path.GetDirectoryName(Application.dataPath); } }

    public void Rebuild()
    {
        foreach (var g in spawned) Destroy(g);
        spawned.Clear();
        int w1 = Draw(leftFile, Vector3.zero);
        Draw(rightFile, new Vector3(w1 + gap, 0, 0));
        PlaceCamera(w1);
    }

    int Draw(string rel, Vector3 origin)
    {
        string path = Path.Combine(Root, rel);
        if (!File.Exists(path)) { Debug.LogWarning("맵 파일 없음: " + path); return 32; }
        var rows = File.ReadAllLines(path).Where(l => l.Trim().Length > 0).ToArray();
        int H = rows.Length, W = rows[0].Length;
        var parent = new GameObject(Path.GetFileNameWithoutExtension(rel)); parent.transform.position = origin; spawned.Add(parent);

        // 경로 계산용 베이스 맵과 장애물 격자
        var baseText = string.Join("\n", rows.Select(r => new string(r.Select(c => "RWCB".IndexOf(c) >= 0 ? '.' : c).ToArray())));
        var bm = BaseMap.Parse(baseText);
        var obs = new int[W, H];

        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                char c = rows[y][x];
                var pos = origin + new Vector3(x, 0, H - 1 - y);   // 첫 줄이 북쪽(+z)
                bool water = c == '~';
                var tile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                tile.transform.SetParent(parent.transform);
                tile.transform.position = pos + new Vector3(0, water ? -0.35f : -0.1f, 0);
                tile.transform.localScale = new Vector3(1f, water ? 0.1f : 0.2f, 1f);
                tile.GetComponent<Renderer>().sharedMaterial = water ? mWater : c == '=' ? mRoad : mGround;

                int k = DirectGrid.Symbols.IndexOf(c);
                if (k > 0) { obs[x, y] = k; PlaceObstacle(k, pos, parent.transform, x * 31 + y * 17); }
                if (c == 'S') Pillar(pos, mStart, parent.transform);
                if (c == 'G') Pillar(pos, mGoal, parent.transform);
            }

        if (showPath)
        {
            var p = PathMetrics.ShortestPath(bm, obs);
            if (p != null)
            {
                var go = new GameObject("path"); go.transform.SetParent(parent.transform);
                var lr = go.AddComponent<LineRenderer>();
                lr.positionCount = p.Count; lr.widthMultiplier = 0.25f; lr.sharedMaterial = mPath;
                for (int i = 0; i < p.Count; i++) lr.SetPosition(i, origin + new Vector3(p[i][0], 0.15f, H - 1 - p[i][1]));
            }
        }
        return W;
    }

    void PlaceObstacle(int k, Vector3 pos, Transform parent, int hash)
    {
        GameObject prefab = k == 1 ? rockPrefab : k == 2 ? wallPrefab : k == 3 ? cratePrefab : bushPrefab;
        GameObject g;
        float yaw = (hash % 4) * 90f;
        if (prefab != null) { g = Instantiate(prefab, pos, Quaternion.Euler(0, yaw, 0), parent); return; }
        switch (k)
        {
            case 1: g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.transform.localScale = new Vector3(0.9f, 0.7f, 0.9f); g.transform.position = pos + Vector3.up * 0.3f; g.GetComponent<Renderer>().sharedMaterial = mRock; break;
            case 2: g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.transform.localScale = new Vector3(1f, 1.6f, 1f); g.transform.position = pos + Vector3.up * 0.8f; g.GetComponent<Renderer>().sharedMaterial = mWall; break;
            case 3: g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.transform.localScale = Vector3.one * 0.7f; g.transform.position = pos + Vector3.up * 0.35f; g.transform.rotation = Quaternion.Euler(0, yaw / 6f, 0); g.GetComponent<Renderer>().sharedMaterial = mCrate; break;
            default: g = GameObject.CreatePrimitive(PrimitiveType.Sphere); g.transform.localScale = new Vector3(0.95f, 0.8f, 0.95f); g.transform.position = pos + Vector3.up * 0.35f; g.GetComponent<Renderer>().sharedMaterial = mBush; break;
        }
        g.transform.SetParent(parent);
    }

    void Pillar(Vector3 pos, Material m, Transform parent)
    {
        var g = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        g.transform.SetParent(parent); g.transform.position = pos + Vector3.up * 1f; g.transform.localScale = new Vector3(0.6f, 1f, 0.6f);
        g.GetComponent<Renderer>().sharedMaterial = m;
    }

    void PlaceCamera(int w)
    {
        var cam = Camera.main;
        if (cam == null) { var go = new GameObject("Main Camera"); go.tag = "MainCamera"; cam = go.AddComponent<Camera>(); }
        float cx = (w * 2 + gap) / 2f;
        cam.transform.position = new Vector3(cx, 38f, -22f);
        cam.transform.LookAt(new Vector3(cx, 0, 14f));
        cam.fieldOfView = 50f;
        if (FindObjectOfType<Light>() == null)
        {
            var l = new GameObject("Sun").AddComponent<Light>(); l.type = LightType.Directional;
            l.transform.rotation = Quaternion.Euler(50, -30, 0);
        }
    }

    string editL, editR;
    void OnGUI()
    {
        if (editL == null) { editL = leftFile; editR = rightFile; }
        if (!hidePanel)
        {
            GUILayout.BeginArea(new Rect(10, 10, 460, 120), GUI.skin.box);
            editL = GUILayout.TextField(editL); editR = GUILayout.TextField(editR);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("다시 그리기", GUILayout.Width(120))) { leftFile = editL; rightFile = editR; Rebuild(); }
            if (GUILayout.Button("스크린샷 (2배 해상도)", GUILayout.Width(170))) StartCoroutine(Capture());
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }
        var st = new GUIStyle(GUI.skin.label) { fontSize = 28 }; st.normal.textColor = Color.black;
        GUI.Label(new Rect(Screen.width * 0.18f, Screen.height - 60, 600, 50), leftLabel, st);
        GUI.Label(new Rect(Screen.width * 0.62f, Screen.height - 60, 600, 50), rightLabel, st);
    }

    bool hidePanel;
    System.Collections.IEnumerator Capture()
    {   // 입력 시스템 설정과 상관없이 동작하도록 키 대신 버튼으로 찍는다. 찍는 동안 입력 칸은 숨긴다.
        hidePanel = true; yield return null;
        string dir = Path.Combine(Root, "Screenshots"); Directory.CreateDirectory(dir);
        string f = Path.Combine(dir, "compare_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png");
        ScreenCapture.CaptureScreenshot(f, 2);
        yield return null; yield return null;
        hidePanel = false;
        Debug.Log("스크린샷 저장: " + f);
    }
}
