using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace PriestessPet
{
    // A preview of the actual desktop renderer at its normal 180 px size.
    // Full opaque panels make alpha defects visible on dark and light surfaces.
    public static class AnimationPreview
    {
        private const int Width = 600;
        private const int Height = 500;
        private const int FrameCount = 120;
        private const int DelayCentiseconds = 4;

        public static void Render(SpriteAtlas atlas, string path)
        { Render(atlas,path,false); }
        public static void Render(SpriteAtlas atlas, string path,bool ai)
        {
            if (atlas == null) throw new ArgumentNullException("atlas");
            if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("A preview output path is required.", "path");

            using (Bitmap canvas = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            using (Bitmap indexedCanvas = new Bitmap(Width, Height, PixelFormat.Format8bppIndexed))
            using (Graphics graphics = Graphics.FromImage(canvas))
            using (Font title = new Font("Segoe UI", 18, FontStyle.Bold, GraphicsUnit.Pixel))
            using (Font label = new Font("Segoe UI", 12, FontStyle.Regular, GraphicsUnit.Pixel))
            using (Brush ink = new SolidBrush(Color.FromArgb(242, 239, 250)))
            using (Brush muted = new SolidBrush(Color.FromArgb(181, 176, 197)))
            using (Brush dark = new SolidBrush(Color.FromArgb(45, 42, 57)))
            using (Brush light = new SolidBrush(Color.FromArgb(245, 243, 248)))
            using (MemoryStream animation = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(animation, Encoding.ASCII))
            {
                graphics.CompositingMode = CompositingMode.SourceOver;
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                Color[] palette = null;
                Dictionary<int, byte> colorLookup = new Dictionary<int, byte>();
                byte[] sourcePixels = new byte[Width * Height * 4];
                byte[] indexedPixels = new byte[((Width + 3) / 4) * 4 * Height];

                for (int frame = 0; frame < FrameCount; frame++)
                {
                    // Integer centiseconds avoid floating-point errors at step boundaries.
                    int elapsed = frame * DelayCentiseconds;
                    int walk = ai?AiAnimation.Frame(false,elapsed/100.0):12 + (elapsed / 10) % 8;
                    int climb = ai?AiAnimation.Frame(true,elapsed/100.0):20 + (elapsed / 12) % 8;
                    graphics.Clear(Color.FromArgb(24, 23, 31));
                    graphics.DrawString("PRIESTESS  /  Animation preview", title, ink, 16, 12);
                    graphics.DrawString((ai?"THINKING":"WALK")+"  /  Dark", label, muted, 16, 48);
                    graphics.DrawString((ai?"THINKING":"WALK")+"  /  Light", label, muted, 314, 48);
                    graphics.DrawString((ai?"READING":"CLIMB")+"  /  Dark", label, muted, 16, 264);
                    graphics.DrawString((ai?"READING":"CLIMB")+"  /  Light", label, muted, 314, 264);
                    graphics.FillRectangle(dark, 12, 69, 282, 187);
                    graphics.FillRectangle(light, 306, 69, 282, 187);
                    graphics.FillRectangle(dark, 12, 285, 282, 187);
                    graphics.FillRectangle(light, 306, 285, 282, 187);

                    Bitmap walkBitmap = atlas.GetFrame(walk, 180, false);
                    Bitmap climbBitmap = atlas.GetFrame(climb, 180, false);
                    graphics.DrawImageUnscaled(walkBitmap, 63, 72);
                    graphics.DrawImageUnscaled(walkBitmap, 357, 72);
                    graphics.DrawImageUnscaled(climbBitmap, 63, 288);
                    graphics.DrawImageUnscaled(climbBitmap, 357, 288);
                    graphics.DrawString(ai?"Actual 180 px sprites  /  Thinking & reading with glasses  /  4.8 second loop":"Actual 180 px sprites  /  8 frames per action  /  Seamless 4.8 second loop", label, muted, 16, 479);

                    // The default GDI+ GIF encoder dithers true-color input, even
                    // flat backgrounds. A shared adaptive palette avoids that
                    // grain and prevents palette changes between animation frames.
                    if (palette == null)
                    {
                        palette = CreatePalette(canvas);
                        ColorPalette indexedPalette = indexedCanvas.Palette;
                        for (int i = 0; i < palette.Length; i++) indexedPalette.Entries[i] = palette[i];
                        indexedCanvas.Palette = indexedPalette;
                    }
                    IndexCanvas(canvas, indexedCanvas, palette, colorLookup, sourcePixels, indexedPixels);

                    GifFrame encoded;
                    using (MemoryStream still = new MemoryStream())
                    {
                        indexedCanvas.Save(still, ImageFormat.Gif);
                        encoded = ReadStill(still.ToArray());
                    }
                    if (frame == 0) WriteHeader(writer, encoded);
                    WriteFrame(writer, encoded);
                }
                writer.Write((byte)0x3b);
                writer.Flush();
                byte[] output = animation.ToArray();
                Validate(output);
                string absolutePath = Path.GetFullPath(path);
                string directory = Path.GetDirectoryName(absolutePath);
                if (!String.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(absolutePath, output);
            }
        }

        private sealed class ColorSample
        {
            internal int Red, Green, Blue, Count;
        }

        private sealed class ColorBox
        {
            internal List<ColorSample> Samples;
            internal int Axis, Range, Count;

            internal ColorBox(List<ColorSample> samples)
            {
                Samples = samples;
                int[] low = { 255, 255, 255 }, high = { 0, 0, 0 };
                foreach (ColorSample sample in samples)
                {
                    int[] value = { sample.Red, sample.Green, sample.Blue };
                    Count += sample.Count;
                    for (int i = 0; i < 3; i++) { low[i] = Math.Min(low[i], value[i]); high[i] = Math.Max(high[i], value[i]); }
                }
                for (int i = 0; i < 3; i++)
                    if (high[i] - low[i] > Range) { Range = high[i] - low[i]; Axis = i; }
            }
        }

        private static Color[] CreatePalette(Bitmap canvas)
        {
            Color[] result = new Color[256];
            result[0] = Color.FromArgb(24, 23, 31);
            result[1] = Color.FromArgb(45, 42, 57);
            result[2] = Color.FromArgb(245, 243, 248);
            result[3] = Color.FromArgb(242, 239, 250);
            result[4] = Color.FromArgb(181, 176, 197);
            HashSet<int> reserved = new HashSet<int>();
            for (int i = 0; i < 5; i++) reserved.Add(result[i].ToArgb() & 0xffffff);
            int[] counts = new int[32768], reds = new int[32768], greens = new int[32768], blues = new int[32768];
            byte[] pixels = new byte[Width * Height * 4];
            BitmapData source = canvas.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(source.Scan0, pixels, 0, pixels.Length); }
            finally { canvas.UnlockBits(source); }
            for (int i = 0; i < pixels.Length; i += 4)
            {
                int blue = pixels[i], green = pixels[i + 1], red = pixels[i + 2];
                if (reserved.Contains((red << 16) | (green << 8) | blue)) continue;
                int bin = (red >> 3 << 10) | (green >> 3 << 5) | (blue >> 3);
                counts[bin]++; reds[bin] += red; greens[bin] += green; blues[bin] += blue;
            }
            List<ColorSample> samples = new List<ColorSample>();
            for (int i = 0; i < counts.Length; i++)
                if (counts[i] != 0) samples.Add(new ColorSample { Red = reds[i] / counts[i], Green = greens[i] / counts[i], Blue = blues[i] / counts[i], Count = counts[i] });
            List<ColorBox> boxes = new List<ColorBox>();
            if (samples.Count != 0) boxes.Add(new ColorBox(samples));
            while (boxes.Count < 251)
            {
                int selected = -1;
                double priority = -1;
                for (int i = 0; i < boxes.Count; i++)
                {
                    ColorBox candidate = boxes[i];
                    double score = candidate.Range * Math.Sqrt(candidate.Count);
                    if (candidate.Samples.Count > 1 && score > priority) { selected = i; priority = score; }
                }
                if (selected < 0) break;
                ColorBox box = boxes[selected];
                int axis = box.Axis;
                box.Samples.Sort(delegate(ColorSample a, ColorSample b)
                {
                    return (axis == 0 ? a.Red : axis == 1 ? a.Green : a.Blue).CompareTo(axis == 0 ? b.Red : axis == 1 ? b.Green : b.Blue);
                });
                int cumulative = 0, split = 1;
                for (int i = 0; i < box.Samples.Count - 1; i++)
                {
                    cumulative += box.Samples[i].Count;
                    split = i + 1;
                    if (cumulative >= box.Count / 2) break;
                }
                boxes[selected] = new ColorBox(box.Samples.GetRange(0, split));
                boxes.Add(new ColorBox(box.Samples.GetRange(split, box.Samples.Count - split)));
            }
            for (int i = 0; i < boxes.Count; i++)
            {
                int red = 0, green = 0, blue = 0;
                foreach (ColorSample sample in boxes[i].Samples)
                {
                    red += sample.Red * sample.Count; green += sample.Green * sample.Count; blue += sample.Blue * sample.Count;
                }
                result[i + 5] = Color.FromArgb(red / boxes[i].Count, green / boxes[i].Count, blue / boxes[i].Count);
            }
            for (int i = boxes.Count + 5; i < result.Length; i++) result[i] = result[0];
            return result;
        }

        private static void IndexCanvas(Bitmap canvas, Bitmap indexed, Color[] palette, Dictionary<int, byte> lookup, byte[] sourcePixels, byte[] indexedPixels)
        {
            BitmapData source = canvas.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try { Marshal.Copy(source.Scan0, sourcePixels, 0, sourcePixels.Length); }
            finally { canvas.UnlockBits(source); }
            int targetStride = ((Width + 3) / 4) * 4;
            for (int y = 0; y < Height; y++) for (int x = 0; x < Width; x++)
            {
                int offset = (y * Width + x) * 4;
                int blue = sourcePixels[offset], green = sourcePixels[offset + 1], red = sourcePixels[offset + 2];
                int key = (red << 16) | (green << 8) | blue;
                byte nearest;
                if (!lookup.TryGetValue(key, out nearest))
                {
                    nearest = 0;
                    int distance = Int32.MaxValue;
                    for (int i = 0; i < palette.Length; i++)
                    {
                        int dr = red - palette[i].R, dg = green - palette[i].G, db = blue - palette[i].B;
                        int candidate = dr * dr + dg * dg + db * db;
                        if (candidate < distance) { distance = candidate; nearest = (byte)i; if (distance == 0) break; }
                    }
                    lookup.Add(key, nearest);
                }
                indexedPixels[y * targetStride + x] = nearest;
            }
            BitmapData target = indexed.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
            try { Marshal.Copy(indexedPixels, 0, target.Scan0, indexedPixels.Length); }
            finally { indexed.UnlockBits(target); }
        }

        private sealed class GifFrame
        {
            internal byte[] Palette;
            internal byte PaletteSizeBits;
            internal byte DescriptorFlags;
            internal byte[] ImageData;
        }

        private static GifFrame ReadStill(byte[] data)
        {
            if (data.Length < 14 || Encoding.ASCII.GetString(data, 0, 3) != "GIF")
                throw new InvalidDataException("The platform GIF encoder returned invalid data.");
            if (Word(data, 6) != Width || Word(data, 8) != Height)
                throw new InvalidDataException("The encoded preview frame has unexpected dimensions.");
            int offset = 13;
            byte[] globalPalette = null;
            byte globalSize = (byte)(data[10] & 7);
            if ((data[10] & 0x80) != 0) globalPalette = Take(data, ref offset, 3 * (1 << (globalSize + 1)));
            while (offset < data.Length)
            {
                byte marker = data[offset++];
                if (marker == 0x21)
                {
                    Require(data, offset, 1);
                    offset++; // The still image's extension label.
                    SkipSubBlocks(data, ref offset);
                }
                else if (marker == 0x2c)
                {
                    Require(data, offset, 9);
                    if (Word(data, offset) != 0 || Word(data, offset + 2) != 0 ||
                        Word(data, offset + 4) != Width || Word(data, offset + 6) != Height)
                        throw new InvalidDataException("The platform GIF encoder did not produce a complete frame.");
                    byte flags = data[offset + 8];
                    offset += 9;
                    byte paletteSize = (byte)(flags & 7);
                    byte[] palette;
                    if ((flags & 0x80) != 0) palette = Take(data, ref offset, 3 * (1 << (paletteSize + 1)));
                    else { palette = globalPalette; paletteSize = globalSize; }
                    if (palette == null) throw new InvalidDataException("The GIF frame has no color palette.");
                    int imageStart = offset;
                    Require(data, offset, 1);
                    offset++; // LZW minimum code size.
                    SkipSubBlocks(data, ref offset);
                    byte[] image = new byte[offset - imageStart];
                    Array.Copy(data, imageStart, image, 0, image.Length);
                    return new GifFrame { Palette = palette, PaletteSizeBits = paletteSize, DescriptorFlags = flags, ImageData = image };
                }
                else throw new InvalidDataException("Unexpected block in the platform GIF encoder output.");
            }
            throw new InvalidDataException("The platform GIF encoder returned no image.");
        }

        private static void WriteHeader(BinaryWriter writer, GifFrame first)
        {
            writer.Write(Encoding.ASCII.GetBytes("GIF89a"));
            writer.Write((ushort)Width);
            writer.Write((ushort)Height);
            writer.Write((byte)(0xf0 | first.PaletteSizeBits));
            writer.Write((byte)0); // Background palette index; each frame covers the complete canvas.
            writer.Write((byte)0); // Square pixels.
            writer.Write(first.Palette);
            writer.Write(new byte[] { 0x21, 0xff, 0x0b });
            writer.Write(Encoding.ASCII.GetBytes("NETSCAPE2.0"));
            writer.Write(new byte[] { 3, 1, 0, 0, 0 }); // Loop indefinitely.
        }

        private static void WriteFrame(BinaryWriter writer, GifFrame frame)
        {
            // Disposal 2 restores the background; there is no transparent color.
            writer.Write(new byte[] { 0x21, 0xf9, 4, 8 });
            writer.Write((ushort)DelayCentiseconds);
            writer.Write(new byte[] { 0, 0 });
            writer.Write((byte)0x2c);
            writer.Write((ushort)0);
            writer.Write((ushort)0);
            writer.Write((ushort)Width);
            writer.Write((ushort)Height);
            writer.Write((byte)(0x80 | (frame.DescriptorFlags & 0x40) | frame.PaletteSizeBits));
            writer.Write(frame.Palette);
            writer.Write(frame.ImageData);
        }

        private static void Validate(byte[] data)
        {
            if (Encoding.ASCII.GetString(data, 0, 6) != "GIF89a" || Word(data, 6) != Width || Word(data, 8) != Height)
                throw new InvalidDataException("The animation header is invalid.");
            int offset = 13 + 3 * (1 << ((data[10] & 7) + 1));
            int frames = 0, controls = 0;
            bool loop = false;
            while (offset < data.Length)
            {
                byte marker = data[offset++];
                if (marker == 0x21)
                {
                    Require(data, offset, 1);
                    byte label = data[offset++];
                    if (label == 0xf9)
                    {
                        Require(data, offset, 6);
                        if (data[offset] != 4 || data[offset + 1] != 8 || Word(data, offset + 2) != DelayCentiseconds || data[offset + 5] != 0)
                            throw new InvalidDataException("An animation delay or disposal setting is invalid.");
                        controls++;
                    }
                    else if (label == 0xff)
                    {
                        Require(data, offset, 17);
                        loop = data[offset] == 11 && Encoding.ASCII.GetString(data, offset + 1, 11) == "NETSCAPE2.0" &&
                            data[offset + 12] == 3 && data[offset + 13] == 1 && Word(data, offset + 14) == 0;
                    }
                    SkipSubBlocks(data, ref offset);
                }
                else if (marker == 0x2c)
                {
                    Require(data, offset, 9);
                    if (Word(data, offset) != 0 || Word(data, offset + 2) != 0 || Word(data, offset + 4) != Width || Word(data, offset + 6) != Height)
                        throw new InvalidDataException("The animation contains a partial or misaligned frame.");
                    byte flags = data[offset + 8];
                    offset += 9;
                    if ((flags & 0x80) != 0) Take(data, ref offset, 3 * (1 << ((flags & 7) + 1)));
                    Require(data, offset, 1);
                    offset++;
                    SkipSubBlocks(data, ref offset);
                    frames++;
                }
                else if (marker == 0x3b)
                {
                    if (offset != data.Length || frames != FrameCount || controls != FrameCount || !loop)
                        throw new InvalidDataException("The animation is missing frames, timing, or its loop extension.");
                    return;
                }
                else throw new InvalidDataException("The animation contains an invalid block.");
            }
            throw new InvalidDataException("The animation is missing its GIF trailer.");
        }

        private static int Word(byte[] data, int offset)
        {
            Require(data, offset, 2);
            return data[offset] | data[offset + 1] << 8;
        }

        private static void Require(byte[] data, int offset, int count)
        {
            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new InvalidDataException("Truncated GIF data.");
        }

        private static byte[] Take(byte[] data, ref int offset, int count)
        {
            Require(data, offset, count);
            byte[] block = new byte[count];
            Array.Copy(data, offset, block, 0, count);
            offset += count;
            return block;
        }

        private static void SkipSubBlocks(byte[] data, ref int offset)
        {
            while (true)
            {
                Require(data, offset, 1);
                int count = data[offset++];
                if (count == 0) return;
                Require(data, offset, count);
                offset += count;
            }
        }
    }
}
