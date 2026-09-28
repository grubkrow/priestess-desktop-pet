param()
$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# Setup is the only part that downloads from the internet. Chat and attached image context use
# the local model service, with its cloud features disabled.
$petAiRoot = $null
$petSidecar = Join-Path $PSScriptRoot 'Priestess-ai-root.txt'
if (Test-Path -LiteralPath $petSidecar) {
    $petConfiguredRoots = @(Get-Content -LiteralPath $petSidecar -Encoding UTF8 | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
    if ($petConfiguredRoots.Count -ne 1) {
        throw 'Priestess-ai-root.txt must contain exactly one AI folder path on one line.'
    }
    try {
        $petConfiguredRoot = $petConfiguredRoots[0]
        if (-not [IO.Path]::IsPathRooted($petConfiguredRoot)) { $petConfiguredRoot = Join-Path $PSScriptRoot $petConfiguredRoot }
        $petAiRoot = [IO.Path]::GetFullPath($petConfiguredRoot)
    } catch {
        throw 'Priestess-ai-root.txt contains an invalid folder path.'
    }
    if (Test-Path -LiteralPath $petAiRoot -PathType Leaf) { throw 'Priestess-ai-root.txt must point to a folder, not a file.' }
    # Keep this target even before ollama.exe exists so interrupted setup can be retried.
}
$petAncestor = Get-Item -LiteralPath $PSScriptRoot
while ($null -eq $petAiRoot -and $null -ne $petAncestor) {
    $petCandidate = Join-Path $petAncestor.FullName 'ai'
    if (Test-Path -LiteralPath (Join-Path $petCandidate 'ollama\ollama.exe')) {
        $petAiRoot = $petCandidate
        break
    }
    $petAncestor = $petAncestor.Parent
}
if ($null -eq $petAiRoot) { $petAiRoot = Join-Path $PSScriptRoot 'ai' }
$petAiRoot = [IO.Path]::GetFullPath($petAiRoot)
$petAiRoot | Set-Content -LiteralPath $petSidecar -Encoding UTF8
$petRuntime = Join-Path $petAiRoot 'ollama'
$petDownloads = Join-Path $petAiRoot 'downloads'
New-Item -ItemType Directory -Force -Path $petRuntime,$petDownloads | Out-Null
Write-Output ('Local AI folder: ' + $petAiRoot)

if (-not (Test-Path -LiteralPath (Join-Path $petRuntime 'ollama.exe'))) {
    $petArchive = Join-Path $petDownloads 'ollama-windows-amd64.zip'
    $petExpectedHash = 'a7dd1b174f39d3d1b8a25d4cbc86045d0e190b17187bfdcbe2f2ee3b5a11470e'
    $petValidArchive = (Test-Path -LiteralPath $petArchive) -and ((Get-FileHash -LiteralPath $petArchive -Algorithm SHA256).Hash -eq $petExpectedHash)
    if (-not $petValidArchive) {
        Write-Output 'Downloading the portable Ollama runtime to this folder...'
        Invoke-WebRequest -Uri 'https://github.com/ollama/ollama/releases/download/v0.34.0/ollama-windows-amd64.zip' -OutFile $petArchive
    }
    if ((Get-FileHash -LiteralPath $petArchive -Algorithm SHA256).Hash -ne $petExpectedHash) {
        throw 'The runtime did not match the official SHA256 checksum. Run setup again.'
    }
    Expand-Archive -LiteralPath $petArchive -DestinationPath $petRuntime -Force
}

$petSource = Join-Path $PSScriptRoot 'source'
if (-not (Test-Path -LiteralPath (Join-Path $petSource 'SetupLocalAi.cs'))) { $petSource = $PSScriptRoot }
$petCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $petCompiler)) { $petCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $petCompiler)) { throw 'The .NET Framework C# compiler is needed to prepare local AI.' }
$petSetupHost = Join-Path $petAiRoot 'SetupLocalAi.exe'
$petCompilerArguments = @('/nologo','/target:exe','/platform:anycpu','/optimize+','/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Net.Http.dll','/r:System.Web.Extensions.dll',('/out:' + $petSetupHost),(Join-Path $petSource 'SetupLocalAi.cs'),(Join-Path $petSource 'LocalAiService.cs'),(Join-Path $petSource 'AiRootLocator.cs'))
& $petCompiler @petCompilerArguments
if ($LASTEXITCODE -ne 0) { throw 'The local AI setup helper could not be built.' }
& $petSetupHost $petAiRoot
if ($LASTEXITCODE -ne 0) { throw 'Local AI setup did not finish. Check the message above and run setup again.' }
Write-Output 'Ready. Open Chat with Priestess from her right-click menu. Attach files when you want to give her extra context.'
