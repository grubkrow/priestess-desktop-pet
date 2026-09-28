using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Windows.Forms;

namespace PriestessPet
{
    public enum CareReaction { None, Pat, Eat }

    /// <summary>
    /// Immutable, premultiplied care frames. The atlas remains owned by the caller;
    /// this object owns every bitmap it returns. Do not edit or dispose those frames.
    /// </summary>
    public sealed class CareEffects : IDisposable
    {
        private const double FrameSeconds = 0.12;
        private static readonly int[] PatFrames = { 32, 32, 33, 33, 34, 34, 34, 33, 32, 32, 35, 35, 35, 35 };
        private readonly Dictionary<int, Dictionary<string, Bitmap>> frames = new Dictionary<int, Dictionary<string, Bitmap>>();
        private readonly List<int> sizeOrder = new List<int>();
        private SpriteAtlas sourceAtlas;
        private bool disposed;

        public Bitmap Frame(SpriteAtlas atlas, int size, CareReaction reaction, double age, FoodKind food)
        {
            if (disposed) throw new ObjectDisposedException("CareEffects");
            if (atlas == null) throw new ArgumentNullException("atlas");
            if (!Object.ReferenceEquals(sourceAtlas, atlas))
            {
                Clear();
                sourceAtlas = atlas;
            }
            size = Math.Max(1, Math.Min(512, size));
            if (Double.IsNaN(age) || Double.IsInfinity(age)) age = 0;
            int tick = (int)Math.Floor(Math.Max(0, Math.Min(3, age)) / FrameSeconds + 0.000001);
            int sprite = 28;
            int foodStage = 0;
            if (reaction == CareReaction.Pat)
            {
                sprite = PatFrames[Math.Min(PatFrames.Length - 1, tick)];
            }
            else if (reaction == CareReaction.Eat)
            {
                // A short anticipation, alternating chewing drawings, then delight.
                sprite = tick < 4 ? 36 : tick >= 21 ? 39 : 37 + ((tick - 4) / 2) % 2;
                foodStage = tick < 7 ? 4 : tick < 12 ? 3 : tick < 16 ? 2 : tick < 20 ? 1 : 0;
            }
            if (food != FoodKind.Berry && food != FoodKind.Cookie && food != FoodKind.Onigiri)
                food = FoodKind.Berry;
            // Once eaten, all foods share the same immutable bitmap.
            if (foodStage == 0) food = FoodKind.Berry;
            string key = sprite + ":" + (int)food + ":" + foodStage;
            Dictionary<string, Bitmap> bucket = GetSizeBucket(size);
            Bitmap cached;
            if (bucket.TryGetValue(key, out cached)) return cached;

            Bitmap composed = new Bitmap(size, size, PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(composed))
                {
                    g.CompositingMode = CompositingMode.SourceCopy;
                    g.DrawImageUnscaled(atlas.GetFrame(sprite, size, false), 0, 0);
                    if (foodStage > 0)
                    {
                        g.CompositingMode = CompositingMode.SourceOver;
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        float scale = foodStage == 4 ? 1f : foodStage == 3 ? 0.82f : foodStage == 2 ? 0.63f : 0.40f;
                        float side = size * 0.13f * scale;
                        // The generated eating drawings hold their hands around this point.
                        RectangleF foodBounds = new RectangleF(size * 0.50f - side / 2, size * 0.505f - side / 2, side, side);
                        CarePanel.DrawFood(g, food, foodBounds);
                    }
                }
                bucket.Add(key, composed);
                return composed;
            }
            catch
            {
                composed.Dispose();
                throw;
            }
        }

        private Dictionary<string, Bitmap> GetSizeBucket(int size)
        {
            Dictionary<string, Bitmap> bucket;
            if (frames.TryGetValue(size, out bucket))
            {
                sizeOrder.Remove(size);
                sizeOrder.Add(size);
                return bucket;
            }
            // The three normal sizes (120, 180, 240) fit permanently. If a tiny
            // monitor forces a different size, keep the same bounded cache.
            if (sizeOrder.Count >= 3)
            {
                int oldSize = sizeOrder[0];
                foreach (Bitmap bitmap in frames[oldSize].Values) bitmap.Dispose();
                frames.Remove(oldSize);
                sizeOrder.RemoveAt(0);
            }
            bucket = new Dictionary<string, Bitmap>();
            frames.Add(size, bucket);
            sizeOrder.Add(size);
            return bucket;
        }

        private void Clear()
        {
            foreach (Dictionary<string, Bitmap> bucket in frames.Values)
                foreach (Bitmap bitmap in bucket.Values) bitmap.Dispose();
            frames.Clear();
            sizeOrder.Clear();
        }

        public void Dispose()
        {
            if (disposed) return;
            Clear();
            sourceAtlas = null;
            disposed = true;
        }
    }

    /// <summary>A nonactivating, click-through care bubble. The owner hides it when its reaction ends.</summary>
    public sealed class CareFeedback : LayeredPetWindow
    {
        private const int FeedbackWidth = 230;
        private const int FeedbackHeight = 76;
        private string lastMessage;
        private bool lastHearts;
        private int lastPhase = -1;
        private int lastPlacement = -1;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT: every visible pixel passes mouse input through.
                return parameters;
            }
        }

        public void UpdateFeedback(string message, bool hearts, double age, Rectangle petRect, Rectangle workArea, bool topMost)
        {
            if (Double.IsNaN(age) || Double.IsInfinity(age)) age = 0;
            age = Math.Max(0, Math.Min(60, age));
            string text = message ?? String.Empty;
            int phase = hearts ? (int)Math.Floor(age / 0.08) : 0;
            int placement = 0;
            int x = petRect.Left + (petRect.Width - FeedbackWidth) / 2;
            int y = petRect.Top - FeedbackHeight + 8;
            if (y < workArea.Top)
            {
                if (workArea.Right - petRect.Right >= FeedbackWidth - 18)
                {
                    placement = 1;
                    x = petRect.Right - 18;
                    y = petRect.Top + 8;
                }
                else if (petRect.Left - workArea.Left >= FeedbackWidth - 18)
                {
                    placement = 2;
                    x = petRect.Left - FeedbackWidth + 18;
                    y = petRect.Top + 8;
                }
                else
                {
                    placement = 3;
                    y = petRect.Bottom - 8;
                }
            }
            x = Math.Max(workArea.Left, Math.Min(workArea.Right - FeedbackWidth, x));
            y = Math.Max(workArea.Top, Math.Min(workArea.Bottom - FeedbackHeight, y));
            if (TopMost != topMost) TopMost = topMost;

            if (phase != lastPhase || text != lastMessage || hearts != lastHearts || placement != lastPlacement)
            {
                using (Bitmap image = DrawFeedback(text, hearts, phase * 0.08, placement))
                {
                    Present(image, x, y);
                }
                // Present copied pixels to its native surface; do not leave a
                // reference to the disposed temporary as its managed-frame cache.
                InvalidateFrame();
                lastPhase = phase;
                lastMessage = text;
                lastHearts = hearts;
                lastPlacement = placement;
            }
            else MovePet(x, y);
            if (!Visible) Show();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            lastPhase = -1;
            base.OnHandleDestroyed(e);
        }

        private static Bitmap DrawFeedback(string message, bool hearts, double age, int placement)
        {
            Bitmap image = new Bitmap(FeedbackWidth, FeedbackHeight, PixelFormat.Format32bppPArgb);
            try
            {
                using (Graphics g = Graphics.FromImage(image))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    RectangleF bubble = new RectangleF(10, 20, 210, 40);
                    using (GraphicsPath shadow = RoundedRectangle(new RectangleF(10, 22, 210, 40), 14))
                    using (Brush shadowInk = new SolidBrush(Color.FromArgb(52, 16, 9, 33)))
                        g.FillPath(shadowInk, shadow);
                    using (GraphicsPath outline = Bubble(bubble, placement))
                    using (Brush fill = new SolidBrush(Color.FromArgb(248, 52, 35, 75)))
                    using (Pen edge = new Pen(Color.FromArgb(245, 187, 148, 225), 1.5f))
                    {
                        g.FillPath(fill, outline);
                        g.DrawPath(edge, outline);
                    }
                    using (Font font = new Font("Segoe UI", 9.5f, FontStyle.Bold, GraphicsUnit.Point))
                    using (Brush ink = new SolidBrush(Color.FromArgb(255, 250, 243, 255)))
                    using (StringFormat format = new StringFormat())
                    {
                        format.Alignment = StringAlignment.Center;
                        format.LineAlignment = StringAlignment.Center;
                        format.Trimming = StringTrimming.EllipsisCharacter;
                        g.DrawString(message, font, ink, new RectangleF(21, 23, 188, 33), format);
                    }
                    if (hearts)
                    {
                        for (int i = 0; i < 3; i++)
                        {
                            double progress = (age * 0.66 + i * 0.31) % 1;
                            float cx = i == 0 ? 12 : i == 1 ? 219 : 189;
                            cx += (float)Math.Sin(progress * Math.PI * 2 + i) * 3;
                            float cy = (float)(43 - progress * 29 - (i == 2 ? 13 : 0));
                            float side = (float)(9.5 + Math.Sin(progress * Math.PI) * 3.5);
                            int alpha = (int)(245 * Math.Min(1, (1 - progress) * 3.8));
                            DrawHeart(g, new RectangleF(cx - side / 2, cy, side, side), alpha);
                        }
                    }
                }
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        private static GraphicsPath Bubble(RectangleF box, int placement)
        {
            // A single contour avoids an internal seam where the tail meets the body.
            float r = 12;
            GraphicsPath path = new GraphicsPath();
            path.StartFigure();
            path.AddArc(box.Left, box.Top, r * 2, r * 2, 180, 90);
            if (placement == 3)
            {
                path.AddLine(box.Left + r, box.Top, 103, box.Top);
                path.AddLine(103, box.Top, 115, 9);
                path.AddLine(115, 9, 125, box.Top);
            }
            path.AddLine(placement == 3 ? 125 : box.Left + r, box.Top, box.Right - r, box.Top);
            path.AddArc(box.Right - r * 2, box.Top, r * 2, r * 2, 270, 90);
            if (placement == 2)
            {
                path.AddLine(box.Right, box.Top + r, box.Right, 35);
                path.AddLine(box.Right, 35, 229, 45);
                path.AddLine(229, 45, box.Right, 48);
            }
            path.AddLine(box.Right, placement == 2 ? 48 : box.Top + r, box.Right, box.Bottom - r);
            path.AddArc(box.Right - r * 2, box.Bottom - r * 2, r * 2, r * 2, 0, 90);
            if (placement == 0)
            {
                path.AddLine(box.Right - r, box.Bottom, 126, box.Bottom);
                path.AddLine(126, box.Bottom, 113, 71);
                path.AddLine(113, 71, 105, box.Bottom);
            }
            path.AddLine(placement == 0 ? 105 : box.Right - r, box.Bottom, box.Left + r, box.Bottom);
            path.AddArc(box.Left, box.Bottom - r * 2, r * 2, r * 2, 90, 90);
            if (placement == 1)
            {
                path.AddLine(box.Left, box.Bottom - r, box.Left, 48);
                path.AddLine(box.Left, 48, 1, 45);
                path.AddLine(1, 45, box.Left, 35);
            }
            path.AddLine(box.Left, placement == 1 ? 35 : box.Bottom - r, box.Left, box.Top + r);
            path.CloseFigure();
            return path;
        }

        private static GraphicsPath RoundedRectangle(RectangleF box, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float diameter = radius * 2;
            path.AddArc(box.Left, box.Top, diameter, diameter, 180, 90);
            path.AddArc(box.Right - diameter, box.Top, diameter, diameter, 270, 90);
            path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(box.Left, box.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static void DrawHeart(Graphics g, RectangleF box, int alpha)
        {
            float x = box.X, y = box.Y, w = box.Width, h = box.Height;
            using (GraphicsPath heart = new GraphicsPath())
            {
                heart.AddBezier(x + w * 0.50f, y + h * 0.23f, x + w * 0.05f, y - h * 0.12f, x - w * 0.12f, y + h * 0.42f, x + w * 0.50f, y + h);
                heart.AddBezier(x + w * 0.50f, y + h, x + w * 1.12f, y + h * 0.42f, x + w * 0.95f, y - h * 0.12f, x + w * 0.50f, y + h * 0.23f);
                heart.CloseFigure();
                using (Brush fill = new SolidBrush(Color.FromArgb(alpha, 255, 145, 189))) g.FillPath(fill, heart);
                using (Pen edge = new Pen(Color.FromArgb(alpha, 116, 49, 91), 0.9f)) g.DrawPath(edge, heart);
                using (Brush highlight = new SolidBrush(Color.FromArgb(alpha * 2 / 3, 255, 237, 244)))
                    g.FillEllipse(highlight, x + w * 0.17f, y + h * 0.21f, w * 0.15f, h * 0.13f);
            }
        }
    }
}
