using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace PriestessPet
{
    // Green is keyed globally, including enclosed gaps; neutral whites stay opaque.
    public sealed class SpriteAtlas : IDisposable
    {
        public const int FrameCount=48;
        private readonly Bitmap[] originals=new Bitmap[FrameCount];
        private readonly PointF[] faceAnchors=new PointF[FrameCount];
        private readonly Dictionary<string,Bitmap> cache=new Dictionary<string,Bitmap>();
        private readonly List<string> metrics=new List<string>();
        public static readonly string[] Names={"Idle (base)","Blink (base)","Walk A (base)","Walk B (base)","Sit","Sleep","Climb A (base)","Climb B (base)","Hang","Fall / drag","Land","Shrug","Walk 1","Walk 2","Walk 3","Walk 4","Walk 5","Walk 6","Walk 7","Walk 8","Climb 1","Climb 2","Climb 3","Climb 4","Climb 5","Climb 6","Climb 7","Climb 8","Idle open","Blink close","Blink shut","Blink open","Pat 1","Pat 2","Pat 3","Pat 4","Eat 1","Eat 2","Eat 3","Eat 4","Thinking 1","Thinking 2","Thinking blink","Thinking 4","Reading 1","Reading 2","Reading blink","Reading smile"};
        public SpriteAtlas()
        {
            Rectangle[] legacy={
                new Rectangle(20,0,327,371),new Rectangle(389,0,336,371),new Rectangle(751,0,329,371),new Rectangle(1117,0,309,371),
                new Rectangle(20,379,327,331),new Rectangle(366,452,373,250),new Rectangle(749,370,340,349),new Rectangle(1118,370,315,349),
                new Rectangle(28,717,309,369),new Rectangle(346,724,385,362),new Rectangle(739,748,355,338),new Rectangle(1112,722,336,364)};
            Load("Priestess.Base",0,12,4,3,legacy);
            Load("Priestess.Walk",12,8,4,2,null);
            Load("Priestess.Climb",20,8,4,2,null);
            Load("Priestess.Idle",28,4,2,2,null);
            Load("Priestess.Care",32,8,4,2,null);
            Load("Priestess.Thought",40,8,4,2,null,true);
        }
        private void Load(string resource,int offset,int count,int columns,int rows,Rectangle[] crops,bool preserveAlpha=false)
        {
            Bitmap[] cells=new Bitmap[count];Rectangle[] bounds=new Rectangle[count];PointF[] faces=new PointF[count];
            try
            {
                using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream(resource))
                {
                    if(stream==null)throw new FileNotFoundException("Missing artwork: "+resource);
                    using(Bitmap sheet=new Bitmap(stream))
                    {
                        for(int i=0;i<count;i++)
                        {
                            Rectangle crop;
                            if(crops!=null)
                            {
                                Rectangle r=crops[i];
                                crop=Rectangle.FromLTRB(r.Left*sheet.Width/1448,r.Top*sheet.Height/1086,r.Right*sheet.Width/1448,r.Bottom*sheet.Height/1086);
                            }
                            else crop=Rectangle.FromLTRB((i%columns)*sheet.Width/columns,(i/columns)*sheet.Height/rows,((i%columns)+1)*sheet.Width/columns,((i/columns)+1)*sheet.Height/rows);
                            using(Bitmap raw=sheet.Clone(crop,PixelFormat.Format32bppArgb))cells[i]=preserveAlpha?CleanAlpha(raw,Names[offset+i]):KeyGreen(raw,Names[offset+i]);
                            bounds[i]=OpaqueBounds(cells[i]);faces[i]=FindFace(cells[i],bounds[i]);
                        }
                    }
                }
                double scale=1.02;
                if(offset!=0)
                {
                    double height=0;
                    for(int i=0;i<count;i++)height+=bounds[i].Height;
                    scale=(offset==20?350.0:360.0)/(height/count);
                    for(int i=0;i<count;i++)scale=Math.Min(scale,376.0/Math.Max(bounds[i].Width,bounds[i].Height));
                }
                // One scale per sequence: ground baseline and face x stay aligned.
                // Climbing has a fixed wall contact and face y.
                double climbFaceY=130;
                if(offset==20)
                {
                    double minimum=0,maximum=384;
                    for(int i=0;i<count;i++)
                    {
                        minimum=Math.Max(minimum,4+(faces[i].Y-bounds[i].Top)*scale);
                        maximum=Math.Min(maximum,380-(bounds[i].Bottom-faces[i].Y)*scale);
                    }
                    climbFaceY=Math.Max(minimum,Math.Min(maximum,130));
                }
                for(int i=0;i<count;i++)
                {
                    int index=offset+i;Rectangle b=bounds[i];
                    double s=offset==0?Math.Min(1.02,376.0/Math.Max(b.Width,b.Height)):scale;
                    double w=b.Width*s,h=b.Height*s,left=(384-w)/2,top=380-h;
                    if(offset==12)left=244-(faces[i].X-b.Left)*s;
                    if(offset==28||offset==32)left=192-(faces[i].X-b.Left)*s;
                    if(offset==20){left=380-w;top=climbFaceY-(faces[i].Y-b.Top)*s;}
                    if(index==6||index==7)left=380-w;
                    if(index==8)top=4;
                    left=Math.Max(4,Math.Min(380-w,left));top=Math.Max(4,Math.Min(380-h,top));
                    faceAnchors[index]=new PointF((float)(left+(faces[i].X-b.Left)*s),(float)(top+(faces[i].Y-b.Top)*s));
                    // Glasses split the light face into separate islands. The
                    // new frontal poses use their centered silhouette instead.
                    if(offset==40)faceAnchors[index]=new PointF(192,140);
                    originals[index]=new Bitmap(384,384,PixelFormat.Format32bppPArgb);
                    using(Graphics g=Graphics.FromImage(originals[index]))
                    {
                        Configure(g);
                        g.DrawImage(cells[i],new RectangleF((float)left,(float)top,(float)w,(float)h),b,GraphicsUnit.Pixel);
                    }
                    metrics.Add(String.Format(System.Globalization.CultureInfo.InvariantCulture,"{0}: scale={1:F4}, face=({2:F2},{3:F2}), bounds={4}",Names[index],s,left+(faces[i].X-b.Left)*s,top+(faces[i].Y-b.Top)*s,OpaqueBounds(originals[index])));
                }
            }
            finally{foreach(Bitmap b in cells)if(b!=null)b.Dispose();}
        }
        private static Bitmap CleanAlpha(Bitmap source,string name)
        {
            Bitmap result=source.Clone(new Rectangle(0,0,source.Width,source.Height),PixelFormat.Format32bppArgb);
            BitmapData d=result.LockBits(new Rectangle(0,0,result.Width,result.Height),ImageLockMode.ReadWrite,PixelFormat.Format32bppArgb);
            byte[] data=new byte[d.Stride*result.Height];Marshal.Copy(d.Scan0,data,0,data.Length);
            int empty=0,solidWhite=0;
            for(int p=0;p<data.Length;p+=4)
            {
                // Keep the supplied alpha and soft outline. The source's opaque
                // interiors sit at 253/255, so snap only near-opaque pixels.
                if(data[p+3]<=8){data[p]=data[p+1]=data[p+2]=data[p+3]=0;empty++;}
                else if(data[p+3]>=248)data[p+3]=255;
                if(data[p+3]==255&&data[p]>210&&data[p+1]>210&&data[p+2]>210)solidWhite++;
            }
            Marshal.Copy(data,0,d.Scan0,data.Length);result.UnlockBits(d);
            if(empty<source.Width*source.Height/15||solidWhite<100)
            {result.Dispose();throw new InvalidDataException("Supplied alpha/opaque-white check failed: "+name);}
            return result;
        }
        private static Bitmap KeyGreen(Bitmap source,string name)
        {
            int w=source.Width,h=source.Height;
            BitmapData d=source.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            int stride=d.Stride;byte[] data=new byte[stride*h];Marshal.Copy(d.Scan0,data,0,data.Length);source.UnlockBits(d);
            int keyCount=0,whiteCount=0;
            for(int y=0;y<h;y++)for(int x=0;x<w;x++)
            {
                int k=y*stride+x*4;int b=data[k],g=data[k+1],r=data[k+2],excess=g-Math.Max(r,b);
                bool pureKey=g>=205&&r<=55&&b<=55&&excess>=165;
                if(pureKey){data[k]=data[k+1]=data[k+2]=data[k+3]=0;keyCount++;continue;}
                if(excess>8)
                {
                    // Recover edge alpha and remove the green matte before resizing.
                    double alpha=Math.Max(0,1-excess/255.0);
                    if(alpha<0.08){data[k]=data[k+1]=data[k+2]=data[k+3]=0;keyCount++;continue;}
                    data[k]=(byte)Math.Min(255,b/alpha);
                    data[k+1]=(byte)Math.Min(255,Math.Max(0,(g-(1-alpha)*255)/alpha));
                    data[k+2]=(byte)Math.Min(255,r/alpha);
                    data[k+3]=(byte)Math.Round(alpha*255);
                }
                else
                {
                    data[k+3]=255;
                    if(r>=210&&g>=210&&b>=210)whiteCount++;
                }
            }
            if(keyCount<w*h/15||whiteCount<100)throw new InvalidDataException("Key/opaque-white check failed: "+name);
            Bitmap result=new Bitmap(w,h,PixelFormat.Format32bppArgb);
            d=result.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
            Marshal.Copy(data,0,d.Scan0,data.Length);result.UnlockBits(d);return result;
        }
        private static Rectangle OpaqueBounds(Bitmap b)
        {
            int x0=b.Width,y0=b.Height,x1=-1,y1=-1;
            BitmapData d=b.LockBits(new Rectangle(0,0,b.Width,b.Height),ImageLockMode.ReadOnly,b.PixelFormat);
            byte[] data=new byte[d.Stride*b.Height];Marshal.Copy(d.Scan0,data,0,data.Length);int stride=d.Stride;b.UnlockBits(d);
            for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)
                if(data[y*stride+x*4+3]>=24){x0=Math.Min(x0,x);y0=Math.Min(y0,y);x1=Math.Max(x1,x);y1=Math.Max(y1,y);}
            if(x1<x0)throw new InvalidDataException("Empty animation cel.");
            return Rectangle.FromLTRB(x0,y0,x1+1,y1+1);
        }
        private static PointF FindFace(Bitmap b,Rectangle box)
        {
            int w=b.Width,h=b.Height,n=w*h;bool[] white=new bool[n],seen=new bool[n];int[] queue=new int[n];
            BitmapData d=b.LockBits(new Rectangle(0,0,w,h),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
            byte[] pixels=new byte[d.Stride*h];int stride=d.Stride;Marshal.Copy(d.Scan0,pixels,0,pixels.Length);b.UnlockBits(d);
            for(int y=box.Top+box.Height/8;y<box.Top+(int)(box.Height*.54);y++)for(int x=box.Left;x<box.Right;x++)
            {int k=y*stride+x*4;white[y*w+x]=pixels[k+3]>240&&pixels[k]>175&&pixels[k+1]>170&&pixels[k+2]>190;}
            int largest=0;PointF center=new PointF(box.Left+box.Width*.5f,box.Top+box.Height*.32f);
            for(int start=0;start<n;start++)
            {
                if(!white[start]||seen[start])continue;
                int head=0,tail=1;long sumX=0,sumY=0;queue[0]=start;seen[start]=true;
                while(head<tail)
                {
                    int p=queue[head++],x=p%w,y=p/w;sumX+=x;sumY+=y;
                    for(int dir=0;dir<4;dir++)
                    {
                        int xx=x+(dir==0?1:dir==1?-1:0),yy=y+(dir==2?1:dir==3?-1:0);
                        if(xx<0||yy<0||xx>=w||yy>=h)continue;int q=yy*w+xx;
                        if(white[q]&&!seen[q]){seen[q]=true;queue[tail++]=q;}
                    }
                }
                if(tail>largest){largest=tail;center=new PointF((float)sumX/tail,(float)sumY/tail);}
            }
            return center;
        }
        private static void Configure(Graphics g)
        {g.CompositingMode=CompositingMode.SourceCopy;g.InterpolationMode=InterpolationMode.HighQualityBicubic;g.PixelOffsetMode=PixelOffsetMode.HighQuality;}
        public bool IsHead(int frame,int size,bool left,Point point)
        {
            frame=Math.Max(0,Math.Min(FrameCount-1,frame));
            if(size<1||point.X<0||point.Y<0||point.X>=size||point.Y>=size)return false;
            float x=(left?size-1-point.X:point.X)*384f/size,y=point.Y*384f/size;
            PointF face=faceAnchors[frame];
            return x>=face.X-98&&x<=face.X+98&&y>=face.Y-125&&y<=face.Y+38;
        }
        public Bitmap GetFrame(int frame,int size,bool left)
        {
            frame=Math.Max(0,Math.Min(FrameCount-1,frame));size=Math.Max(1,Math.Min(512,size));
            string key=frame+":"+size+":"+left;Bitmap b;if(cache.TryGetValue(key,out b))return b;
            b=new Bitmap(size,size,PixelFormat.Format32bppPArgb);
            using(Graphics g=Graphics.FromImage(b)){Configure(g);g.DrawImage(originals[frame],new Rectangle(0,0,size,size));}
            if(left)b.RotateFlip(RotateFlipType.RotateNoneFlipX);
            cache.Add(key,b);return b;
        }
        public void RenderContactSheet(string path){RenderContactSheet(path,false);}
        public void RenderContactSheet(string path,bool light)
        {
            using(Bitmap image=new Bitmap(1600,74+((FrameCount+7)/8)*226))using(Graphics g=Graphics.FromImage(image))
            using(Font title=new Font("Segoe UI",20,FontStyle.Bold))using(Font label=new Font("Segoe UI",9))
            using(Brush ink=new SolidBrush(light?Color.FromArgb(30,26,40):Color.FromArgb(235,230,247)))
            {
                g.Clear(light?Color.White:Color.FromArgb(25,22,35));g.DrawString("PRIESTESS  /  "+FrameCount+" animation cels",title,ink,18,12);
                for(int i=0;i<FrameCount;i++)
                {
                    int x=(i%8)*200,y=62+(i/8)*226;
                    using(Brush tile=new SolidBrush(light?Color.FromArgb(248,247,252):Color.FromArgb(48,43,64)))g.FillRectangle(tile,x+4,y,192,218);
                    g.DrawImage(GetFrame(i,180,false),x+10,y+4);g.DrawString(Names[i],label,ink,x+12,y+194);
                }
                image.Save(path,ImageFormat.Png);
            }
        }
        public void WriteMetrics(string path){File.WriteAllLines(path,metrics.ToArray());}
        public string Validate()
        {
            for(int i=0;i<FrameCount;i++)
            {
                Bitmap b=GetFrame(i,180,false);int empty=0,solidWhite=0,solid=0;
                for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)
                {
                    Color c=b.GetPixel(x,y);if(c.A==0)empty++;if(c.A>245)solid++;
                    if(c.A>245&&c.R>210&&c.G>210&&c.B>210)solidWhite++;
                    if(c.A>40&&c.G-Math.Max(c.R,c.B)>12)throw new InvalidDataException("Green spill remains: "+Names[i]);
                }
                if(empty<3500||solid<1500||solidWhite<120)throw new InvalidDataException("Transparency/opaque outfit coverage: "+Names[i]);
                for(int p=0;p<180;p++)
                    if(b.GetPixel(p,0).A>16||b.GetPixel(0,p).A>16||b.GetPixel(179,p).A>16||b.GetPixel(p,179).A>16)throw new InvalidDataException("Clipped animation cel: "+Names[i]);
                Bitmap mirror=GetFrame(i,180,true);
                for(int y=0;y<180;y+=13)for(int x=0;x<180;x+=13)
                    if(b.GetPixel(x,y)!=mirror.GetPixel(179-x,y))throw new InvalidDataException("Mirror mismatch.");
                BitmapData d=b.LockBits(new Rectangle(0,0,180,180),ImageLockMode.ReadOnly,PixelFormat.Format32bppPArgb);
                byte[] raw=new byte[d.Stride*180];Marshal.Copy(d.Scan0,raw,0,raw.Length);b.UnlockBits(d);
                for(int p=0;p<raw.Length;p+=4)if(raw[p]>raw[p+3]||raw[p+1]>raw[p+3]||raw[p+2]>raw[p+3])throw new InvalidDataException("Invalid premultiplied alpha.");
            }
            if(GetFrame(12,1,false).Width!=1)throw new InvalidDataException("Tiny monitor size mismatch.");
            if(!IsHead(28,180,false,new Point(90,45))||IsHead(28,180,false,new Point(90,160)))throw new InvalidDataException("Head/body hit regions overlap incorrectly.");
            return "PASS: "+FrameCount+" cels; clean alpha; opaque outfit; no green spill; margins and mirroring; head-pat hit regions.";
        }
        public void Dispose(){foreach(Bitmap b in cache.Values)b.Dispose();cache.Clear();foreach(Bitmap b in originals)if(b!=null)b.Dispose();}
    }
}
