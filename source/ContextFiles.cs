using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace PriestessPet
{
    public enum ContextAttachmentKind { Text, Document, Image }

    // An attachment owns a snapshot, never a live handle to the user's file.
    public sealed class ContextAttachment : IDisposable
    {
        private string text;
        private byte[] image;
        private bool disposed;
        public string Name { get; private set; }
        public ContextAttachmentKind Kind { get; private set; }
        public bool Truncated { get; private set; }
        public bool IsImage { get { return Kind == ContextAttachmentKind.Image; } }
        public int CharacterCount { get { CheckAlive(); return text.Length; } }
        public string Text { get { CheckAlive(); return text; } }

        internal ContextAttachment(string name, string text, ContextAttachmentKind kind, bool truncated, byte[] image)
        { Name = name; this.text = text ?? ""; Kind = kind; Truncated = truncated; this.image = image; }

        public string ToBase64()
        {
            CheckAlive();
            if (!IsImage) throw new InvalidOperationException("This attachment is a text document.");
            return Convert.ToBase64String(image);
        }

        public ContextAttachment Clone()
        { CheckAlive(); return new ContextAttachment(Name, text, Kind, Truncated, image == null ? null : (byte[])image.Clone()); }

        private void CheckAlive() { if (disposed) throw new ObjectDisposedException("ContextAttachment"); }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; text = "";
            if (image != null) { Array.Clear(image, 0, image.Length); image = null; }
        }
    }

    public static class ContextFiles
    {
        public const int MaxAttachments = 4;
        public const int MaxFileBytes = 20 * 1024 * 1024;
        public const int MaxExtractedCharacters = 50000;
        public const int MaxPdfPages = 100;
        private const int MaxXmlBytes = 16 * 1024 * 1024;
        private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private const string StrictWordNamespace = "http://purl.oclc.org/ooxml/wordprocessingml/main";
        private static readonly HashSet<string> textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".txt", ".md", ".markdown", ".csv", ".tsv", ".json", ".jsonl", ".xml", ".yaml", ".yml", ".log",
            ".ini", ".cfg", ".conf", ".toml", ".html", ".htm", ".css", ".js", ".jsx", ".ts", ".tsx",
            ".py", ".cs", ".c", ".h", ".cpp", ".hpp", ".java", ".rs", ".go", ".sql", ".sh", ".ps1",
            ".bat", ".cmd", ".rb", ".php", ".swift", ".kt", ".lua", ".r", ".tex", ".srt", ".vtt"
        };
        private static readonly HashSet<string> imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif"
        };
        public const string FileDialogFilter = "Documents and images|*.txt;*.md;*.markdown;*.csv;*.tsv;*.json;*.jsonl;*.xml;*.yaml;*.yml;*.log;*.ini;*.cfg;*.conf;*.toml;*.html;*.htm;*.css;*.js;*.jsx;*.ts;*.tsx;*.py;*.cs;*.c;*.h;*.cpp;*.hpp;*.java;*.rs;*.go;*.sql;*.sh;*.ps1;*.bat;*.cmd;*.rb;*.php;*.swift;*.kt;*.lua;*.r;*.tex;*.srt;*.vtt;*.docx;*.pdf;*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*";

        public static bool IsSupported(string path)
        {
            try
            {
                string extension = Path.GetExtension(path ?? "");
                return textExtensions.Contains(extension) || imageExtensions.Contains(extension) ||
                    String.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase) || String.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase);
            }
            catch (ArgumentException) { return false; }
        }

        public static ContextAttachment Load(string path, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (String.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Choose a file to attach.");
            if (Directory.Exists(path)) throw new InvalidOperationException("Drop individual files, rather than a folder.");
            if (!IsSupported(path)) throw new InvalidOperationException("That file type is not supported. Use text or code, DOCX, a PDF with selectable text, or PNG/JPG/BMP/GIF.");
            string name = SafeName(Path.GetFileName(path));
            string extension = Path.GetExtension(path);
            try
            {
                using (FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (file.Length > MaxFileBytes) throw new InvalidOperationException("Each attachment can be at most 20 MB.");
                    if (file.Length == 0) throw new InvalidOperationException("This file is empty.");
                    if (imageExtensions.Contains(extension)) return ReadImage(file, name, cancellationToken);
                    bool truncated;
                    string text;
                    ContextAttachmentKind kind;
                    if (String.Equals(extension, ".docx", StringComparison.OrdinalIgnoreCase))
                    { text = ReadDocx(file, cancellationToken, out truncated); kind = ContextAttachmentKind.Document; }
                    else if (String.Equals(extension, ".pdf", StringComparison.OrdinalIgnoreCase))
                    { text = ReadPdf(file, cancellationToken, out truncated); kind = ContextAttachmentKind.Document; }
                    else { text = ReadText(file, cancellationToken, out truncated); kind = ContextAttachmentKind.Text; }
                    cancellationToken.ThrowIfCancellationRequested();
                    if (String.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("This file has no readable text.");
                    return new ContextAttachment(name, text, kind, truncated, null);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (PdfDocumentEncryptedException) { throw new InvalidOperationException("This PDF needs a password. Attach an unlocked copy."); }
            catch (UnauthorizedAccessException) { throw new InvalidOperationException("Priestess cannot read this file. Choose a file you have access to."); }
            catch (FileNotFoundException) { throw new InvalidOperationException("That file has moved or been deleted. Attach it again from its current location."); }
            catch (DirectoryNotFoundException) { throw new InvalidOperationException("That file's folder is no longer available."); }
            catch (InvalidDataException) { throw new InvalidOperationException("This document is damaged or is not a valid file of this type."); }
            catch (XmlException) { throw new InvalidOperationException("This DOCX contains damaged or unsupported document XML. Try exporting it as text or PDF."); }
            catch (DecoderFallbackException) { throw new InvalidOperationException("This text file uses an unsupported encoding. Save a copy as UTF-8 or UTF-16 and attach it again."); }
            catch (IOException) { throw new InvalidOperationException("This file could not be read. Close any program locking it and try again."); }
        }

        private static string SafeName(string name)
        {
            StringBuilder safe = new StringBuilder();
            foreach (char c in name) safe.Append(Char.IsControl(c) ? ' ' : c);
            return safe.ToString();
        }

        private static string ReadText(Stream file, CancellationToken token, out bool truncated)
        {
            byte[] sample = new byte[Math.Min(512, (int)file.Length)];
            int sampled = file.Read(sample, 0, sample.Length); file.Position = 0;
            Encoding encoding = new UTF8Encoding(false, true);
            int skip = 0;
            if (sampled >= 3 && sample[0] == 0xEF && sample[1] == 0xBB && sample[2] == 0xBF) skip = 3;
            else if (sampled >= 2 && sample[0] == 0xFF && sample[1] == 0xFE)
            { encoding = new UnicodeEncoding(false, false, true); skip = 2; }
            else if (sampled >= 2 && sample[0] == 0xFE && sample[1] == 0xFF)
            { encoding = new UnicodeEncoding(true, false, true); skip = 2; }
            else if (sampled >= 8)
            {
                int evenZeros = 0, oddZeros = 0;
                for (int i = 0; i < sampled; i++) if (sample[i] == 0) { if (i % 2 == 0) evenZeros++; else oddZeros++; }
                if (oddZeros > sampled / 5 && evenZeros == 0) encoding = new UnicodeEncoding(false, false, true);
                else if (evenZeros > sampled / 5 && oddZeros == 0) encoding = new UnicodeEncoding(true, false, true);
            }
            file.Position = skip;
            StringBuilder result = new StringBuilder();
            char[] buffer = new char[4096];
            using (StreamReader reader = new StreamReader(file, encoding, false, 4096, true))
            {
                while (result.Length <= MaxExtractedCharacters)
                {
                    token.ThrowIfCancellationRequested();
                    int count = reader.Read(buffer, 0, Math.Min(buffer.Length, MaxExtractedCharacters + 1 - result.Length));
                    if (count == 0) break;
                    for (int i = 0; i < count; i++)
                        if (Char.IsControl(buffer[i]) && buffer[i] != '\r' && buffer[i] != '\n' && buffer[i] != '\t' && buffer[i] != '\f')
                            throw new InvalidOperationException("This file contains binary data. Attach a text export instead.");
                    result.Append(buffer, 0, count);
                }
            }
            truncated = result.Length > MaxExtractedCharacters;
            if (truncated) result.Length = MaxExtractedCharacters;
            return TrimDanglingSurrogate(result.ToString()).Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string ReadDocx(Stream file, CancellationToken token, out bool truncated)
        {
            StringBuilder result = new StringBuilder(); truncated = false;
            using (ZipArchive archive = new ZipArchive(file, ZipArchiveMode.Read, true))
            {
                ZipArchiveEntry document = archive.GetEntry("word/document.xml");
                if (document == null) throw new InvalidDataException("Missing DOCX document.");
                foreach (string partName in new string[] { "word/document.xml", "word/footnotes.xml", "word/endnotes.xml" })
                {
                    ZipArchiveEntry part = archive.GetEntry(partName);
                    if (part == null) continue;
                    if (result.Length >= MaxExtractedCharacters) { truncated = true; break; }
                    if (part.Length > MaxXmlBytes) throw new InvalidOperationException("This DOCX expands to a very large document. Export a smaller section as text or PDF.");
                    XmlReaderSettings settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxXmlBytes, IgnoreComments = true };
                    using (Stream raw = part.Open())
                    using (Stream bounded = new BoundedReadStream(raw, MaxXmlBytes, token))
                    using (XmlReader reader = XmlReader.Create(bounded, settings))
                    {
                        int textDepth = -1;
                        while (reader.Read())
                        {
                            token.ThrowIfCancellationRequested();
                            bool isWord = reader.NamespaceURI == WordNamespace || reader.NamespaceURI == StrictWordNamespace;
                            if (reader.NodeType == XmlNodeType.Element && isWord)
                            {
                                if (reader.LocalName == "t" && !reader.IsEmptyElement) textDepth = reader.Depth;
                                else if (reader.LocalName == "tab") result.Append('\t');
                                else if (reader.LocalName == "br" || reader.LocalName == "cr") result.Append('\n');
                            }
                            else if ((reader.NodeType == XmlNodeType.Text || reader.NodeType == XmlNodeType.CDATA || reader.NodeType == XmlNodeType.SignificantWhitespace) && textDepth >= 0)
                                result.Append(reader.Value, 0, Math.Min(reader.Value.Length, MaxExtractedCharacters + 1 - result.Length));
                            else if (reader.NodeType == XmlNodeType.EndElement && isWord)
                            {
                                if (reader.LocalName == "t") textDepth = -1;
                                else if (reader.LocalName == "p") result.Append('\n');
                                else if (reader.LocalName == "tc") result.Append('\t');
                            }
                            if (result.Length > MaxExtractedCharacters) { truncated = true; result.Length = MaxExtractedCharacters; break; }
                        }
                    }
                    if (truncated) break;
                }
            }
            return TrimDanglingSurrogate(result.ToString()).Trim();
        }

        private static string ReadPdf(Stream file, CancellationToken token, out bool truncated)
        {
            StringBuilder result = new StringBuilder(); truncated = false; bool foundText = false;
            try
            {
                using (PdfDocument document = PdfDocument.Open(file))
                {
                    int pages = Math.Min(document.NumberOfPages, MaxPdfPages);
                    for (int pageNumber = 1; pageNumber <= pages; pageNumber++)
                    {
                        token.ThrowIfCancellationRequested();
                        string pageText = ContentOrderTextExtractor.GetText(document.GetPage(pageNumber));
                        token.ThrowIfCancellationRequested();
                        if (!String.IsNullOrWhiteSpace(pageText))
                        {
                            foundText = true;
                            string label = "[Page " + pageNumber + "]\n";
                            int available = MaxExtractedCharacters - result.Length;
                            if (available <= label.Length) { truncated = true; break; }
                            result.Append(label);
                            available -= label.Length;
                            result.Append(pageText, 0, Math.Min(pageText.Length, available));
                            if (pageText.Length >= available) { truncated = true; break; }
                            if (result.Length < MaxExtractedCharacters) result.Append('\n');
                        }
                    }
                    truncated = truncated || document.NumberOfPages > pages;
                }
            }
            catch (PdfDocumentEncryptedException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception error)
            {
                if (error is OutOfMemoryException) throw;
                throw new InvalidOperationException("This PDF could not be read. Try an unlocked PDF or a text export.", error);
            }
            if (!foundText) throw new InvalidOperationException("This PDF has no selectable text in the pages checked. For a scanned PDF, attach page images (PNG/JPG) or an OCR text export.");
            return TrimDanglingSurrogate(result.ToString()).Trim();
        }

        private static ContextAttachment ReadImage(Stream file, string name, CancellationToken token)
        {
            try
            {
                using (Image source = Image.FromStream(file, false, true))
                {
                    token.ThrowIfCancellationRequested();
                    if (source.Width < 1 || source.Height < 1 || source.Width > 16000 || source.Height > 16000 || (long)source.Width * source.Height > 40000000)
                        throw new InvalidOperationException("This image is too large. Resize it to at most 40 megapixels before attaching it.");
                    // A white background keeps transparent documents legible; GIFs use their first frame.
                    double scale = Math.Min(1, 1600.0 / Math.Max(source.Width, source.Height));
                    using (Bitmap bitmap = new Bitmap(Math.Max(1, (int)Math.Round(source.Width * scale)), Math.Max(1, (int)Math.Round(source.Height * scale))))
                    {
                        using (Graphics graphics = Graphics.FromImage(bitmap))
                        { graphics.Clear(Color.White); graphics.DrawImage(source, new Rectangle(0, 0, bitmap.Width, bitmap.Height)); }
                        using (ImageSnapshot snapshot = ImageEncoder.Encode(bitmap, DateTime.UtcNow, token))
                            return new ContextAttachment(name, "", ContextAttachmentKind.Image, false, Convert.FromBase64String(snapshot.ToBase64()));
                    }
                }
            }
            catch (ArgumentException) { throw new InvalidOperationException("This image is damaged or is not a supported PNG/JPG/BMP/GIF file."); }
            catch (System.Runtime.InteropServices.ExternalException) { throw new InvalidOperationException("This image could not be decoded. Save it as PNG or JPG and try again."); }
        }

        // All labels and document content remain reference data inside the current user message.
        public static string ComposeText(IList<ContextAttachment> attachments, string query, int maxChars, out bool excerpted)
        {
            excerpted = false;
            if (maxChars < 256) throw new ArgumentOutOfRangeException("maxChars");
            if (attachments == null || attachments.Count == 0) return "";
            StringBuilder result = new StringBuilder();
            int count = Math.Min(attachments.Count, MaxAttachments);
            excerpted = attachments.Count > count;
            for (int i = 0; i < count; i++)
            {
                ContextAttachment attachment = attachments[i];
                if (attachment == null) continue;
                int remaining = maxChars - result.Length;
                int budget = remaining / (count - i);
                if (budget < 96) { excerpted = true; break; }
                string name = attachment.Name.Length > 120 ? attachment.Name.Substring(0, 117) + "..." : attachment.Name;
                string header = "\n[Attached file " + (i + 1) + ": " + name + "]\n";
                if (attachment.IsImage)
                { AppendBounded(result, header + "[Image attached to this message; not a live desktop capture.]\n", maxChars); continue; }
                int textBudget = Math.Max(0, budget - header.Length - 100);
                bool partial = attachment.Truncated || attachment.Text.Length > textBudget;
                excerpted |= partial;
                AppendBounded(result, header, maxChars);
                if (partial) AppendBounded(result, "[Partial context: selected excerpts only; omitted sections may contain other information.]\n", maxChars);
                AppendBounded(result, SelectExcerpt(attachment.Text, query, textBudget), maxChars);
                AppendBounded(result, "\n", maxChars);
            }
            return TrimDanglingSurrogate(result.ToString());
        }

        private sealed class TextChunk
        { public int Start; public int Length; public int Score; }

        private static string SelectExcerpt(string text, string query, int budget)
        {
            if (budget <= 0) return "";
            if (text.Length <= budget) return text;
            List<string> terms = new List<string>();
            foreach (Match match in Regex.Matches(query ?? "", @"[\p{L}\p{N}_]{3,}", RegexOptions.CultureInvariant))
            {
                string term = match.Value.ToLowerInvariant();
                if (term != "the" && term != "this" && term != "that" && term != "what" && term != "with" && term != "from" && term != "file" && term != "please" && term != "about" && !terms.Contains(term)) terms.Add(term);
                if (terms.Count == 24) break;
            }
            int chunkSize = Math.Min(900, Math.Max(160, budget / 2 - 60));
            List<TextChunk> chunks = new List<TextChunk>();
            for (int start = 0; start < text.Length; start += chunkSize)
            {
                TextChunk chunk = new TextChunk { Start = start, Length = Math.Min(chunkSize, text.Length - start), Score = start == 0 ? 1 : 0 };
                string content = text.Substring(start, chunk.Length);
                foreach (string term in terms)
                {
                    int at = content.IndexOf(term, StringComparison.OrdinalIgnoreCase);
                    if (at >= 0) chunk.Score += 100;
                }
                chunks.Add(chunk);
            }
            chunks.Sort(delegate(TextChunk a, TextChunk b) { int rank = b.Score.CompareTo(a.Score); return rank != 0 ? rank : a.Start.CompareTo(b.Start); });
            List<TextChunk> chosen = new List<TextChunk>(); int used = 0;
            foreach (TextChunk chunk in chunks)
            {
                int room = budget - used - 70;
                if (room <= 0) break;
                TextChunk selected = new TextChunk { Start = chunk.Start, Length = Math.Min(chunk.Length, room), Score = chunk.Score };
                chosen.Add(selected); used += selected.Length + 70;
            }
            chosen.Sort(delegate(TextChunk a, TextChunk b) { return a.Start.CompareTo(b.Start); });
            StringBuilder result = new StringBuilder();
            foreach (TextChunk chunk in chosen)
            {
                string page = "";
                int pageStart = text.LastIndexOf("[Page ", chunk.Start, StringComparison.Ordinal);
                if (pageStart >= 0)
                {
                    int end = text.IndexOf(']', pageStart);
                    if (end >= 0 && end - pageStart < 20) page = text.Substring(pageStart, end - pageStart + 1) + " ";
                }
                AppendBounded(result, "\n[Excerpt " + page + "characters " + (chunk.Start + 1) + "–" + (chunk.Start + chunk.Length) + "]\n", budget);
                AppendBounded(result, text.Substring(chunk.Start, chunk.Length), budget);
                AppendBounded(result, "\n", budget);
            }
            return TrimDanglingSurrogate(result.ToString());
        }

        private static void AppendBounded(StringBuilder output, string text, int maximum)
        { int count = Math.Min(text.Length, maximum - output.Length); if (count > 0) output.Append(text, 0, count); }
        private static string TrimDanglingSurrogate(string value)
        { return value.Length > 0 && Char.IsHighSurrogate(value[value.Length - 1]) ? value.Substring(0, value.Length - 1) : value; }

        private sealed class BoundedReadStream : Stream
        {
            private readonly Stream source; private readonly long limit; private readonly CancellationToken token; private long count;
            public BoundedReadStream(Stream source, long limit, CancellationToken token) { this.source = source; this.limit = limit; this.token = token; }
            public override int Read(byte[] buffer, int offset, int length)
            {
                token.ThrowIfCancellationRequested();
                int read = source.Read(buffer, offset, (int)Math.Min(length, limit - count + 1)); count += read;
                if (count > limit) throw new InvalidDataException("Expanded document exceeds its read limit.");
                return read;
            }
            public override bool CanRead { get { return true; } } public override bool CanSeek { get { return false; } } public override bool CanWrite { get { return false; } }
            public override long Length { get { throw new NotSupportedException(); } } public override long Position { get { return count; } set { throw new NotSupportedException(); } }
            public override void Flush() { } public override long Seek(long offset, SeekOrigin origin) { throw new NotSupportedException(); }
            public override void SetLength(long value) { throw new NotSupportedException(); } public override void Write(byte[] buffer, int offset, int count) { throw new NotSupportedException(); }
        }
    }
}
