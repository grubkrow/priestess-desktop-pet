# Troubleshooting

Start with the [quick start](QUICKSTART.md). This repository distributes the stable **Priestess v8** desktop pet.

| Problem | What to try |
| --- | --- |
| There is no `Priestess.exe`. | Download the Priestess ZIP under **Assets** on the [latest release](https://github.com/grubkrow/priestess-desktop-pet/releases/latest). GitHub's source archives do not contain the built app. |
| The app reports a missing DLL. | Extract the complete release again. Keep every bundled DLL beside `Priestess.exe`; copying just the executable is not enough. |
| The pet does not start. | Use Windows 10/11 with .NET Framework 4.8. Extract into a writable folder, then check for `priestess-error.log` beside the app. |
| I cannot find the pet. | Open `Priestess.exe` again or use the tray icon's **Bring Priestess back** command. |
| She will not sit on a window. | Restore the window from maximized, leave space above its top edge, and enable **Land on app windows**. Keep the window visible; covered or minimized windows cannot hold her. |
| She refuses a snack. | Check **Care & snacks...**. She declines food when full; fullness decreases while the app runs. |
| Chat says the AI files or model are missing. | Run the one-time setup command in [Quick start](QUICKSTART.md#3-add-local-ai-optional). If you moved the folder, see below. |
| Setup was interrupted. | Check your connection and free disk space, then run the same setup command again from the extracted folder. Keep the `ai` folder so downloaded files can be reused. |
| Chat is slow or seems stuck on its first reply. | Initial model loading takes time. Close other demanding apps, ask a short question, and try fewer/smaller attachments. Use **Cancel** to stop a reply. Performance depends on your hardware. |
| A PDF has no readable text. | Scanned PDFs are not OCR-processed. Attach a relevant page as an image, or provide a PDF with selectable text. |
| She does not remember yesterday's conversation. | Chat history is kept only while its window is open. Closing chat or choosing **New conversation** clears it. Care progress has a separate save file. |
| She cannot see my screen. | Expected: screen awareness is removed in v8. Attach your own screenshot to discuss it. |

## After moving the folder

1. Choose **Quit** in the pet's menu before moving files.
2. Move the complete extracted folder, including `priestess-care.xml` and, if it is stored there, `ai`.
3. If **Priestess-ai-root.txt** exists beside the executable, open it in Notepad. Replace the old location with the **full path to your current `ai` folder**. Use one line, without quotation marks. That folder must contain `ollama\ollama.exe` and the existing model files.
4. Save and reopen `Priestess.exe`.

If you have no existing AI installation to keep, remove the obsolete `Priestess-ai-root.txt` and rerun setup; it will select an available nearby runtime or create `ai` beside the pet. Leave your care save in place.

Advanced users: `PRIESTESS_AI_ROOT` can also select an AI folder. If you previously set this environment variable to another valid installation, update or remove it when changing installations.

## Report a problem

Open a [GitHub issue](https://github.com/grubkrow/priestess-desktop-pet/issues) with:

- Your Windows version and Priestess release version.
- What you clicked, what happened, and whether it repeats after reopening the app.
- The exact error message; include relevant contents of `priestess-error.log` if the app produced one.

For AI setup failures, include the last error printed in PowerShell. Remove personal folder names and private attachment/chat content before posting a public report.
