using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PriestessPet
{
    // Exercises the native edit control, rather than assigning Text and assuming
    // that a user can type. Only this test's owned handles receive input.
    internal static class ChatInputTests
    {
        private const int WmChar = 0x0102;
        private const int WmMouseActivate = 0x0021;
        private static int assertions;
        private static readonly BindingFlags PrivateInstance = BindingFlags.NonPublic | BindingFlags.Instance;

        internal static string Run(SpriteAtlas atlas)
        {
            assertions = 0;
            using (LocalAiService service = new LocalAiService())
            using (LayeredPetWindow owner = new LayeredPetWindow())
            using (ChatPanel panel = new ChatPanel(atlas, new PetCare(), service, null))
            {
                Rectangle area = Screen.PrimaryScreen.WorkingArea;
                owner.Present(atlas.GetFrame(28, 120, false), area.Left + 30, area.Top + 40);
                owner.Show();
                panel.Location = new Point(area.Left + 170, area.Top + 40);
                panel.Show(owner);
                Application.DoEvents();
                TextBox composer = (TextBox)panel.Controls.Find("ChatMessage", true)[0];
                Button send = (Button)panel.Controls.Find("SendChatButton", true)[0];
                composer.Focus();
                Check(GetFocus() == composer.Handle, "The owned chat gives its composer native keyboard focus.");
                Check(SendMessage(composer.Handle, WmMouseActivate, panel.Handle,
                    new IntPtr((0x0201 << 16) | 1)).ToInt32() == 1,
                    "Clicking the chat requests activation even though its pet owner does not.");

                TypeNative(composer, "Hello", owner, atlas, area);
                Check(composer.Text == "Hello", "Idle composer accepts native character input while the pet animates.");

                string submitted = "A message already sent";
                CancellationTokenSource pending = BeginPending(panel, composer);
                try
                {
                    Check(!composer.ReadOnly, "A pending local reply keeps the composer editable.");
                    TypeNative(composer, "Next draft \u2014 \u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35", owner, atlas, area);
                    string draft = composer.Text;
                    Check(draft == "Next draft \u2014 \u0e2a\u0e27\u0e31\u0e2a\u0e14\u0e35", "Pending composer accepts native Unicode input.");
                    Check(!send.Enabled, "Typing a draft does not allow a duplicate concurrent request.");
                    Complete(panel, Task.FromResult("A local reply"), null, submitted, pending);
                    pending = null;
                    Check(composer.Text == draft, "Completing a reply preserves the next draft.");
                    Check(send.Enabled, "The next draft can be sent when the reply finishes.");
                }
                finally { ReleasePending(panel, pending); }

                pending = BeginPending(panel, composer);
                try
                {
                    TypeNative(composer, "Draft during cancellation", owner, atlas, area);
                    pending.Cancel();
                    Complete(panel, Task.FromResult("Discard this reply"), null, submitted, pending);
                    pending = null;
                    Check(composer.Text == "Draft during cancellation", "Canceling preserves a newer draft.");
                }
                finally { ReleasePending(panel, pending); }

                pending = BeginPending(panel, composer);
                try
                {
                    TypeNative(composer, "Draft during error", owner, atlas, area);
                    Complete(panel, Task.FromResult("Unused"), new InvalidOperationException("Test model failure"), submitted, pending);
                    pending = null;
                    Check(composer.Text == "Draft during error", "A model failure preserves a newer draft.");
                }
                finally { ReleasePending(panel, pending); }

                pending = BeginPending(panel, composer);
                try
                {
                    Complete(panel, Task.FromResult("Unused"), new InvalidOperationException("Test model failure"), submitted, pending);
                    pending = null;
                    Check(composer.Text == submitted, "A failure restores the submitted message when there is no newer draft.");
                }
                finally { ReleasePending(panel, pending); }

                pending = BeginPending(panel, composer);
                try
                {
                    pending.Cancel();
                    Complete(panel, Task.FromResult("Unused"), null, submitted, pending);
                    pending = null;
                    Check(composer.Text == submitted, "Canceling restores the submitted message when there is no newer draft.");
                }
                finally { ReleasePending(panel, pending); }
                panel.Close(); owner.Close();
            }
            return "Chat native input: " + assertions + " assertions passed (focus, typing during replies, Unicode, draft preservation, cancel, retry).";
        }

        private static CancellationTokenSource BeginPending(ChatPanel panel, TextBox composer)
        {
            composer.Clear();
            CancellationTokenSource pending = new CancellationTokenSource();
            typeof(ChatPanel).GetField("request", PrivateInstance).SetValue(panel, pending);
            typeof(ChatPanel).GetMethod("UpdateInputState", PrivateInstance).Invoke(panel, null);
            composer.Focus();
            return pending;
        }

        private static void Complete(ChatPanel panel, Task<string> task, Exception error, string input, CancellationTokenSource pending)
        {
            try
            {
                typeof(ChatPanel).GetMethod("CompleteSend", PrivateInstance).Invoke(panel, new object[] { task, error, input, pending });
            }
            catch (TargetInvocationException invocation)
            {
                throw invocation.InnerException ?? invocation;
            }
        }

        private static void ReleasePending(ChatPanel panel, CancellationTokenSource pending)
        {
            if (pending == null) return;
            typeof(ChatPanel).GetField("request", PrivateInstance).SetValue(panel, null);
            pending.Dispose();
        }

        private static void TypeNative(TextBox composer, string text, LayeredPetWindow owner, SpriteAtlas atlas, Rectangle area)
        {
            for (int index = 0; index < text.Length; index++)
            {
                if (GetFocus() != composer.Handle)
                    throw new InvalidOperationException("Chat input test lost native focus before a character was delivered.");
                SendMessage(composer.Handle, WmChar, new IntPtr(text[index]), IntPtr.Zero);
                owner.Present(atlas.GetFrame(28 + index % 4, 120, false), area.Left + 30 + index % 7, area.Top + 40);
                Application.DoEvents();
            }
        }

        private static void Check(bool condition, string description)
        {
            assertions++;
            if (!condition) throw new InvalidOperationException("Chat input regression: " + description);
        }

        [DllImport("user32.dll")] private static extern IntPtr GetFocus();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SendMessage(IntPtr window, int message, IntPtr wParam, IntPtr lParam);
    }
}
