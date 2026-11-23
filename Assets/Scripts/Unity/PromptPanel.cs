// PromptPanel.cs
// Unity 화면에서 프롬프트를 입력 → LLM이 JSON 파라미터 생성 → MapView가 WFC로 맵 생성.
// 사용법: MapView가 붙은 GameObject에 같이 붙이고, Inspector에서 Provider·Model·Base Url을 정한 뒤 Play.
// API 키는 Inspector에 넣지 않는다 (씬 파일에 저장되어 OneDrive로 동기화되는 것을 막기 위해).
//   → 환경 변수 LLM_API_KEY 또는 사용자 폴더의 .llm_api_key 파일 (LlmApi.ResolveApiKey 참고)
// LLM 응답 로그는 프로젝트 폴더의 LLMLogs/ 에 저장된다 (Assets 밖이라 Unity가 가져오지 않음).
// Unity 2020.2 이상 필요 (UnityWebRequest.result).

using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ObstacleWFC;

[RequireComponent(typeof(MapView))]
public class PromptPanel : MonoBehaviour
{
    [Header("LLM 설정 (API 키는 여기 넣지 않음)")]
    public LlmProvider provider = LlmProvider.OpenAICompatible;
    public string model = "gemini-3.8-flash";
    [Tooltip("비우면 기본 주소. OpenAI 형식을 쓰는 다른 서비스는 여기에 주소를 넣는다")]
    public string baseUrl = "https://generativelanguage.googleapis.com/v1beta/openai";
    public float temperature = 0.7f;
    public bool sendTemperature = true;
    [Tooltip("생각 모드. 비우면 모델 기본값. 로컬 Ollama 모델은 \"none\"")]
    public string reasoningEffort = "";

    MapView mapView;
    string prompt = "북쪽에 장애물을 몰아서 배치하고, 시작점에서 목표 지점까지 길은 남겨줘";
    string status = "";
    string lastResponse = "";
    bool busy;
    Vector2 scroll;

    void Awake() { mapView = GetComponent<MapView>(); }

    void OnGUI()
    {
        float s = MapView.GuiScale();
        GUILayout.BeginArea(new Rect(10, 130, 520, Screen.height / s - 140), GUI.skin.box);
        GUILayout.Label("프롬프트");
        prompt = GUILayout.TextArea(prompt, GUILayout.Height(60));
        GUILayout.BeginHorizontal();
        GUI.enabled = !busy;
        if (GUILayout.Button(busy ? "LLM 응답 대기 중..." : "LLM으로 생성", GUILayout.Width(160))) StartCoroutine(Run());
        GUI.enabled = true;
        if (GUILayout.Button("Inspector 값으로 되돌리기", GUILayout.Width(180))) mapView.ClearOverride();
        GUI.enabled = !busy;
        if (GUILayout.Button("모델 목록", GUILayout.Width(100))) StartCoroutine(ListModels());
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.Label(status);
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Label(lastResponse);
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    IEnumerator Run()
    {
        busy = true; status = ""; lastResponse = "";
        var cfg = new LlmConfig
        {
            Provider = provider,
            Model = model,
            BaseUrl = baseUrl,
            Temperature = sendTemperature ? (double?)Math.Round((double)temperature, 3) : null, // float→double 오차(0.699999…) 제거
            ReasoningEffort = reasoningEffort,
            ApiKey = LlmApi.KeyFor(baseUrl)
        };
        if (string.IsNullOrEmpty(cfg.ApiKey))
        {
            status = "API 키가 없습니다 (환경 변수 LLM_API_KEY 또는 사용자 폴더의 .llm_api_key 파일)";
            busy = false; yield break;
        }

        string keyProblem = LlmApi.DiagnoseKey(cfg.ApiKey);
        if (keyProblem != null) { status = keyProblem; busy = false; yield break; }

        LlmHttpRequest req;
        try { req = LlmApi.Build(cfg, Prompts.ParamsSystem(mapView.BaseMapData), prompt); }
        catch (Exception e) { status = e.Message; busy = false; yield break; }

        string body = null;
        int retries = 0;
        for (int attempt = 0; ; attempt++)
        {
            using (var www = new UnityWebRequest(req.Url, "POST"))
            {
                www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(req.Body));
                www.downloadHandler = new DownloadHandlerBuffer();
                foreach (var h in req.Headers) www.SetRequestHeader(h.Key, h.Value);
                www.timeout = 120;
                status = attempt == 0 ? "LLM 요청 중..." : string.Format("재시도 {0}/{1} 요청 중...", attempt, LlmApi.RetryDelaysSeconds.Length);
                yield return www.SendWebRequest();

                body = www.downloadHandler.text;
                if (www.result == UnityWebRequest.Result.Success) break;

                bool canRetry = LlmApi.IsRetryable(www.responseCode) && attempt < LlmApi.RetryDelaysSeconds.Length;
                if (!canRetry)
                {
                    status = "요청 실패: " + www.error + (retries > 0 ? " (재시도 " + retries + "회 후)" : "");
                    lastResponse = body;
                    busy = false; yield break;
                }
                int wait = LlmApi.RetryDelaysSeconds[attempt];
                retries++;
                for (int sec = wait; sec > 0; sec--)
                {
                    status = string.Format("서버 응답 {0} → {1}초 뒤 재시도 ({2}/{3})", www.responseCode, sec, retries, LlmApi.RetryDelaysSeconds.Length);
                    yield return new WaitForSeconds(1f);
                }
            }
        }

        string text;
        try { text = LlmApi.ExtractText(cfg, body); }
        catch (Exception e) { status = e.Message; lastResponse = body; busy = false; yield break; }

        var rep = ParamSchema.Parse(text);
        SaveLog(cfg, text, rep, retries);
        lastResponse = text + "\n\n" + string.Join("\n", rep.Errors);
        if (!rep.JsonFound) { status = "응답에서 JSON을 찾지 못했습니다"; busy = false; yield break; }

        mapView.overrideParams = rep.Params;
        mapView.Generate();
        status = string.Format("스키마 준수 {0} | 순수 JSON {1} | 재시도 {2}회\n적용: {3}",
            rep.SchemaValid, rep.PureJson, retries, ParamSchema.ToJson(rep.Params));
        busy = false;
    }

    // 이 키로 쓸 수 있는 모델 이름을 불러온다 (OpenAI 호환 주소의 /models). 키 값은 표시하지 않는다.
    IEnumerator ListModels()
    {
        busy = true; status = "모델 목록 요청 중..."; lastResponse = "";
        string key = LlmApi.KeyFor(baseUrl);
        if (string.IsNullOrEmpty(key)) { status = "API 키가 없습니다"; busy = false; yield break; }
        var cfg = new LlmConfig { Provider = provider, BaseUrl = baseUrl, Model = "x" };
        using (var www = UnityWebRequest.Get(cfg.ResolvedBaseUrl + "/models"))
        {
            if (provider == LlmProvider.Anthropic) { www.SetRequestHeader("x-api-key", key); www.SetRequestHeader("anthropic-version", "2023-06-01"); }
            else www.SetRequestHeader("Authorization", "Bearer " + key);
            www.timeout = 60;
            yield return www.SendWebRequest();
            if (www.result != UnityWebRequest.Result.Success) { status = "목록 실패: " + www.error; lastResponse = www.downloadHandler.text; busy = false; yield break; }
            var names = new System.Collections.Generic.List<string>();
            try
            {
                var root = MiniJson.Parse(www.downloadHandler.text) as System.Collections.Generic.Dictionary<string, object>;
                foreach (var o in (System.Collections.Generic.List<object>)root["data"])
                {
                    var d = o as System.Collections.Generic.Dictionary<string, object>;
                    if (d != null && d.ContainsKey("id")) names.Add(((string)d["id"]).Replace("models/", ""));
                }
            }
            catch (Exception e) { status = "목록 해석 실패: " + e.Message; lastResponse = www.downloadHandler.text; busy = false; yield break; }
            names.Sort();
            status = "모델 " + names.Count + "개";
            lastResponse = string.Join("\n", names.ToArray());
        }
        busy = false;
    }

    // 프로젝트 폴더/LLMLogs/ 에 저장 (키는 저장하지 않음)
    void SaveLog(LlmConfig cfg, string text, ParseReport rep, int retries)
    {
        string dir = Path.Combine(Path.GetDirectoryName(Application.dataPath), "LLMLogs");
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        File.WriteAllText(Path.Combine(dir, stamp + "_params.txt"), text);
        File.WriteAllText(Path.Combine(dir, stamp + "_meta.json"),
            "{\"source\":\"unity\",\"prompt\":" + MiniJson.Quote(prompt) +
            ",\"provider\":" + MiniJson.Quote(cfg.Provider.ToString()) +
            ",\"model\":" + MiniJson.Quote(cfg.Model) +
            ",\"temperature\":" + (cfg.Temperature.HasValue ? MiniJson.Num(cfg.Temperature.Value) : "null") +
            ",\"retries\":" + retries +
            ",\"json_found\":" + (rep.JsonFound ? "true" : "false") +
            ",\"pure_json\":" + (rep.PureJson ? "true" : "false") +
            ",\"schema_valid\":" + (rep.SchemaValid ? "true" : "false") +
            ",\"time\":" + MiniJson.Quote(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK")) + "}");
    }
}
