using System;
using System.Drawing;

namespace PriestessPet
{
    public static class MotionTests
    {
        public static string Run()
        {
            int assertions = 0;
            PetMotion pet = new PetMotion(new Rectangle(-1600, -900, 1600, 900), 180, 42);
            Check(pet.X < 0 && pet.Y < 0, "Negative monitor coordinates are preserved", ref assertions);
            Contained(pet, ref assertions);

            pet.BeginDrag();
            pet.DragTo(-900, -800);
            double start = pet.Y;
            pet.EndDrag();
            Check(pet.State == PetState.Fall, "Release above floor starts falling", ref assertions);
            pet.Update(0.05);
            Check(pet.Y > start, "Gravity moves downward", ref assertions);
            bool landed = false;
            for (int i = 0; i < 200; i++)
            {
                pet.Update(0.02);
                if (pet.State == PetState.Land) { landed = true; break; }
            }
            Check(landed && Math.Abs(pet.Y - (pet.Bounds.Bottom - pet.Size)) < 0.001,
                "Fall lands exactly on floor", ref assertions);

            pet.Recall();
            pet.Command(PetState.Walk);
            pet.Paused = true;
            double x = pet.X;
            double y = pet.Y;
            int frame = pet.Frame;
            double animationAge = pet.AnimationAge;
            pet.Update(2);
            Check(pet.X == x && pet.Y == y && pet.Frame == frame && pet.AnimationAge == animationAge,
                "Pause freezes animation and motion", ref assertions);
            pet.BeginDrag();
            pet.DragTo(-99999, -99999);
            Check(pet.X == pet.Bounds.Left && pet.Y == pet.Bounds.Top, "Paused dragging works and clamps", ref assertions);
            pet.EndDrag();
            y = pet.Y;
            pet.Update(0.1);
            Check(pet.Y == y, "Paused release waits for resume", ref assertions);
            pet.Paused = false;
            pet.Update(0.1);
            Check(pet.Y > y, "Resume continues falling", ref assertions);

            pet.Recall();
            pet.SetSize(300);
            Check(pet.Size == 300 && pet.Y == pet.Bounds.Bottom - 300, "Resize preserves floor contact", ref assertions);
            pet.SetBounds(new Rectangle(-40, -25, 60, 40));
            Check(pet.Size == 40, "Small monitor limits sprite size", ref assertions);
            Contained(pet, ref assertions);
            pet.SetBounds(new Rectangle(100, 100, 1000, 700));
            Check(pet.Size == 300, "Returning to large monitor restores preferred size", ref assertions);
            Contained(pet, ref assertions);
            pet.SetBounds(Rectangle.Empty);
            Check(pet.Size == 1 && pet.Bounds.Width == 1 && pet.Bounds.Height == 1,
                "Empty bounds remain usable", ref assertions);
            for (int i = 0; i < 1000; i++) { pet.Update(0.1); Contained(pet, ref assertions); }

            pet = new PetMotion(new Rectangle(-200, -100, 480, 400), 120, 17);
            pet.Command(PetState.Climb);
            bool climbed = false;
            bool hung = false;
            for (int i = 0; i < 800; i++)
            {
                pet.Update(0.05);
                if (pet.State == PetState.Climb) climbed = true;
                if (pet.State == PetState.Hang) { hung = true; break; }
                Contained(pet, ref assertions);
            }
            Check(climbed && hung, "Climb command reaches wall and ceiling", ref assertions);
            Check(pet.Y == pet.Bounds.Top, "Hanging attaches at ceiling", ref assertions);
            pet.Command(PetState.Sleep);
            Check(pet.State == PetState.Fall, "Airborne sleep waits for landing", ref assertions);
            for (int i = 0; i < 200 && pet.State != PetState.Sleep; i++) pet.Update(0.05);
            Check(pet.State == PetState.Sleep && pet.Frame == 5, "Queued sleep begins after landing", ref assertions);
            pet.Command(PetState.Shrug);
            Check(pet.State == PetState.Shrug && pet.Frame == 11, "Command interrupts sleep", ref assertions);
            foreach (PetState state in Enum.GetValues(typeof(PetState)))
            {
                pet.Recall();
                pet.Command(state);
                for (int i = 0; i < 120; i++) { pet.Update(0.02); Contained(pet, ref assertions); }
            }

            AnimationCycles(ref assertions);

            pet = new PetMotion(new Rectangle(0, 0, 4000, 4000), 180, 24);
            Check(pet.Frame == 28, "Idle begins with open eyes", ref assertions);
            Advance(pet, 3.14);
            Check(pet.State == PetState.Idle && pet.Frame == 28, "Idle holds open eyes between blinks", ref assertions);
            Advance(pet, 0.06);
            Check(pet.Frame == 29, "Blink closes through half-open eyes", ref assertions);
            animationAge = pet.AnimationAge;
            pet.Paused = true;
            Advance(pet, 0.5);
            Check(pet.Frame == 29 && pet.AnimationAge == animationAge, "Pause preserves a blink in progress", ref assertions);
            pet.Paused = false;
            Advance(pet, 0.08);
            Check(pet.Frame == 30, "Blink reaches closed eyes", ref assertions);
            Advance(pet, 0.08);
            Check(pet.Frame == 31, "Blink reopens through its recovery frame", ref assertions);
            Advance(pet, 0.06);
            Check(pet.State == PetState.Idle && pet.Frame == 28, "Blink returns to open eyes after 0.24 seconds", ref assertions);
            pet.Command(PetState.Land);
            Check(pet.AnimationAge == 0 && pet.Frame == 10, "Landing resets its easing clock", ref assertions);
            Advance(pet, 0.30);
            Check(pet.State == PetState.Land, "Landing holds for a visible recovery", ref assertions);
            Advance(pet, 0.06);
            Check(pet.State == PetState.Idle && pet.Frame == 28, "Landing returns smoothly to idle timing", ref assertions);

            PetMotion a = new PetMotion(new Rectangle(-500, -300, 800, 600), 180, 8);
            PetMotion b = new PetMotion(a.Bounds, a.Size, 8);
            for (int i = 0; i < 50000; i++)
            {
                if (i % 913 == 0)
                {
                    int size = 30 + (i % 650);
                    a.SetSize(size); b.SetSize(size);
                }
                if (i % 1511 == 0)
                {
                    Rectangle bounds = i % 2 == 0 ? new Rectangle(-1500, -50, 240, 220) : new Rectangle(10, 20, 1100, 800);
                    a.SetBounds(bounds); b.SetBounds(bounds);
                }
                a.Update(0.05); b.Update(0.05);
                Contained(a, ref assertions);
                Check(a.X == b.X && a.Y == b.Y && a.State == b.State && a.Frame == b.Frame && a.AnimationAge == b.AnimationAge,
                    "Fixed seed produces deterministic simulation", ref assertions);
            }

            pet = new PetMotion(new Rectangle(0, 0, 800, 600), 180, 1);
            pet.Command(PetState.Walk);
            x = pet.X;
            pet.Update(9999);
            Check(Math.Abs(pet.X - x) < 20, "Long frame delta is clamped", ref assertions);
            x = pet.X; y = pet.Y;
            pet.Update(Double.NaN); pet.Update(Double.PositiveInfinity); pet.Update(-5);
            Check(pet.X == x && pet.Y == y, "Invalid deltas are ignored", ref assertions);
            pet.SpeedMultiplier = Double.NaN;
            Check(pet.SpeedMultiplier == 1, "Invalid speed uses default", ref assertions);
            pet.SpeedMultiplier = 999;
            Check(pet.SpeedMultiplier == 3, "Speed is bounded", ref assertions);
            pet.BeginDrag();
            pet.DragTo(Double.NaN, Double.PositiveInfinity);
            Contained(pet, ref assertions);
            return "Motion tests passed (" + assertions + " assertions).";
        }

        private static void Contained(PetMotion pet, ref int assertions)
        {
            Check(!Double.IsNaN(pet.X) && !Double.IsNaN(pet.Y) &&
                pet.Size >= 1 && pet.Size <= pet.Bounds.Width && pet.Size <= pet.Bounds.Height &&
                pet.X >= pet.Bounds.Left - 0.001 && pet.Y >= pet.Bounds.Top - 0.001 &&
                pet.X + pet.Size <= (double)pet.Bounds.Left + pet.Bounds.Width + 0.001 &&
                pet.Y + pet.Size <= (double)pet.Bounds.Top + pet.Bounds.Height + 0.001,
                "Pet stays inside working area", ref assertions);
            Check(pet.Frame >= 0 && pet.Frame <= 31, "Frame stays inside sprite sheet", ref assertions);
        }

        private static void AnimationCycles(ref int assertions)
        {
            double[] speeds = { 0.25, 0.65, 1.0, 1.6, 3.0 };
            foreach (double speed in speeds)
            {
                VerifyCycle(PetState.Walk, 12, 0.10, speed, ref assertions);
                VerifyCycle(PetState.Climb, 20, 0.12, speed, ref assertions);
            }
        }

        private static void VerifyCycle(PetState state, int firstFrame, double frameDuration, double speed, ref int assertions)
        {
            PetMotion pet = new PetMotion(new Rectangle(0, 0, 4000, 4000), 180, 11);
            if (state == PetState.Climb)
            {
                // Start beside the wall so the test covers climbing, not approach time.
                pet.BeginDrag();
                pet.DragTo(pet.Bounds.Left, pet.Bounds.Bottom - pet.Size);
                pet.EndDrag();
                pet.Command(PetState.Climb);
                pet.Update(0.001);
            }
            else pet.Command(state);
            pet.SpeedMultiplier = speed;
            Check(pet.State == state && pet.Frame == firstFrame && pet.AnimationAge == 0,
                "New action starts at the first animation frame", ref assertions);
            int visited = 0;
            Advance(pet, frameDuration * 0.5 / speed);
            for (int i = 0; i <= 16; i++)
            {
                Check(pet.State == state && pet.Frame == firstFrame + i % 8,
                    "Eight-frame " + state + " cycle preserves order at speed " + speed, ref assertions);
                Check(Math.Abs(pet.AnimationAge - frameDuration * (i + 0.5)) < 0.000001,
                    "Animation clock scales with movement speed", ref assertions);
                visited |= 1 << (pet.Frame - firstFrame);
                if (i < 16) Advance(pet, frameDuration / speed);
            }
            Check(visited == 255, "Every new " + state + " frame is displayed", ref assertions);
        }

        private static void Advance(PetMotion pet, double seconds)
        {
            // Stay below the suspended-UI clamp, even at the slowest pet speed.
            while (seconds > 0.00000001)
            {
                double step = Math.Min(seconds, 0.01);
                pet.Update(step);
                seconds -= step;
            }
        }

        private static void Check(bool condition, string message, ref int assertions)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Motion test failed: " + message);
        }
    }
}
