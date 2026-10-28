<#
.SYNOPSIS
  Publishes the self-contained x64 app and compiles the Inno Setup installer.
  Output: installer\Output\iProfilerDesktop-Setup-<version>.exe
#>
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$publishDir = Join-Path $repo 'artifacts\publish\win-x64'

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw 'ISCC.exe (Inno Setup 6) not found.' }

if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }

# Self-contained: the client machine needs no .NET runtime installed.
dotnet publish (Join-Path $repo 'src\IProfiler.App\IProfiler.App.csproj') `
    -c Release -r win-x64 --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

& $iscc "/DPublishDir=$publishDir" (Join-Path $PSScriptRoot 'iprofiler.iss')
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)" }

Get-ChildItem (Join-Path $PSScriptRoot 'Output') -Filter '*.exe' |
    Select-Object Name, @{ n = 'SizeMB'; e = { [math]::Round($_.Length / 1MB, 1) } }
