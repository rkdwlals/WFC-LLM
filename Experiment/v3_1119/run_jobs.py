# run_jobs.py — jobs JSON의 요청을 로컬 Ollama(gemma4:e4b)에 차례로 보내 응답을 저장한다. (2026-11-19)
# Unity ExperimentRunner와 같은 요청 형식: OpenAI 호환 /v1/chat/completions,
#   {"model", "temperature": 0.7, "reasoning_effort": "none", "messages": [system, user]}
# 사용법 (이 파일이 있는 폴더에서):  python run_jobs.py jobs_e2.json responses_e2 [--auto]
#   --auto: 모델 ID가 본실험과 다르면 묻지 않고 중단
#   이미 저장된 응답은 건너뛰므로 중간에 멈춰도 다시 실행하면 이어서 진행된다.
import json, os, sys, time, urllib.request, datetime

BASE = "http://localhost:11434"
MODEL = "gemma4:e4b"
EXPECTED_ID = "dc35e8d9c606"   # 본실험에 쓴 gemma4:e4b 모델 ID (ollama list의 ID)

def post(url, body, timeout=600):
    req = urllib.request.Request(url, data=json.dumps(body, ensure_ascii=False).encode("utf-8"),
                                 headers={"content-type": "application/json", "Authorization": "Bearer ollama"})
    with urllib.request.urlopen(req, timeout=timeout) as r:
        return json.loads(r.read().decode("utf-8"))

def model_id():
    with urllib.request.urlopen(BASE + "/api/tags", timeout=30) as r:
        tags = json.loads(r.read().decode("utf-8"))
    for m in tags.get("models", []):
        if m.get("name") == MODEL or m.get("model") == MODEL:
            return m.get("digest", "")[:12]
    return ""

def main():
    jobs_path, out_dir = sys.argv[1], sys.argv[2]
    os.makedirs(out_dir, exist_ok=True)
    spec = json.load(open(jobs_path, encoding="utf-8"))
    mid = model_id()
    print("모델", MODEL, "ID", mid, "(본실험 ID와 일치)" if mid == EXPECTED_ID else "(주의: 본실험 ID " + EXPECTED_ID + "와 다름)")
    if mid != EXPECTED_ID:
        if "--auto" in sys.argv:
            print("모델 ID가 달라 중단합니다 (--auto 모드)", flush=True); sys.exit(2)
        if input("계속할까요? (y/n) ").strip().lower() != "y":
            return
    jobs = spec["jobs"]; done = 0
    for i, j in enumerate(jobs, 1):
        out = os.path.join(out_dir, j["id"] + ".txt")
        if os.path.exists(out):
            done += 1; continue
        body = {"model": MODEL, "temperature": 0.7, "reasoning_effort": "none",
                "messages": [{"role": "system", "content": spec["systems"][j["system"]]},
                             {"role": "user", "content": j["user"]}]}
        for attempt in range(4):
            try:
                t0 = time.time()
                res = post(BASE + "/v1/chat/completions", body)
                text = res["choices"][0]["message"]["content"]
                sec = round(time.time() - t0, 1)
                break
            except Exception as e:
                print("  오류, 재시도", attempt + 1, e); time.sleep(5)
        else:
            print("실패:", j["id"]); continue
        with open(out, "w", encoding="utf-8", newline="") as f:
            f.write(text)
        meta = {"id": j["id"], "model": MODEL, "model_id": mid, "provider": "OpenAICompatible", "temperature": 0.7,
                "reasoning_effort": "none", "retries": attempt, "seconds": sec,
                "finish_reason": res["choices"][0].get("finish_reason"),
                "time": datetime.datetime.now().astimezone().isoformat(timespec="seconds")}
        json.dump(meta, open(os.path.join(out_dir, j["id"] + ".meta.json"), "w", encoding="utf-8"), ensure_ascii=False)
        done += 1
        print(f"[{done}/{len(jobs)}] {j['id']} {sec}s", flush=True)
    print("끝. 저장된 응답", sum(1 for j in jobs if os.path.exists(os.path.join(out_dir, j['id'] + '.txt'))), "/", len(jobs))

if __name__ == "__main__":
    main()
