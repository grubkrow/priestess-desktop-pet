using System;
using System.Drawing;

namespace PriestessPet
{
    public enum PetState { Idle, Walk, Sit, Sleep, Climb, Hang, Fall, Land, Shrug, Drag }

    // Pure motion model: coordinates refer to the square sprite canvas.
    public sealed class PetMotion
    {
        private readonly Random random;
        private int preferredSize;
        private double stateAge;
        private double remaining;
        private double velocityY;
        private double driftX;
        private double speedMultiplier = 1.0;
        private bool approachingWall;
        private PetState afterLanding = PetState.Idle;

        public double X { get; private set; }
        public double Y { get; private set; }
        public int Size { get; private set; }
        public Rectangle Bounds { get; private set; }
        public PetState State { get; private set; }
        public bool FacingLeft { get; private set; }
        public bool Paused { get; set; }
        public bool IsPerched { get; private set; }
        // Scaled time within the current action; renderers can use it for gentle
        // breathing or landing easing without advancing animation while paused.
        public double AnimationAge { get { return stateAge; } }
        public double SpeedMultiplier
        {
            get { return speedMultiplier; }
            set { speedMultiplier = IsFinite(value) ? Clamp(value, 0.25, 3.0) : 1.0; }
        }

        public int Frame
        {
            get
            {
                switch (State)
                {
                    case PetState.Idle:
                        double blinkAge = stateAge % 3.4;
                        if (blinkAge < 3.16) return 28;
                        if (blinkAge < 3.24) return 29;
                        if (blinkAge < 3.32) return 30;
                        return 31;
                    case PetState.Walk: return 12 + ((int)(stateAge / 0.10) % 8);
                    case PetState.Sit: return 4;
                    case PetState.Sleep: return 5;
                    case PetState.Climb: return 20 + ((int)(stateAge / 0.12) % 8);
                    case PetState.Hang: return 8;
                    case PetState.Fall:
                    case PetState.Drag: return 9;
                    case PetState.Land: return 10;
                    case PetState.Shrug: return 11;
                    default: return 28;
                }
            }
        }

        private double Left { get { return Bounds.Left; } }
        private double Right { get { return (double)Bounds.Left + Bounds.Width - Size; } }
        private double Ceiling { get { return Bounds.Top; } }
        private double Floor { get { return (double)Bounds.Top + Bounds.Height - Size; } }
        private bool OnFloor { get { return Y >= Floor - 0.01; } }
        private double Scale { get { return Size / 180.0; } }

        public PetMotion(Rectangle bounds, int size, int seed)
        {
            random = new Random(seed);
            preferredSize = Math.Max(1, size);
            Bounds = Normalize(bounds);
            Size = FitSize(preferredSize);
            FacingLeft = false;
            Recall();
        }

        public void SetBounds(Rectangle bounds)
        {
            bool wasGrounded = OnFloor;
            bool wasHanging = State == PetState.Hang;
            Bounds = Normalize(bounds);
            Size = FitSize(preferredSize);
            if (wasGrounded) Y = Floor;
            if (wasHanging) Y = Ceiling;
            Constrain();
            if (State == PetState.Climb) X = FacingLeft ? Left : Right;
        }

        public void SetSize(int size)
        {
            bool wasGrounded = OnFloor;
            bool wasHanging = State == PetState.Hang;
            double centerX = X + Size * 0.5;
            double feetY = Y + Size;
            preferredSize = Math.Max(1, size);
            Size = FitSize(preferredSize);
            X = centerX - Size * 0.5;
            Y = wasGrounded ? Floor : feetY - Size;
            if (wasHanging) Y = Ceiling;
            Constrain();
            if (State == PetState.Climb) X = FacingLeft ? Left : Right;
        }

        public void Recall()
        {
            IsPerched = false;
            X = Left + (Right - Left) * 0.5;
            Y = Floor;
            velocityY = 0;
            driftX = 0;
            approachingWall = false;
            afterLanding = PetState.Idle;
            Enter(PetState.Idle);
        }

        public void BeginDrag()
        {
            IsPerched = false;
            approachingWall = false;
            afterLanding = PetState.Idle;
            velocityY = 0;
            driftX = 0;
            Enter(PetState.Drag);
        }

        public void DragTo(double x, double y)
        {
            if (State != PetState.Drag) return;
            if (IsFinite(x)) X = x;
            if (IsFinite(y)) Y = y;
            Constrain();
        }

        public void EndDrag()
        {
            if (State != PetState.Drag) return;
            if (OnFloor) Enter(PetState.Land);
            else StartFall(PetState.Idle, 0);
        }

        public void Command(PetState state)
        {
            if (!Enum.IsDefined(typeof(PetState), state)) return;
            if (state == PetState.Drag) { BeginDrag(); return; }
            if (IsPerched)
            {
                if (state == PetState.Idle || state == PetState.Sit ||
                    state == PetState.Sleep || state == PetState.Shrug)
                {
                    Enter(state);
                    return;
                }
                IsPerched = false;
            }
            approachingWall = false;
            afterLanding = PetState.Idle;
            if (state == PetState.Fall)
            {
                StartFall(PetState.Idle, 0);
                return;
            }
            // Ground actions wait for a physical landing instead of teleporting.
            if (!OnFloor)
            {
                StartFall(state == PetState.Land ? PetState.Idle : state, 0);
                return;
            }
            if (state == PetState.Climb || state == PetState.Hang)
                ApproachWall();
            else Enter(state);
        }

        public void Update(double dt)
        {
            if (Paused || State == PetState.Drag || !IsFinite(dt) || dt <= 0) return;
            // A suspended UI must never advance through minutes of animation at once.
            double left = Math.Min(dt, 0.1) * SpeedMultiplier;
            while (left > 0.0000001)
            {
                double step = Math.Min(left, 0.02);
                Tick(step);
                left -= step;
            }
            Constrain();
        }

        // The desktop window owner validates the host and supplies a canvas
        // position. Keeping host discovery outside this class preserves a pure
        // motion model and never changes another application's window.
        public void AttachPerch(double x, double y)
        {
            if (!IsFinite(x) || !IsFinite(y)) return;
            IsPerched = true;
            X = x;
            Y = y;
            velocityY = driftX = 0;
            approachingWall = false;
            afterLanding = PetState.Idle;
            Constrain();
            Enter(PetState.Sit);
            remaining = Between(20, 45);
        }

        public void MovePerch(double x, double y)
        {
            if (!IsPerched || !IsFinite(x) || !IsFinite(y)) return;
            X = x;
            Y = y;
            Constrain();
        }

        public void DetachPerch()
        {
            if (!IsPerched) return;
            IsPerched = false;
            if (OnFloor) Enter(PetState.Idle);
            else StartFall(PetState.Idle, 0);
        }

        private void Tick(double dt)
        {
            stateAge += dt;
            remaining -= dt;
            if (IsPerched)
            {
                if (remaining <= 0)
                {
                    if (State == PetState.Idle)
                    {
                        Enter(random.NextDouble() < 0.8 ? PetState.Sit : PetState.Sleep);
                        remaining = Between(20, 45);
                    }
                    else Enter(PetState.Idle);
                }
                Constrain();
                return;
            }
            switch (State)
            {
                case PetState.Walk:
                    Y = Floor;
                    X += (FacingLeft ? -1 : 1) * Math.Max(12, 52 * Scale) * dt;
                    if ((FacingLeft && X <= Left) || (!FacingLeft && X >= Right))
                    {
                        X = FacingLeft ? Left : Right;
                        if (approachingWall || random.NextDouble() < 0.6)
                        {
                            approachingWall = false;
                            Enter(PetState.Climb);
                        }
                        else FacingLeft = !FacingLeft;
                    }
                    if (State == PetState.Walk && !approachingWall && remaining <= 0)
                        Enter(PetState.Idle);
                    break;
                case PetState.Climb:
                    X = FacingLeft ? Left : Right;
                    Y -= Math.Max(12, 62 * Scale) * dt;
                    if (Y <= Ceiling)
                    {
                        Y = Ceiling;
                        Enter(PetState.Hang);
                    }
                    break;
                case PetState.Hang:
                    Y = Ceiling;
                    if (remaining <= 0) StartFall(PetState.Idle, FacingLeft ? 20 * Scale : -20 * Scale);
                    break;
                case PetState.Fall:
                    velocityY += Math.Max(150, 1100 * Scale) * dt;
                    X += driftX * dt;
                    Y += velocityY * dt;
                    if (Y >= Floor)
                    {
                        Y = Floor;
                        velocityY = 0;
                        driftX = 0;
                        Enter(PetState.Land);
                    }
                    break;
                case PetState.Land:
                    Y = Floor;
                    if (remaining <= 0)
                    {
                        PetState next = afterLanding;
                        afterLanding = PetState.Idle;
                        if (next == PetState.Climb || next == PetState.Hang) ApproachWall();
                        else Enter(next);
                    }
                    break;
                case PetState.Idle:
                    Y = Floor;
                    if (remaining <= 0) ChooseNextAction();
                    break;
                case PetState.Sit:
                case PetState.Sleep:
                case PetState.Shrug:
                    Y = Floor;
                    if (remaining <= 0) Enter(PetState.Idle);
                    break;
            }
            Constrain();
        }

        private void ApproachWall()
        {
            FacingLeft = X - Left <= Right - X;
            approachingWall = true;
            Enter(PetState.Walk);
        }

        private void StartFall(PetState next, double drift)
        {
            approachingWall = false;
            afterLanding = next;
            velocityY = 0;
            driftX = drift;
            Enter(PetState.Fall);
        }

        private void ChooseNextAction()
        {
            double choice = random.NextDouble();
            if (choice < 0.52)
            {
                FacingLeft = random.Next(2) == 0;
                Enter(PetState.Walk);
            }
            else if (choice < 0.74) Enter(PetState.Sit);
            else if (choice < 0.88) Enter(PetState.Shrug);
            else Enter(PetState.Sleep);
        }

        private void Enter(PetState state)
        {
            State = state;
            stateAge = 0;
            switch (state)
            {
                // Leave time for one complete blink before another action.
                case PetState.Idle: remaining = Between(3.5, 5.8); break;
                case PetState.Walk: remaining = Between(3, 9); break;
                case PetState.Sit: remaining = Between(3, 7); break;
                case PetState.Sleep: remaining = Between(18, 40); break;
                case PetState.Hang: remaining = Between(1.5, 4); break;
                case PetState.Land: remaining = 0.35; break;
                case PetState.Shrug: remaining = 1.6; break;
                default: remaining = Double.PositiveInfinity; break;
            }
        }

        private double Between(double min, double max) { return min + random.NextDouble() * (max - min); }
        private int FitSize(int requested) { return Math.Min(requested, Math.Min(Bounds.Width, Bounds.Height)); }
        private static Rectangle Normalize(Rectangle bounds)
        {
            return new Rectangle(bounds.X, bounds.Y, Math.Max(1, bounds.Width), Math.Max(1, bounds.Height));
        }
        private void Constrain()
        {
            X = Clamp(X, Left, Right);
            Y = Clamp(Y, Ceiling, Floor);
        }
        private static bool IsFinite(double value) { return !Double.IsNaN(value) && !Double.IsInfinity(value); }
        private static double Clamp(double value, double min, double max) { return Math.Max(min, Math.Min(max, value)); }
    }
}
