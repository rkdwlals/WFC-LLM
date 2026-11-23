// MapView.cs
// Unity에서 베이스 맵 + WFC 장애물 레이어를 색깔 네모로 그려서 확인하는 스크립트.
// 사용법: 빈 GameObject에 붙이고, Base Map Text에 base_map_01.txt(TextAsset)를 넣은 뒤 Play.
// 화면 왼쪽 위 버튼으로 재생성 / CSV 저장.

using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ObstacleWFC;

public class MapView : MonoBehaviour
{
    [Header("입력")]
    public TextAsset baseMapText;
    public int seed = 0;
    public int maxAttempts = 20;
    public bool pathRequired = true;

    [Header("태그 배수 (tag_multipliers)")]
    public float rock = 1f;
    public float wall = 1f;
    public float crate = 1f;
    public float bush = 1f;

    [Header("구역 밀도 (region_density) 예: N:high,NW:high,S:low")]
    public string regionDensity = "";

    [Header("표시")]
    public float cellSize = 1f;

    BaseMap baseMap;
    public BaseMap BaseMapData { get { return baseMap; } }
    GenerationResult result;
    GenerationParams currentParams;
    readonly List<GameObject> spawned = new List<GameObject>();
    Sprite square;
    string status = "";

    static readonly Dictionary<BaseTile, Color> BaseColors = new Dictionary<BaseTile, Color>
    {
        { BaseTile.Ground, new Color(0.80f, 0.74f, 0.58f) },
        { BaseTile.Water,  new Color(0.30f, 0.55f, 0.85f) },
        { BaseTile.Road,   new Color(0.55f, 0.55f, 0.55f) },
        { BaseTile.Spawn,  new Color(0.97f, 0.97f, 0.97f) }, // 흰색 (수풀 초록과 구분)
        { BaseTile.Goal,   new Color(0.90f, 0.25f, 0.25f) },
    };

    // ObstacleSet.Tags 순서: empty, rock, wall, crate, bush
    static readonly Color[] ObstacleColors =
    {
        Color.clear,
        new Color(0.45f, 0.45f, 0.48f), // rock
        new Color(0.25f, 0.20f, 0.18f), // wall
        new Color(0.72f, 0.48f, 0.22f), // crate
        new Color(0.18f, 0.50f, 0.20f), // bush
    };

    void Start()
    {
        var tex = new Texture2D(1, 1);
        tex.SetPixel(0, 0, Color.white);
        tex.Apply();
        square = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);

        baseMap = BaseMap.Parse(baseMapText.text);
        Generate();
    }

    public GenerationParams BuildParams()
    {
        var p = GenerationParams.Default();
        p.PathRequired = pathRequired;
        p.TagMultipliers["rock"] = rock;
        p.TagMultipliers["wall"] = wall;
        p.TagMultipliers["crate"] = crate;
        p.TagMultipliers["bush"] = bush;
        foreach (var part in regionDensity.Split(','))
        {
            var kv = part.Split(':');
            if (kv.Length == 2) p.RegionDensity[kv[0].Trim()] = kv[1].Trim();
        }
        return p;
    }

    // LLM이 만든 파라미터. 값이 있으면 Inspector 값 대신 이것을 쓴다 (PromptPanel이 설정)
    [System.NonSerialized] public GenerationParams overrideParams;

    [ContextMenu("Use Inspector Params")]
    public void ClearOverride() { overrideParams = null; Generate(); }

    [ContextMenu("Generate")]
    public void Generate()
    {
        if (baseMap == null) return;
        currentParams = overrideParams ?? BuildParams();
        var gen = new ObstacleGenerator(baseMap);
        result = gen.Generate(currentParams, seed, maxAttempts);
        Draw();

        if (!result.Success) { status = "모든 시도에서 모순 발생"; return; }
        double ratio = MapMetrics.ObstacleRatio(baseMap, result.Obstacles, currentParams);
        status = string.Format("seed {0} | 시도 {1}회 | 경로 {2} | 장애물 비율 {3:P1} | 금지 위반 {4} | 인접 위반 {5}",
            seed, result.Attempts, result.PathOk ? "OK" : "실패", ratio,
            MapMetrics.ForbiddenViolations(baseMap, result.Obstacles, currentParams),
            MapMetrics.AdjacencyViolations(result.Obstacles));
    }

    void Draw()
    {
        foreach (var go in spawned) Destroy(go);
        spawned.Clear();

        for (int x = 0; x < baseMap.Width; x++)
            for (int y = 0; y < baseMap.Height; y++)
            {
                // 텍스트 첫 줄(y=0)이 화면 맨 위에 오도록 뒤집는다
                var pos = new Vector3(x * cellSize, (baseMap.Height - 1 - y) * cellSize, 0);
                spawned.Add(MakeCell(pos, BaseColors[baseMap.Tiles[x, y]], cellSize, 0));

                if (result != null && result.Success && result.Obstacles[x, y] != ObstacleSet.Empty)
                    spawned.Add(MakeCell(pos, ObstacleColors[result.Obstacles[x, y]], cellSize * 0.8f, 1));
            }

        var cam = Camera.main;
        if (cam != null)
        {
            cam.orthographic = true;
            cam.orthographicSize = baseMap.Height * cellSize * 0.55f;
            // 맵을 화면 오른쪽에 붙여서 왼쪽을 UI 자리로 비운다 (화면이 좁으면 가운데)
            float center = (baseMap.Width - 1) * cellSize / 2f;
            float halfW = cam.orthographicSize * cam.aspect;
            float camX = Mathf.Min(center, (baseMap.Width + 0.5f) * cellSize - halfW);
            cam.transform.position = new Vector3(camX, (baseMap.Height - 1) * cellSize / 2f, -10f);
        }
    }

    GameObject MakeCell(Vector3 pos, Color color, float size, int order)
    {
        var go = new GameObject("cell");
        go.transform.SetParent(transform);
        go.transform.position = pos;
        go.transform.localScale = new Vector3(size, size, 1);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = square;
        sr.color = color;
        sr.sortingOrder = order;
        return go;
    }

    [ContextMenu("Export CSV")]
    public void ExportCsv()
    {
        if (result == null || !result.Success) return;
        string path = Path.Combine(Application.persistentDataPath, "obstacles_seed" + seed + ".csv");
        File.WriteAllText(path, MapMetrics.ToCsv(result.Obstacles));
        status = "저장: " + path;
        Debug.Log(status);
    }

    // 입력 시스템 설정과 무관하게 동작하도록 OnGUI 버튼 사용
    // 화면 높이 1080 기준으로 UI 크기를 맞춘다 (QHD 등 고해상도에서 글씨가 작아지는 문제)
    public static float GuiScale()
    {
        float s = Mathf.Max(1f, Screen.height / 1080f) * 1.5f;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        return s;
    }

    void OnGUI()
    {
        GuiScale();
        GUILayout.BeginArea(new Rect(10, 10, 520, 120));
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("다음 seed로 재생성", GUILayout.Width(160))) { seed++; Generate(); }
        if (GUILayout.Button("같은 seed로 재생성", GUILayout.Width(160))) Generate();
        if (GUILayout.Button("CSV 저장", GUILayout.Width(100))) ExportCsv();
        GUILayout.EndHorizontal();
        GUILayout.Label(status);
        GUILayout.EndArea();
    }
}
