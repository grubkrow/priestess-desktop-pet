using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace PriestessPet
{
    // A regular, keyboard-accessible companion window. The atlas owns its portrait.
    public sealed class CarePanel : Form
    {
        private static readonly Color Ink = Color.FromArgb(245, 236, 251);
        private static readonly Color Muted = Color.FromArgb(190, 172, 207);
        private static readonly Color Surface = Color.FromArgb(43, 34, 58);
        private static readonly Color Lilac = Color.FromArgb(192, 151, 239);
        private static readonly Color Pink = Color.FromArgb(240, 149, 183);
        private readonly Bitmap portrait;
        private readonly PetCare care;
        private readonly ToolTip tips = new ToolTip();
        private readonly Timer refreshTimer = new Timer();
        private readonly Font titleFont = new Font("Segoe UI", 25, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font headingFont = new Font("Segoe UI", 15, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font textFont = new Font("Segoe UI", 13, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font smallFont = new Font("Segoe UI", 11, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly Font tinyFont = new Font("Segoe UI", 10, FontStyle.Bold, GraphicsUnit.Pixel);
        private string message = "A head pat or a little snack makes her day.";
        private bool releasing;

        public CarePanel(SpriteAtlas atlas, PetCare care, Action pat, Action<FoodKind> feed)
        {
            if (atlas == null) throw new ArgumentNullException("atlas");
            if (care == null) throw new ArgumentNullException("care");
            if (pat == null) throw new ArgumentNullException("pat");
            if (feed == null) throw new ArgumentNullException("feed");
            this.care = care;
            portrait = atlas.GetFrame(28, 120, false);
            Text = "Priestess - Care";
            Name = "PriestessCarePanel";
            AccessibleName = "Care for Priestess";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(404, 456);
            BackColor = Color.FromArgb(29, 23, 40);
            ForeColor = Ink;
            DoubleBuffered = true;
            KeyPreview = true;

            AddFoodButton(FoodKind.Berry, 24, 10, 0, feed);
            AddFoodButton(FoodKind.Cookie, 146, 16, 1, feed);
            AddFoodButton(FoodKind.Onigiri, 268, 28, 2, feed);
            CareButton patButton = new CareButton(null, "Pat her head", "", true);
            patButton.Name = "PatHeadButton";
            patButton.AccessibleName = "Pat Priestess on the head";
            patButton.AccessibleDescription = "Give Priestess a gentle head pat.";
            patButton.SetBounds(24, 341, 356, 44);
            patButton.TabIndex = 3;
            patButton.Click += delegate
            {
                pat();
                if (!IsDisposed && !Disposing) RefreshCare(null);
            };
            Controls.Add(patButton);
            tips.SetToolTip(patButton, "You can also click her head on the desktop.");

            refreshTimer.Interval = 1000;
            refreshTimer.Tick += delegate { if (!IsDisposed && !Disposing) RefreshCare(null); };
            Shown += delegate { refreshTimer.Start(); };
            FormClosed += delegate { refreshTimer.Stop(); };
            RefreshCare(null);
        }

        private void AddFoodButton(FoodKind food, int left, int fullness, int tabIndex, Action<FoodKind> feed)
        {
            string foodName = PetCare.FoodName(food);
            CareButton button = new CareButton(food, foodName, "+" + fullness + " fullness", false);
            button.Name = "Feed" + food + "Button";
            button.AccessibleName = "Feed " + foodName;
            button.AccessibleDescription = PetCare.FoodDescription(food) + ". Adds " + fullness + " fullness.";
            button.SetBounds(left, 251, 112, 76);
            button.TabIndex = tabIndex;
            button.Click += delegate
            {
                feed(food);
                if (!IsDisposed && !Disposing) RefreshCare(null);
            };
            Controls.Add(button);
            tips.SetToolTip(button, PetCare.FoodDescription(food));
        }

        public void RefreshCare(string message)
        {
            if (IsDisposed || Disposing) return;
            if (!String.IsNullOrWhiteSpace(message)) this.message = message;
            AccessibleDescription = care.Mood + ". Fullness " + Percent(care.Fullness) +
                " percent. Affection " + Percent(care.Affection) + " percent. " + this.message;
            Invalidate();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { e.Handled = true; Close(); return; }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            GraphicsState state = g.Save();
            g.ScaleTransform(ClientSize.Width / 404f, ClientSize.Height / 456f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            using (SolidBrush glow = new SolidBrush(Color.FromArgb(47, 34, 65))) g.FillEllipse(glow, 17, 35, 121, 117);
            using (Pen ring = new Pen(Color.FromArgb(75, 53, 94), 1)) g.DrawEllipse(ring, 18, 36, 119, 115);
            DrawText(g, "A LITTLE MOMENT TOGETHER", tinyFont, Muted, new RectangleF(24, 15, 355, 15));
            g.DrawImage(portrait, new RectangleF(18, 31, 124, 124));
            DrawHeart(g, new RectangleF(119, 49, 14, 13), Pink);
            DrawSparkle(g, 27, 49, 4, Lilac);
            DrawSparkle(g, 132, 127, 3, Lilac);
            DrawText(g, "Priestess", titleFont, Ink, new RectangleF(153, 44, 225, 35));
            DrawText(g, care.Mood, headingFont, Lilac, new RectangleF(155, 84, 220, 39));
            DrawText(g, care.TotalPats + (care.TotalPats==1?" pat  ·  ":" pats  ·  ") + care.TotalMeals + (care.TotalMeals==1?" meal":" meals"), smallFont, Muted, new RectangleF(155, 129, 224, 18));

            DrawGauge(g, new RectangleF(24, 161, 172, 57), "Fullness", care.Fullness, Lilac);
            DrawGauge(g, new RectangleF(208, 161, 172, 57), "Affection", care.Affection, Pink);
            DrawText(g, "Treat time", headingFont, Ink, new RectangleF(24, 228, 140, 21));
            DrawText(g, "Something tasty for her", smallFont, Muted, new RectangleF(231, 231, 149, 16));

            DrawText(g, message, textFont, Ink, new RectangleF(28, 395, 348, 34), StringAlignment.Center);
            DrawText(g, "Click her head to pat · Drag to pick her up", smallFont, Muted, new RectangleF(15, 435, 374, 16), StringAlignment.Center);
            g.Restore(state);
        }

        private void DrawGauge(Graphics g, RectangleF bounds, string name, double value, Color fill)
        {
            FillRound(g, bounds, 11, Surface);
            DrawText(g, name, smallFont, Ink, new RectangleF(bounds.X + 12, bounds.Y + 10, 105, 17));
            DrawText(g, Percent(value) + "%", tinyFont, fill, new RectangleF(bounds.Right - 48, bounds.Y + 11, 36, 16), StringAlignment.Far);
            RectangleF track = new RectangleF(bounds.X + 12, bounds.Y + 36, bounds.Width - 24, 7);
            FillRound(g, track, 3.5f, Color.FromArgb(66, 51, 82));
            float width = track.Width * (float)Math.Max(0, Math.Min(100, value)) / 100;
            if (width > 0) FillRound(g, new RectangleF(track.X, track.Y, Math.Max(2, width), track.Height), Math.Min(3.5f, width / 2), fill);
        }

        private static int Percent(double value) { return (int)Math.Round(Math.Max(0, Math.Min(100, value))); }

        // Render the actual Windows Forms controls without displaying a second window.
        public void SavePreview(string path)
        {
            if (String.IsNullOrEmpty(path)) throw new ArgumentException("A preview path is required.", "path");
            IntPtr handle = Handle;
            // CreateControl skips descendants of an invisible form. Accessing
            // each handle explicitly lets WM_PRINT render the real buttons too.
            foreach (Control child in Controls) { IntPtr childHandle = child.Handle; }
            PerformLayout();
            RefreshCare(null);
            using (Bitmap image = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                DrawToBitmap(image, new Rectangle(0, 0, Width, Height));
                image.Save(path, ImageFormat.Png);
            }
        }

        public static void DrawFood(Graphics g, FoodKind kind, RectangleF bounds)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(bounds.X, bounds.Y);
            g.ScaleTransform(bounds.Width / 64f, bounds.Height / 64f);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color outline = Color.FromArgb(48, 31, 58);
            using (Pen pen = new Pen(outline, 3.2f))
            {
                pen.LineJoin = LineJoin.Round;
                if (kind == FoodKind.Cookie)
                {
                    using (SolidBrush cookie = new SolidBrush(Color.FromArgb(239, 186, 121))) g.FillEllipse(cookie, 9, 8, 47, 48);
                    g.DrawEllipse(pen, 9, 8, 47, 48);
                    using (SolidBrush chip = new SolidBrush(Color.FromArgb(103, 57, 59)))
                    {
                        g.FillEllipse(chip, 20, 16, 7, 8); g.FillEllipse(chip, 37, 22, 8, 7);
                        g.FillEllipse(chip, 20, 35, 8, 8); g.FillEllipse(chip, 36, 43, 6, 6);
                        g.FillEllipse(chip, 44, 34, 5, 5);
                    }
                    using (Pen light = new Pen(Color.FromArgb(255, 220, 161), 3)) g.DrawArc(light, 15, 13, 33, 34, 182, 67);
                }
                else if (kind == FoodKind.Onigiri)
                {
                    using (GraphicsPath rice = new GraphicsPath())
                    {
                        rice.StartFigure(); rice.AddBezier(8, 45, 10, 36, 23, 10, 29, 8);
                        rice.AddBezier(29, 8, 34, 5, 38, 11, 41, 16);
                        rice.AddBezier(41, 16, 46, 24, 56, 43, 56, 47);
                        rice.AddBezier(56, 47, 57, 53, 51, 55, 46, 55);
                        rice.AddLine(46, 55, 18, 55); rice.AddBezier(18, 55, 9, 55, 5, 52, 8, 45); rice.CloseFigure();
                        using (SolidBrush white = new SolidBrush(Color.FromArgb(255, 247, 234))) g.FillPath(white, rice);
                        g.DrawPath(pen, rice);
                    }
                    FillRound(g, new RectangleF(23, 35, 20, 20), 3, Color.FromArgb(57, 66, 62));
                    using (Pen seaweed = new Pen(Color.FromArgb(98, 114, 89), 2)) { g.DrawLine(seaweed, 28, 39, 28, 48); g.DrawLine(seaweed, 34, 40, 34, 51); }
                    using (Pen grains = new Pen(Color.FromArgb(221, 200, 190), 1.6f))
                    { g.DrawLine(grains, 27, 21, 25, 24); g.DrawLine(grains, 37, 26, 39, 28); g.DrawLine(grains, 18, 37, 19, 34); }
                }
                else
                {
                    using (SolidBrush berry = new SolidBrush(Color.FromArgb(213, 89, 141)))
                    {
                        g.FillEllipse(berry, 9, 24, 27, 27); g.DrawEllipse(pen, 9, 24, 27, 27);
                        g.FillEllipse(berry, 28, 23, 27, 27); g.DrawEllipse(pen, 28, 23, 27, 27);
                        g.FillEllipse(berry, 21, 34, 26, 25); g.DrawEllipse(pen, 21, 34, 26, 25);
                    }
                    using (Pen stem = new Pen(Color.FromArgb(101, 155, 116), 4)) g.DrawBezier(stem, 32, 28, 30, 18, 36, 12, 37, 7);
                    using (GraphicsPath leaf = new GraphicsPath())
                    {
                        leaf.AddBezier(32, 20, 21, 23, 16, 15, 16, 10);
                        leaf.AddBezier(16, 10, 26, 8, 32, 13, 32, 20);
                        using (SolidBrush green = new SolidBrush(Color.FromArgb(128, 190, 143))) g.FillPath(green, leaf);
                        g.DrawPath(pen, leaf);
                    }
                    using (Pen shine = new Pen(Color.FromArgb(255, 178, 201), 3))
                    { g.DrawArc(shine, 15, 29, 12, 13, 195, 64); g.DrawArc(shine, 33, 28, 12, 13, 195, 55); g.DrawArc(shine, 27, 40, 11, 10, 195, 60); }
                }
            }
            g.Restore(state);
        }

        private static void DrawText(Graphics g, string text, Font font, Color color, RectangleF bounds)
        { DrawText(g, text, font, color, bounds, StringAlignment.Near); }

        private static void DrawText(Graphics g, string text, Font font, Color color, RectangleF bounds, StringAlignment alignment)
        {
            using (SolidBrush brush = new SolidBrush(color))
            using (StringFormat format = new StringFormat())
            {
                format.Alignment = alignment;
                format.LineAlignment = StringAlignment.Near;
                format.Trimming = StringTrimming.EllipsisWord;
                g.DrawString(text ?? "", font, brush, bounds, format);
            }
        }

        private static GraphicsPath Round(RectangleF bounds, float radius)
        {
            float diameter = Math.Min(Math.Min(bounds.Width, bounds.Height), Math.Max(0.2f, radius * 2));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure(); return path;
        }

        private static void FillRound(Graphics g, RectangleF bounds, float radius, Color color)
        {
            using (GraphicsPath path = Round(bounds, radius))
            using (SolidBrush brush = new SolidBrush(color)) g.FillPath(brush, path);
        }

        private static void DrawHeart(Graphics g, RectangleF bounds, Color color)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(bounds.X, bounds.Y); g.ScaleTransform(bounds.Width / 20, bounds.Height / 20);
            using (GraphicsPath heart = new GraphicsPath())
            using (SolidBrush brush = new SolidBrush(color))
            {
                heart.AddBezier(10, 19, 8, 17, 0, 11, 0, 6);
                heart.AddBezier(0, 6, 0, -1, 8, -2, 10, 4);
                heart.AddBezier(10, 4, 12, -2, 20, -1, 20, 6);
                heart.AddBezier(20, 6, 20, 11, 12, 17, 10, 19);
                heart.CloseFigure(); g.FillPath(brush, heart);
            }
            g.Restore(state);
        }

        private static void DrawSparkle(Graphics g, float x, float y, float radius, Color color)
        {
            using (Pen pen = new Pen(color, 1.5f)) { g.DrawLine(pen, x - radius, y, x + radius, y); g.DrawLine(pen, x, y - radius, x, y + radius); }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !releasing)
            {
                releasing = true;
                refreshTimer.Dispose(); tips.Dispose();
                titleFont.Dispose(); headingFont.Dispose(); textFont.Dispose(); smallFont.Dispose(); tinyFont.Dispose();
            }
            base.Dispose(disposing);
        }

        private sealed class CareButton : Button
        {
            private readonly FoodKind? food;
            private readonly string caption;
            private readonly string detail;
            private readonly bool primary;
            private readonly Font labelFont;
            private readonly Font detailFont = new Font("Segoe UI", 10, FontStyle.Regular, GraphicsUnit.Pixel);
            private bool hovered, pressed;

            public CareButton(FoodKind? food, string caption, string detail, bool primary)
            {
                this.food = food; this.caption = caption; this.detail = detail; this.primary = primary;
                labelFont = new Font("Segoe UI", primary ? 14 : 12, FontStyle.Bold, GraphicsUnit.Pixel);
                Text = caption;
                Cursor = Cursors.Hand;
                FlatStyle = FlatStyle.Flat;
                FlatAppearance.BorderSize = 0;
                BackColor = Color.FromArgb(29, 23, 40);
                ForeColor = Ink;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
            }

            protected override void OnMouseEnter(EventArgs e) { hovered = true; base.OnMouseEnter(e); Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { hovered = pressed = false; base.OnMouseLeave(e); Invalidate(); }
            protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) pressed = true; base.OnMouseDown(e); Invalidate(); }
            protected override void OnMouseUp(MouseEventArgs e) { pressed = false; base.OnMouseUp(e); Invalidate(); }
            protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space) pressed = true; base.OnKeyDown(e); Invalidate(); }
            protected override void OnKeyUp(KeyEventArgs e) { pressed = false; base.OnKeyUp(e); Invalidate(); }
            protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
            protected override void OnLostFocus(EventArgs e) { pressed = false; base.OnLostFocus(e); Invalidate(); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(BackColor);
                GraphicsState state = g.Save();
                float width = primary ? 356 : 112, height = primary ? 44 : 76;
                g.ScaleTransform(Width / width, Height / height);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                Color fill = primary ? (pressed ? Color.FromArgb(146, 106, 185) : hovered ? Color.FromArgb(207, 171, 245) : Lilac) :
                    (pressed ? Color.FromArgb(73, 48, 96) : hovered ? Color.FromArgb(62, 43, 82) : Surface);
                RectangleF bounds = new RectangleF(1, 1, width - 2, height - 2);
                FillRound(g, bounds, 11, fill);
                using (GraphicsPath shape = Round(bounds, 11))
                using (Pen border = new Pen(Focused && ShowFocusCues ? Color.FromArgb(252, 220, 250) : primary ? fill : Color.FromArgb(74, 53, 94), Focused && ShowFocusCues ? 2 : 1)) g.DrawPath(border, shape);
                if (primary)
                {
                    DrawHeart(g, new RectangleF(109, 15, 15, 14), Color.FromArgb(47, 29, 60));
                    DrawText(g, caption, labelFont, Color.FromArgb(40, 24, 52), new RectangleF(134, 12, 190, 21));
                }
                else
                {
                    if (food.HasValue) DrawFood(g, food.Value, new RectangleF(39, 5, 34, 34));
                    DrawText(g, caption, labelFont, Ink, new RectangleF(4, 41, width - 8, 18), StringAlignment.Center);
                    DrawText(g, detail, detailFont, Muted, new RectangleF(4, 59, width - 8, 14), StringAlignment.Center);
                }
                g.Restore(state);
            }

            protected override void Dispose(bool disposing)
            { if (disposing) { labelFont.Dispose(); detailFont.Dispose(); } base.Dispose(disposing); }
        }
    }
}
