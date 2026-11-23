# 격자 텍스트(.RWCB~=SG)를 2D 이미지로. 명령어 작성용 목표 맵 그림에 사용.
import sys, os
from PIL import Image, ImageDraw, ImageFont
FONT = "/usr/share/fonts/opentype/noto/NotoSansCJK-Regular.ttc"
COL = {'.': (236, 229, 206), '~': (96, 150, 214), '=': (190, 170, 130), 'S': (255, 255, 255), 'G': (220, 40, 40),
       'R': (128, 128, 128), 'W': (92, 60, 36), 'C': (235, 145, 40), 'B': (60, 150, 60)}
LEG = [('R', '바위'), ('W', '벽'), ('C', '상자'), ('B', '수풀'), ('~', '물'), ('=', '다리(길)'), ('S', '시작점'), ('G', '목표 지점')]
def render(src, dst, cell=18):
    rows = [l.rstrip('\n') for l in open(src, encoding='utf-8') if l.strip()]
    H, W = len(rows), len(rows[0]); pad = 40
    img = Image.new('RGB', (W * cell + pad * 2, H * cell + pad * 2 + 70), 'white'); d = ImageDraw.Draw(img)
    f = ImageFont.truetype(FONT, 18)
    d.text((pad, 8), '↑ 위쪽이 북쪽', font=f, fill=(0, 0, 0))
    for y, r in enumerate(rows):
        for x, c in enumerate(r):
            x0, y0 = pad + x * cell, pad + y * cell
            d.rectangle([x0, y0, x0 + cell - 1, y0 + cell - 1], fill=COL.get(c, (255, 0, 255)))
            if c == 'S': d.rectangle([x0, y0, x0 + cell - 1, y0 + cell - 1], outline=(0, 0, 0), width=2)
    lx, ly = pad, pad + H * cell + 14
    for i, (c, name) in enumerate(LEG):
        xx = lx + (i % 4) * 150; yy = ly + (i // 4) * 26
        d.rectangle([xx, yy + 3, xx + 16, yy + 19], fill=COL[c], outline=(0, 0, 0))
        d.text((xx + 22, yy), name, font=f, fill=(0, 0, 0))
    img.save(dst)
if __name__ == '__main__':
    src_dir, dst_dir = sys.argv[1], sys.argv[2]; os.makedirs(dst_dir, exist_ok=True)
    for fn in sorted(os.listdir(src_dir)):
        if fn.endswith('.txt'): render(os.path.join(src_dir, fn), os.path.join(dst_dir, fn[:-4] + '.png'))
