# Build Priestess v8

The repository contains stable v8 C# source, sprite sheets, and document-reading libraries. For a ready-to-run app, use the [Windows release ZIP](https://github.com/grubkrow/priestess-desktop-pet/releases/latest).

## Requirements

Windows 10/11, .NET Framework 4.8, and Windows PowerShell. No package restore, AI model, Java, or external build tools are needed.

From the repository root:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\source\build.ps1
```

This writes `Priestess.exe` and its dependency DLLs to the repository root. Keep them together. To select a separate output folder, add `-OutputDirectory .\build-temp\local` to the command.

The compiler embeds the six runtime sprite sheets. `priestess-sheet.png` is an additional artwork reference. AI setup is optional and separate; see the [quick start](../docs/QUICKSTART.md).

## Main components

- `Program.cs`, `LayeredPetWindow.cs`: desktop window, tray menu, and user actions.
- `SpriteAtlas.cs`, `PetMotion.cs`, `AiAnimation.cs`: sprites and animation.
- `PetCare.cs`, `CarePanel.cs`, `CareEffects.cs`: saved care state and treats.
- `WindowPerch.cs`: perching on visible windows.
- `LocalChat.cs`, `LocalAiService.cs`, `AiRootLocator.cs`: local chat and portable runtime discovery.
- `ContextFiles.cs`, `ImageEncoding.cs`: local attachment handling.
- `SetupLocalAi.cs`, `Setup-local-AI.ps1`: first-time runtime/model installation.

## Existing diagnostic modes

These are developer checks, not installation steps. They can create temporary native windows and output files, so run intentionally in a suitable desktop session:

```powershell
Start-Process .\Priestess.exe -ArgumentList '--self-test', '.\checks' -Wait
Start-Process .\Priestess.exe -ArgumentList '--smoke-test', '.\checks' -Wait
```

See the [verification record](../docs/VERIFICATION.md) for checks performed for this publication and historical v8 validation.
