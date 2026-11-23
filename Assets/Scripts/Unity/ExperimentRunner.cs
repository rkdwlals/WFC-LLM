// ExperimentRunner.cs — 일괄 실험: Experiment/v2/jobs.json의 요청을 차례로 LLM에 보내 응답을 저장한다.
// 사용법: 씬의 아무 GameObject에 붙이고 Play → 오른쪽 위 "실험 시작".
//  - 응답은 프로젝트 폴더/Experiment/v2/responses/<id>.txt 에 저장 (키는 저장하지 않음)
//  - 이미 저장된 요청은 건너뛰므로, 중간에 멈추거나 한도가 끝나도 다시 누르면 이어서 진행된다.
//  - API 키는 PromptPanel과 같은 곳(환경 변수 LLM_API_KEY 또는 사용자 폴더의 .llm_api_key)에서 읽는다.
// 맵 생성과 지표 계산은 여기서 하지 않는다 (응답만 모으고, 평가는 Console/Tools/Experiment.cs eval).

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using ObstacleWFC;

public class ExperimentRunner : MonoBehaviour
{
    [Header("LLM 설정 (PromptPanel과 같게)")]
    public LlmProvider provider = LlmProvider.OpenAICompatible;
    public string model = "gemini-3.8-flash";
    public string baseUrl = "https://generativelanguage.googleapis.com/v1beta/openai";
    public float temperature = 0.7f;
    [Tooltip("생각(reasoning) 모드. 비우면 모델 기본값. 로컬 Ollama 모델은 \"none\"으로 끔 (켜 두면 답이 비어 나올 수 있음)")]
    public string reasoningEffort = "";

    [Header("실행")]
    [Tooltip("프로젝트 폴더 기준 실험 폴더")]
    public string experimentDir = "Experiment/v2";
    [Tooltip("요청 사이 대기 시간(초). 무료 등급 분당 한도를 넘지 않도록")]
    public float delaySeconds = 5f;
    [Tooltip("연속 실패가 이 횟수에 이르면 쉬었다가(아래 시간) 다시 시도한다")]
    public int maxConsecutiveFailures = 3;
    [Tooltip("연속 실패 후 쉬는 시간(분). 0이면 쉬지 않고 멈춘다")]
    public float cooldownMinutes = 15f;
    [Tooltip("전체 요청을 몇 바퀴까지 다시 돌지 (실패한 것만 다시 보냄)")]
    public int maxPasses = 20;

    // 503(서버 혼잡)·429(분당 한도)·5xx 재시도 대기(초). 무료 등급은 혼잡이 길어서 길게 기다린다.
    static readonly int[] Waits = { 20, 40, 80, 160, 300, 600 };

    bool running, stopRequested;
    int total, done, failedInRow;
    string status = "대기 중", current = "";

    string Root { get { return Path.GetDirectoryName(Application.dataPath); } }
    string ExpDir { get { return Path.Combine(Root, experimentDir); } }
    string RespDir { get { return Path.Combine(ExpDir, "responses"); } }

    void OnGUI()
    {
        float s = MapView.GuiScale();
        var old = GUI.matrix; GUI.matrix = Matrix4x4.Scale(new Vector3(s, s, 1));
        GUILayout.BeginArea(new Rect(Screen.width / s - 430, 10, 420, 200), GUI.skin.box);
        GUILayout.Label("일괄 실험  (" + done + " / " + total + ")");
        GUILayout.Label("현재: " + current);
        GUILayout.Label(status);
        GUILayout.BeginHorizontal();
        GUI.enabled = !running;
        if (GUILayout.Button("실험 시작 / 이어하기", GUILayout.Width(180))) StartCoroutine(Run());
        GUI.enabled = running;
        if (GUILayout.Button("멈춤", GUILayout.Width(100))) stopRequested = true;
        GUI.enabled = true;
        GUILayout.EndHorizontal();
        GUILayout.EndArea();
        GUI.matrix = old;
    }

    IEnumerator Run()
    {
        running = true; stopRequested = false; failedInRow = 0;
        Application.runInBackground = true;   // Unity 창이 뒤로 가도 멈추지 않게
        string jobsPath = Path.Combine(ExpDir, "jobs.json");
        if (!File.Exists(jobsPath)) { status = "jobs.json이 없습니다: " + jobsPath; running = false; yield break; }

        Dictionary<string, object> root; List<object> jobs; Dictionary<string, object> systems;
        try
        {
            root = (Dictionary<string, object>)MiniJson.Parse(File.ReadAllText(jobsPath, Encoding.UTF8));
            jobs = (List<object>)root["jobs"]; systems = (Dictionary<string, object>)root["systems"];
        }
        catch (Exception e) { status = "jobs.json 읽기 실패: " + e.Message; running = false; yield break; }

        var cfg = new LlmConfig
        {
            Provider = provider, Model = model, BaseUrl = baseUrl,
            Temperature = Math.Round((double)temperature, 3),
            ReasoningEffort = reasoningEffort,
            ApiKey = LlmApi.KeyFor(baseUrl)
        };
        if (string.IsNullOrEmpty(cfg.ApiKey)) { status = "API 키가 없습니다 (LLM_API_KEY 또는 .llm_api_key)"; running = false; yield break; }
        string keyProblem = LlmApi.DiagnoseKey(cfg.ApiKey);
        if (keyProblem != null) { status = keyProblem; running = false; yield break; }

        Directory.CreateDirectory(RespDir);
        total = jobs.Count; done = 0;
        foreach (var o in jobs) if (File.Exists(Path.Combine(RespDir, (string)((Dictionary<string, object>)o)["id"] + ".txt"))) done++;

        // 실패한 요청은 이번 바퀴에서 건너뛰고, 모든 요청을 한 바퀴 돈 뒤 다시 시도한다 (최대 maxPasses 바퀴)
        for (int pass = 1; pass <= maxPasses && done < total && !stopRequested; pass++)
        {
        if (pass > 1)
            for (int sec = (int)(cooldownMinutes * 60); sec > 0 && !stopRequested; sec--)
            {
                status = pass + "번째 바퀴 전 대기: 남은 요청 " + (total - done) + "개, " + (sec / 60) + "분 " + (sec % 60) + "초";
                yield return new WaitForSecondsRealtime(1f);
            }
        foreach (var o in jobs)
        {
            if (stopRequested) { status = "멈춤 (다시 누르면 이어서 진행)"; break; }
            var job = (Dictionary<string, object>)o;
            string id = (string)job["id"];
            string outPath = Path.Combine(RespDir, id + ".txt");
            if (File.Exists(outPath)) continue;
            current = id;

            var req = LlmApi.Build(cfg, (string)systems[(string)job["system"]], (string)job["user"]);
            string body = null, error = null; long code = 0; int retries = 0;
            var started = DateTime.Now;
            for (int attempt = 0; ; attempt++)
            {
                using (var www = new UnityWebRequest(req.Url, "POST"))
                {
                    www.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(req.Body));
                    www.downloadHandler = new DownloadHandlerBuffer();
                    foreach (var h in req.Headers) www.SetRequestHeader(h.Key, h.Value);
                    www.timeout = LlmApi.IsLocal(baseUrl) ? 900 : 180;   // 로컬 모델은 느릴 수 있어 길게
                    status = attempt == 0 ? "요청 중..." : "재시도 " + attempt + " 요청 중...";
                    yield return www.SendWebRequest();
                    body = www.downloadHandler.text; code = www.responseCode;
                    if (www.result == UnityWebRequest.Result.Success) { error = null; break; }
                    error = www.error;
                    if (!LlmApi.IsRetryable(code) || attempt >= Waits.Length) break;
                    retries++;
                    for (int sec = Waits[attempt]; sec > 0 && !stopRequested; sec--)
                    {
                        status = "서버 응답 " + code + " → " + sec + "초 뒤 재시도 (" + retries + "/" + Waits.Length + ")";
                        yield return new WaitForSecondsRealtime(1f);
                    }
                }
            }

            string text = null;
            if (error == null)
            {
                try { text = LlmApi.ExtractText(cfg, body); }
                catch (Exception e) { error = e.Message; }
                if (text != null && text.Trim().Length == 0) { text = null; error = "빈 응답 (생각 모드가 출력 한도를 다 쓴 경우 등)"; }
            }
            if (text == null)
            {
                failedInRow++;
                File.WriteAllText(Path.Combine(RespDir, id + ".error.txt"), "code " + code + "\n" + error + "\n" + body);
                status = "실패 (" + code + "): " + error;
                if (failedInRow >= maxConsecutiveFailures)
                {
                    if (cooldownMinutes <= 0) { status = "연속 " + failedInRow + "회 실패로 멈춤. 마지막 오류 코드 " + code; break; }
                    for (int sec = (int)(cooldownMinutes * 60); sec > 0 && !stopRequested; sec--)
                    {
                        status = "연속 " + failedInRow + "회 실패(코드 " + code + ") → " + (sec / 60) + "분 " + (sec % 60) + "초 쉬고 다시 시도";
                        yield return new WaitForSecondsRealtime(1f);
                    }
                    failedInRow = 0;
                }
                yield return new WaitForSecondsRealtime(delaySeconds);
                continue;
            }

            failedInRow = 0;
            File.WriteAllText(outPath, text, new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(RespDir, id + ".meta.json"),
                "{\"id\":" + MiniJson.Quote(id) + ",\"model\":" + MiniJson.Quote(cfg.Model) +
                ",\"provider\":" + MiniJson.Quote(cfg.Provider.ToString()) +
                ",\"temperature\":" + MiniJson.Num(cfg.Temperature.Value) +
                ",\"reasoning_effort\":" + MiniJson.Quote(cfg.ReasoningEffort ?? "") +
                ",\"retries\":" + retries + ",\"seconds\":" + MiniJson.Num(Math.Round((DateTime.Now - started).TotalSeconds, 1)) +
                ",\"time\":" + MiniJson.Quote(DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ssK")) + "}", new UTF8Encoding(false));
            string errPath = Path.Combine(RespDir, id + ".error.txt");
            if (File.Exists(errPath)) File.Delete(errPath);
            done++;
            status = "저장: " + id;
            yield return new WaitForSecondsRealtime(delaySeconds);
        }
        }
        if (done >= total) status = "완료: " + done + "개 응답 저장";
        current = "";
        running = false;
    }
}
