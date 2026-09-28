# Stable v8 publication record

The Windows release preserves the original, working Priestess v8 executable. It does not include the later Fangyi pet or the retired Odysseus/screen-awareness versions.

**Preserved `Priestess.exe` SHA-256:**

```text
F01643C173C145139DC41FE6069D6B69CE4393C621C98D7240F879B03FF73218
```

## Checks for this publication

- Confirmed the executable matches the original v8 release by SHA-256.
- Compiled the complete application source with the installed .NET Framework compiler into a separate validation folder.
- Compiled the AI setup helper using the setup script's actual compiler inputs.
- Parsed both setup scripts and confirmed their contents match.
- Checked setup retry handling before Ollama is installed, relative-path normalization, and UTF-8 sidecars using a Thai/Japanese folder path in Windows PowerShell 5.1.
- Reviewed the public file list to exclude local model/runtime downloads, care saves, chat state, logs, machine-specific AI paths, and publishing tools.
- Checked the release archive's contents and verified its executable and dependency DLLs against stable v8.

The separate AI installer was repaired for first-time downloaders: it now includes `AiRootLocator.cs` when compiling its helper, supports retrying an unfinished installation, and preserves non-English folder paths. This does not change the shipped desktop-pet executable.

## Original v8 validation

The original v8 verification record reports successful sprite/alpha, motion, care, window-perching, native input, attachment, and local-chat checks, plus a live Gemma 3 4B text/attached-image check. That record is historical evidence for the preserved executable.

The full native-window checks and multi-gigabyte AI installation were **not rerun for this publication**. No new interactive test pet was launched. Performance and AI installation on every downloader's hardware have not been independently verified.
