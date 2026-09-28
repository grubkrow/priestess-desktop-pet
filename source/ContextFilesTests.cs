using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;

namespace PriestessPet
{
    public static class ContextFilesTests
    {
        public static string Run()
        {
            string parent = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            string directory = Path.Combine(parent, "context-file-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            int checks = 0;
            Action<bool, string> check = delegate(bool valid, string label) { if (!valid) throw new InvalidOperationException("Context files test: " + label); checks++; };
            try
            {
                string textPath = Path.Combine(directory, "notes.txt");
                File.WriteAllText(textPath, "Doctor's snack is a cookie.\nPriestess says สวัสดี ♥", new UTF8Encoding(false));
                using (ContextAttachment attachment = ContextFiles.Load(textPath, CancellationToken.None))
                {
                    check(attachment.Name == "notes.txt" && attachment.Kind == ContextAttachmentKind.Text, "only basename and kind are retained");
                    check(attachment.Text.Contains("สวัสดี ♥") && !attachment.Truncated, "UTF8 text and Thai are preserved");
                    check(attachment.CharacterCount == attachment.Text.Length, "character count matches loaded snapshot");
                    File.WriteAllText(textPath, "changed after attachment", Encoding.UTF8);
                    check(attachment.Text.Contains("cookie"), "editing source does not change attachment snapshot");
                    using (ContextAttachment clone = attachment.Clone()) check(clone.Text == attachment.Text, "text clone preserves loaded context");
                }
                foreach (Encoding encoding in new Encoding[] { new UnicodeEncoding(false, true), new UnicodeEncoding(true, true), new UnicodeEncoding(false, false), new UnicodeEncoding(true, false) })
                {
                    File.WriteAllText(textPath, "A UTF16 sentence: Priestess likes cookies. ♥", encoding);
                    using (ContextAttachment attachment = ContextFiles.Load(textPath, CancellationToken.None)) check(attachment.Text.Contains("cookies. ♥"), "UTF16 endian and BOM variants read correctly");
                }
                File.WriteAllText(textPath, new string('a', ContextFiles.MaxExtractedCharacters) + "extra", new UTF8Encoding(false));
                using (ContextAttachment attachment = ContextFiles.Load(textPath, CancellationToken.None))
                    check(attachment.Truncated && attachment.CharacterCount == ContextFiles.MaxExtractedCharacters, "large text is bounded and visibly marked partial");
                File.WriteAllBytes(textPath, new byte[] { 1, 2, 0, 9, 255, 100 });
                check(Rejects(textPath, null), "binary masquerading as text is rejected");
                File.WriteAllText(textPath, "locked", Encoding.UTF8);
                using (FileStream locked = new FileStream(textPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                    check(Rejects(textPath, "locking"), "file sharing failure explains how to retry");
                File.WriteAllText(textPath, "", Encoding.UTF8);
                check(Rejects(textPath, null), "empty text is rejected");
                using (FileStream large = new FileStream(textPath, FileMode.Create)) large.SetLength(ContextFiles.MaxFileBytes + 1L);
                check(Rejects(textPath, "20 MB"), "20 MB source file limit is enforced before decoding");
                check(Rejects(directory, "folder"), "folders do not trigger recursive reads");
                string executable = Path.Combine(directory, "script.exe"); File.WriteAllText(executable, "test data");
                check(Rejects(executable, "file type"), "executable content is unsupported");
                check(ContextFiles.IsSupported("script.ps1") && ContextFiles.IsSupported("REPORT.PDF") && !ContextFiles.IsSupported("archive.zip"), "supported extensions are explicit and case insensitive");
                using (CancellationTokenSource cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel(); bool canceled = false;
                    try { ContextFiles.Load(executable, cancellation.Token); } catch (OperationCanceledException) { canceled = true; }
                    check(canceled, "cancellation wins before opening a file");
                }

                string docxPath = Path.Combine(directory, "research.docx");
                WriteDocx(docxPath, "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Priestess</w:t></w:r><w:r><w:t xml:space=\"preserve\"> wears glasses.</w:t></w:r></w:p><w:tbl><w:tr><w:tc><w:p><w:r><w:t>Cookie</w:t><w:tab/><w:t>7</w:t></w:r></w:p></w:tc></w:tr></w:tbl></w:body></w:document>");
                using (ContextAttachment document = ContextFiles.Load(docxPath, CancellationToken.None))
                {
                    check(document.Kind == ContextAttachmentKind.Document && document.Text.Contains("Priestess wears glasses."), "DOCX adjacent text runs are preserved");
                    check(document.Text.Contains("Cookie\t7"), "DOCX table text and tabs are extracted");
                }
                WriteDocx(docxPath, "<!DOCTYPE w:document [<!ENTITY external SYSTEM \"file:///never-read-by-test\">]><w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>&external;</w:t></w:r></w:p></w:body></w:document>");
                check(Rejects(docxPath, "XML"), "DOCX document type declarations and external entities are prohibited");
                WriteDocx(docxPath, new string('x', 16 * 1024 * 1024 + 1));
                check(Rejects(docxPath, "expands"), "compressed oversized DOCX entries are rejected by expanded size");

                string pdfPath = Path.Combine(directory, "reference.pdf");
                WritePdf(pdfPath, "Priestess brought seven cookies.");
                using (ContextAttachment pdf = ContextFiles.Load(pdfPath, CancellationToken.None))
                    check(pdf.Kind == ContextAttachmentKind.Document && pdf.Text.Contains("[Page 1]") && pdf.Text.Contains("seven cookies"), "PDF text includes useful page labels");
                WritePdf(pdfPath, "");
                check(Rejects(pdfPath, "no selectable text"), "PDF without selectable text directs user to images or OCR export");
                File.WriteAllText(pdfPath, "not a PDF");
                check(Rejects(pdfPath, "PDF could not be read"), "damaged PDFs return a useful error");

                string imagePath = Path.Combine(directory, "picture.png");
                using (Bitmap bitmap = new Bitmap(2000, 400))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                { graphics.Clear(Color.Transparent); graphics.FillRectangle(Brushes.Purple, 100, 100, 100, 100); bitmap.Save(imagePath, ImageFormat.Png); }
                ContextAttachment original = ContextFiles.Load(imagePath, CancellationToken.None);
                ContextAttachment copy = original.Clone();
                string before = copy.ToBase64(); original.Dispose();
                using (copy)
                using (MemoryStream encoded = new MemoryStream(Convert.FromBase64String(copy.ToBase64())))
                using (Bitmap normalized = new Bitmap(encoded))
                {
                    check(copy.IsImage && copy.CharacterCount == 0 && before == copy.ToBase64(), "image worker clone remains valid after original is removed");
                    check(normalized.Width == 1600 && normalized.Height == 320, "image dimensions are bounded while preserving aspect ratio");
                    Color corner = normalized.GetPixel(1, 1);
                    check(corner.R > 240 && corner.G > 240 && corner.B > 240, "transparent image regions become a legible white background");
                }
                bool disposedRejected = false;
                try { original.ToBase64(); } catch (ObjectDisposedException) { disposedRejected = true; }
                check(disposedRejected, "removed image bytes cannot be reused");
                File.WriteAllText(imagePath, "not a picture");
                check(Rejects(imagePath, "image"), "invalid image content is rejected");

                File.WriteAllText(textPath, new string('x', 22000) + "\nThe secret snack is blueberry cheesecake.\n" + new string('y', 16000), new UTF8Encoding(false));
                using (ContextAttachment attachment = ContextFiles.Load(textPath, CancellationToken.None))
                {
                    bool excerpted;
                    string context = ContextFiles.ComposeText(new List<ContextAttachment> { attachment }, "What is the secret snack?", 2600, out excerpted);
                    check(excerpted && context.Length <= 2600 && context.Contains("Partial context"), "aggregate context stays within budget and marks selected excerpts");
                    check(context.Contains("blueberry cheesecake") && context.Contains("notes.txt"), "query terms retrieve a relevant passage past the beginning with its filename");
                }
                File.WriteAllText(textPath, "Cookie count: 7.", Encoding.UTF8);
                using (ContextAttachment attachment = ContextFiles.Load(textPath, CancellationToken.None))
                {
                    bool excerpted;
                    string context = ContextFiles.ComposeText(new List<ContextAttachment> { attachment }, "count", 2000, out excerpted);
                    check(!excerpted && context.Contains("Cookie count: 7."), "small document context is supplied in full");
                }
                return "PASS file context: " + checks + " checks (Unicode, snapshots, limits, DOCX, PDF, images, cancellation, and relevant excerpts).";
            }
            finally
            {
                string resolved = Path.GetFullPath(directory);
                if (resolved.StartsWith(parent.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("context-file-tests-", StringComparison.Ordinal))
                    Directory.Delete(resolved, true);
            }
        }

        private static bool Rejects(string path, string message)
        {
            try { using (ContextAttachment unexpected = ContextFiles.Load(path, CancellationToken.None)) { } return false; }
            catch (InvalidOperationException error) { return message == null || error.Message.IndexOf(message, StringComparison.OrdinalIgnoreCase) >= 0; }
        }

        private static void WriteDocx(string path, string document)
        {
            using (FileStream file = new FileStream(path, FileMode.Create))
            using (ZipArchive zip = new ZipArchive(file, ZipArchiveMode.Create))
            using (Stream entry = zip.CreateEntry("word/document.xml", CompressionLevel.Optimal).Open())
            using (StreamWriter writer = new StreamWriter(entry, new UTF8Encoding(false))) writer.Write(document);
        }

        private static void WritePdf(string path, string text)
        {
            string content = text.Length == 0 ? "" : "BT /F1 12 Tf 40 700 Td (" + text.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)") + ") Tj ET";
            string[] objects = {
                "<< /Type /Catalog /Pages 2 0 R >>",
                "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
                "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
                "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
                "<< /Length " + Encoding.ASCII.GetByteCount(content) + " >>\nstream\n" + content + "\nendstream"
            };
            StringBuilder pdf = new StringBuilder("%PDF-1.4\n"); List<int> offsets = new List<int>();
            for (int i = 0; i < objects.Length; i++) { offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString())); pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n"); }
            int xref = Encoding.ASCII.GetByteCount(pdf.ToString());
            pdf.Append("xref\n0 ").Append(objects.Length + 1).Append("\n0000000000 65535 f \n");
            foreach (int offset in offsets) pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");
            pdf.Append("trailer\n<< /Size ").Append(objects.Length + 1).Append(" /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");
            File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
        }
    }
}
