using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Threading;

namespace PriestessPet
{
    // In-memory image data used for attached image files. It is deliberately
    // separate from desktop capture so chat never reads the user's display.
    public sealed class ImageSnapshot : IDisposable
    {
        private byte[] jpeg;
        public DateTime CreatedUtc { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        internal ImageSnapshot(byte[] jpeg, int width, int height, DateTime createdUtc)
        {
            if (jpeg == null || jpeg.Length == 0 || jpeg.Length > 8 * 1024 * 1024) throw new ArgumentException("Invalid image.");
            this.jpeg = jpeg; Width = width; Height = height; CreatedUtc = createdUtc;
        }
        public string ToBase64()
        {
            if (jpeg == null) throw new ObjectDisposedException("ImageSnapshot");
            return Convert.ToBase64String(jpeg);
        }
        public void Dispose() { if (jpeg != null) { Array.Clear(jpeg, 0, jpeg.Length); jpeg = null; } }
    }

    public static class ImageEncoder
    {
        public static ImageSnapshot Encode(Bitmap image, DateTime createdUtc, CancellationToken cancellationToken)
        {
            if (image == null) throw new ArgumentNullException("image");
            cancellationToken.ThrowIfCancellationRequested();
            double scale = Math.Min(1, 1600.0 / Math.Max(image.Width, image.Height));
            int width = Math.Max(1, (int)Math.Round(image.Width * scale));
            int height = Math.Max(1, (int)Math.Round(image.Height * scale));
            using (Bitmap resized = new Bitmap(width, height, PixelFormat.Format24bppRgb))
            using (MemoryStream memory = new MemoryStream())
            {
                using (Graphics graphics = Graphics.FromImage(resized))
                {
                    graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    graphics.DrawImage(image, new Rectangle(0, 0, width, height));
                }
                cancellationToken.ThrowIfCancellationRequested();
                ImageCodecInfo jpegCodec = null;
                foreach (ImageCodecInfo codec in ImageCodecInfo.GetImageEncoders())
                    if (codec.FormatID == ImageFormat.Jpeg.Guid) { jpegCodec = codec; break; }
                if (jpegCodec == null) throw new InvalidOperationException("JPEG encoding is unavailable.");
                using (EncoderParameters parameters = new EncoderParameters(1))
                {
                    parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 88L);
                    resized.Save(memory, jpegCodec, parameters);
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new ImageSnapshot(memory.ToArray(), width, height, createdUtc);
            }
        }
    }
}
