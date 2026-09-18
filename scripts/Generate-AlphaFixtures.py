"""Independent synthetic raster evaluation, requires Pillow. No WPF/Office accuracy claim.
Usage: python Generate-AlphaFixtures.py OUTPUT FONT_PATH
"""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math, json, sys, io
from functools import lru_cache
out=Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
font=ImageFont.truetype(sys.argv[2],400)
gap_degrees=3.5
S=8;red=(195,32,40);manifest=[]
def cells(code):
    crc=0
    for v in (0xD3,code>>8,code&255):
        crc ^= v
        for _ in range(8):crc=((crc<<1)^ (7 if crc&128 else 0))&255
    word=(0xD3<<20)|(code<<8)|crc
    return [(word>>(27-i))&1 for i in range(28)]
@lru_cache(maxsize=64)
def render(code,name,opacity):
    im=Image.new('RGBA',(96*S,96*S),(0,0,0,0));d=ImageDraw.Draw(im)
    if code is not None and opacity:
        d.arc((5*S,5*S,91*S,91*S),0,360,fill=red+(round(opacity*255),),width=round(1.1*S))
    intervals=[];start=0
    if code is not None:
        for i,bit in enumerate(cells(code)):
            if bit:
                a=(37 if i<14 else 217)+(i%14)*106/13
                intervals.append((start,a-gap_degrees/2));start=a+gap_degrees/2
    intervals.append((start,360))
    for start,end in intervals:
        d.arc((5*S,5*S,91*S,91*S),start,end,fill=red+(255,),width=round(1.1*S))
    delta=(code&3)*2-3 if code is not None else 0
    for y,sign in ((33,1),(63,-1)):
        a=math.radians(sign*delta/2);c=math.cos(a);s=math.sin(a);v=y-48
        half=math.sqrt(43**2-v*v*c*c);t0=-v*s-half;t1=-v*s+half
        d.line(((48+t0*c)*S,(y+t0*s)*S,(48+t1*c)*S,(y+t1*s)*S),fill=red+(255,),width=round(1.1*S))
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
        layer=Image.new('RGBA',im.size,red+(0,))
        layer.paste(red+(255,),(round(48*S-w/2),round(cy-h/2)),mask)
        im=Image.alpha_composite(im,layer)
    return im

@lru_cache(maxsize=6)
def background(kind):
    colors={'white':(255,255,255),'yellow':(255,248,210),'blue':(215,235,255),'gray':(210,210,210),'black-text':(255,255,255),'red-text':(255,255,255)}
    im=Image.new('RGBA',(240*S,240*S),colors[kind]+(255,))
    if kind.endswith('text'):
        d=ImageDraw.Draw(im);f=ImageFont.truetype(sys.argv[2],12*S)
        color=(40,40,40,255) if kind=='black-text' else (170,35,35,255)
        for y in range(28,225,22):d.text((22*S,y*S),'確認済み 2026/09/18 ABC',font=f,fill=color)
    return im

def emit(opacity,bg,name,code,diameter,rotation,offset):
    stamp=render(code,name,opacity)
    size=round(diameter*96/87.1*S)
    resized=stamp.resize((size,size),Image.Resampling.LANCZOS)
    resized=resized.rotate(-rotation,resample=Image.Resampling.BICUBIC,expand=True,fillcolor=(0,0,0,0))
    page=background(bg).copy()
    page.alpha_composite(resized,(round(120*S-resized.width/2+offset*S),round(120*S-resized.height/2-offset*S)))
    page=page.convert('RGB').resize((240,240),Image.Resampling.LANCZOS)
    for jpeg in (False,True):
        processed=page
        if jpeg:
            buf=io.BytesIO();page.save(buf,format='JPEG',quality=80);buf.seek(0);processed=Image.open(buf).convert('RGB')
        file=f'{len(manifest):05}.bgra'
        (out/file).write_bytes(processed.convert('RGBA').tobytes('raw','BGRA'))
        manifest.append(dict(File=file,Width=240,Height=240,Kind='encoded' if code is not None else 'unencoded',Expected=code,Name=name,Diameter=diameter,Rotation=rotation,Offset=offset,Jpeg=jpeg,Opacity=opacity,Background=bg))
    if code==4095 and rotation==0 and offset==0.35 and name=='田中' and diameter==88:
        page.save(out/f'preview-{opacity}-{bg}.png')

for opacity in (0.0,0.3,0.5,0.7):
 for bg in ('white','yellow','blue','gray','black-text','red-text'):
  for name in (('JTC','田中') if bg=='white' else ('田中',)):
   for code in (0,1,0x555,0xAAA,0xABC,4095,None):
    for diameter in (88,176):
     for rotation in (-4,0,4):
      for offset in ((-0.35,0,0.35) if bg=='white' else (0.35,)):
       emit(opacity,bg,name,code,diameter,rotation,offset)
  print(f'generated opacity={opacity} background={bg}',flush=True)
(out/'manifest.json').write_text(json.dumps(manifest,ensure_ascii=False),encoding='utf-8')
print(f'{len(manifest)} synthetic fixtures')
