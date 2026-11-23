// DemoApp.cs — 학술제 본선 시연·체험용 데모 (발표 현장 + 배포본 공용)
// 사용법
//  1) 새 씬(Demo)을 만들고 빈 GameObject에 이 스크립트를 붙인다. 카메라·조명은 없으면 스크립트가 만든다.
//  2) Inspector의 Base Map Text에 Assets/Maps/base_map_01.txt 를 넣는다.
//  3) Play 또는 Windows로 빌드.
// 동작
//  - 직접 입력: 로컬(또는 지정 주소) LLM 서버(Ollama, OpenAI 호환 /v1)에 명령을 보내 제약 JSON을 받고, WFC로 배치한다.
//  - 예시 명령: 서버가 없거나 "저장된 응답 사용"을 켜면, 실험에서 gemma4:e4b가 실제로 낸 응답(반복 1회차)을 그대로 쓴다.
//  - 보안: 이 데모는 어떤 경우에도 실제 API 키를 보내지 않는다 (항상 더미 키 "local").
//  - 데모에서는 "경로 필수 고정"을 기본으로 켠다 (LLM이 path_required=false를 내도 경로를 확보).
//  - 원근 카메라 (2026-11-16 추가): 남쪽 위 40°에서 비스듬히 내려다보고 그림자를 켠다. Inspector의 perspectiveCamera를 끄면 이전 정사영(52°) 화면.
//  - 동선 선택 (2026-11-16 추가): [무작위]가 기본. R1~R3를 고르면 실험(E3)과 같은 동선을 FixedCorridor로 고정하고 보라색으로 표시한다.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ObstacleWFC;

public class DemoApp : MonoBehaviour
{
    [Header("입력")]
    public TextAsset baseMapText;

    [Header("LLM 서버 (Ollama 기본값)")]
    public string serverUrl = "http://localhost:11434/v1";
    public string model = "gemma4:e4b";
    public float temperature = 0.7f;
    public int timeoutSeconds = 600;     // GPU 없는 노트북 대비

    [Header("장애물 모델 (비우면 단순 도형: 바위=회색 구, 벽=높은 갈색 기둥, 상자=주황 상자, 수풀=초록 구)")]
    public GameObject rockPrefab, wallPrefab, cratePrefab, bushPrefab;

    [Header("카메라 (2026-11-16 추가) — 끄면 이전 정사영(52°) 화면")]
    public bool perspectiveCamera = true;
    public float perspectiveFov = 45f;      // 세로 시야각 (넓을수록 원근감이 커지고 카메라가 가까워짐)
    public float perspectivePitch = 40f;    // 내려다보는 각도 ([위에서 보기]를 켜면 90°)

    // ── 예시 명령: 실험 명령어와 gemma4:e4b 실제 응답(Experiment/v2/responses/*_r1.txt)
    class Example { public string Label, Prompt, Json, Source; }
    static readonly Example[] Examples =
    {
        new Example { Label = "포스터 예시 (서술형)", Source = "T03 서술형",
            Prompt = "수풀이 전체적으로 많은 맵인데, 특히 서쪽 절반과 시작점이 있는 남서쪽에 수풀과 바위가 뒤엉켜 빽빽한 덤불 숲처럼 만들어 주세요. 동쪽과 북동쪽은 훨씬 비어 있는 열린 공간으로 남기고, 벽과 상자는 거의 없이 아주 드물게만 넣어 주세요.",
            Json = "{\"tag_multipliers\":{\"rock\":1.5,\"wall\":0.3,\"crate\":0.3,\"bush\":2.5},\"region_density\":{\"NW\":\"mid\",\"N\":\"low\",\"NE\":\"low\",\"W\":\"high\",\"C\":\"mid\",\"E\":\"low\",\"SW\":\"high\",\"S\":\"mid\",\"SE\":\"mid\"},\"hard_constraints\":{\"path_required\":true}}" },
        new Example { Label = "구어체: 연못 주변 수풀", Source = "T09 구어체",
            Prompt = "수풀 비중 높게 가고, 서북쪽이랑 동남쪽 연못 부근에 몰아 줘. 가운데랑 남서쪽은 널찍하게 비우고 나머지 장애물은 띄엄띄엄.",
            Json = "{\"tag_multipliers\":{\"rock\":1.0,\"wall\":1.0,\"crate\":1.0,\"bush\":2.5},\"region_density\":{\"NW\":\"high\",\"N\":\"low\",\"NE\":\"mid\",\"W\":\"mid\",\"C\":\"low\",\"E\":\"mid\",\"SW\":\"low\",\"S\":\"low\",\"SE\":\"high\"},\"hard_constraints\":{\"path_required\":true}}" },
        new Example { Label = "구어체: 바위·수풀 미로", Source = "T10 구어체",
            Prompt = "바위랑 수풀만 잔뜩 깔아서 미로 느낌 나게. 벽이랑 상자는 아예 빼고, 맵 전체가 꽉 차서 길이 구불구불하게.",
            Json = "{\"tag_multipliers\":{\"rock\":3.0,\"wall\":0.0,\"crate\":0.0,\"bush\":3.0},\"region_density\":{\"NW\":\"mid\",\"N\":\"high\",\"NE\":\"high\",\"W\":\"mid\",\"C\":\"mid\",\"E\":\"mid\",\"SW\":\"mid\",\"S\":\"high\",\"SE\":\"high\"},\"hard_constraints\":{\"path_required\":true}}" },
        new Example { Label = "기획서체: 북동 벽·상자", Source = "T07 기획서체",
            Prompt = "모든 장애물 균등 혼합, 중저밀도 / 북동측에 벽·상자 군집으로 목표 지점 앞 완충 구간 설정 / 그 외는 바위·수풀 산개로 가벼운 엄폐 제공",
            Json = "{\"tag_multipliers\":{\"rock\":1.0,\"wall\":1.5,\"crate\":1.5,\"bush\":1.0},\"region_density\":{\"NW\":\"mid\",\"N\":\"mid\",\"NE\":\"high\",\"W\":\"mid\",\"C\":\"mid\",\"E\":\"mid\",\"SW\":\"mid\",\"S\":\"mid\",\"SE\":\"mid\"},\"hard_constraints\":{\"path_required\":true}}" },
        new Example { Label = "숫자형: 구역별 밀도 지정", Source = "T05 숫자형",
            Prompt = "구역별 장애물 밀도(장애물을 놓을 수 있는 칸 중 장애물 비율)를 북서 0.15, 북 0.10, 북동 0.10, 서 0.10, 중앙 0.45, 동 0.10, 남서 0.10, 남 0.15, 남동 0.25로 맞춰줘. 장애물 종류 비율은 바위 10%, 벽 60%, 상자 15%, 수풀 15%로 해줘. 시작점에서 목표 지점까지 길은 남겨줘.",
            Json = "{\"tag_multipliers\":{\"rock\":0.1,\"wall\":6.0,\"crate\":1.5,\"bush\":1.5},\"region_density\":{\"NW\":\"mid\",\"N\":\"low\",\"NE\":\"low\",\"W\":\"low\",\"C\":\"high\",\"E\":\"low\",\"SW\":\"low\",\"S\":\"mid\",\"SE\":\"mid\"},\"hard_constraints\":{\"path_required\":true}}" },
    };

    // ── 상태
    BaseMap baseMap;
    GenerationParams current;          // 지금 적용 중인 제약 (null이면 아직 없음 → WFC 단독)
    int seed = 1;
    int[,] obsMain, obsBase;           // 제안 기법 / WFC 단독(비교용)
    GenerationResult resMain;
    int mAdj, mForb; bool mPath; double mRatio;
    string prompt = "";
    int exampleIndex = -1;             // 예시를 고른 뒤 글을 고치면 -1
    string promptAtExample = "";
    bool busy, online, checking;
    string serverModels = "";
    float busySince;
    string status = "명령을 입력하거나 예시를 고른 뒤 [생성]을 누르세요.";
    string source = "";                // 응답 출처 표시
    string rawResponse = "";
    bool forcePath = true, useCache = false, compare = true, topView = false, showSettings = false, showRaw = false;

    // ── 동선 선택 (2026-11-16 추가, E3 시연): 0 = 무작위 경로(기존 동작), 1~3 = 실험 E3의 R1~R3 동선
    //    경유점은 Experiment/v2/analysis/route_e3_1116/Experiment_e3.cs.txt의 Routes와 같다.
    int routeMode = 0;
    static readonly string[] RouteLabels = { "무작위", "R1 직선", "R2 서쪽", "R3 동쪽" };
    static readonly int[][][] RouteWaypoints =
    {
        new[] { new[] { 2, 30 }, new[] { 15, 17 }, new[] { 15, 14 }, new[] { 29, 1 } },                                                                       // R1 직선형: 다리 경유
        new[] { new[] { 2, 30 }, new[] { 0, 30 }, new[] { 0, 17 }, new[] { 14, 17 }, new[] { 14, 14 }, new[] { 0, 14 }, new[] { 0, 0 }, new[] { 29, 0 }, new[] { 29, 1 } }, // R2 서쪽·북쪽 우회
        new[] { new[] { 2, 30 }, new[] { 31, 30 }, new[] { 31, 17 }, new[] { 17, 17 }, new[] { 17, 14 }, new[] { 31, 14 }, new[] { 31, 1 }, new[] { 29, 1 } },           // R3 동쪽 우회
    };
    List<int[]>[] routes;              // 경유점을 이은 동선 칸 (Start에서 계산, 맵에 맞지 않으면 null)
    bool mRouteKept; int mSpLen;
    Vector2 scroll;

    // ── 렌더링
    readonly List<GameObject> spawned = new List<GameObject>();
    Material baseMat;
    readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
    Camera cam;
    Vector3 mainOrigin, baseOrigin;
    const float Gap = 5f;
    const float PanelRefW = 480f;      // 왼쪽 사이드바 폭 (1080p 기준 px, 화면 높이에 비례)
    float PanelFrac { get { return Mathf.Min(0.45f, PanelRefW * UiScale / Mathf.Max(1, Screen.width)); } }
    int lastW, lastH;

    static readonly string[] RegionKo = { "북서", "북", "북동", "서", "중앙", "동", "남서", "남", "남동" };
    static readonly string[] TagKo = { "바위", "벽", "상자", "수풀" };
    static readonly string[] TagEn = { "rock", "wall", "crate", "bush" };

    void Start()
    {
        Application.runInBackground = true;
        EnableIme();
        if (baseMapText == null) { status = "Base Map Text가 비어 있습니다 (Assets/Maps/base_map_01.txt)"; return; }
        baseMap = BaseMap.Parse(baseMapText.text);
        routes = BuildRoutes();

        // 기본 재질: Assets/Resources/DemoBase.mat (URP Lit). Resources 폴더 재질은 셰이더와 함께 빌드에 반드시 포함된다
        //  (도형 기본 재질만 쓰면 빌드에서 셰이더가 빠져 화면이 분홍색으로 나옴)
        baseMat = Resources.Load<Material>("DemoBase");
        if (baseMat == null)
        {
            var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            baseMat = tmp.GetComponent<Renderer>().sharedMaterial;
            Destroy(tmp);
        }

        SetupCameraAndLight();
        ApplyLayout();
        Regenerate();
        StartCoroutine(CheckServer());
    }

    // 한글 입력(IME) 켜기. IMGUI 입력 칸의 한글 조합은 기존 입력 관리자(Input Manager)가 켜져 있어야 동작한다
    //  → Project Settings > Player > Active Input Handling 을 "Both"로 둘 것.
    void EnableIme()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        Input.imeCompositionMode = IMECompositionMode.On;
#endif
#if ENABLE_INPUT_SYSTEM
        if (UnityEngine.InputSystem.Keyboard.current != null) UnityEngine.InputSystem.Keyboard.current.SetIMEEnabled(true);
#endif
    }

    // ───────────────────────────── 생성
    void Regenerate()
    {
        var gen = new ObstacleGenerator(baseMap);
        var p = Conditions.Clone(current ?? GenerationParams.Default());   // 원본(LLM 응답)은 그대로 둔다
        if (forcePath) p.PathRequired = true;
        var route = RouteCells();
        if (route != null) { p.PathRequired = true; p.FixedCorridor = route; }   // Clone은 FixedCorridor를 복사하지 않으므로 여기서 넣는다
        resMain = gen.Generate(p, seed, 20);
        obsMain = resMain.Success ? resMain.Obstacles : null;
        var pBase = GenerationParams.Default();
        if (route != null) pBase.FixedCorridor = route;   // 비교 맵도 같은 동선 → 두 맵의 차이는 명령에서만 생긴다
        obsBase = compare && current != null ? gen.Generate(pBase, seed, 20).Obstacles : null;
        if (resMain.Success)
        {   // 검사 결과는 생성할 때 한 번만 계산한다 (OnGUI는 매 프레임 여러 번 불림)
            mAdj = MapMetrics.AdjacencyViolations(resMain.Obstacles);
            mForb = MapMetrics.ForbiddenViolations(baseMap, resMain.Obstacles, p);
            var sp = PathMetrics.ShortestPath(baseMap, resMain.Obstacles);
            mPath = sp != null; mSpLen = sp != null ? sp.Count : 0;
            mRouteKept = route != null && route.TrueForAll(c => resMain.Obstacles[c[0], c[1]] == ObstacleSet.Empty);
            mRatio = MapMetrics.ObstacleRatio(baseMap, resMain.Obstacles, p);
        }
        Rebuild();
    }

    List<int[]> RouteCells() { return routeMode > 0 && routes != null ? routes[routeMode - 1] : null; }

    // 경유점 사이를 상하좌우 최단 경로(BFS, 물 제외)로 잇는다. 실험 E3(Experiment_e3.cs)의 Segment·BuildRoute와 같은 방식·같은 이웃 순서
    List<int[]>[] BuildRoutes()
    {
        var all = new List<int[]>[RouteWaypoints.Length];
        int W = baseMap.Width, H = baseMap.Height;
        int[] dx = { 1, -1, 0, 0 }, dy = { 0, 0, 1, -1 };
        for (int r = 0; r < RouteWaypoints.Length; r++)
        {
            var wp = RouteWaypoints[r]; var cells = new List<int[]>(); var seen = new HashSet<int>(); bool ok = true;
            for (int i = 0; i + 1 < wp.Length && ok; i++)
            {
                if (wp[i][0] >= W || wp[i][1] >= H || wp[i + 1][0] >= W || wp[i + 1][1] >= H) { ok = false; break; }
                var prev = new int[W * H]; for (int k = 0; k < prev.Length; k++) prev[k] = -2;
                int s = wp[i][0] * H + wp[i][1], t = wp[i + 1][0] * H + wp[i + 1][1];
                var q = new Queue<int>(); q.Enqueue(s); prev[s] = -1;
                while (q.Count > 0)
                {
                    int u = q.Dequeue(); if (u == t) break; int ux = u / H, uy = u % H;
                    for (int d = 0; d < 4; d++)
                    {
                        int nx = ux + dx[d], ny = uy + dy[d]; if (nx < 0 || ny < 0 || nx >= W || ny >= H) continue;
                        int v = nx * H + ny; if (prev[v] != -2 || baseMap.Tiles[nx, ny] == BaseTile.Water) continue;
                        prev[v] = u; q.Enqueue(v);
                    }
                }
                if (prev[t] == -2) { ok = false; break; }
                var seg = new List<int[]>(); for (int v = t; v != -1; v = prev[v]) seg.Add(new[] { v / H, v % H }); seg.Reverse();
                foreach (var c in seg) if (seen.Add(c[0] * H + c[1])) cells.Add(c);
            }
            all[r] = ok && ObstacleGenerator.ValidateCorridor(baseMap, cells) == null ? cells : null;   // 다른 베이스 맵이면 동선을 쓰지 않는다
        }
        return all;
    }

    void Apply(string llmText, string from)
    {
        rawResponse = llmText;
        var rep = ParamSchema.Parse(llmText);
        if (!rep.JsonFound) { status = "응답에서 제약(JSON)을 찾지 못했습니다. 다시 시도해 주세요."; source = from; return; }
        current = rep.Params;
        bool llmPath = current.PathRequired;
        source = from + (rep.SchemaValid ? "" : " · 스키마 일부 위반 → 기본값으로 대체") + (!llmPath && forcePath ? " · LLM이 경로 필수를 껐으나 데모 설정으로 켬" : "");
        Regenerate();
        status = "생성 완료";
    }

    IEnumerator Run()
    {
        if (busy) yield break;
        bool isExample = exampleIndex >= 0 && prompt == promptAtExample;
        if (isExample && (useCache || !online))
        {
            Apply(Examples[exampleIndex].Json, "저장된 실제 응답 (gemma4:e4b, 실험 " + Examples[exampleIndex].Source + ")");
            yield break;
        }
        if (!online)
        {
            status = "LLM 서버에 연결되지 않아 직접 입력은 쓸 수 없습니다. 예시 명령은 저장된 응답으로 실행됩니다.";
            yield break;
        }
        if (string.IsNullOrEmpty(prompt.Trim())) { status = "명령을 입력해 주세요."; yield break; }

        busy = true; busySince = Time.realtimeSinceStartup; status = "LLM 응답 대기 중..."; rawResponse = "";
        var cfg = new LlmConfig
        {
            Provider = LlmProvider.OpenAICompatible, Model = model, BaseUrl = serverUrl,
            Temperature = Math.Round((double)temperature, 3), ReasoningEffort = "none",
            ApiKey = "local"   // 데모는 실제 키를 절대 보내지 않는다
        };
        LlmHttpRequest req;
        try { req = LlmApi.Build(cfg, Prompts.ParamsSystem(baseMap), prompt); }
        catch (Exception e) { status = e.Message; busy = false; yield break; }

        using (var www = new UnityWebRequest(req.Url, "POST"))
        {
            www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(req.Body));
            www.downloadHandler = new DownloadHandlerBuffer();
            foreach (var h in req.Headers) www.SetRequestHeader(h.Key, h.Value);
            www.timeout = timeoutSeconds;
            yield return www.SendWebRequest();
            float sec = Time.realtimeSinceStartup - busySince;
            if (www.result != UnityWebRequest.Result.Success)
            {
                status = "요청 실패: " + www.error + " — 서버 주소와 모델 이름을 확인하세요.";
                rawResponse = www.downloadHandler.text; busy = false; yield break;
            }
            string text;
            try { text = LlmApi.ExtractText(cfg, www.downloadHandler.text); }
            catch (Exception e) { status = e.Message; rawResponse = www.downloadHandler.text; busy = false; yield break; }
            busy = false;
            Apply(text, string.Format("실시간 LLM ({0}, {1:0.0}초)", model, sec));
        }
    }

    IEnumerator CheckServer()
    {
        checking = true; online = false; serverModels = "";
        using (var www = UnityWebRequest.Get(serverUrl.TrimEnd('/') + "/models"))
        {
            www.SetRequestHeader("Authorization", "Bearer local");
            www.timeout = 4;
            yield return www.SendWebRequest();
            if (www.result == UnityWebRequest.Result.Success)
            {
                string t = www.downloadHandler.text;
                online = t.Contains("\"" + model + "\"") || t.Contains(model);
                serverModels = online ? "" : "서버는 응답했지만 모델 '" + model + "'이 없습니다 (ollama pull " + model + ")";
            }
        }
        checking = false;
    }



    // ───────────────────────────── 화면 배치 (해상도가 바뀌면 다시 계산 → FHD·노트북·16:10 모두에서 잘리지 않게)
    float UiScale { get { return Mathf.Max(0.75f, Screen.height / 1080f); } }

    void Update()
    {
        if (Screen.width != lastW || Screen.height != lastH) ApplyLayout();
    }

    void ApplyLayout()
    {
        lastW = Screen.width; lastH = Screen.height;
        if (cam != null) cam.rect = new Rect(PanelFrac, 0f, 1f - PanelFrac, 1f);
        PlaceCamera();
    }

    void SetupCameraAndLight()
    {
        cam = Camera.main;
        if (cam == null) { var go = new GameObject("Main Camera"); go.tag = "MainCamera"; cam = go.AddComponent<Camera>(); }
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.93f, 0.94f, 0.96f);
        if (FindObjectOfType<Light>() == null)
        {
            var l = new GameObject("Sun").AddComponent<Light>(); l.type = LightType.Directional;
            l.transform.rotation = Quaternion.Euler(55, -30, 0);
        }
        // 원근 화면에서 높이가 보이도록 방향광 그림자를 켠다 (2026-11-16 추가). 이미 켜져 있으면 그대로 둔다
        var sunLight = FindObjectOfType<Light>();
        if (perspectiveCamera && sunLight != null && sunLight.type == LightType.Directional && sunLight.shadows == LightShadows.None) sunLight.shadows = LightShadows.Soft;
    }

    // 정사영(orthographic) 카메라: 두 맵이 같은 크기로 보여 비교가 공정하고, 화면 비율에 맞춰 계산으로 딱 맞춘다
    void PlaceCamera()
    {
        if (cam == null || baseMap == null) return;
        if (perspectiveCamera) { PlacePerspective(); return; }
        float W = baseMap.Width, H = baseMap.Height;
        float totalW = obsBase != null ? W * 2 + Gap : W;
        var rot = Quaternion.Euler(topView ? 90f : 52f, 0f, 0f);
        cam.orthographic = true;
        cam.transform.rotation = rot;
        Vector3 r = rot * Vector3.right, u = rot * Vector3.up, f = rot * Vector3.forward;
        float xMin = 1e9f, xMax = -1e9f, yMin = 1e9f, yMax = -1e9f;
        foreach (var xx in new[] { -0.5f, totalW - 0.5f })
            foreach (var zz in new[] { -0.5f, H - 0.5f })
                foreach (var yy in new[] { -0.4f, 2.1f })
                {
                    var c = new Vector3(xx, yy, zz);
                    float a = Vector3.Dot(c, r), b = Vector3.Dot(c, u);
                    xMin = Mathf.Min(xMin, a); xMax = Mathf.Max(xMax, a); yMin = Mathf.Min(yMin, b); yMax = Mathf.Max(yMax, b);
                }
        float ph = Mathf.Max(1f, cam.pixelHeight);
        float top = 125f * UiScale / ph, bot = 72f * UiScale / ph, side = 0.035f;   // 위: HUD·맵 이름 배너 / 아래: 범례 바
        float usableH = 1f - top - bot;
        float size = Mathf.Max((yMax - yMin) / 2f / usableH, (xMax - xMin) / 2f / (cam.aspect * (1f - 2f * side)));
        cam.orthographicSize = size;
        float v0 = bot + usableH / 2f;
        var center = r * ((xMin + xMax) / 2f) + u * ((yMin + yMax) / 2f);
        cam.transform.position = center - u * ((v0 - 0.5f) * 2f * size) - f * 200f;
        cam.nearClipPlane = 0.1f; cam.farClipPlane = 500f;
    }

    // Play 중 Inspector에서 카메라 값을 바꾸면 바로 다시 배치 (2026-11-16 추가)
    void OnValidate()
    {
        if (Application.isPlaying && cam != null && baseMap != null) PlaceCamera();
    }

    // 원근 카메라 (2026-11-16 추가): 남쪽 위에서 비스듬히 내려다본다.
    //  맵 모서리 8점이 화면 여백(위: HUD·맵 이름 배너, 아래: 범례 바) 안에 들어오도록 거리와 위치를 반복해서 맞춘다
    void PlacePerspective()
    {
        float W = baseMap.Width, H = baseMap.Height;
        float totalW = obsBase != null ? W * 2 + Gap : W;
        cam.orthographic = false;
        cam.fieldOfView = perspectiveFov;
        cam.nearClipPlane = 0.3f; cam.farClipPlane = 1000f;
        var rot = Quaternion.Euler(topView ? 90f : perspectivePitch, 0f, 0f);
        cam.transform.rotation = rot;
        Vector3 r = rot * Vector3.right, u = rot * Vector3.up, f = rot * Vector3.forward;
        var center = new Vector3((totalW - 1f) / 2f, 0f, (H - 1f) / 2f);
        float ph = Mathf.Max(1f, cam.pixelHeight);
        float top = 125f * UiScale / ph, bot = 72f * UiScale / ph, side = 0.035f;
        float usableH = 1f - top - bot, v0 = bot + usableH / 2f;
        var corners = new List<Vector3>();
        foreach (var xx in new[] { -0.5f, totalW - 0.5f })
            foreach (var zz in new[] { -0.5f, H - 0.5f })
                foreach (var yy in new[] { -0.4f, 2.1f })
                    corners.Add(new Vector3(xx, yy, zz));
        float tanHalf = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float dist = Mathf.Max(totalW, H) * 2f;
        Vector3 off = Vector3.zero;
        for (int it = 0; it < 40; it++)
        {
            cam.transform.position = center + off - f * dist;
            float vx0 = 1e9f, vx1 = -1e9f, vy0 = 1e9f, vy1 = -1e9f;
            foreach (var c in corners)
            {
                var v = cam.WorldToViewportPoint(c);
                vx0 = Mathf.Min(vx0, v.x); vx1 = Mathf.Max(vx1, v.x); vy0 = Mathf.Min(vy0, v.y); vy1 = Mathf.Max(vy1, v.y);
            }
            float worldH = 2f * dist * tanHalf, worldW = worldH * cam.aspect;
            off += r * (((vx0 + vx1) / 2f - 0.5f) * worldW) + u * (((vy0 + vy1) / 2f - v0) * worldH);   // 가운데 맞추기
            float scale = Mathf.Max((vx1 - vx0) / (1f - 2f * side), (vy1 - vy0) / usableH);             // 크기 맞추기
            dist *= Mathf.Lerp(1f, scale, 0.7f);
        }
        cam.transform.position = center + off - f * dist;
    }

    // ───────────────────────────── 3D 표시
    Material M(string key, Color c)
    {
        Material m;
        if (!mats.TryGetValue(key, out m)) { m = new Material(baseMat); m.color = c; mats[key] = m; }
        return m;
    }

    static readonly Color CRock = new Color(0.50f, 0.50f, 0.52f), CWall = new Color(0.40f, 0.26f, 0.16f),
                          CCrate = new Color(0.90f, 0.57f, 0.18f), CBush = new Color(0.22f, 0.58f, 0.24f);

    void Rebuild()
    {
        foreach (var g in spawned) Destroy(g);
        spawned.Clear();
        int W = baseMap.Width;
        bool two = obsBase != null;
        baseOrigin = Vector3.zero;
        mainOrigin = two ? new Vector3(W + Gap, 0, 0) : Vector3.zero;
        if (two) DrawMap(obsBase, baseOrigin, "WFC 단독");
        DrawMap(obsMain, mainOrigin, "제안 기법");
        PlaceCamera();
    }

    void DrawMap(int[,] obs, Vector3 origin, string name)
    {
        var parent = new GameObject(name); parent.transform.position = origin; spawned.Add(parent);
        int W = baseMap.Width, H = baseMap.Height;
        for (int y = 0; y < H; y++)
            for (int x = 0; x < W; x++)
            {
                var pos = origin + new Vector3(x, 0, H - 1 - y);   // 첫 줄이 북쪽(+z)
                var t = baseMap.Tiles[x, y];
                bool water = t == BaseTile.Water;
                var tile = Box(PrimitiveType.Cube, pos + new Vector3(0, water ? -0.35f : -0.1f, 0), new Vector3(1f, water ? 0.1f : 0.2f, 1f), 0f,
                    water ? M("water", new Color(0.30f, 0.55f, 0.85f)) : t == BaseTile.Road ? M("road", new Color(0.62f, 0.52f, 0.38f)) : M("ground", new Color(0.80f, 0.76f, 0.62f)));
                tile.transform.SetParent(parent.transform);
                if (t == BaseTile.Spawn) Box(PrimitiveType.Cylinder, pos + Vector3.up, new Vector3(0.6f, 1f, 0.6f), 0f, M("start", Color.white)).transform.SetParent(parent.transform);
                if (t == BaseTile.Goal) Box(PrimitiveType.Cylinder, pos + Vector3.up, new Vector3(0.6f, 1f, 0.6f), 0f, M("goal", new Color(0.9f, 0.15f, 0.15f))).transform.SetParent(parent.transform);
                if (obs != null && obs[x, y] != ObstacleSet.Empty) Obstacle(obs[x, y], pos, parent.transform, x, y);
            }
        var route = RouteCells();
        if (route != null)
            foreach (var c in route)
            {   // 지정 동선: 바닥 위 얇은 보라색 판 (노란 경로 띠보다 아래). 노란 띠가 판을 벗어나면 지름길이 있다는 뜻
                var rp = origin + new Vector3(c[0], 0.005f, H - 1 - c[1]);
                Box(PrimitiveType.Cube, rp, new Vector3(0.92f, 0.02f, 0.92f), 0f, M("route", new Color(0.58f, 0.36f, 0.82f))).transform.SetParent(parent.transform);
            }
        if (obs != null)
        {   // 경로: 칸 사이를 잇는 납작한 노란 띠 (조명을 받아도 색이 그대로 보이도록 선 대신 상자로 그림)
            var p = PathMetrics.ShortestPath(baseMap, obs);
            if (p != null)
                for (int i = 1; i < p.Count; i++)
                {
                    var a = origin + new Vector3(p[i - 1][0], 0.03f, H - 1 - p[i - 1][1]);
                    var b = origin + new Vector3(p[i][0], 0.03f, H - 1 - p[i][1]);
                    var d = b - a;
                    var seg = Box(PrimitiveType.Cube, (a + b) * 0.5f, new Vector3(0.26f, 0.05f, d.magnitude + 0.26f), 0f, M("path", new Color(1f, 0.85f, 0.1f)));
                    seg.transform.rotation = Quaternion.LookRotation(d);
                    seg.transform.SetParent(parent.transform);
                }
        }
    }

    // 장애물: 단순 도형 (모양 + 색으로 종류 구분). 프리팹을 넣으면 그것을 쓴다
    void Obstacle(int k, Vector3 pos, Transform parent, int x, int y)
    {
        int hash = (x * 73856093) ^ (y * 19349663);
        float yaw = (hash & 3) * 90f;
        GameObject prefab = k == 1 ? rockPrefab : k == 2 ? wallPrefab : k == 3 ? cratePrefab : bushPrefab;
        GameObject g;
        if (prefab != null) { Instantiate(prefab, pos, Quaternion.Euler(0, yaw, 0), parent); return; }
        switch (k)
        {
            case 1: g = Box(PrimitiveType.Sphere, pos + Vector3.up * 0.3f, new Vector3(0.9f, 0.7f, 0.9f), 0f, M("rock", CRock)); break;          // 바위: 납작한 회색 구
            case 2: g = Box(PrimitiveType.Cube, pos + Vector3.up * 0.8f, new Vector3(1f, 1.6f, 1f), 0f, M("wall", CWall)); break;                // 벽: 높은 갈색 기둥
            case 3: g = Box(PrimitiveType.Cube, pos + Vector3.up * 0.35f, Vector3.one * 0.7f, yaw / 6f, M("crate", CCrate)); break;              // 상자: 낮은 주황 상자
            default: g = Box(PrimitiveType.Sphere, pos + Vector3.up * 0.35f, new Vector3(0.95f, 0.8f, 0.95f), 0f, M("bush", CBush)); break;      // 수풀: 초록 구
        }
        g.transform.SetParent(parent);
    }

    GameObject Box(PrimitiveType t, Vector3 pos, Vector3 scale, float yaw, Material m)
    {
        var g = GameObject.CreatePrimitive(t);
        Destroy(g.GetComponent<Collider>());
        g.transform.position = pos; g.transform.localScale = scale; g.transform.rotation = Quaternion.Euler(0, yaw, 0);
        g.GetComponent<Renderer>().sharedMaterial = m;
        return g;
    }

    // ───────────────────────────── UI (IMGUI: 입력 시스템 설정과 무관하게 동작)
    // 화면 3분할: ① 왼쪽 사이드바(입력·생성·제약 요약, 맨 아래 시연자용 시스템 바) ② 맵 위 HUD(검사 결과 배지 + 보기 전환) ③ 맵 아래 범례 바
    // 모든 좌표는 1080p 기준(px)으로 쓰고 화면 높이에 비례해 확대·축소한다
    GUIStyle sTitle, sH, sBody, sSmall, sCell, sBtnBig, sOk, sBad, sExample, sSecond, sTgSys, sTgHud, sSide, sBoxBg, sSys, sSysBtn, sHud, sBadge, sBanner, sBannerMain, sLegendBar, sRouteBtn, sRouteOn;
    float S { get { return UiScale; } }

    static Texture2D Rounded(Color fill, int r)
    {
        int n = 2 * r + 4;
        var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
        t.wrapMode = TextureWrapMode.Clamp; t.hideFlags = HideFlags.DontSave;
        for (int y = 0; y < n; y++)
            for (int x = 0; x < n; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float cx = Mathf.Clamp(px, r, n - r), cy = Mathf.Clamp(py, r, n - r);
                float d = Mathf.Sqrt((px - cx) * (px - cx) + (py - cy) * (py - cy));
                var c = fill; c.a *= Mathf.Clamp01(r - d + 0.5f);
                t.SetPixel(x, y, c);
            }
        t.Apply();
        return t;
    }

    static GUIStyle Filled(GUIStyle from, Color fill, Color hover, int r)
    {
        var s = new GUIStyle(from);
        s.normal.background = Rounded(fill, r); s.hover.background = Rounded(hover, r); s.active.background = Rounded(Color.Lerp(hover, Color.black, 0.15f), r);
        s.focused.background = s.normal.background;
        s.border = new RectOffset(r + 1, r + 1, r + 1, r + 1);
        return s;
    }

    static void TextColor(GUIStyle s, Color c)
    {
        s.normal.textColor = s.hover.textColor = s.active.textColor = s.focused.textColor = c;
        s.onNormal.textColor = s.onHover.textColor = s.onActive.textColor = s.onFocused.textColor = c;
    }

    void Styles()
    {
        if (sTitle != null) return;
        var L = GUI.skin.label;
        sTitle = new GUIStyle(L) { fontSize = 28, fontStyle = FontStyle.Bold, wordWrap = true };
        sH = new GUIStyle(L) { fontSize = 20, fontStyle = FontStyle.Bold };
        sBody = new GUIStyle(L) { fontSize = 18, wordWrap = true, richText = true };
        sSmall = new GUIStyle(L) { fontSize = 15, wordWrap = true, richText = true };
        sCell = new GUIStyle(GUI.skin.box) { fontSize = 16, alignment = TextAnchor.MiddleCenter };
        sOk = new GUIStyle(sBody); sOk.normal.textColor = new Color(0.18f, 0.80f, 0.44f);
        sBad = new GUIStyle(sBody); sBad.normal.textColor = new Color(0.91f, 0.30f, 0.24f);

        // [생성]: 파란 배경 강조
        sBtnBig = Filled(GUI.skin.button, new Color(0.16f, 0.45f, 0.90f), new Color(0.22f, 0.53f, 0.98f), 6);
        sBtnBig.fontSize = 22; sBtnBig.fontStyle = FontStyle.Bold; TextColor(sBtnBig, Color.white);

        // 사이드바 배경 / 제약 요약 박스 / 시스템 바
        sSide = new GUIStyle(GUIStyle.none); sSide.normal.background = Rounded(new Color(0.12f, 0.14f, 0.17f), 1); sSide.border = new RectOffset(2, 2, 2, 2);
        sBoxBg = Filled(GUIStyle.none, new Color(1f, 1f, 1f, 0.06f), new Color(1f, 1f, 1f, 0.06f), 6);
        sBoxBg.padding = new RectOffset(12, 12, 10, 10); sBoxBg.margin = new RectOffset(0, 0, 6, 6);
        sSys = new GUIStyle(L) { fontSize = 14, wordWrap = true, richText = true }; TextColor(sSys, new Color(0.62f, 0.65f, 0.70f));
        sSysBtn = new GUIStyle(GUI.skin.button) { fontSize = 14 };

        // 맵 위 HUD / 배지 / 맵 이름 배너 / 범례 바 (반투명 어두운 배경)
        sHud = Filled(GUIStyle.none, new Color(0.08f, 0.10f, 0.13f, 0.82f), new Color(0.08f, 0.10f, 0.13f, 0.82f), 8);
        sHud.padding = new RectOffset(16, 12, 6, 6);
        sBadge = new GUIStyle(L) { fontSize = 18, fontStyle = FontStyle.Bold, richText = true, alignment = TextAnchor.MiddleLeft, wordWrap = false }; TextColor(sBadge, new Color(0.93f, 0.94f, 0.95f));
        sBanner = Filled(GUIStyle.none, new Color(0.30f, 0.33f, 0.38f, 0.92f), new Color(0.30f, 0.33f, 0.38f, 0.92f), 8);
        sBanner.fontSize = 21; sBanner.fontStyle = FontStyle.Bold; sBanner.alignment = TextAnchor.MiddleCenter; TextColor(sBanner, Color.white);
        sBannerMain = Filled(sBanner, new Color(0.16f, 0.45f, 0.90f, 0.95f), new Color(0.16f, 0.45f, 0.90f, 0.95f), 8);
        sLegendBar = Filled(GUIStyle.none, new Color(0.08f, 0.10f, 0.13f, 0.82f), new Color(0.08f, 0.10f, 0.13f, 0.82f), 8);
        sLegendBar.fontSize = 18; sLegendBar.richText = true; sLegendBar.alignment = TextAnchor.MiddleCenter; TextColor(sLegendBar, new Color(0.93f, 0.94f, 0.95f));

        sExample = new GUIStyle(GUI.skin.button) { fontSize = 16, wordWrap = true };
        sRouteBtn = new GUIStyle(GUI.skin.button) { fontSize = 15 };
        sRouteOn = Filled(GUI.skin.button, new Color(0.45f, 0.28f, 0.70f), new Color(0.52f, 0.34f, 0.78f), 6);   // 고른 동선 버튼: 보라색 계열 (물 색과 구분)
        sRouteOn.fontSize = 15; sRouteOn.fontStyle = FontStyle.Bold; TextColor(sRouteOn, Color.white);
        sSecond = new GUIStyle(GUI.skin.button) { fontSize = 16 };
        sTgSys = new GUIStyle(GUI.skin.toggle) { fontSize = 14 }; TextColor(sTgSys, new Color(0.62f, 0.65f, 0.70f));
        sTgHud = new GUIStyle(GUI.skin.toggle) { fontSize = 17 }; TextColor(sTgHud, new Color(0.93f, 0.94f, 0.95f));
        GUI.skin.button.fontSize = 18; GUI.skin.textArea.fontSize = 19; GUI.skin.textField.fontSize = 16; GUI.skin.toggle.fontSize = 18;
    }

    void OnGUI()
    {
        Styles();
        float s = S;
        GUI.matrix = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, new Vector3(s, s, 1f));
        float refW = Screen.width / s, refH = Screen.height / s, pw = PanelRefW;

        // ① 왼쪽 사이드바: 위(입력·생성·제약 요약, 넘치면 스크롤) + 맨 아래(시스템 바)
        GUI.Box(new Rect(0, 0, pw, refH), "", sSide);
        float sysH = showSettings ? 250f : 118f;
        GUILayout.BeginArea(new Rect(18, 16, pw - 34, refH - sysH - 24));
        scroll = GUILayout.BeginScrollView(scroll);
        DrawSidebar();
        GUILayout.EndScrollView();
        GUILayout.EndArea();
        GUILayout.BeginArea(new Rect(18, refH - sysH, pw - 34, sysH - 10));
        GUILayout.FlexibleSpace();
        DrawSystemBar();
        GUILayout.EndArea();

        // ② 맵 위 HUD, ③ 맵 이름 배너·범례 바
        DrawHud(pw, refW);
        DrawMapBanners(s);
        string legend = "<color=#95A5A6>●</color> 바위    <color=#6E473B>■</color> 벽    <color=#E67E22>■</color> 상자    <color=#27AE60>●</color> 수풀    " +
                        "<color=#F1C40F>━</color> 경로    " + (routeMode > 0 ? "<color=#945CD1>■</color> 동선    " : "") + "<color=#ECF0F1>■</color> 시작    <color=#E74C3C>■</color> 목표";
        float lw = Mathf.Min(routeMode > 0 ? 820f : 720f, refW - pw - 40f);
        GUI.Box(new Rect(pw + (refW - pw - lw) / 2f, refH - 58, lw, 42), legend, sLegendBar);
    }

    void DrawSidebar()
    {
        GUILayout.Label("자연어 명령 → LLM 제약 → WFC 장애물 배치", sTitle);
        GUILayout.Space(10);

        // 명령 입력: 라벨 줄 오른쪽에 [붙여넣기] [지우기]
        GUILayout.BeginHorizontal();
        GUILayout.Label("명령 입력", sH);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("붙여넣기", sSysBtn, GUILayout.Width(78), GUILayout.Height(26))) prompt = GUIUtility.systemCopyBuffer ?? "";   // 한글 입력이 안 되는 환경 대비
        if (GUILayout.Button("지우기", sSysBtn, GUILayout.Width(64), GUILayout.Height(26))) prompt = "";
        GUILayout.EndHorizontal();
        string before = prompt;
        prompt = GUILayout.TextArea(prompt, GUILayout.Height(112));
#if !ENABLE_LEGACY_INPUT_MANAGER
        GUILayout.Label("※ 한글 입력이 안 되면 메모장에 쓰고 [붙여넣기]", sSys);
#endif
        if (prompt != before && prompt != promptAtExample) exampleIndex = -1;

        // 예시 명령: 2열 그리드
        GUILayout.Space(6);
        GUILayout.Label("예시 명령 (실험에 쓴 명령)", sSmall);
        for (int i = 0; i < Examples.Length; i += 2)
        {
            GUILayout.BeginHorizontal();
            for (int j = i; j < i + 2; j++)
            {
                if (j >= Examples.Length) { GUILayout.Label("", GUILayout.ExpandWidth(true)); continue; }
                if (GUILayout.Button(Examples[j].Label, sExample, GUILayout.Height(38), GUILayout.ExpandWidth(true))) { exampleIndex = j; prompt = promptAtExample = Examples[j].Prompt; }
            }
            GUILayout.EndHorizontal();
        }

        // 실행 액션 바: [생성] 7 : [다른 초안] 3
        GUILayout.Space(10);
        float aw = PanelRefW - 34 - 30 - 8;   // 스크롤 막대·여백 제외
        GUILayout.BeginHorizontal();
        GUI.enabled = !busy;
        if (GUILayout.Button(busy ? string.Format("LLM 응답 대기 중... {0:0}초", Time.realtimeSinceStartup - busySince) : "생성", sBtnBig, GUILayout.Width(aw * 0.7f), GUILayout.Height(48))) StartCoroutine(Run());
        if (GUILayout.Button("다른 초안\n(seed 변경)", sSecond, GUILayout.Width(aw * 0.3f), GUILayout.Height(48))) { seed++; Regenerate(); }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        bool nf = GUILayout.Toggle(forcePath, " 경로 필수 고정");
        if (nf != forcePath) { forcePath = nf; Regenerate(); }
        GUILayout.BeginHorizontal();
        GUILayout.Label("동선", sBody, GUILayout.Width(44));
        int nr = routeMode;
        for (int i = 0; i < RouteLabels.Length; i++)
        {
            GUI.enabled = i == 0 || (routes != null && routes[i - 1] != null);
            if (GUILayout.Button(RouteLabels[i], i == routeMode ? sRouteOn : sRouteBtn, GUILayout.Height(30))) nr = i;
        }
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        if (nr != routeMode) { routeMode = nr; Regenerate(); }
        if (routeMode > 0) GUILayout.Label("보라색 칸(지정 동선)은 비워 두고 배치합니다. 경로 필수는 자동으로 켜집니다. 노란 경로가 보라색을 벗어나면 더 짧은 지름길이 있다는 뜻입니다.", sSmall);
        GUILayout.Label(status, sBody);

        // LLM 추출 제약 조건 (요약 박스)
        GUILayout.BeginVertical(sBoxBg);
        GUILayout.Label("LLM 추출 제약 조건", sH);
        if (current == null)
            GUILayout.Label("아직 생성 전입니다. 명령을 입력하고 [생성]을 누르면 LLM이 만든 제약(구역 밀도, 장애물 종류 가중치, 경로 필수)이 여기에 표시됩니다.", sSmall);
        else
        {
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical();
            GUILayout.Label("구역 밀도 (북쪽이 위)", sSmall);
            for (int r = 0; r < 3; r++)
            {
                GUILayout.BeginHorizontal();
                for (int c = 0; c < 3; c++)
                {
                    int k = r * 3 + c; string lv;
                    if (!current.RegionDensity.TryGetValue(Regions.Names[k], out lv)) lv = "mid";
                    var old = GUI.backgroundColor;
                    GUI.backgroundColor = lv == "high" ? new Color(0.35f, 0.45f, 0.75f) : lv == "low" ? new Color(0.92f, 0.95f, 1f) : new Color(0.68f, 0.76f, 0.92f);
                    GUILayout.Box(RegionKo[k] + "\n" + (lv == "high" ? "높음" : lv == "low" ? "낮음" : "보통"), sCell, GUILayout.Width(66), GUILayout.Height(46));
                    GUI.backgroundColor = old;
                }
                GUILayout.EndHorizontal();
            }
            GUILayout.EndVertical();
            GUILayout.Space(10);
            GUILayout.BeginVertical();
            GUILayout.Label("장애물 종류 가중치", sSmall);
            for (int i = 0; i < 4; i++)
            {
                double v; if (!current.TagMultipliers.TryGetValue(TagEn[i], out v)) v = 1.0;
                GUILayout.BeginHorizontal();
                GUILayout.Label(TagKo[i], sBody, GUILayout.Width(44));
                GUILayout.Label("<b>×" + v.ToString("0.0") + "</b>", sBody, GUILayout.Width(54));
                GUILayout.Box("", GUILayout.Width((float)Math.Min(v, 6.0) / 6f * 70f + 2f), GUILayout.Height(14));
                GUILayout.EndHorizontal();
            }
            GUILayout.Label("경로 필수: " + (current.PathRequired ? "예" : "아니오") + (forcePath && !current.PathRequired ? " → 데모 설정으로 켬" : ""), sSmall);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }
        if (source != "") GUILayout.Label("응답 출처: " + source, sSmall);
        if (rawResponse != "")
        {
            showRaw = GUILayout.Toggle(showRaw, " LLM 원문 응답 보기");
            if (showRaw) GUILayout.Label(rawResponse, sSmall);
        }
        GUILayout.EndVertical();
    }

    // 시연자 전용: 작고 덜 눈에 띄게, 사이드바 맨 아래
    void DrawSystemBar()
    {
        var line = GUILayoutUtility.GetRect(1f, 1f, GUILayout.ExpandWidth(true));
        var oc = GUI.color; GUI.color = new Color(1f, 1f, 1f, 0.15f); GUI.DrawTexture(line, Texture2D.whiteTexture); GUI.color = oc;
        GUILayout.Space(4);
        if (showSettings)
        {
            GUILayout.Label("서버 주소 (Ollama 기본 http://localhost:11434/v1)", sSys);
            serverUrl = GUILayout.TextField(serverUrl);
            GUILayout.Label("모델 이름", sSys);
            model = GUILayout.TextField(model);
            GUILayout.Space(4);
        }
        GUILayout.BeginHorizontal();
        if (checking) GUILayout.Label("● LLM 서버 확인 중...", sSys);
        else if (online) GUILayout.Label("<color=#2ECC71>●</color> LLM 연결됨 (" + model + ")", sSys);
        else GUILayout.Label("<color=#E74C3C>●</color> LLM 연결 안 됨 (예시만 저장된 응답으로 실행)", sSys);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("다시 확인", sSysBtn, GUILayout.Width(76), GUILayout.Height(24))) StartCoroutine(CheckServer());
        if (GUILayout.Button(showSettings ? "설정 닫기" : "설정", sSysBtn, GUILayout.Width(70), GUILayout.Height(24))) showSettings = !showSettings;
        if (GUILayout.Button("종료", sSysBtn, GUILayout.Width(50), GUILayout.Height(24))) Application.Quit();
        GUILayout.EndHorizontal();
        if (serverModels != "") GUILayout.Label(serverModels, sSys);
        useCache = GUILayout.Toggle(useCache, " 예시 명령은 저장된 응답 사용 (빠른 시연용)", sTgSys);
    }

    // ② 맵 위 HUD: 왼쪽 = 배치 결과 검사 배지, 오른쪽 끝 = 보기 전환
    void DrawHud(float pw, float refW)
    {
        GUILayout.BeginArea(new Rect(pw + 20, 15, refW - pw - 40, 48), sHud);
        GUILayout.BeginHorizontal(GUILayout.Height(36));
        const string sep = "  <color=#5D6D7E>|</color>  ";
        bool narrow = refW - pw < 1350f;   // 좁은 화면(1280×800 등)에서는 짧은 문구
        string txt;
        if (busy) txt = string.Format("<color=#F1C40F>LLM이 제약을 만드는 중... {0:0}초</color>", Time.realtimeSinceStartup - busySince);
        else if (resMain == null) txt = "";
        else if (!resMain.Success) txt = "Seed: " + seed + sep + "<color=#E74C3C>WFC 실패 (20회 모두 모순) → [다른 초안]</color>";
        else
            txt = "Seed: " + seed + sep +
                  Pass(mPath, (narrow ? "" : "시작~목표 ") + (mPath ? "경로 확보" : "경로 없음")) + sep +
                  Pass(mAdj == 0, (narrow ? "인접 위반 " : "인접 규칙 위반 ") + mAdj + "칸") + sep +
                  Pass(mForb == 0, "지형 침범 " + mForb + "칸") + sep +
                  (RouteCells() != null
                      ? Pass(mRouteKept, "동선 유지") + sep + "최단 " + mSpLen + "칸 / 동선 " + RouteCells().Count + "칸"
                      : "장애물 비율: <color=#F1C40F>" + mRatio.ToString("P0") + "</color>");
        GUILayout.Label(txt, sBadge, GUILayout.Height(36));
        GUILayout.FlexibleSpace();
        bool nc = GUILayout.Toggle(compare, " WFC 단독과 비교", sTgHud, GUILayout.Height(36));
        GUILayout.Space(10);
        bool nt = GUILayout.Toggle(topView, " 위에서 보기", sTgHud, GUILayout.Height(36));
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
        if (nc != compare) { compare = nc; Regenerate(); }
        if (nt != topView) { topView = nt; PlaceCamera(); }
    }

    static string Pass(bool ok, string text)
    {
        return ok ? "<color=#2ECC71>● " + text + "</color>" : "<color=#E74C3C>● " + text + "</color>";
    }

    // 각 맵 위쪽 가운데: 모드 이름 배너
    void DrawMapBanners(float s)
    {
        if (cam == null || baseMap == null) return;
        if (obsBase != null) MapBanner(baseOrigin, "① WFC 단독 (명령 반영 없음)", sBanner, s);
        MapBanner(mainOrigin, current == null ? "WFC 기본값 (명령 없음)" : "제안 기법 (LLM 제약 → WFC)", sBannerMain, s);
    }

    void MapBanner(Vector3 origin, string text, GUIStyle st, float s)
    {
        var p = cam.WorldToScreenPoint(origin + new Vector3(baseMap.Width / 2f - 0.5f, 1.6f, baseMap.Height - 0.5f));   // 맵 뒤쪽(북쪽) 가장자리
        if (p.z < 0) return;
        float bw = 400f, bh = 40f;
        GUI.Box(new Rect(p.x / s - bw / 2f, (Screen.height - p.y) / s - bh - 10f, bw, bh), text, st);
    }
}
