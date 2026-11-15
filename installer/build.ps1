# Builds the release installer: artifacts\ObsTurboMuteAlarm-Setup-<version>.exe
# Requires the .NET 8 SDK and Inno Setup 6 (winget install JRSoftware.InnoSetup).
param(
    [string]$Version = "1.0.0",
    [switch]$SkipTests
)

$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$artifacts = Join-Path $root "artifacts"

function Invoke-Checked([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$exe failed with exit code $LASTEXITCODE" }
}

$iscc = (Get-Command ISCC.exe -ErrorAction SilentlyContinue).Source
if (-not $iscc) {
    $iscc = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    ) | Where-Object { Test-Path $_ } | Select-Object -First 1
}
if (-not $iscc) { throw "Inno Setup 6 (ISCC.exe) not found. Install it with: winget install JRSoftware.InnoSetup" }

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }

if (-not $SkipTests) {
    Invoke-Checked dotnet @("test", "$root\ObsTurboMuteAlarm.sln", "--nologo", "-v", "q")
}

# Main app: one self-contained exe, no .NET install needed.
Invoke-Checked dotnet @("publish", "$root\src\ObsTurboMuteAlarm", "-c", "Release", "-r", "win-x64",
    "--self-contained", "true", "-p:PublishSingleFile=true", "-p:EnableCompressionInSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true", "-p:DebugType=none", "-p:Version=$Version",
    "-o", "$artifacts\app", "--nologo")

# Optional ICP plugin: loaded at runtime from the app folder when present.
Invoke-Checked dotnet @("build", "$root\src\ObsTurboMuteAlarm.Icp", "-c", "Release", "-p:Version=$Version", "--nologo", "-v", "q")
$icpBin = "$root\src\ObsTurboMuteAlarm.Icp\bin\Release\net8.0-windows"
New-Item -ItemType Directory -Force "$artifacts\icp" | Out-Null
Copy-Item "$icpBin\ObsTurboMuteAlarm.Icp.dll", "$icpBin\HidSharp.dll" "$artifacts\icp"

Invoke-Checked $iscc @("/Q", "/DAppVersion=$Version", "/DArtifactsDir=$artifacts", "$PSScriptRoot\ObsTurboMuteAlarm.iss")

Get-Item "$artifacts\ObsTurboMuteAlarm-Setup-$Version.exe"
