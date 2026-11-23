// LlmApi.cs
// LLM API 요청 본문을 만들고 응답에서 텍스트를 꺼내는 코드.
// 실제 HTTP 전송은 환경마다 다르게 한다 (Unity: UnityWebRequest / 콘솔: HttpClient).

using System;
using System.Collections.Generic;
using System.Text;

namespace ObstacleWFC
{
    public enum LlmProvider
    {
        Anthropic,          // Claude — POST {BaseUrl}/messages
        OpenAICompatible    // OpenAI 형식 — POST {BaseUrl}/chat/completions (같은 형식을 쓰는 다른 서비스도 BaseUrl만 바꿔서 사용)
    }

    public class LlmConfig
    {
        public LlmProvider Provider = LlmProvider.Anthropic;
        public string Model = "";
        public string ApiKey = "";
        public string BaseUrl = "";          // 비우면 기본 주소
        public double? Temperature = 0.7;    // null이면 요청에서 뺀다 (temperature를 거부하는 모델용)
        public int MaxTokens = 4096;         // Anthropic 필수. OpenAI 형식에서는 보내지 않음
        public string ReasoningEffort = "";  // OpenAI 형식 reasoning_effort. 비우면 보내지 않음. 로컬(Ollama) 생각 모드 끄기: "none"

        public string ResolvedBaseUrl
        {
            get
            {
                if (!string.IsNullOrEmpty(BaseUrl)) return BaseUrl.TrimEnd('/');
                return Provider == LlmProvider.Anthropic ? "https://api.anthropic.com/v1" : "https://api.openai.com/v1";
            }
        }
    }

    public class LlmHttpRequest
    {
        public string Url;
        public Dictionary<string, string> Headers = new Dictionary<string, string>();
        public string Body;
    }

    public static class LlmApi
    {
        // 서버 과부하·일시 오류는 잠시 뒤 다시 보낸다. 응답 내용이 없는 실패라 실험 결과(응답)에는 영향을 주지 않는다.
        public static readonly int[] RetryDelaysSeconds = { 5, 15, 30 };
        public static bool IsRetryable(long httpStatus)
        {
            return httpStatus == 429 || httpStatus == 500 || httpStatus == 502 || httpStatus == 503 || httpStatus == 504;
        }

        // API 키 찾기: 1) 환경 변수 LLM_API_KEY  2) 사용자 폴더의 .llm_api_key 파일 (예: C:\Users\이름\.llm_api_key)
        // 키를 Unity 씬·프로젝트 폴더에 두지 않기 위함 (프로젝트가 OneDrive로 동기화되므로).
        public static string LastKeySource = "";   // 키를 어디서 읽었는지 (값은 담지 않음)

        // 내 컴퓨터에서 돌리는 로컬 서버(Ollama, LM Studio 등)는 키가 필요 없으므로 진짜 키를 보내지 않는다.
        public static bool IsLocal(string baseUrl)
        {
            if (string.IsNullOrEmpty(baseUrl)) return false;
            string u = baseUrl.ToLowerInvariant();
            return u.Contains("://localhost") || u.Contains("://127.0.0.1");
        }
        public static string KeyFor(string baseUrl)
        {
            if (IsLocal(baseUrl)) { LastKeySource = "로컬 서버 (키 불필요)"; return "local"; }
            return ResolveApiKey();
        }

        public static string ResolveApiKey()
        {
            LastKeySource = "";
            string k = Environment.GetEnvironmentVariable("LLM_API_KEY");
            if (!string.IsNullOrEmpty(k) && Clean(k).Length > 0) { LastKeySource = "환경 변수 LLM_API_KEY"; return Clean(k); }
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                foreach (var name in new[] { ".llm_api_key", ".llm_api_key.txt" }) // 메모장이 .txt를 붙이는 경우 대비
                {
                    string path = System.IO.Path.Combine(home, name);
                    if (System.IO.File.Exists(path))
                    {
                        string info;
                        string v = ReadKeyFile(path, out info);
                        if (v.Length > 0) { LastKeySource = "파일 " + path + ", " + info; return v; }
                    }
                }
            }
            catch (Exception) { }
            return "";
        }

        // 키 파일을 인코딩과 상관없이 읽는다.
        //  1) 인코딩 판별: UTF-16 LE/BE(BOM 또는 0x00이 많음, 메모장 "유니코드" 저장) / 그 외는 UTF-8
        //  2) 문자로 풀어낸 뒤 인쇄 가능한 ASCII(0x21~0x7E)만 남긴다 → BOM, 줄바꿈, 보이지 않는 문자 모두 제거
        // info: 판별한 인코딩·파일 크기·앞 2바이트 (키 값은 담지 않음)
        static string ReadKeyFile(string path, out string info)
        {
            byte[] b = System.IO.File.ReadAllBytes(path);
            int zeros = 0;
            foreach (byte x in b) if (x == 0) zeros++;

            string text, enc;
            if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) { text = Encoding.Unicode.GetString(b, 2, b.Length - 2); enc = "UTF-16LE"; }
            else if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) { text = Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2); enc = "UTF-16BE"; }
            else if (b.Length >= 2 && zeros * 4 > b.Length)
            {
                bool le = b[1] == 0;
                text = (le ? Encoding.Unicode : Encoding.BigEndianUnicode).GetString(b);
                enc = le ? "UTF-16LE(BOM 없음)" : "UTF-16BE(BOM 없음)";
            }
            else
            {
                int skip = b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF ? 3 : 0;
                text = new UTF8Encoding(false, false).GetString(b, skip, b.Length - skip);
                enc = skip == 3 ? "UTF-8 BOM" : "UTF-8";
            }

            var sb = new StringBuilder();
            foreach (char c in text) if (c >= 0x21 && c <= 0x7E) sb.Append(c);
            string head = b.Length >= 2 ? b[0].ToString("X2") + " " + b[1].ToString("X2") : "-";
            info = string.Format("{0}, {1}바이트, 앞 {2}", enc, b.Length, head);
            return sb.ToString().Trim('"', '\'');
        }

        // 공백·줄바꿈·보이지 않는 문자 제거, 양끝 따옴표 제거 (키 자체에는 이런 문자가 없다)
        // 보이지 않는 문자 = 유니코드 Format(Cf) 분류: BOM, 폭 없는 공백, 글자 방향 표시(웹에서 복사할 때 딸려 옴) 등
        static string Clean(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in s)
            {
                var cat = char.GetUnicodeCategory(c);
                if (char.IsWhiteSpace(c) || cat == System.Globalization.UnicodeCategory.Format ||
                    cat == System.Globalization.UnicodeCategory.Control) continue;
                sb.Append(c);
            }
            return sb.ToString().Trim('"', '\'');
        }

        // 키에 헤더로 보낼 수 없는 문자가 있으면 이유를 돌려준다 (키 값은 드러내지 않음). 문제없으면 null.
        public static string DiagnoseKey(string key)
        {
            int hangul = 0, other = 0;
            var codes = new List<string>();
            foreach (char c in key)
            {
                if (c >= 0x21 && c <= 0x7E) continue;
                if ((c >= 0xAC00 && c <= 0xD7A3) || (c >= 0x3131 && c <= 0x318E)) hangul++;
                else { other++; string code = "U+" + ((int)c).ToString("X4"); if (!codes.Contains(code)) codes.Add(code); }
            }
            if (hangul == 0 && other == 0) return null;
            // 영문·숫자가 아닌 문자의 코드 번호만 보여준다 (키 값은 드러나지 않음)
            return string.Format("키({0}, {1}글자)에 쓸 수 없는 문자가 있습니다: 한글 {2}개, 기타 {3}개{4}",
                LastKeySource, key.Length, hangul, other, codes.Count > 0 ? " [" + string.Join(", ", codes) + "]" : "");
        }

        public static LlmHttpRequest Build(LlmConfig c, string system, string user)
        {
            if (string.IsNullOrEmpty(c.Model)) throw new ArgumentException("모델 이름(Model)이 비어 있습니다.");
            var req = new LlmHttpRequest();
            var sb = new StringBuilder("{");
            sb.Append("\"model\":").Append(MiniJson.Quote(c.Model));
            if (c.Temperature.HasValue) sb.Append(",\"temperature\":").Append(MiniJson.Num(c.Temperature.Value));

            if (c.Provider == LlmProvider.Anthropic)
            {
                req.Url = c.ResolvedBaseUrl + "/messages";
                req.Headers["x-api-key"] = c.ApiKey;
                req.Headers["anthropic-version"] = "2023-06-01";
                sb.Append(",\"max_tokens\":").Append(c.MaxTokens);
                sb.Append(",\"system\":").Append(MiniJson.Quote(system));
                sb.Append(",\"messages\":[{\"role\":\"user\",\"content\":").Append(MiniJson.Quote(user)).Append("}]");
            }
            else
            {
                req.Url = c.ResolvedBaseUrl + "/chat/completions";
                req.Headers["Authorization"] = "Bearer " + c.ApiKey;
                if (!string.IsNullOrEmpty(c.ReasoningEffort)) sb.Append(",\"reasoning_effort\":").Append(MiniJson.Quote(c.ReasoningEffort));
                sb.Append(",\"messages\":[");
                sb.Append("{\"role\":\"system\",\"content\":").Append(MiniJson.Quote(system)).Append("},");
                sb.Append("{\"role\":\"user\",\"content\":").Append(MiniJson.Quote(user)).Append("}]");
            }
            sb.Append('}');
            req.Headers["content-type"] = "application/json";
            req.Body = sb.ToString();
            return req;
        }

        // 응답 본문(JSON)에서 모델이 쓴 텍스트를 꺼낸다. API 오류 응답이면 그 메시지로 예외.
        public static string ExtractText(LlmConfig c, string responseBody)
        {
            var root = MiniJson.Parse(responseBody) as Dictionary<string, object>;
            if (root == null) throw new FormatException("응답이 JSON 객체가 아닙니다.");

            object err;
            if (root.TryGetValue("error", out err) && err != null)
            {
                var ed = err as Dictionary<string, object>;
                object msg = null;
                if (ed != null) ed.TryGetValue("message", out msg);
                throw new InvalidOperationException("API 오류: " + (msg ?? err));
            }

            if (c.Provider == LlmProvider.Anthropic)
            {
                var content = Get(root, "content") as List<object>;
                if (content == null) throw new FormatException("응답에 content가 없습니다.");
                var sb = new StringBuilder();
                foreach (var block in content)
                {
                    var b = block as Dictionary<string, object>;
                    if (b != null && (Get(b, "type") as string) == "text") sb.Append(Get(b, "text") as string);
                }
                return sb.ToString();
            }
            else
            {
                var choices = Get(root, "choices") as List<object>;
                if (choices == null || choices.Count == 0) throw new FormatException("응답에 choices가 없습니다.");
                var msg = Get(choices[0] as Dictionary<string, object>, "message") as Dictionary<string, object>;
                var text = Get(msg, "content") as string;
                if (text == null) throw new FormatException("응답에 message.content가 없습니다.");
                return text;
            }
        }

        static object Get(Dictionary<string, object> d, string k)
        {
            object v;
            return d != null && d.TryGetValue(k, out v) ? v : null;
        }
    }
}
