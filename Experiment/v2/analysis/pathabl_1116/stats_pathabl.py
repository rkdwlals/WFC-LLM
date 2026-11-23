# E2 통계: 경로 선확보 끔(ZS_PF) vs 원래(ZS). 목표 맵별 평균 단위 쌍체 Wilcoxon, 12개 검정 Holm 보정, bootstrap 95% CI(10,000회, seed 0)
import pandas as pd, numpy as np
from scipy.stats import wilcoxon
d=pd.read_csv('pathabl_1116.csv')
tm=d.groupby(['target','ptype','cond'])[['region_mae','block_mae','type_l1']].mean().reset_index()
rows=[]
for pt in ['D','X','H1','H2']:
  for met in ['region_mae','block_mae','type_l1']:
    a=tm[(tm.ptype==pt)&(tm.cond=='ZS_PF')].set_index('target')[met]
    b=tm[(tm.ptype==pt)&(tm.cond=='ZS')].set_index('target')[met].loc[a.index]
    diff=a-b; nz=diff[diff!=0]; ranks=nz.abs().rank()
    rb=(ranks[nz>0].sum()-ranks[nz<0].sum())/ranks.sum()
    rows.append([pt,met,b.mean(),a.mean(),diff.mean(),wilcoxon(a,b).pvalue,rb])
r=pd.DataFrame(rows,columns=['ptype','metric','ZS','ZS_PF','diff','p','r_rb'])
p=r.p.values; o=np.argsort(p); m=len(p); adj=np.empty(m); run=0
for i,k in enumerate(o): run=max(run,min(1,(m-i)*p[k])); adj[k]=run
r['p_holm']=adj; print(r.round(4).to_string())
