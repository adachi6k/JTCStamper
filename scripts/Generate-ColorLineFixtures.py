"""Independent Pillow experiment; not WPF/Office. OUTPUT FONT_PATH [--preview-only | --holdout]."""
from pathlib import Path
from functools import lru_cache
from PIL import Image, ImageDraw, ImageFont
import math,json,sys,io,random
out=Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
font_path=sys.argv[2];font=ImageFont.truetype(font_path,400)
S=8;RED=(195,32,40);VERMILION=(220,75,30);GAP=3.5;LINE_LENGTH=6
PROFILES=[(mode,palette) for mode in ('ring12','split12','split16') for palette in ('red','vermilion')]+[('hybrid16','red')]
def encode(code,mode):
    payload=16 if mode in ('split16','hybrid16') else 12
    if not 0<=code<(1<<payload):raise ValueError('payload')
    tag={'ring12':0xD3,'split12':0xD4,'split16':0xD5,'hybrid16':0xD6}[mode]
    crc=0
    for v in (tag,code>>8,code&255):
        crc^=v
        for _ in range(8):crc=((crc<<1)^(7 if crc&128 else 0))&255
    ring_payload=payload if mode=='ring12' else payload-4
    n=ring_payload+16
    word=(tag<<(ring_payload+8))|((code&((1<<ring_payload)-1))<<8)|crc
    ring=[(word>>(n-1-i))&1 for i in range(n)]
    line=[] if mode=='ring12' else [(code>>(payload-1-i))&1 for i in range(4)]
    return ring,line
@lru_cache(maxsize=128)
def render(code,name,mode,palette):
    base,accent=(RED,VERMILION) if palette=='red' else (VERMILION,RED)
    im=Image.new('RGBA',(96*S,96*S),(0,0,0,0));d=ImageDraw.Draw(im)
    if mode!='hybrid16' or code is None:d.arc((5*S,5*S,91*S,91*S),0,360,fill=base+(255,),width=round(1.1*S))
    ring,line=encode(code,mode) if code is not None else ([],[])
    half=len(ring)//2;start=0
    for i,bit in enumerate(ring):
        if bit:
            a=(37 if i<half else 217)+(i%half)*106/(half-1)
            if mode=='hybrid16':
                d.arc((5*S,5*S,91*S,91*S),start,a-GAP/2,fill=base+(255,),width=round(1.1*S));start=a+GAP/2
            else:d.arc((5*S,5*S,91*S,91*S),a-GAP/2,a+GAP/2,fill=accent+(255,),width=round(1.1*S))
    if mode=='hybrid16' and code is not None:d.arc((5*S,5*S,91*S,91*S),start,360,fill=base+(255,),width=round(1.1*S))
    delta=(code&3)*2-3 if code is not None else 0
    for row,(y,sign) in enumerate(((33,1),(63,-1))):
        a=math.radians(sign*delta/2);c=math.cos(a);s=math.sin(a);v=y-48
        extent=math.sqrt(43**2-v*v*c*c);t0=-v*s-extent;t1=-v*s+extent
        point=lambda t:((48+t*c)*S,(y+t*s)*S)
        d.line((point(t0),point(t1)),fill=base+(255,),width=round(1.1*S))
        for j,t in enumerate((-24,24)):
            if line and line[row*2+j]:
                d.line((point(t-LINE_LENGTH/2),point(t+LINE_LENGTH/2)),fill=accent+(255,),width=round(1.1*S))
    for text,band in ((name,-1),("'26.09.18",0),('(印)',1)):
        b=font.getbbox(text);glyph=Image.new('L',(b[2]-b[0],b[3]-b[1]),0)
        ImageDraw.Draw(glyph).text((-b[0],-b[1]),text,font=font,fill=255)
        lo=0;hi=24/glyph.height
        for _ in range(40):
            scale=(lo+hi)/2;h=glyph.height*scale;w=glyph.width*scale
            cy=30-h/2 if band<0 else 66+h/2 if band>0 else 48
            if (w/2)**2+(abs(cy-48)+h/2)**2<=41**2:lo=scale
            else:hi=scale
        w=max(1,round(glyph.width*lo*S));h=max(1,round(glyph.height*lo*S))
        cy=30*S-h/2 if band<0 else 66*S+h/2 if band>0 else 48*S
        mask=glyph.resize((w,h),Image.Resampling.LANCZOS)
        layer=Image.new('RGBA',im.size,base+(0,));layer.paste(base+(255,),(round(48*S-w/2),round(cy-h/2)),mask)
        im=Image.alpha_composite(im,layer)
    return im
@lru_cache(maxsize=6)
def background(kind):
    colors={'white':(255,255,255),'yellow':(255,248,210),'blue':(215,235,255),'gray':(210,210,210),'black-text':(255,255,255),'red-text':(255,255,255)}
    im=Image.new('RGBA',(240*S,240*S),colors[kind]+(255,))
    if kind.endswith('text'):
        d=ImageDraw.Draw(im);f=ImageFont.truetype(font_path,12*S)
        color=(40,40,40,255) if kind=='black-text' else (170,35,35,255)
        for y in range(28,225,22):d.text((22*S,y*S),'確認済み 2026/09/18 ABC',font=f,fill=color)
    return im

def fixtures():
    index=0
    for mode,palette in PROFILES:
      codes=(0,1,0x5555,0xAAAA,0xABCD,0xFFFF,None) if mode in ('split16','hybrid16') else (0,1,0x555,0xAAA,0xBCD,0xFFF,None)
      for bg in ('white','yellow','blue','gray','black-text','red-text'):
       for name in (('JTC','田中') if bg=='white' else ('田中',)):
        for code in codes:
         for diameter in (88,176):
          for rotation in (-4,0,4):
           for offset in ((-.35,0,.35) if bg=='white' else (.35,)):
            for jpeg in (False,True):
             yield dict(File=f'{index:05}.bgra',Width=240,Height=240,Mode=mode,Palette=palette,Background=bg,Name=name,Kind='encoded' if code is not None else 'unencoded',Expected=code,Diameter=diameter,Rotation=rotation,Offset=offset,Jpeg=jpeg)
             index+=1

def holdout_fixtures():
    # Fixed unseen payloads. Decode never receives these expected values.
    excluded={0,1,0x5555,0xAAAA,0xABCD,0xFFFF}
    codes=random.Random(20260918).sample([x for x in range(65536) if x not in excluded],32)
    index=0
    for mode in ('split16','hybrid16'):
     for k,code in enumerate(codes):
      for diameter in (88,176):
       for jpeg in (False,True):
        yield dict(File=f'{index:05}.bgra',Width=240,Height=240,Mode=mode,Palette='red',Background='white',Name=('JTC','田中')[k%2],Kind='encoded',Expected=code,Diameter=diameter,Rotation=(-4,0,4)[k%3],Offset=(-.35,0,.35)[(k//3)%3],Jpeg=jpeg)
        index+=1

def page_for(f):
    stamp=render(f['Expected'],f['Name'],f['Mode'],f['Palette']);size=round(f['Diameter']*96/87.1*S)
    resized=stamp.resize((size,size),Image.Resampling.LANCZOS).rotate(-f['Rotation'],resample=Image.Resampling.BICUBIC,expand=True,fillcolor=(0,0,0,0))
    page=background(f['Background']).copy()
    page.alpha_composite(resized,(round(120*S-resized.width/2+f['Offset']*S),round(120*S-resized.height/2-f['Offset']*S)))
    return page.convert('RGB').resize((240,240),Image.Resampling.LANCZOS)
if __name__=='__main__':
    if '--preview-only' in sys.argv:
        for mode,palette in PROFILES:render(0xFFFF if mode in ('split16','hybrid16') else 0xFFF,'JTC',mode,palette).save(out/f'{mode}-{palette}.png')
    else:
        manifest=list(holdout_fixtures() if '--holdout' in sys.argv else fixtures());(out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False),encoding='utf-8')
        page=None
        for f in manifest:
            if not f['Jpeg']:page=page_for(f)
            processed=page
            if f['Jpeg']:
                b=io.BytesIO();page.save(b,format='JPEG',quality=80);b.seek(0);processed=Image.open(b).convert('RGB')
            (out/f['File']).write_bytes(processed.convert('RGBA').tobytes('raw','BGRA'))
            if int(f['File'][:5])%924==923:print(f"completed {f['Mode']} {f['Palette']}",flush=True)
        (out/'complete').write_text(str(len(manifest)))
        print(f'{len(manifest)} fixtures',flush=True)
