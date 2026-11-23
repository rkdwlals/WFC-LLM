# 목표 맵 그림 렌더러 (기존 Experiment/v2/images/*.png와 같은 색·배치를 재현)
import sys, os
from PIL import Image, ImageDraw, ImageFont
COL = {'.':(236,229,206),'~':(96,150,214),'=':(190,170,130),'S':(255,255,255),'G':(220,40,40),
       'R':(128,128,128),'W':(92,60,36),'C':(235,145,40),'B':(60,150,60)}
FONT = '/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc'
def render(grid_path, out):
    rows=[l.rstrip('\n') for l in open(grid_path,encoding='utf-8') if l.strip()]
    cs=18; ox=oy=40; im=Image.new('RGB',(656,726),(255,255,255)); d=ImageDraw.Draw(im)
    f=ImageFont.truetype(FONT,17)
    d.text((40,8),'↑ 위쪽이 북쪽',font=f,fill=(0,0,0))
    for y,r in enumerate(rows):
        for x,c in enumerate(r):
            x0,y0=ox+x*cs,oy+y*cs
            d.rectangle([x0,y0,x0+cs-1,y0+cs-1],fill=COL[c])
            if c=='S': d.rectangle([x0+1,y0+1,x0+cs-2,y0+cs-2],outline=(0,0,0),width=2)
    leg=[('R','바위'),('W','벽'),('C','상자'),('B','수풀'),('~','물'),('=','다리(길)'),('S','시작점'),('G','목표 지점')]
    for i,(c,name) in enumerate(leg):
        lx=40+(i%4)*150; ly=632+(i//4)*28
        d.rectangle([lx,ly,lx+17,ly+17],fill=COL[c],outline=(0,0,0) if c=='S' else None,width=2 if c=='S' else 0)
        d.text((lx+26,ly-3),name,font=f,fill=(0,0,0))
    im.save(out)
if __name__=='__main__':
    src,dst=sys.argv[1],sys.argv[2]; os.makedirs(dst,exist_ok=True)
    for fn in sorted(os.listdir(src)):
        if fn.startswith('T') and fn.endswith('.txt'): render(os.path.join(src,fn), os.path.join(dst,fn[:-4]+'.png'))
