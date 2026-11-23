# stats1119.py — 실험 2·3 통계 (본실험 table5와 같은 방식: 목표별 평균 → 쌍체 Wilcoxon, 순위이연상관 r, Holm 보정, bootstrap 95% CI)
import sys, json
import numpy as np, pandas as pd
from scipy.stats import wilcoxon

def per_target(df, ptype, cond, metric):
    d = df[(df.ptype == ptype) & (df.cond == cond)]
    return d.groupby('target')[metric].mean()

def rank_biserial(a, b):
    d = (a - b); d = d[d != 0]
    ranks = pd.Series(np.abs(d)).rank().values
    wp = ranks[d > 0].sum(); wm = ranks[d < 0].sum()
    return (wp - wm) / (wp + wm) if (wp + wm) > 0 else 0.0

def boot_ci(a, b, n=10000, seed=0):
    rng = np.random.default_rng(seed); d = (a - b).values; k = len(d)
    m = np.array([d[rng.integers(0, k, k)].mean() for _ in range(n)])
    return d.mean(), np.percentile(m, 2.5), np.percentile(m, 97.5)

def holm(ps):
    ps = np.asarray(ps, float); n = len(ps); order = np.argsort(ps); adj = np.empty(n); run = 0
    for i, idx in enumerate(order):
        run = max(run, min(1.0, (n - i) * ps[idx])); adj[idx] = run
    return adj

def compare(df, specs):
    """specs: [(label, ptypeA, condA, ptypeB, condB, metric)]"""
    rows = []
    for lab, pa, ca, pb, cb, met in specs:
        a = per_target(df, pa, ca, met); b = per_target(df, pb, cb, met)
        idx = a.index.intersection(b.index); a, b = a[idx], b[idx]
        p = wilcoxon(a, b).pvalue
        mean, lo, hi = boot_ci(a, b)
        rows.append(dict(비교=lab, 목표수=len(idx), 앞평균=a.mean(), 뒤평균=b.mean(), 차이=mean, CI_lo=lo, CI_hi=hi, p=p, r=rank_biserial(a, b)))
    out = pd.DataFrame(rows); out['p_Holm'] = holm(out.p)
    return out

if __name__ == '__main__':
    # 검증: 본실험 results.csv로 table5의 첫 행 재현
    df = pd.read_csv(sys.argv[1])
    print(compare(df, [('[D] ZS vs B0 region_mae', 'D', 'ZS', '-', 'B0', 'region_mae'),
                       ('[H2] ZS vs B2 region_mae', 'H2', 'ZS', 'H2', 'B2', 'region_mae')]).to_string())
