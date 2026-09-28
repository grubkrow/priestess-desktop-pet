using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PriestessPet
{
    /// <summary>
    /// A bounded native smoke test. It sends no mouse/keyboard input and briefly
    /// shows only two tiny, nonactivating test windows for alpha hit testing.
    /// </summary>
    public static class NativeWindowTests
    {
        public static string Run(SpriteAtlas atlas)
        {
            if (atlas == null)
                throw new ArgumentNullException("atlas");
            Bitmap[] frames = new Bitmap[SpriteAtlas.FrameCount];
            for (int i = 0; i < frames.Length; i++)
                frames[i] = atlas.GetFrame(i, 96, (i & 1) == 1);
            Bitmap large = atlas.GetFrame(0, 180, false);
            int stableGdiCount;
            int loopGdiCount;
            int beforeDisposal;

            using (LayeredPetWindow pet = new LayeredPetWindow())
            {
                pet.Present(frames[0], -250, -180);
                Require(pet.Bounds == new Rectangle(-250, -180, 96, 96), "negative screen coordinates");
                int styles = GetWindowLong(pet.Handle, -20);
                Require((styles & 0x00080000) != 0, "WS_EX_LAYERED");
                Require((styles & 0x00000080) != 0, "WS_EX_TOOLWINDOW");
                Require((styles & 0x08000000) != 0, "WS_EX_NOACTIVATE");
                Require((styles & 0x00000020) == 0, "opaque pixels remain interactive");

                pet.Present(large, -170, -120);
                Require(pet.Bounds == new Rectangle(-170, -120, 180, 180), "native and managed bounds after resizing");
                pet.TopMost = false;
                pet.MovePet(-160, -110);
                Require((GetWindowLong(pet.Handle, -20) & 0x00000008) == 0, "movement honors TopMost=false");
                pet.TopMost = true;
                pet.MovePet(-150, -100);
                Require((GetWindowLong(pet.Handle, -20) & 0x00000008) != 0, "movement honors TopMost=true");

                // Warm up the exact frame/size set before measuring native handles.
                for (int i = 0; i < frames.Length; i++)
                    pet.Present(frames[i], -250, -180);
                stableGdiCount = GdiCount();
                for (int i = 0; i < 2000; i++)
                {
                    Bitmap frame = frames[i % frames.Length];
                    pet.Present(frame, -250 + (i % 40), -180);
                    pet.Present(frame, -249 + (i % 40), -180); // Cached frame path.
                    pet.MovePet(-248 + (i % 40), -179);
                }
                loopGdiCount = GdiCount();
                Require(loopGdiCount == stableGdiCount, "stable GDI handles across 2000 frame/movement cycles");
                pet.InvalidateFrame();
                pet.Present(frames[0], -250, -180);

                VerifyAlphaHitTesting(pet);
                beforeDisposal = GdiCount();
            }

            int afterDisposal = GdiCount();
            Require(afterDisposal <= beforeDisposal - 2, "native bitmap and drawing context released on disposal");
            return "PASS: native alpha click-through and solid-body hit testing; no focus theft; " +
                "negative coordinates; resizing; TopMost toggle; 2000 frame/move cycles with stable GDI handles (" +
                stableGdiCount + " -> " + loopGdiCount + "); native resources released.";
        }

        private static void VerifyAlphaHitTesting(LayeredPetWindow pet)
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            int x = area.Left + Math.Max(0, Math.Min(80, area.Width - 96));
            int y = area.Top + Math.Max(0, Math.Min(80, area.Height - 96));
            using (Bitmap opaque = new Bitmap(96, 96, PixelFormat.Format32bppArgb))
            using (Bitmap cutout = new Bitmap(96, 96, PixelFormat.Format32bppArgb))
            using (LayeredPetWindow backing = new LayeredPetWindow())
            {
                using (Graphics graphics = Graphics.FromImage(opaque))
                    graphics.Clear(Color.FromArgb(255, 38, 33, 49));
                using (Graphics graphics = Graphics.FromImage(cutout))
                {
                    graphics.Clear(Color.Transparent);
                    using (Brush brush = new SolidBrush(Color.FromArgb(255, 116, 74, 155)))
                        graphics.FillRectangle(brush, 24, 24, 48, 48);
                }
                IntPtr foreground = GetForegroundWindow();
                try
                {
                    backing.Present(opaque, x, y);
                    backing.Show();
                    pet.Present(cutout, x, y);
                    pet.Show();
                    Application.DoEvents();
                    Require(GetForegroundWindow() == foreground, "Show and Present do not steal foreground focus");
                    Require(WindowFromPoint(new NativePoint(x + 48, y + 48)) == pet.Handle,
                        "opaque pixels receive mouse hit testing");
                    Require(WindowFromPoint(new NativePoint(x + 8, y + 8)) == backing.Handle,
                        "fully transparent pixels pass mouse hit testing to the window below");
                }
                finally
                {
                    pet.Hide();
                    backing.Hide();
                    pet.InvalidateFrame();
                }
            }
        }

        private static int GdiCount()
        {
            return GetGuiResources(GetCurrentProcess(), 0);
        }

        private static void Require(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException("Native window test failed: " + description + ".");
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;
            internal NativePoint(int x, int y) { X = x; Y = y; }
        }

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(NativePoint point);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [DllImport("user32.dll")]
        private static extern int GetGuiResources(IntPtr process, int flags);
    }
}
