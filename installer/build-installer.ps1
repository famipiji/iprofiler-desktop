<#
.SYNOPSIS
  Publishes the self-contained x64 app and compiles the Inno Setup installer.
  Output: installer\Output\iProfilerDesktop-Setup-<version>.exe (-spike.exe with -WithSpike)
.PARAMETER VcRuntimeDir
  Folder with the x64 Visual C++ runtime DLLs (msvcp140, vcruntime140, vcruntime140_1, vcomp140) to install
  next to the exe. Default: the newest VS Redist folder (needs the VS "MSVC C++ x64 build tools" component).
.PARAMETER WithSpike
  Milestone 1 only: also install the IProfiler.Spike console for the end-to-end check in Windows Sandbox.
#>
param(
    [string]$VcRuntimeDir,
    [switch]$WithSpike
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repo 'artifacts\publish\win-x64'
$vcRuntimeDlls = 'msvcp140.dll', 'vcruntime140.dll', 'vcruntime140_1.dll', 'vcomp140.dll'

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'ISCC.exe (Inno Setup 6) not found.' }

# VC++ runtime: copy the four DLLs into one folder for the installer.
if (-not $VcRuntimeDir) {
    $redist = Get-ChildItem "$env:ProgramFiles\Microsoft Visual Studio\*\*\VC\Redist\MSVC\*\x64" -Directory -ErrorAction SilentlyContinue |
        Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $redist) { throw 'No VS VC\Redist\MSVC\<ver>\x64 folder: install the VS C++ build tools or pass -VcRuntimeDir.' }
    $VcRuntimeDir = $redist.FullName
}
$vcStage = Join-Path $repo 'artifacts\vcruntime-x64'
if (Test-Path $vcStage) { Remove-Item $vcStage -Recurse -Force }
New-Item -ItemType Directory $vcStage | Out-Null
foreach ($dll in $vcRuntimeDlls) {
    $found = Get-ChildItem $VcRuntimeDir -Recurse -Filter $dll | Where-Object { $_.FullName -notmatch 'debug|onecore' } | Select-Object -First 1
    if (-not $found) { throw "$dll not found under $VcRuntimeDir" }
    Copy-Item $found.FullName $vcStage
}

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

# Self-contained: the client machine needs no .NET runtime installed.
dotnet publish (Join-Path $repo 'src\IProfiler.App\IProfiler.App.csproj') `
    -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

$isccArgs = @("/DPublishDir=$publishDir", "/DVcRuntimeDir=$vcStage")
if ($WithSpike) {
    # Publish the spike on its own, then stage only the files the app doesn't already install. Shared files must
    # be identical, or one of the two exes would run against the other's version.
    $spikePublish = Join-Path $repo 'artifacts\publish\spike-win-x64'
    $spikeExtra = Join-Path $repo 'artifacts\publish\spike-extra'
    foreach ($dir in $spikePublish, $spikeExtra) { if (Test-Path $dir) { Remove-Item $dir -Recurse -Force } }
    dotnet publish (Join-Path $repo 'spike\IProfiler.Spike\IProfiler.Spike.csproj') `
        -c Release -r win-x64 --self-contained true -o $spikePublish
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish (spike) failed ($LASTEXITCODE)" }

    $different = @()
    foreach ($file in Get-ChildItem $spikePublish -Recurse -File) {
        $relative = $file.FullName.Substring($spikePublish.Length + 1)
        $inApp = Join-Path $publishDir $relative
        if (Test-Path $inApp) {
            if ((Get-FileHash $inApp).Hash -ne (Get-FileHash $file.FullName).Hash) { $different += $relative }
        } else {
            $target = Join-Path $spikeExtra $relative
            New-Item -ItemType Directory (Split-Path $target) -Force | Out-Null
            Copy-Item $file.FullName $target
        }
    }
    # The host files (deps.json, runtimeconfig.json) are per exe and never shared.
    if ($different) { throw "Spike and app publish differ in: $($different -join ', ')" }
    $isccArgs += "/DSpikeDir=$spikeExtra"
}

& $iscc @isccArgs (Join-Path $PSScriptRoot 'iprofiler.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

Get-ChildItem (Join-Path $PSScriptRoot 'Output') -Filter '*.exe' |
    Select-Object Name, @{ n = 'SizeMB'; e = { [math]::Round($_.Length / 1MB, 1) } }
