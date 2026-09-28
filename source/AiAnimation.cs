using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace PriestessPet
{
    public static class AiAnimation
    {
        public static int Frame(bool reading,double age)
        {
            if(Double.IsNaN(age)||Double.IsInfinity(age)||age<0)age=0;
            double phase=age%4.8;
            // Long open-eye holds and a short blink give the small gestures
            // time to read. Each loop returns to its first pose.
            int cel=phase<1.35?0:phase<2.48?1:phase<2.64?2:phase<3.65?3:0;
            return (reading?44:40)+cel;
        }
        public static double ReadingDuration(string text)
        {return Math.Max(6,Math.Min(18,4+(text==null?0:text.Length)*0.045));}
        public static string Validate(SpriteAtlas atlas)
        {
            for(int i=40;i<48;i++)
            {
                if(!atlas.IsHead(i,180,false,new Point(90,45))||atlas.IsHead(i,180,false,new Point(90,162)))
                    throw new InvalidDataException("AI pose head/body hit regions are incorrect: "+SpriteAtlas.Names[i]);
                Bitmap b=atlas.GetFrame(i,180,false);int bottom=-1;
                for(int y=160;y<180;y++)for(int x=0;x<180;x++)if(b.GetPixel(x,y).A>100)bottom=Math.Max(bottom,y);
                if(bottom<176||bottom>178)throw new InvalidDataException("AI pose foot baseline moved.");
            }
            if(Frame(false,0)!=40||Frame(true,0)!=44||Frame(false,4.8)!=40||Frame(true,Double.NaN)!=44)
                throw new InvalidDataException("AI animation loop does not begin at its open-eye pose.");
            return "PASS: 8 thinking/reading cels; aligned soles and head-pat regions; stable looping and bounded reply-reading duration.";
        }
        public static void RenderContactSheet(SpriteAtlas atlas,string path,bool light)
        {
            using(Bitmap image=new Bitmap(1120,708,PixelFormat.Format32bppArgb))
            using(Graphics g=Graphics.FromImage(image))
            using(Font title=new Font("Segoe UI",21,FontStyle.Bold))
            using(Font label=new Font("Segoe UI",11))
            using(Brush ink=new SolidBrush(light?Color.FromArgb(40,28,54):Color.FromArgb(241,231,252)))
            {
                g.Clear(light?Color.White:Color.FromArgb(25,22,35));
                g.DrawString("PRIESTESS / Listening & reading",title,ink,22,14);
                g.DrawString("Thinking while she processes your message",label,ink,24,61);
                g.DrawString("Glasses on while she shares her reply",label,ink,24,376);
                for(int i=0;i<8;i++)
                {
                    int x=22+(i%4)*274,y=i<4?89:404;
                    using(Brush tile=new SolidBrush(light?Color.FromArgb(248,246,251):Color.FromArgb(44,36,58)))g.FillRectangle(tile,x,y,258,276);
                    g.DrawImageUnscaled(atlas.GetFrame(40+i,240,false),x+9,y+5);
                    g.DrawString(SpriteAtlas.Names[40+i],label,ink,x+15,y+247);
                }
                image.Save(path,ImageFormat.Png);
            }
        }
        public static void RenderPreview(SpriteAtlas atlas,string path){AnimationPreview.Render(atlas,path,true);}
        public static void ExportSheet(SpriteAtlas atlas,string path)
        {
            using(Bitmap sheet=new Bitmap(1536,768,PixelFormat.Format32bppArgb))
            using(Graphics g=Graphics.FromImage(sheet))
            {
                g.Clear(Color.Transparent);
                for(int i=0;i<8;i++)g.DrawImageUnscaled(atlas.GetFrame(40+i,384,false),(i%4)*384,(i/4)*384);
                sheet.Save(path,ImageFormat.Png);
            }
        }
    }
}
