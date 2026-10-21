# Build Container Quick Bind with the .NET Framework csc (no dotnet SDK needed).
#
# Usage:
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
#   powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1 -GameDir "D:\SPT" -Deploy
param(
    [string]$GameDir = "C:\SPT",
    [string]$Configuration = "Release",
    [switch]$Deploy
)

$ErrorActionPreference = "Stop"

$root     = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc      = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$managed  = Join-Path $GameDir "EscapeFromTarkov_Data\Managed"
$core     = Join-Path $GameDir "BepInEx\core"
$outDir   = Join-Path $root "dist"
$outDll   = Join-Path $outDir "ContainerQuickBind.dll"

if (-not (Test-Path $csc))     { throw "csc.exe not found: $csc" }
if (-not (Test-Path $managed)) { throw "Managed dir not found: $managed" }
if (-not (Test-Path $core))    { throw "BepInEx core not found: $core" }

New-Item -ItemType Directory -Force -Path $outDir | Out-Null

# netstandard facade: Assembly-CSharp references netstandard, csc must resolve it.
$facade = $null
foreach ($v in @("v4.8", "v4.7.2", "v4.7.1", "v4.7", "v4.6.2", "v4.6.1", "v4.6")) {
    $p = "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\$v\Facades\netstandard.dll"
    if (Test-Path $p) { $facade = $p; break }
}

$refs = @(
    (Join-Path $managed "Assembly-CSharp.dll"),
    (Join-Path $managed "ItemComponent.Types.dll"),
    (Join-Path $managed "ItemTemplate.Types.dll"),
    (Join-Path $managed "Comfort.dll"),
    (Join-Path $managed "Comfort.Unity.dll"),
    (Join-Path $managed "DissonanceVoip.dll"),
    (Join-Path $managed "UnityEngine.dll"),
    (Join-Path $managed "UnityEngine.CoreModule.dll"),
    (Join-Path $managed "UnityEngine.UI.dll"),
    (Join-Path $core "BepInEx.dll"),
    (Join-Path $core "0Harmony.dll")
)
if ($facade) { $refs += $facade }

foreach ($r in $refs) {
    if (-not (Test-Path $r)) { throw "reference missing: $r" }
}

$sources = Get-ChildItem -Path (Join-Path $root "src") -Filter *.cs -Recurse | ForEach-Object { $_.FullName }
if (-not $sources) { throw "no .cs files under src\" }

# Must pass as an array: string concatenation gets mangled by PowerShell (CS2021 / CS2001).
$argv = @(
    "/nologo",
    "/target:library",
    "/langversion:5",
    "/optimize+",
    "/warn:4",
    "/define:RELEASE",
    "/out:$outDll"
)
foreach ($r in $refs) { $argv += "/r:$r" }
foreach ($s in $sources) { $argv += $s }

Write-Host "csc $($argv.Count) args -> $outDll" -ForegroundColor Cyan
& $csc @argv
$code = $LASTEXITCODE

if ($code -ne 0) {
    Write-Host "BUILD FAILED (exit $code)" -ForegroundColor Red
    exit $code
}

$size = (Get-Item $outDll).Length
Write-Host "BUILD OK  $outDll  ($size bytes)" -ForegroundColor Green

if ($Deploy) {
    $plugins = Join-Path $GameDir "BepInEx\plugins"
    Copy-Item $outDll $plugins -Force
    Write-Host "Deployed -> $plugins" -ForegroundColor Green
}
