"""Reproducible ECC experiment, not production format. OUTPUT FONT_PATH. Requires Pillow."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont, ImageFilter
import math,json,sys,io,random
from functools import lru_cache
out=Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
font=ImageFont.truetype(sys.argv[2],400)
S=8;red=(195,32,40);gap_degrees=3.5

def encode(code,mode):
    if mode=='crc28':
        crc=0
        for value in (0xD3,code>>8,code&255):
            crc^=value
            for _ in range(8):crc=((crc<<1)^(7 if crc&128 else 0))&255
        word=(0xD3<<20)|(code<<8)|crc;n=28
    else:
        # Polynomial remainder, independent implementation of the C# codec.
        shifted=code<<11;remainder=shifted
        for bit in range(22,10,-1):
            if remainder&(1<<bit):remainder^=0xAE3<<(bit-11)
        word23=shifted|remainder
        word=(0xD3<<24)|(word23<<1)|(word23.bit_count()&1);n=32
        if mode=='split40':
            crc=0
            for value in (0xD3,code>>8,code&255):
                crc^=value
                for _ in range(8):crc=((crc<<1)^(7 if crc&128 else 0))&255
            word=(word<<8)|crc;n=40
    return [(word>>(n-1-i))&1 for i in range(n)]
@lru_cache(maxsize=256)
def render(code,name,bits,mode):
    ring_count=36 if mode=="split40" else 28 if mode=="split32" else len(bits)
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
        if mode in ('split32','split40'):
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
rng=random.Random(20260923)
codes=[0,1,0x555,0xAAA,0xABC,0xFFF]+rng.sample([x for x in range(4096) if x not in (0,1,0x555,0xAAA,0xABC,0xFFF)],16)
holdout='--holdout' in sys.argv
if holdout:
    excluded=set(codes);rng=random.Random(20260924)
    codes=rng.sample([x for x in range(4096) if x not in excluded],32)
manifest=[]
def save(mode,kind,code,bits,transform,index):
    bits=list(bits)
    if transform in ('flip1','flip2'):
        # Deliberate damage outside the sync marker; baseline and ECC use the same positions.
        for i in ([10] if transform=='flip1' else [10,18]):bits[i]^=1
    stamp=render(code,(('佐藤','鈴木') if holdout else ('JTC','田中'))[index%2],tuple(bits),mode)
    diameter=176 if transform=='large-jpeg80' else 88
    rotation=((-6,2,6) if holdout else (-4,0,4))[index%3];offset=((-0.2,0,0.2) if holdout else (-.35,0,.35))[(index//3)%3]
    size=round(diameter*96/87.1*S)
    resized=stamp.resize((size,size),Image.Resampling.LANCZOS).rotate(-rotation,resample=Image.Resampling.BICUBIC,expand=True,fillcolor='white')
    page=Image.new('RGB',(240*S,240*S),'white')
    page.paste(resized,(round(120*S-resized.width/2+offset*S),round(120*S-resized.height/2-offset*S)))
    page=page.resize((240,240),Image.Resampling.LANCZOS)
    if transform=='blur':page=page.filter(ImageFilter.GaussianBlur(.55))
    if 'jpeg' in transform:
        buf=io.BytesIO();page.save(buf,format='JPEG',quality=60 if transform=='jpeg60' else 80);buf.seek(0);page=Image.open(buf).convert('RGB')
    filename=f'{len(manifest):05}.bgra';(out/filename).write_bytes(page.convert('RGBA').tobytes('raw','BGRA'))
    manifest.append(dict(File=filename,Width=240,Height=240,Mode=mode,Kind=kind,Expected=code if kind=='encoded' else None,Transform=transform,Diameter=diameter,Rotation=rotation,Offset=offset))
    if kind=='encoded' and code==0xABC and transform=='png':page.crop((66,66,174,174)).save(out/f'{mode}-preview.png')
for mode in ([x for x in sys.argv[3:] if x != '--holdout'] or ('crc28','ring32','split32')):
    for k,code in enumerate(codes):
        for transform in ('png','jpeg80','jpeg60','large-jpeg80','blur','flip1','flip2'):save(mode,'encoded',code,encode(code,mode),transform,k)
    n=28 if mode=='crc28' else 40 if mode=='split40' else 32
    for k in range(24):
        for kind in ('no-code','random','marker-random'):
            bits=[0]*n if kind=='no-code' else [rng.randrange(2) for _ in range(n)]
            if kind=='marker-random':bits[:8]=encode(0,mode)[:8]
            for transform in ('png','jpeg80'):save(mode,kind,k%4,bits,transform,k)
    print(mode,flush=True)
(out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False),encoding='utf-8')
print(f'{len(manifest)} images',flush=True)
