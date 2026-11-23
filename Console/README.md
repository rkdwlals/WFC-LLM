# Console — Unity 밖에서 쓰는 테스트·실험 도구

Unity는 Assets 폴더 안의 스크립트만 컴파일하므로, 콘솔 전용 코드는 여기에 둔다.
핵심 코드는 Unity와 같은 파일(../Assets/Scripts/Core)을 그대로 쓴다.

| 파일 | 역할 |
| --- | --- |
| Test/TestRunner.cs | WFC 동작 테스트 (seed 재현성, 위반 0, 밀도·구성 반영) |
| Test/LlmTests.cs | 가짜 LLM 응답으로 LLM 연동 코드 테스트 (API 키 불필요) |
| Tools/RunPrompt.cs | 프롬프트 하나로 B0 / A1 / 제안 / B1 네 조건 비교, LLM 응답 logs/에 저장 |

빌드 예시 (Graduate 폴더에서, Mono 기준):

    mcs -out:test.exe Assets/Scripts/Core/*.cs Console/Test/TestRunner.cs
    mono test.exe Assets/Maps/base_map_01.txt

    mcs -out:runprompt.exe -r:System.Net.Http Assets/Scripts/Core/*.cs Console/Tools/RunPrompt.cs
    mono runprompt.exe --map Assets/Maps/base_map_01.txt --provider anthropic --model 모델이름 --prompt "..."

*.meta 파일은 Unity가 Assets 안에 있을 때 만든 것이라 여기서는 쓰이지 않는다.
