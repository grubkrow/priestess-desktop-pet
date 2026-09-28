using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace PriestessPet
{
    public sealed class WindowSupport
    {
        public IntPtr Handle { get; private set; }
        public Rectangle WindowBounds { get; private set; }
        public double HorizontalFraction { get; private set; }
        public double X { get; private set; }
        public double Y { get; private set; }
        public string Title { get; private set; }
        internal uint ProcessId { get; private set; }
        internal WindowSupport(WindowPerch.WindowInfo window, double center, double y, int size, double? anchorFraction = null)
        {
            Handle = window.Handle;
            WindowBounds = window.Bounds;
            ProcessId = window.ProcessId;
            HorizontalFraction = anchorFraction ?? ((center - window.Bounds.Left) / window.Bounds.Width);
            X = center - size * 0.5;
            Y = y;
            Title = window.Title;
        }
    }

    // Read-only window discovery. No activation, movement, resizing, subclassing,
    // or cross-process messages are needed to follow another application's frame.
    public sealed class WindowPerch
    {
        public const double SoleRatio = 380.0 / 384.0;
        private readonly uint excludedProcess;
        private readonly Predicate<IntPtr> windowFilter;

        public WindowPerch() : this(Process.GetCurrentProcess().Id) { }
        public WindowPerch(int excludedProcessId) : this(excludedProcessId, null) { }
        internal WindowPerch(int excludedProcessId, Predicate<IntPtr> filter)
        {
            excludedProcess = (uint)Math.Max(0, excludedProcessId);
            windowFilter = filter;
        }

        public bool TryFindNearest(Rectangle workArea, int size, double petX, double petY, out WindowSupport support)
        {
            return FindNearest(Snapshot(), workArea, size, petX, petY, out support);
        }

        public bool TryFindLanding(Rectangle workArea, int size, double petX,
            double previousY, double currentY, out WindowSupport support)
        {
            return FindLanding(Snapshot(), workArea, size, petX, previousY, currentY, out support);
        }

        public bool TryRefresh(WindowSupport prior, Rectangle workArea, int size, out WindowSupport support)
        {
            return Refresh(Snapshot(), prior, workArea, size, out support);
        }

        internal sealed class WindowInfo
        {
            internal IntPtr Handle;
            internal Rectangle Bounds;
            internal uint ProcessId;
            internal string Title;
            internal bool Eligible;
            internal WindowInfo(IntPtr handle, Rectangle bounds, bool eligible, uint processId, string title)
            {
                Handle = handle; Bounds = bounds; Eligible = eligible;
                ProcessId = processId; Title = title ?? "Window";
            }
        }

        // EnumWindows supplies top-to-bottom Z order. Ineligible app windows can
        // still cover another window's edge, so they remain in the snapshot.
        private List<WindowInfo> Snapshot()
        {
            List<WindowInfo> windows = new List<WindowInfo>();
            EnumWindows(delegate(IntPtr handle, IntPtr unused)
            {
                if (windowFilter != null && !windowFilter(handle)) return true;
                uint processId;
                GetWindowThreadProcessId(handle, out processId);
                if (processId == excludedProcess || !IsWindowVisible(handle) || IsIconic(handle)) return true;
                uint styles = unchecked((uint)GetWindowLong(handle, -20));
                if ((styles & 0x00000020) != 0 || IsCloaked(handle)) return true;
                StringBuilder className = new StringBuilder(256);
                GetClassName(handle, className, className.Capacity);
                string name = className.ToString();
                if (name == "Progman" || name == "WorkerW" || name == "Shell_TrayWnd" ||
                    name == "Shell_SecondaryTrayWnd" || name == "tooltips_class32") return true;
                Rectangle bounds;
                if (!TryBounds(handle, out bounds) || bounds.Width <= 0 || bounds.Height <= 0) return true;
                StringBuilder title = new StringBuilder(256);
                GetWindowText(handle, title, title.Capacity);
                bool eligible = (styles & 0x00000080) == 0 && !IsZoomed(handle);
                windows.Add(new WindowInfo(handle, bounds, eligible, processId,
                    title.Length == 0 ? "Window" : title.ToString()));
                return true;
            }, IntPtr.Zero);
            return windows;
        }

        internal static bool FindNearest(IList<WindowInfo> windows, Rectangle work, int size,
            double petX, double petY, out WindowSupport support)
        {
            support = null;
            if (!ValidInput(work, size, petX, petY)) return false;
            double best = Double.PositiveInfinity;
            for (int i = 0; i < windows.Count; i++)
            {
                double low, high, y;
                if (!PlacementRange(windows[i], work, size, out low, out high, out y)) continue;
                double wanted = petX + size * 0.5;
                List<double> choices = new List<double>();
                choices.Add(Clamp(wanted, low, high));
                choices.Add(low); choices.Add(high);
                double halfFeet = FeetHalfWidth(size);
                // A partly obscured top edge may still have a usable segment.
                for (int j = 0; j < i; j++)
                {
                    Rectangle cover = windows[j].Bounds;
                    if (!CoversTop(cover, windows[i].Bounds.Top)) continue;
                    choices.Add(Clamp(cover.Left - halfFeet - 2, low, high));
                    choices.Add(Clamp(cover.Right + halfFeet + 2, low, high));
                }
                foreach (double center in choices)
                {
                    if (Covered(windows, i, center, size)) continue;
                    double dx = center - wanted;
                    double dy = y - petY;
                    double distance = dx * dx + dy * dy;
                    if (distance >= best) continue;
                    best = distance;
                    support = new WindowSupport(windows[i], center, y, size);
                }
            }
            return support != null;
        }

        internal static bool FindLanding(IList<WindowInfo> windows, Rectangle work, int size,
            double petX, double previousY, double currentY, out WindowSupport support)
        {
            support = null;
            if (!ValidInput(work, size, petX, previousY) || !Finite(currentY) || currentY < previousY) return false;
            double before = previousY + size * SoleRatio;
            double after = currentY + size * SoleRatio;
            double center = petX + size * 0.5;
            int bestTop = Int32.MaxValue;
            for (int i = 0; i < windows.Count; i++)
            {
                double low, high, y;
                WindowInfo window = windows[i];
                if (!PlacementRange(window, work, size, out low, out high, out y)) continue;
                // A tiny tolerance catches a release directly on a title-bar edge.
                if (before > window.Bounds.Top + 2 || after < window.Bounds.Top - 1 ||
                    center < low || center > high || window.Bounds.Top >= bestTop || Covered(windows, i, center, size)) continue;
                bestTop = window.Bounds.Top;
                support = new WindowSupport(window, center, y, size);
            }
            return support != null;
        }

        internal static bool Refresh(IList<WindowInfo> windows, WindowSupport prior,
            Rectangle work, int size, out WindowSupport support)
        {
            support = null;
            if (prior == null || !ValidInput(work, size, prior.X, prior.Y)) return false;
            for (int i = 0; i < windows.Count; i++)
            {
                WindowInfo window = windows[i];
                if (window.Handle != prior.Handle || window.ProcessId != prior.ProcessId) continue;
                double low, high, y;
                if (!PlacementRange(window, work, size, out low, out high, out y)) return false;
                double center = Clamp(window.Bounds.Left + window.Bounds.Width * prior.HorizontalFraction, low, high);
                if (Covered(windows, i, center, size)) return false;
                support = new WindowSupport(window, center, y, size, prior.HorizontalFraction);
                return true;
            }
            return false;
        }

        private static bool PlacementRange(WindowInfo window, Rectangle work, int size,
            out double low, out double high, out double y)
        {
            low = high = y = 0;
            Rectangle bounds = window.Bounds;
            if (!window.Eligible || bounds.Width < Math.Max(60, size * 0.36 + 8) || bounds.Height < 40) return false;
            y = bounds.Top - size * SoleRatio;
            if (y < work.Top || y + size > work.Bottom) return false;
            double halfFeet = FeetHalfWidth(size);
            low = Math.Max(bounds.Left + halfFeet + 4, work.Left + size * 0.5);
            high = Math.Min(bounds.Right - halfFeet - 4, work.Right - size * 0.5);
            return low <= high;
        }

        private static bool Covered(IList<WindowInfo> windows, int hostIndex, double center, int size)
        {
            double halfFeet = FeetHalfWidth(size);
            int top = windows[hostIndex].Bounds.Top;
            for (int i = 0; i < hostIndex; i++)
            {
                Rectangle cover = windows[i].Bounds;
                if (CoversTop(cover, top) && cover.Left < center + halfFeet && cover.Right > center - halfFeet)
                    return true;
            }
            return false;
        }

        private static bool CoversTop(Rectangle cover, int top) { return cover.Top <= top + 2 && cover.Bottom > top; }
        private static double FeetHalfWidth(int size) { return Math.Max(5, size * 0.18); }
        private static bool ValidInput(Rectangle work, int size, double x, double y)
        {
            return size > 0 && work.Width >= size && work.Height >= size && Finite(x) && Finite(y);
        }
        private static bool Finite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static double Clamp(double value, double low, double high) { return Math.Max(low, Math.Min(high, value)); }

        private static bool TryBounds(IntPtr handle, out Rectangle bounds)
        {
            NativeRect rect;
            try
            {
                if (DwmGetWindowRect(handle, 9, out rect, Marshal.SizeOf(typeof(NativeRect))) == 0)
                {
                    bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                    return bounds.Width > 0 && bounds.Height > 0;
                }
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            if (GetWindowRect(handle, out rect))
            {
                bounds = Rectangle.FromLTRB(rect.Left, rect.Top, rect.Right, rect.Bottom);
                return true;
            }
            bounds = Rectangle.Empty;
            return false;
        }

        private static bool IsCloaked(IntPtr handle)
        {
            try
            {
                int cloaked;
                return DwmGetWindowInt(handle, 14, out cloaked, sizeof(int)) == 0 && cloaked != 0;
            }
            catch (DllNotFoundException) { return false; }
            catch (EntryPointNotFoundException) { return false; }
        }

        private delegate bool EnumWindowCallback(IntPtr handle, IntPtr parameter);
        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect { internal int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr handle);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, StringBuilder text, int count);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr handle, StringBuilder text, int count);
        [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr handle, int index);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr handle, out NativeRect rect);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowRect(IntPtr handle, int attribute, out NativeRect value, int size);
        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")] private static extern int DwmGetWindowInt(IntPtr handle, int attribute, out int value, int size);
    }
}
