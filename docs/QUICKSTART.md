# Bring Priestess home

**Windows 10 or 11 · .NET Framework 4.8 · no installer, administrator access, or Java needed.** The desktop pet works immediately; local AI is optional.

## 1. Download and start

1. Open the [latest stable release](https://github.com/grubkrow/priestess-desktop-pet/releases/latest).
2. Under **Assets**, download **`Priestess-v8-Windows.zip`**. The automatic **Source code** downloads and GitHub's **Code → Download ZIP** are for building the app, not running it.
3. Right-click the downloaded ZIP and choose **Extract All**. Pick a folder you can write to, on a drive with room to spare.
4. Open the extracted folder and double-click **Priestess.exe**. Keep all included DLLs, the `source` folder, and `Setup-local-AI.ps1` together. Do not launch from inside the ZIP.

## 2. Meet your pet

Right-click Priestess or her icon beside the Windows clock to open her menu. If the tray icon is hidden, expand the **^** menu.

| You want to… | Do this |
| --- | --- |
| Pick her up | Drag her, then release to drop her. |
| Give her a pat | Click her head, or choose **Pat her head**. |
| Feed her | Choose **Feed a snack** for a berry, cookie, or onigiri. |
| Check her mood | Open **Care & snacks...** for fullness and affection. |
| Choose a pose | Use **Actions** for walking, sitting, sleeping, and more. Double-click her body for a shrug. |
| Make her bigger | Choose **Size → Large · 240 px**. **Speed** adjusts her movement. |
| Sit on a program | Restore a window from maximized, leave room above it, then choose **Sit on a window** or drop her on its top edge. |
| Find her again | Choose **Bring Priestess back**, or open `Priestess.exe` again. |
| Close the pet | Choose **Quit**. |

Window perching follows the window as it moves. She detaches when it closes, minimizes, maximizes, or becomes covered. Choose **Return to desktop** to drop down; disable **Land on app windows** to stop automatic perching.

## 3. Add local AI (optional)

Use a **64-bit Windows PC** for the bundled AI setup. Setup needs an internet connection and **several GB of free space** for Ollama and Gemma 3 4B. Download time and reply speed depend on your connection and hardware. No account, API key, or monthly subscription is required.

1. In File Explorer, open the extracted pet folder.
2. Click its address bar, type `powershell`, and press **Enter**.
3. Paste this command and press **Enter**:

   ```powershell
   powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Setup-local-AI.ps1
   ```

4. Wait for **Local AI is ready**. The script downloads the runtime and model and checks a reply and an attached test image. This can take a while.
5. Open **Chat with Priestess...** from her menu and send a message. The first reply needs time to load the model.

Ordinary chat works offline after setup. A new installation stores AI files in an `ai` folder beside the app; setup can also reuse a nearby existing installation. The setup command applies its execution-policy option only to that PowerShell process.

**Chat controls:** **Enter** sends, **Shift+Enter** starts a new line, and **Cancel** stops a pending reply. **New conversation** clears the current conversation and attachments. Closing chat also clears the session. She keeps up to ten completed exchanges while chat stays open; care progress is saved separately.

## 4. Give her a file

Drag files into chat or click **Attach files...**, then ask about them. She accepts text/code, Markdown, CSV, JSON, DOCX, PDFs with selectable text, and PNG/JPG/BMP/GIF images.

- Up to **4 files**, including **2 images**, with a **20 MB limit per file**.
- Attachments stay available for follow-up questions in the same conversation.
- Click the filename/file count to remove an attachment, or **Clear** to remove all. Your original files stay untouched.

Priestess processes attached content on your PC. **This version has no screen awareness and never captures your desktop.** To discuss something on screen, save a screenshot yourself and attach it. Image answers can be mistaken, so check important details against the original.

## Keep your progress

Choose **Quit** before moving or updating the app. Keep **priestess-care.xml** to preserve mood, fullness, affection, pats, and meals. Keep the **ai** folder to avoid downloading the model again.

For updates, extract the new release into a new folder, then copy your care save and move/copy your AI folder beside the new executable. If AI lives elsewhere or you move the app to another drive, follow [the folder-move instructions](TROUBLESHOOTING.md#after-moving-the-folder).

[Need help? Open troubleshooting →](TROUBLESHOOTING.md)
