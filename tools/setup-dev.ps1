<#
  iProfiler Desktop - Task 0.2: install the dev tools in one go.

  Run in an ADMIN PowerShell (Start -> type "PowerShell" -> right-click -> Run as administrator):
      cd C:\Users\Dell\source\iprofiler-desktop
      powershell -ExecutionPolicy Bypass -File tools\setup-dev.ps1

  Installs (skips anything already installed):
    - .NET 10 SDK
    - Visual Studio Community 2026 with the ".NET desktop development" workload (WPF)
    - Git
    - Inno Setup 6
  and enables Windows Sandbox (needs Windows Pro/Enterprise; a restart is required afterwards).
  Visual Studio is a large download (several GB) - expect 20-60 minutes in total.
#>

$ErrorActionPreference = "Continue"

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# --- Admin check ---
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
           ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "Please run this script in an ADMIN PowerShell (right-click -> Run as administrator)." -ForegroundColor Red
    exit 1
}

# --- winget check ---
if (-not (Get-Command winget -ErrorAction SilentlyContinue)) {
    Write-Host "winget not found. Install 'App Installer' from the Microsoft Store, then re-run." -ForegroundColor Red
    exit 1
}

$common = @("--accept-package-agreements", "--accept-source-agreements", "--silent", "-e")

function Install-Pkg($id, $name, $extra = @()) {
    Step "Installing $name ($id)"
    $listed = winget list --id $id -e 2>$null | Select-String -SimpleMatch $id
    if ($listed) { Write-Host "$name already installed - skipping." -ForegroundColor Green; return }
    winget install --id $id @common @extra
    if ($LASTEXITCODE -ne 0) { Write-Host "${name}: winget exit code $LASTEXITCODE (see message above)." -ForegroundColor Yellow }
}

Install-Pkg "Microsoft.DotNet.SDK.10" ".NET 10 SDK"
Install-Pkg "Git.Git" "Git"
Install-Pkg "JRSoftware.InnoSetup" "Inno Setup 6"
Install-Pkg "Microsoft.VisualStudio.Community" "Visual Studio Community 2026" @(
    "--override",
    "--wait --passive --norestart --add Microsoft.VisualStudio.Workload.ManagedDesktop --includeRecommended"
)

# --- Windows Sandbox ---
Step "Enabling Windows Sandbox"
$edition = (Get-CimInstance Win32_OperatingSystem).Caption
if ($edition -match "Home") {
    Write-Host "This PC runs $edition. Windows Sandbox needs Pro/Enterprise - skipped." -ForegroundColor Yellow
    Write-Host "Alternative for installer tests: a Hyper-V or VirtualBox VM with a clean Windows." -ForegroundColor Yellow
} else {
    $f = Get-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM
    if ($f.State -eq "Enabled") {
        Write-Host "Windows Sandbox already enabled." -ForegroundColor Green
    } else {
        Enable-WindowsOptionalFeature -Online -FeatureName Containers-DisposableClientVM -All -NoRestart | Out-Null
        Write-Host "Windows Sandbox enabled - RESTART the PC before using it." -ForegroundColor Yellow
        Write-Host "(If it fails: turn on virtualization (VT-x/AMD-V) in the BIOS, then re-run.)"
    }
}

# --- Verify ---
Step "Verify (open a NEW PowerShell window if a command isn't found yet)"
$env:Path = [Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [Environment]::GetEnvironmentVariable("Path","User")
Write-Host "dotnet SDKs:";   dotnet --list-sdks
Write-Host "git:";           git --version
$iscc = @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") | Where-Object { Test-Path $_ } | Select-Object -First 1
Write-Host "Inno Setup:";    if ($iscc) { $iscc } else { "not found" }
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
Write-Host "Visual Studio:"; if (Test-Path $vswhere) { & $vswhere -latest -products * -requires Microsoft.VisualStudio.Workload.ManagedDesktop -property displayName } else { "not found" }
Write-Host "`nDone. Copy everything from '=== Verify' down and send it to Claude." -ForegroundColor Cyan
