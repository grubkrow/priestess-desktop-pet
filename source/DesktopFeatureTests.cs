using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace PriestessPet
{
    internal static class DesktopFeatureTests
    {
        internal static string Run(SpriteAtlas atlas,string folder)
        {
            Rectangle area=Screen.FromPoint(Cursor.Position).WorkingArea;
            using(TestWindow host=new TestWindow())
            {
                host.Text="A window for Priestess";
                host.StartPosition=FormStartPosition.Manual;
                host.Bounds=new Rectangle(area.Left+90,area.Top+230,Math.Min(680,area.Width-180),Math.Min(330,area.Height-280));
                host.BackColor=Color.FromArgb(244,241,249);
                host.Controls.Add(new Label {Text="A little company while you work.",AutoSize=false,TextAlign=ContentAlignment.MiddleCenter,Dock=DockStyle.Fill,Font=new Font("Segoe UI",16),ForeColor=Color.FromArgb(70,49,94)});
                host.Show();Application.DoEvents();
                IntPtr hostHandle=host.Handle;
                WindowPerch source=new WindowPerch(0,delegate(IntPtr handle){return handle==hostHandle;});
                using(PetWindow pet=new PetWindow(atlas,Path.Combine(folder,"window-test-care.xml"),true,source))
                {
                    try{pet.Show();Application.DoEvents();return pet.VerifyWindowFeatures(host,folder);}
                    finally{pet.Close();}
                }
            }
        }
        internal static void RenderPreview(Form host,Bitmap frame,WindowSupport support,string folder)
        {
            using(Bitmap canvas=new Bitmap(800,520,PixelFormat.Format32bppArgb))
            using(Graphics g=Graphics.FromImage(canvas))
            using(Bitmap window=new Bitmap(host.Width,host.Height))
            using(Font title=new Font("Segoe UI",18,FontStyle.Bold))
            using(Font caption=new Font("Segoe UI",10))
            using(Brush text=new SolidBrush(Color.FromArgb(235,222,251)))
            {
                g.Clear(Color.FromArgb(28,22,40));g.SmoothingMode=SmoothingMode.AntiAlias;
                g.DrawString("A seat on your window",title,text,24,20);
                g.DrawString("She follows the window, and drops when it closes or minimizes.",caption,text,25,58);
                host.DrawToBitmap(window,new Rectangle(0,0,window.Width,window.Height));
                float windowX=(800-host.Width)/2f,windowY=190;
                g.DrawImageUnscaled(window,(int)windowX,(int)windowY);
                float frameX=windowX+(float)support.X-host.Left;
                float frameY=windowY+(float)support.Y-host.Top;
                g.DrawImageUnscaled(frame,(int)frameX,(int)frameY);
                canvas.Save(Path.Combine(folder,"window-perch-preview.png"),ImageFormat.Png);
            }
        }
        private sealed class TestWindow:Form
        {
            protected override bool ShowWithoutActivation {get{return true;}}
        }
    }
}
