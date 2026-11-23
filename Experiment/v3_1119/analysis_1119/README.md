# 추가 실험 (2026-11-19) — 실험 2: 교정 전 서술형 원문 / 실험 3: 새 베이스 맵 2개

## 재현 방법 (Mono 6.12)
    mcs -out:exp1119.exe Assets/Scripts/Core/*.cs Experiment/v2/analysis/RuleBaseline_frozen.cs \
        Experiment/v3_1119/analysis_1119/src/RuleBaselineM.cs Experiment/v3_1119/analysis_1119/src/Experiment_1119.cs
    # 실험 2 (base_map_01, 응답 폴더에 본실험 ZS 응답 + responses_e2)
    mono exp1119.exe evalx <폴더> Assets/Maps/base_map_01.txt <응답폴더> cmds_raw.json e2_raw.csv
    # 실험 3
    mono exp1119.exe targets <폴더> maps/base_map_02.txt          # 목표 맵 생성 (seed 5000+i, 본실험과 같은 20개 조합)
    mono exp1119.exe evalx <폴더> maps/base_map_02.txt <응답폴더> cmds.json e3_02.csv anchors
    python stats1119.py / analyze_e3.py                             # 통계

## 검증
- scripts_1009/Experiment.cs를 Mono로 다시 실행 → results.csv md5 e63ca3381983b8e0c1fd86183713e325 (원본과 일치)
- evalx(anchors 모드, base_map_01)로 본실험 B0·UB·B2·ZS 3,600행 재계산 → 원본과 전부 일치 (check_edited.csv)
- stats1119.py → 본실험 table5의 p·r, bootstrap CI 재현

## 실험 3 절차
- 새 베이스 맵: base_map_02(아래쪽 가로 강·다리 2개, 시작점 NW, 목표 SE), base_map_03(세로 강·다리 1개, 시작점 W, 목표 E)
- 목표 맵: 본실험과 같은 패턴·종류 조합 20개 (Experiment.cs targets 모드 그대로)
- 그림: render.py (본실험 그림과 같은 색·배치)
- 명령어: 정답을 모르는 별도 에이전트(Claude Sonnet) 4개가 그림만 보고 작성. 서술형(D)과 새 문체(H1 구어체, H2 기획서체)는 서로 다른 에이전트. 숫자 0건. 연구자 수정 없음 (writer_*_raw.json = 원문)
- LLM: gemma4:e4b (ID dc35e8d9c606), Ollama, temperature 0.7, reasoning_effort none — run_jobs.py (본실험 ExperimentRunner와 같은 요청 형식)
- B2: 언어 사전은 고정본과 동일, 맵 의존 위치어(시작점·목표·다리·강 방향)만 RuleBaselineM.SetAnchors로 새 맵에 맞춤
