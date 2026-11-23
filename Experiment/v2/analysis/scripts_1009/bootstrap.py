# bootstrap.py — 주요 비교의 95% 신뢰구간 (목표 맵 단위 재표본추출) + 다양성 요약
# 사용: python3 bootstrap.py <results.csv> <diversity.csv> <출력 폴더>
# 집계 방식은 analyze.py와 같음: seed·반복 평균 → 목표 맵 1개가 분석 단위 (n=20)
import sys, os
import numpy as np, pandas as pd

R, DV, OUT = sys.argv[1], sys.argv[2], sys.argv[3]; os.makedirs(OUT, exist_ok=True)
B = 10000; rng = np.random.default_rng(20261009)
r = pd.read_csv(R)
b1s = r[r.cond == 'B1s'].copy(); b1s['cond'] = 'B1'
r = pd.concat([r[~r.cond.isin(['B1', 'B1s'])], b1s])
tm = r.groupby(['target', 'ptype', 'cond'])['region_mae'].mean().reset_index()
base = tm[tm.ptype == '-']
tm = pd.concat([tm[tm.ptype != '-']] + [base.assign(ptype=p) for p in ['D', 'X', 'H1', 'H2']])

def get(c, p):
    return tm[(tm.cond == c) & (tm.ptype == p)].set_index('target').region_mae

def ci(v):
    v = np.asarray(v); idx = rng.integers(0, len(v), (B, len(v))); m = v[idx].mean(1)
    return v.mean(), np.percentile(m, 2.5), np.percentile(m, 97.5)

PT = {'D': '서술형', 'X': '명시형', 'H1': '신규 문체(구어체)', 'H2': '신규 문체(기획서체)'}
rows = []
# 1) 조건별 평균 MAE의 95% CI
for p in ['D', 'X', 'H1', 'H2']:
    for c in ['B0', 'B1', 'B2', 'ZS', 'FS', 'A3', 'UB']:
        s = get(c, p)
        if len(s) == 0: continue
        m, lo, hi = ci(s.values); rows.append(['평균', PT[p], c, '', len(s), m, lo, hi])
# 2) 쌍체 차이(앞 - 뒤)의 95% CI: 음수면 앞 조건의 오차가 작음
pairs = [('D', 'ZS', 'B0'), ('X', 'ZS', 'B0'), ('D', 'ZS', 'B1'), ('D', 'ZS', 'B2'), ('X', 'ZS', 'B2'),
         ('H1', 'ZS', 'B2'), ('H2', 'ZS', 'B2'), ('H1', 'ZS', 'B0'), ('H2', 'ZS', 'B0'),
         ('D', 'FS', 'ZS'), ('X', 'FS', 'ZS'), ('D', 'A3', 'ZS')]
for p, a, b in pairs:
    A, Bv = get(a, p), get(b, p); j = A.index.intersection(Bv.index)
    m, lo, hi = ci(A[j].values - Bv[j].values)
    rows.append(['차이', PT[p], a, b, len(j), m, lo, hi])
t = pd.DataFrame(rows, columns=['종류', '명령어', '조건', '비교 대상', '목표 수', '값', 'CI 하한', 'CI 상한'])
t['0 포함'] = np.where(t['종류'] == '차이', np.where((t['CI 하한'] <= 0) & (t['CI 상한'] >= 0), '예', '아니오'), '')
t.round(4).to_csv(os.path.join(OUT, 'table6_bootstrap.csv'), index=False, encoding='utf-8-sig')

# 3) 다양성: 목표별 평균 → 조건별 평균과 95% CI
d = pd.read_csv(DV)
rows = []
for (kind, p, c), s in d[d.target != 'ALL'].groupby(['kind', 'ptype', 'cond']):
    v = s.groupby('target')[['tile_div', 'occ_div']].mean()
    m, lo, hi = ci(v.tile_div.values); mo = v.occ_div.mean()
    rows.append([kind, p, c, len(v), m, lo, hi, mo])
x = d[d.target == 'ALL']; m, lo, hi = ci(x.tile_div.values)
rows.append(['cross_target', '-', 'UB_cross', len(x), m, lo, hi, x.occ_div.mean()])
pd.DataFrame(rows, columns=['비교 단위', '명령어', '조건', 'n', 'tile_div', 'CI 하한', 'CI 상한', 'occ_div']).round(4) \
  .to_csv(os.path.join(OUT, 'table7_diversity.csv'), index=False, encoding='utf-8-sig')
print(t.round(3).to_string()); print(pd.read_csv(os.path.join(OUT, 'table7_diversity.csv')).to_string())
