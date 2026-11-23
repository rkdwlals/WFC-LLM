# E4 논문용 3D 그림 (2026-11-16)

Unity 화면 캡처가 아니라, 맵 글자 파일을 읽어 DemoApp.cs와 같은 모양·크기·색 규칙(데모 v9 기준, 동선 보라색)으로 다시 그린 그림이다.
렌더러: three.js 0.160.0 + 헤드리스 Chromium(Playwright), 정사영 카메라 52° (데모와 같음). 최단 경로는 PathMetrics.ShortestPath와 같은 BFS(이웃 순서 +x, -x, +y, -y).

## 예시 선정 규칙 (그리기 전에 고정)
H2(기획서체) 명령에서 목표별 ZS−B2 region_mae 차이가 20개 목표의 중앙값(−0.062)에 가장 가까운 목표. 동률(T01, T15)이면 번호가 작은 목표 → T01 (차이 −0.060).
대표 맵 = seed 100, 반복 1회차 (실험 코드가 maps/에 저장하는 규칙 그대로).

## 그림 2 (fig2_T01_H2_compare.png)
(a) 목표 맵 T01 (grids/T01.txt) / (b) B0 region_mae 0.1508 / (c) B2·H2 0.1694 / (d) ZS·H2 0.1058
입력: Experiment/v2/maps/T01_B0.txt, T01_H2_B2.txt, T01_H2_ZS.txt 와 같은 내용(회귀 확인한 재생성본)

## 그림 3 (fig3_T01_H2_routes.png)
같은 응답(T01_H2_ZS_r1, path_required=true), seed 100
(a) 무작위 경로 57칸 0.1058 / (b) R1 동선 57 · 최단 57 · 0.1105 / (c) R2 동선 91 · 최단 91 · 0.1174 (같은 길이의 다른 경로) / (d) R3 동선 89 · 최단 87 · 0.1208
입력: inputs/T01_H2_ZS_PT·R1·R2·R3.txt (inputs/Dump.cs.txt로 생성, PT는 maps/T01_H2_ZS_PT.txt와 동일 확인), 동선은 route_e3_1116/route_R*.txt
렌더러가 계산한 최단 경로 길이(57, 91, 87)는 route_e3.csv의 sp_len과 일치

## 한계
한 목표·한 seed의 예시이며 통계 근거는 원고 표 3·4·8이다.
