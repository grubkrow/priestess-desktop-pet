using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PriestessPet
{
    public static class WindowPerchTests
    {
        private static int checks;
        public static string Run()
        {
            checks = 0;
            Geometry();
            Motion();
            NativeWindows();
            return "PASS: " + checks + " window-perching checks: drop collision, visible top edges, negative coordinates, " +
                "normalized resize/movement, safe detach, stable perched actions, owned native windows, and no focus theft.";
        }

        private static WindowPerch.WindowInfo Window(int id, Rectangle bounds, bool eligible)
        {
            return new WindowPerch.WindowInfo(new IntPtr(id), bounds, eligible, 123, "Test window " + id);
        }

        private static void Geometry()
        {
            Rectangle work = new Rectangle(-1600, -200, 1600, 1100);
            List<WindowPerch.WindowInfo> windows = new List<WindowPerch.WindowInfo>();
            windows.Add(Window(1, new Rectangle(-1300, 300, 600, 400), true));
            WindowSupport support;
            Require(WindowPerch.FindNearest(windows, work, 180, -1100, 700, out support), "nearest restored window");
            Near(support.X, -1100, "nearest preserves horizontal center");
            Near(support.Y + 180 * WindowPerch.SoleRatio, 300, "visible sole rests exactly on top edge");
            Require(support.X < 0, "negative monitor coordinates stay negative");

            WindowSupport landing;
            Require(WindowPerch.FindLanding(windows, work, 180, -1100, 100, 150, out landing), "fall crosses window top");
            Require(!WindowPerch.FindLanding(windows, work, 180, -1100, 200, 220, out landing), "no snap upward through a window");
            Require(!WindowPerch.FindLanding(windows, work, 180, -1100, 150, 100, out landing), "upward motion does not land");
            Require(!WindowPerch.FindLanding(windows, work, 180, -600, 100, 150, out landing), "horizontal miss");
            windows.Add(Window(2, new Rectangle(-1300, 400, 600, 300), true));
            Require(WindowPerch.FindLanding(windows, work, 180, -1100, 0, 400, out landing) && landing.Handle == new IntPtr(1), "large step selects first crossed platform");

            WindowSupport refreshed;
            windows[0].Bounds = new Rectangle(-1250, 350, 900, 400);
            Require(WindowPerch.Refresh(windows, support, work, 180, out refreshed), "host resize and movement");
            Near(refreshed.X + 90, -1250 + 900 * support.HorizontalFraction, "normalized horizontal offset");
            Near(refreshed.Y + 180 * WindowPerch.SoleRatio, 350, "vertical host following");
            windows[0].Bounds = new Rectangle(-2000, 350, 900, 400);
            Require(WindowPerch.Refresh(windows, refreshed, work, 180, out landing), "host moving partly offscreen clamps pet safely");
            Near(landing.HorizontalFraction, support.HorizontalFraction, "screen clamp preserves original host anchor");
            windows[0].Bounds = new Rectangle(-1250, 350, 900, 400);
            Require(WindowPerch.Refresh(windows, landing, work, 180, out landing), "host moving back restores original anchor");
            Near(landing.X, refreshed.X, "anchor does not drift after screen clamping");
            windows.Insert(0, Window(3, new Rectangle(-1600, 250, 1600, 450), false));
            Require(!WindowPerch.Refresh(windows, refreshed, work, 180, out landing), "ineligible front window can cover host");
            Require(!WindowPerch.FindNearest(windows, work, 180, -1000, 100, out landing), "fully covered edges are unavailable");

            windows.RemoveAt(0);
            windows[0].Eligible = false;
            Require(!WindowPerch.Refresh(windows, refreshed, work, 180, out landing), "maximized or otherwise ineligible host detaches");
            windows.RemoveAt(0);
            Require(!WindowPerch.Refresh(windows, refreshed, work, 180, out landing), "closed hidden or minimized host detaches");
            windows.Clear();
            windows.Add(Window(1, new Rectangle(-1300, -100, 600, 400), true));
            Require(!WindowPerch.FindNearest(windows, work, 180, -1000, 0, out landing), "no room above high window");
            windows[0].Bounds = new Rectangle(10, 300, 600, 400);
            Require(!WindowPerch.FindNearest(windows, work, 180, -1000, 0, out landing), "offscreen edge rejected");
            windows[0].Bounds = new Rectangle(-1300, 300, 50, 400);
            Require(!WindowPerch.FindNearest(windows, work, 180, -1000, 0, out landing), "too narrow host rejected");
            windows[0].Bounds = new Rectangle(-1700, 300, 600, 400);
            Require(WindowPerch.FindNearest(windows, work, 180, -1600, 0, out landing), "partially offscreen host remains usable");
            Require(landing.X >= work.Left, "entire pet stays on screen");

            windows.Clear();
            windows.Add(Window(3, new Rectangle(-1250, 200, 300, 400), false));
            windows.Add(Window(1, new Rectangle(-1400, 350, 700, 400), true));
            Require(WindowPerch.FindNearest(windows, work, 180, -1170, 500, out landing), "nearest finds uncovered top segment");
            Require(landing.X + 90 - 180 * .18 >= -950 || landing.X + 90 + 180 * .18 <= -1250, "feet avoid occluder");
            Require(!WindowPerch.FindNearest(windows, work, 0, 0, 0, out landing), "zero size rejected");
            Require(!WindowPerch.FindLanding(windows, work, 180, Double.NaN, 0, 500, out landing), "invalid coordinates rejected");
        }

        private static void Motion()
        {
            PetMotion motion = new PetMotion(new Rectangle(-1600, -200, 1600, 1100), 180, 77);
            motion.AttachPerch(-1200, 200);
            Require(motion.IsPerched && motion.State == PetState.Sit, "attach begins sitting");
            for (int i = 0; i < 2400; i++) motion.Update(.05);
            Near(motion.X, -1200, "perch has no wandering horizontal teleport");
            Near(motion.Y, 200, "perch stays above desktop over several action cycles");
            Require(motion.IsPerched, "perch remains until the host disappears or an action detaches");
            foreach (PetState state in new PetState[] { PetState.Idle, PetState.Sit, PetState.Sleep, PetState.Shrug })
            {
                motion.Command(state);
                motion.Update(.05);
                Require(motion.IsPerched && motion.State == state, "stationary command remains perched: " + state);
                Near(motion.Y, 200, "stationary command preserves support: " + state);
            }
            motion.MovePerch(-1100, 220);
            Near(motion.X, -1100, "move support x");
            Near(motion.Y, 220, "move support y");
            motion.Paused = true;
            motion.MovePerch(-1050, 230);
            Near(motion.Y, 230, "window following works while movement is paused");
            motion.DetachPerch();
            Require(!motion.IsPerched && motion.State == PetState.Fall, "lost support prepares physical fall");
            motion.Paused = false;
            motion.Update(.05);
            Require(motion.Y > 230, "detached pet falls");
            motion.AttachPerch(-1200, 200);
            motion.BeginDrag();
            Require(!motion.IsPerched && motion.State == PetState.Drag, "drag detaches immediately");
            motion.EndDrag();
            Require(motion.State == PetState.Fall, "drop resumes physics");
            motion.AttachPerch(-1200, 200);
            motion.Command(PetState.Walk);
            Require(!motion.IsPerched && motion.State == PetState.Fall, "walk first descends to desktop");
            motion.AttachPerch(-1200, 200);
            motion.Recall();
            Require(!motion.IsPerched, "recall detaches");
            Near(motion.Y, 720, "recall returns to desktop floor");
        }

        private static void NativeWindows()
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            const int size = 96;
            if (work.Width < 500 || work.Height < 420)
                throw new InvalidOperationException("Window perching native tests require a 500 x 420 working area.");
            IntPtr foreground = GetForegroundWindow();
            using (NonActivatingTestForm host = new NonActivatingTestForm())
            using (NonActivatingTestForm cover = new NonActivatingTestForm())
            {
                host.Text = "Priestess window support test";
                host.SetBounds(work.Left + 100, work.Top + 190, 300, 180);
                host.Show();
                PumpDwm();
                IntPtr handle = host.Handle;
                WindowPerch service = new WindowPerch(0, delegate(IntPtr item) { return item == handle || item == cover.Handle; });
                WindowSupport support;
                Require(service.TryFindNearest(work, size, host.Left + 100, host.Top - size, out support) && support.Handle == handle,
                    "native enumeration and DWM frame discovery");
                WindowPerch excluded = new WindowPerch(Process.GetCurrentProcess().Id, delegate(IntPtr item) { return item == handle; });
                WindowSupport result;
                Require(!excluded.TryFindNearest(work, size, 0, 0, out result), "own-process windows excluded");
                host.SetBounds(host.Left + 35, host.Top + 20, 340, 170);
                PumpDwm();
                Require(service.TryRefresh(support, work, size, out result), "native resized host stays attached");
                Near(result.X + size * .5, result.WindowBounds.Left + result.WindowBounds.Width * support.HorizontalFraction, "native normalized resize position");
                Require(result.WindowBounds.Top > support.WindowBounds.Top, "native host move discovered");
                support = result;

                cover.Text = "Priestess occlusion test";
                cover.SetBounds(work.Left + 50, host.Top - 20, 430, 120);
                cover.Show();
                PumpDwm();
                Require(!service.TryRefresh(support, work, size, out result), "native front window hides support");
                cover.Hide();
                host.Hide();
                PumpDwm();
                Require(!service.TryRefresh(support, work, size, out result), "native hidden host safely detaches");
                host.Show();
                PumpDwm();
                ShowWindow(handle, 7); // SW_SHOWMINNOACTIVE: test only our own window.
                PumpDwm();
                Require(!service.TryRefresh(support, work, size, out result), "native minimized host safely detaches");
                host.Close();
                PumpDwm();
                Require(!service.TryRefresh(support, work, size, out result), "native closed host safely detaches");
                Require(GetForegroundWindow() == foreground, "discovery and owned test forms never steal focus");
            }
        }

        private sealed class NonActivatingTestForm : Form
        {
            internal NonActivatingTestForm()
            {
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                BackColor = Color.FromArgb(43, 34, 55);
            }
            protected override bool ShowWithoutActivation { get { return true; } }
            protected override CreateParams CreateParams
            {
                get { CreateParams value = base.CreateParams; value.ExStyle |= 0x08000000; return value; }
            }
        }

        private static void PumpDwm()
        {
            Application.DoEvents();
            try { DwmFlush(); }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            Application.DoEvents();
        }

        private static void Require(bool condition, string name)
        {
            checks++;
            if (!condition) throw new InvalidOperationException("Window perch test failed: " + name + ".");
        }
        private static void Near(double actual, double expected, string name) { Require(Math.Abs(actual - expected) < .01, name); }
        [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr handle, int command);
        [DllImport("dwmapi.dll")] private static extern int DwmFlush();
    }
}
