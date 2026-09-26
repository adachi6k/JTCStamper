"""Capacity experiment, NOT production. OUTPUT FONT_PATH. Requires Pillow.
Shares the earlier synthetic renderer's circle/text/line layout. No AI images.
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import math,json,sys,io,random
from functools import lru_cache
out=Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
font=ImageFont.truetype(sys.argv[2],400)
S=8;red=(195,32,40);gap_degrees=3.5

def checksum(code,mode):
    values=[0xD3,code>>8,code&255] if mode==12 else [0xD3,mode]+list(code.to_bytes((mode+7)//8,'big'))
    crc=0
    for value in values:
        crc^=value
        for _ in range(8):crc=((crc<<1)^(7 if crc&128 else 0))&255
    return crc

def encode(code,mode):
    n=mode+16;word=(0xD3<<(mode+8))|(code<<8)|checksum(code,mode)
    return [(word>>(n-1-i))&1 for i in range(n)]
@lru_cache(maxsize=256)
def render(code,name,bits,mode):
    ring_count=28 if mode==12 else mode+12
    im=Image.new('RGB',(96*S,96*S),'white');d=ImageDraw.Draw(im)
    intervals=[];start=0
    if code is not None:
        for i,bit in enumerate(bits[:ring_count]):
            if bit:
                half=ring_count//2
                a=(37 if i<half else 217)+(i%half)*106/(half-1)
                intervals.append((start,a-gap_degrees/2));start=a+gap_degrees/2
    intervals.append((start,360))
    for start,end in intervals:
        d.arc((5*S,5*S,91*S,91*S),start,end,fill=red,width=round(1.1*S))
    delta=(code&3)*2-3 if code is not None else 0
    for y,sign in ((33,1),(63,-1)):
        a=math.radians(sign*delta/2);c=math.cos(a);s=math.sin(a);v=y-48
        half=math.sqrt(43**2-v*v*c*c);t0=-v*s-half;t1=-v*s+half
        point=lambda t:((48+t*c)*S,(y+t*s)*S)
        cuts=[]
        if mode != 12:
            row=0 if y==33 else 1
            cuts=[(t-1.6,t+1.6) for j,t in enumerate((-24,24)) if bits[ring_count+row*2+j]]
        start=t0
        for left,right in cuts+[(t1,t1)]:
            d.line((point(start),point(left)),fill=red,width=round(1.1*S));start=right
    for text,band in ((name,-1),("'26.09.18",0),('(印)',1)):
        bounds=font.getbbox(text);glyph=Image.new('L',(bounds[2]-bounds[0],bounds[3]-bounds[1]),0)
        ImageDraw.Draw(glyph).text((-bounds[0],-bounds[1]),text,font=font,fill=255)
        lo=0;hi=24/glyph.height
        for _ in range(40):
            scale=(lo+hi)/2;h=glyph.height*scale;w=glyph.width*scale
            cy=30-h/2 if band<0 else 66+h/2 if band>0 else 48
            if (w/2)**2+(abs(cy-48)+h/2)**2 <=41**2:lo=scale
            else:hi=scale
        w=max(1,round(glyph.width*lo*S));h=max(1,round(glyph.height*lo*S))
        cy=30*S-h/2 if band<0 else 66*S+h/2 if band>0 else 48*S
        mask=glyph.resize((w,h),Image.Resampling.LANCZOS)
        im.paste(red,(round(48*S-w/2),round(cy-h/2)),mask)
    return im

# Same latent values across widths. No threshold tuning after holdout results.
rng=random.Random(20261001)
values=[0,0xffffff,0x555555,0xaaaaaa]+rng.sample(range(1,0xfffffe),20)
manifest=[]
def save(mode,kind,code,bits,transform,index):
    stamp=render(code,('佐藤','鈴木')[index%2],tuple(bits),mode)
    diameter=176 if transform=='jpeg176' else 88
    rotation=4 if transform=='rot4' else 0
    offset=(-.35,0,.35)[index%3]
    size=round(diameter*96/87.1*S)
    resized=stamp.resize((size,size),Image.Resampling.LANCZOS).rotate(-rotation,resample=Image.Resampling.BICUBIC,expand=True,fillcolor='white')
    page=Image.new('RGB',(240*S,240*S),'white')
    page.paste(resized,(round(120*S-resized.width/2+offset*S),round(120*S-resized.height/2-offset*S)))
    page=page.resize((240,240),Image.Resampling.LANCZOS)
    if transform=='blur':page=page.filter(ImageFilter.GaussianBlur(.55))
    if transform.startswith('jpeg'):
        buf=io.BytesIO();page.save(buf,format='JPEG',quality=60 if transform=='jpeg60' else 80);buf.seek(0);page=Image.open(buf).convert('RGB')
    filename=f'{len(manifest):05}.bgra';(out/filename).write_bytes(page.convert('RGBA').tobytes('raw','BGRA'))
    manifest.append(dict(File=filename,Width=240,Height=240,Bits=mode,Kind=kind,Expected=code if kind=='encoded' else None,Transform=transform,Split='development' if index<12 else 'holdout',Diameter=diameter,Rotation=rotation,Offset=offset))
    if kind=='encoded' and index in (0,1,2,3,12) and transform in ('png','jpeg80','rot4','jpeg176'):
        box=(20,20,220,220) if diameter==176 else (66,66,174,174)
        page.crop(box).save(out/f'preview-{mode}-{index}-{transform}.png')
for mode in (12,16,20,24):
    for index,value in enumerate(values):
        code=value&((1<<mode)-1)
        for transform in ('png','jpeg80','jpeg60','rot4','jpeg176','blur'):
            save(mode,'encoded',code,encode(code,mode),transform,index)
    for index in range(8):
        for kind in ('no-code','random','marker-random'):
            bits=[0]*(mode+16) if kind=='no-code' else [rng.randrange(2) for _ in range(mode+16)]
            if kind=='marker-random':bits[:8]=encode(0,mode)[:8]
            for transform in ('png','jpeg80'):save(mode,kind,0,bits,transform,index)
    print(f'{mode} bit generated',flush=True)
(out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False),encoding='utf-8')
(out/'protocol.json').write_text(json.dumps(dict(seed=20261001,values=values,notes='Synthetic Pillow, auto-detected crop. 12/16/20/24 payload; 8 marker + 8 CRC; no ECC. Mode supplied to decoder. No history matching, no claim of false-history acceptance measurement.'),indent=2))
