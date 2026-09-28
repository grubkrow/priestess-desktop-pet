param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'
$compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compilerPath)) {
    $compilerPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compilerPath)) {
    throw 'Install Microsoft .NET Framework 4.8 before building.'
}
$petOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $petOutput | Out-Null
$petSources = @('Program.cs', 'SpriteAtlas.cs', 'AiAnimation.cs', 'PetMotion.cs', 'LayeredPetWindow.cs', 'MotionTests.cs', 'NativeWindowTests.cs', 'AnimationPreview.cs', 'PetCare.cs', 'CareTests.cs', 'CarePanel.cs', 'CareEffects.cs', 'WindowPerch.cs', 'WindowPerchTests.cs', 'LocalAiService.cs', 'LocalChat.cs', 'LocalChatTests.cs', 'ImageEncoding.cs', 'ChatActivity.cs', 'DesktopFeatureTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$petArguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/warn:4', '/codepage:65001', '/r:System.dll', '/r:System.Core.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', ('/win32manifest:' + (Join-Path $PSScriptRoot 'app.manifest')), ('/out:' + (Join-Path $petOutput 'Priestess.exe')))
$petArguments += '/r:System.Xml.dll'
$petArguments += '/r:System.Security.dll'
$petSources += 'AiRootLocator.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ }
$petSources += @('ContextFiles.cs','ContextFilesTests.cs','ChatInputTests.cs','AttachmentUiTests.cs') | ForEach-Object { Join-Path $PSScriptRoot $_ }
$petArguments += '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll'
$petLibraries = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'lib') -Filter '*.dll' -File
foreach ($petLibrary in $petLibraries) { $petArguments += '/r:' + $petLibrary.FullName }
$petArguments += '/r:System.Net.Http.dll', '/r:System.Web.Extensions.dll'
foreach ($petAsset in @('Base', 'Walk', 'Climb', 'Idle', 'Care', 'Thought')) { $petArguments += '/resource:' + (Join-Path $PSScriptRoot ('priestess-' + $petAsset.ToLower() + '.png')) + ',Priestess.' + $petAsset }
& $compilerPath @petArguments @petSources
if ($LASTEXITCODE -ne 0) { throw 'Priestess build failed.' }
foreach ($petLibrary in $petLibraries) { Copy-Item -LiteralPath $petLibrary.FullName -Destination $petOutput -Force }
Write-Output ('Built ' + (Join-Path $petOutput 'Priestess.exe'))
