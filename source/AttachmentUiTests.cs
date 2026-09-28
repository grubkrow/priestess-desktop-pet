using System;
using System.Collections;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace PriestessPet
{
    internal static class AttachmentUiTests
    {
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        private static int assertions;

        internal static string Run(SpriteAtlas atlas, string folder)
        {
            assertions = 0;
            string fixtures = Path.Combine(folder, "attachment-ui-fixtures");
            Directory.CreateDirectory(fixtures);
            string firstPath = Path.Combine(fixtures, "priestess-first.txt");
            string secondPath = Path.Combine(fixtures, "priestess-second.txt");
            string thirdPath = Path.Combine(fixtures, "priestess-canceled.txt");
            File.WriteAllText(firstPath, "Project nickname: Lantern. First milestone: clean sprites.");
            File.WriteAllText(secondPath, "Second milestone: a comfortable chat. Favorite snack: a cookie.");
            File.WriteAllText(thirdPath, "This canceled attachment must never enter the chat context.");

            using (LocalAiService service = new LocalAiService())
            using (ChatPanel panel = new ChatPanel(atlas, new PetCare(), service, null))
            {
                panel.Location = new Point(Screen.PrimaryScreen.WorkingArea.Left + 120, Screen.PrimaryScreen.WorkingArea.Top + 30);
                panel.Show(); Application.DoEvents();
                TextBox composer = (TextBox)Find(panel, "ChatMessage");
                RichTextBox transcript = (RichTextBox)Find(panel, "ChatTranscript");
                Button summary = (Button)Find(panel, "AttachmentSummary");
                Button attach = (Button)Find(panel, "AttachFilesButton");
                Button clear = (Button)Find(panel, "ClearFilesButton");
                Button send = (Button)Find(panel, "SendChatButton");
                Check(panel.AllowDrop && composer.AllowDrop && transcript.AllowDrop, "The form, composer, and transcript accept file drops.");

                DragEventArgs plainText = DragArgs(new DataObject(DataFormats.Text, "ordinary text"));
                RaiseDrag(composer, "OnDragEnter", plainText);
                Check(plainText.Effect == DragDropEffects.None, "Text-only drags are not treated as file attachments.");

                composer.Focus(); TypeNative(composer, "Please read my notes");
                string originalDraft = composer.Text;
                DropFile(composer, firstPath);
                Check(IsLoading(panel), "Dropping onto the composer starts a file load.");
                Check(!composer.ReadOnly && composer.Enabled, "The composer stays editable while a file loads.");
                Check(!send.Enabled && !attach.Enabled, "Send and another picker wait for the current file load.");
                Check(composer.Text == originalDraft, "Dropping a file does not replace the draft.");
                TypeNative(composer, " together");
                Check(composer.Text == originalDraft + " together", "Native typing remains available during attachment loading.");
                DragEventArgs blocked = FileDrag(firstPath);
                RaiseDrag(transcript, "OnDragEnter", blocked);
                Check(blocked.Effect == DragDropEffects.None, "A second drag waits until the current load finishes.");
                WaitForLoad(panel);
                IList files = Files(panel);
                Check(files.Count == 1, "The composer drop adds one attachment.");
                Check(summary.Text == Path.GetFileName(firstPath), "A single attachment displays its filename.");
                Check(((ContextAttachment)files[0]).Text.Contains("Lantern"), "The attachment contains the dropped file's text.");
                Check(send.Enabled && attach.Enabled && clear.Enabled, "Chat actions recover after the file is ready.");

                string draftAfterFirst = composer.Text;
                DropFile(transcript, secondPath);
                Check(IsLoading(panel), "Dropping onto the transcript starts a file load.");
                Check(composer.Text == draftAfterFirst, "A transcript drop preserves the draft.");
                composer.Focus(); TypeNative(composer, " please");
                WaitForLoad(panel);
                files = Files(panel);
                Check(files.Count == 2, "Transcript and composer drops accumulate attachments.");
                Check(summary.Text.StartsWith("2 files", StringComparison.Ordinal), "Multiple attachments display the count.");
                Check(((ContextAttachment)files[0]).Name == Path.GetFileName(firstPath) &&
                    ((ContextAttachment)files[1]).Name == Path.GetFileName(secondPath), "Both dropped filenames remain in context.");
                Check(composer.Text == draftAfterFirst + " please", "Completing the second load preserves newer typing.");
                panel.SavePreview(Path.Combine(folder,"attachment-ui-preview.png"));

                ContextAttachment first = (ContextAttachment)files[0];
                ContextAttachment second = (ContextAttachment)files[1];
                string finalDraft = composer.Text;
                clear.PerformClick();
                Check(Files(panel).Count == 0, "Clear removes all attachment references.");
                Check(summary.Text.StartsWith("Drop files here", StringComparison.Ordinal) && !clear.Enabled, "Clear resets attachment UI.");
                Check(composer.Text == finalDraft, "Clear preserves the message draft.");
                Check(IsDisposed(first) && IsDisposed(second), "Clear disposes the loaded attachment snapshots.");

                DropFile(composer, thirdPath);
                Check(IsLoading(panel), "The canceled-drop case starts loading.");
                clear.PerformClick();
                WaitForLoad(panel);
                Check(Files(panel).Count == 0, "Clearing during loading cannot reattach a stale result.");
                Check(composer.Text == finalDraft, "Canceling an attachment load preserves the draft.");
                Check(send.Enabled && attach.Enabled, "Controls recover after an attachment load is canceled.");
                panel.Close();
            }
            return "Attachment UI: " + assertions + " assertions passed (composer/transcript drops, native typing during load, names, clear, disposal, stale cancellation).";
        }

        private static Control Find(Control parent, string name)
        {
            Control[] matches = parent.Controls.Find(name, true);
            if (matches.Length != 1) throw new InvalidOperationException("Attachment UI test could not find " + name + ".");
            return matches[0];
        }

        private static IList Files(ChatPanel panel)
        { return (IList)typeof(ChatPanel).GetField("attachments", PrivateInstance).GetValue(panel); }

        private static bool IsLoading(ChatPanel panel)
        { return typeof(ChatPanel).GetField("attachmentLoad", PrivateInstance).GetValue(panel) != null; }

        private static void WaitForLoad(ChatPanel panel)
        {
            Stopwatch limit = Stopwatch.StartNew();
            while (IsLoading(panel) && limit.Elapsed < TimeSpan.FromSeconds(8))
            { Application.DoEvents(); Thread.Sleep(5); }
            Check(!IsLoading(panel), "The bounded local file load finishes.");
        }

        private static DragEventArgs DragArgs(IDataObject data)
        { return new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy, DragDropEffects.None); }

        private static DragEventArgs FileDrag(string path)
        { return DragArgs(new DataObject(DataFormats.FileDrop, new string[] { path })); }

        private static void DropFile(Control target, string path)
        {
            DragEventArgs e = FileDrag(path);
            RaiseDrag(target, "OnDragEnter", e);
            Check(e.Effect == DragDropEffects.Copy, "A supported file drag offers Copy on " + target.Name + ".");
            RaiseDrag(target, "OnDragDrop", e);
        }

        private static void RaiseDrag(Control target, string method, DragEventArgs e)
        {
            try { typeof(Control).GetMethod(method, PrivateInstance).Invoke(target, new object[] { e }); }
            catch (TargetInvocationException failure) { throw failure.InnerException ?? failure; }
        }

        private static void Click(Control control)
        {
            try { typeof(Control).GetMethod("OnClick", PrivateInstance).Invoke(control, new object[] { EventArgs.Empty }); }
            catch (TargetInvocationException failure) { throw failure.InnerException ?? failure; }
        }

        private static void TypeNative(TextBox composer, string text)
        {
            if (GetFocus() != composer.Handle)
                throw new InvalidOperationException("Attachment UI test has no native focus on its owned composer.");
            foreach (char letter in text) SendMessage(composer.Handle, 0x0102, new IntPtr(letter), IntPtr.Zero);
        }

        private static bool IsDisposed(ContextAttachment attachment)
        {
            try { string unused = attachment.Text; return false; }
            catch (ObjectDisposedException) { return true; }
        }

        private static void Check(bool condition, string description)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Attachment UI regression: " + description);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetFocus();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
