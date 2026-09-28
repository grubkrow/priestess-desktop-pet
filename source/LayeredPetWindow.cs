using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PriestessPet
{
    /// <summary>
    /// An alpha-blended, nonactivating desktop window. Fully transparent pixels
    /// naturally pass mouse input to the desktop; visible pixels receive events.
    /// Call Present before Show to avoid displaying an uninitialized window.
    /// Presented bitmaps remain owned by the caller and should be immutable.
    /// </summary>
    public class LayeredPetWindow : Form
    {
        private const int WsExLayered = 0x00080000;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExNoActivate = 0x08000000;
        private const int WmMouseActivate = 0x0021;
        private const int MaNoActivate = 3;
        private const int WmEraseBackground = 0x0014;
        private const uint UlwAlpha = 0x00000002;
        private const uint SwpNoSize = 0x0001;
        private const uint SwpNoZOrder = 0x0004;
        private const uint SwpNoActivate = 0x0010;
        private readonly int ownerThreadId = Thread.CurrentThread.ManagedThreadId;
        private NativeSurface surface;
        private Bitmap lastFrame;

        public LayeredPetWindow()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            MaximizeBox = false;
            MinimizeBox = false;
            SetStyle(ControlStyles.Selectable, false);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExLayered | WsExToolWindow | WsExNoActivate;
                return parameters;
            }
        }

        /// <summary>
        /// Displays an ARGB/PArgb bitmap at physical screen coordinates, including
        /// negative coordinates. The native surface is reused while dimensions
        /// stay the same; repeated calls with the same bitmap only move the pet.
        /// This method does not change the window's visibility.
        /// </summary>
        public void Present(Bitmap premultipliedOrArgbBitmap, int x, int y)
        {
            VerifyThreadAndLifetime();
            if (premultipliedOrArgbBitmap == null)
                throw new ArgumentNullException("premultipliedOrArgbBitmap");
            PixelFormat format = premultipliedOrArgbBitmap.PixelFormat;
            if (format != PixelFormat.Format32bppArgb && format != PixelFormat.Format32bppPArgb)
                throw new ArgumentException("Pet frames must be 32-bit ARGB or premultiplied ARGB bitmaps.", "premultipliedOrArgbBitmap");

            if (Object.ReferenceEquals(lastFrame, premultipliedOrArgbBitmap) && surface != null)
            {
                MovePet(x, y);
                return;
            }

            int width = premultipliedOrArgbBitmap.Width;
            int height = premultipliedOrArgbBitmap.Height;
            if (surface == null || surface.Width != width || surface.Height != height)
            {
                NativeSurface replacement = new NativeSurface(width, height);
                if (surface != null)
                    surface.Dispose();
                surface = replacement;
            }

            // Forget the cache before copying so that a failed update is retryable.
            lastFrame = null;
            surface.CopyFrom(premultipliedOrArgbBitmap);
            NativePoint destination = new NativePoint(x, y);
            NativePoint source = new NativePoint(0, 0);
            NativeSize size = new NativeSize(width, height);
            BlendFunction blend = new BlendFunction();
            blend.BlendOp = 0; // AC_SRC_OVER
            blend.SourceConstantAlpha = 255;
            blend.AlphaFormat = 1; // AC_SRC_ALPHA, premultiplied color channels
            if (!UpdateLayeredWindow(Handle, IntPtr.Zero, ref destination, ref size,
                surface.Dc, ref source, 0, ref blend, UlwAlpha))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not present the transparent pet window.");
            // UpdateLayeredWindow can change native bounds without WinForms
            // refreshing its cached Left/Top/Width/Height values.
            UpdateBounds();
            lastFrame = premultipliedOrArgbBitmap;
        }

        /// <summary>Moves the existing native surface without copying or allocating a bitmap.</summary>
        public void MovePet(int x, int y)
        {
            VerifyThreadAndLifetime();
            if (!SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, SwpNoSize | SwpNoZOrder | SwpNoActivate))
                throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not move the pet window.");
        }

        /// <summary>Call before Present if an existing bitmap has been edited in place.</summary>
        public void InvalidateFrame()
        {
            VerifyThreadAndLifetime();
            lastFrame = null;
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmMouseActivate)
            {
                message.Result = new IntPtr(MaNoActivate);
                return;
            }
            if (message.Msg == WmEraseBackground)
            {
                message.Result = new IntPtr(1);
                return;
            }
            base.WndProc(ref message);
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            // A recreated HWND needs a full alpha update even if its frame is unchanged.
            lastFrame = null;
            base.OnHandleDestroyed(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lastFrame = null;
                if (surface != null)
                {
                    surface.Dispose();
                    surface = null;
                }
            }
            base.Dispose(disposing);
        }

        private void VerifyThreadAndLifetime()
        {
            if (IsDisposed || Disposing)
                throw new ObjectDisposedException(GetType().Name);
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
                throw new InvalidOperationException("Present and MovePet must run on the window's UI thread.");
        }

        /// <summary>Call once, before creating any controls or reading screen geometry.</summary>
        public static void EnableDpiAwareness()
        {
            try
            {
                if (SetProcessDpiAwarenessContext(new IntPtr(-4))) // PER_MONITOR_AWARE_V2
                    return;
                if (Marshal.GetLastWin32Error() == 5) // A manifest or host already configured DPI.
                    return;
            }
            catch (EntryPointNotFoundException) { }

            try
            {
                int result = SetProcessDpiAwareness(2); // PROCESS_PER_MONITOR_DPI_AWARE
                if (result == 0 || result == unchecked((int)0x80070005))
                    return;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }

            try { SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { }
        }

        private sealed class NativeSurface : IDisposable
        {
            internal readonly int Width;
            internal readonly int Height;
            internal IntPtr Dc;
            private IntPtr bitmap;
            private IntPtr originalBitmap;
            private IntPtr bits;
            private readonly byte[] row;

            internal NativeSurface(int width, int height)
            {
                Width = width;
                Height = height;
                row = new byte[checked(width * 4)];
                try
                {
                    Dc = CreateCompatibleDC(IntPtr.Zero);
                    if (Dc == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not allocate the pet drawing context.");
                    BitmapInfo info = new BitmapInfo();
                    info.Header.Size = (uint)Marshal.SizeOf(typeof(BitmapInfoHeader));
                    info.Header.Width = width;
                    info.Header.Height = -height; // Top-down BGRA rows.
                    info.Header.Planes = 1;
                    info.Header.BitCount = 32;
                    info.Header.Compression = 0; // BI_RGB
                    info.Header.SizeImage = checked((uint)(row.Length * height));
                    bitmap = CreateDIBSection(Dc, ref info, 0, out bits, IntPtr.Zero, 0);
                    if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not allocate the transparent pet bitmap.");
                    originalBitmap = SelectObject(Dc, bitmap);
                    if (originalBitmap == IntPtr.Zero || originalBitmap == new IntPtr(-1))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "Could not select the pet bitmap.");
                }
                catch
                {
                    Dispose();
                    throw;
                }
            }

            ~NativeSurface()
            {
                Dispose();
            }

            internal void CopyFrom(Bitmap source)
            {
                BitmapData data = source.LockBits(new Rectangle(0, 0, Width, Height),
                    ImageLockMode.ReadOnly, source.PixelFormat);
                try
                {
                    bool premultiplied = source.PixelFormat == PixelFormat.Format32bppPArgb;
                    for (int y = 0; y < Height; ++y)
                    {
                        Marshal.Copy(IntPtr.Add(data.Scan0, checked(y * data.Stride)), row, 0, row.Length);
                        if (!premultiplied)
                        {
                            for (int p = 0; p < row.Length; p += 4)
                            {
                                int alpha = row[p + 3];
                                row[p] = (byte)((row[p] * alpha + 127) / 255);
                                row[p + 1] = (byte)((row[p + 1] * alpha + 127) / 255);
                                row[p + 2] = (byte)((row[p + 2] * alpha + 127) / 255);
                            }
                        }
                        Marshal.Copy(row, 0, IntPtr.Add(bits, checked(y * row.Length)), row.Length);
                    }
                }
                finally
                {
                    source.UnlockBits(data);
                }
            }

            public void Dispose()
            {
                if (Dc != IntPtr.Zero && originalBitmap != IntPtr.Zero && originalBitmap != new IntPtr(-1))
                    SelectObject(Dc, originalBitmap);
                originalBitmap = IntPtr.Zero;
                if (bitmap != IntPtr.Zero)
                {
                    DeleteObject(bitmap);
                    bitmap = IntPtr.Zero;
                }
                if (Dc != IntPtr.Zero)
                {
                    DeleteDC(Dc);
                    Dc = IntPtr.Zero;
                }
                bits = IntPtr.Zero;
                GC.SuppressFinalize(this);
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;
            internal NativePoint(int x, int y) { X = x; Y = y; }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeSize
        {
            internal int Width;
            internal int Height;
            internal NativeSize(int width, int height) { Width = width; Height = height; }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BlendFunction
        {
            internal byte BlendOp;
            internal byte BlendFlags;
            internal byte SourceConstantAlpha;
            internal byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            internal uint Size;
            internal int Width;
            internal int Height;
            internal ushort Planes;
            internal ushort BitCount;
            internal uint Compression;
            internal uint SizeImage;
            internal int XPelsPerMeter;
            internal int YPelsPerMeter;
            internal uint ClrUsed;
            internal uint ClrImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfo
        {
            internal BitmapInfoHeader Header;
            internal uint Colors;
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc,
            ref NativePoint destination, ref NativeSize size, IntPtr sourceDc,
            ref NativePoint source, uint colorKey, ref BlendFunction blend, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr dc);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfo info,
            uint usage, out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr value);

        [DllImport("gdi32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr context);

        [DllImport("shcore.dll")]
        private static extern int SetProcessDpiAwareness(int awareness);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();
    }
}
