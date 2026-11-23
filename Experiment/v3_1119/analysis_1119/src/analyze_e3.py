# analyze_e3.py — 실험 3(새 베이스 맵 2개) 분석. 본실험(base_map_01)과 같은 비교·지표·통계
import sys, pandas as pd
sys.path.insert(0, '/tmp/claude-0/-home-claude/69d72519-4a16-5062-a1b7-7304a2e3de6c/scratchpad/exp/src1119')
from stats1119 import compare, per_target
W = '/tmp/claude-0/-home-claude/69d72519-4a16-5062-a1b7-7304a2e3de6c/scratchpad/exp/'

def specs():
    s = []
    for pt in ('D', 'X', 'H1', 'H2'):
        s.append((f'[{pt}] ZS vs B0 region_mae', pt, 'ZS', '-', 'B0', 'region_mae'))
    for pt in ('D', 'X', 'H1', 'H2'):
        for met in ('region_mae', 'block_mae', 'type_l1'):
            s.append((f'[{pt}] ZS vs B2 {met}', pt, 'ZS', pt, 'B2', met))
    return s

def summary(df, label):
    rows = []
    for pt in ('D', 'X', 'H1', 'H2'):
        r = {'map': label, 'ptype': pt}
        for c in ('B0', 'B2', 'ZS', 'UB'):
            p = '-' if c in ('B0', 'UB') else pt
            r[c] = per_target(df, p, c, 'region_mae').mean()
        z = df[(df.ptype == pt) & (df.cond == 'ZS')]
        r['schema'] = z.schema_valid.mean(); r['pure'] = z.pure_json.mean(); r['path'] = z.path_ok.mean()
        r['adj'] = z.adj_viol.mean(); r['forbid'] = z.forbid_viol.mean(); r['n_resp'] = z.groupby(['target', 'rep']).ngroups
        rows.append(r)
    return pd.DataFrame(rows)

if __name__ == '__main__':
    dfs = {}
    for m in ('02', '03'):
        df = pd.read_csv(W + f'e3_{m}/e3_{m}.csv'); dfs[m] = df
    pooled = pd.concat([d.assign(target=m + '_' + d.target) for m, d in dfs.items()])
    pd.set_option('display.width', 250)
    for m, df in list(dfs.items()) + [('02+03', pooled)]:
        print('=== map', m); print(summary(df, m).round(4).to_string(index=False))
        print(compare(df, specs()).round(4).to_string(index=False))
