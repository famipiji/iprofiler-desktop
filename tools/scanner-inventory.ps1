<#
  iProfiler Desktop - Task 0.1 scanner inventory helper.

  Run on each client PC that has a scanner attached (normal user is fine, no admin needed):
      powershell -ExecutionPolicy Bypass -File scanner-inventory.ps1 -Site "Client ABC"

  It only READS information (no changes to the PC) and writes one CSV next to this script:
      scanner-inventory_<site>_<computer>.csv
  Bring the CSVs back and copy the rows into TWAIN_Scanner_Inventory.xlsx.
#>
param([string]$Site = "unknown-site")

$ErrorActionPreference = "SilentlyContinue"
$rows = @()
$os = Get-CimInstance Win32_OperatingSystem
$osText = "$($os.Caption) $($os.OSArchitecture) build $($os.BuildNumber)"

# TWAIN Data Source Manager (TWAIN 2.x) presence, 32- and 64-bit
$dsm32 = Test-Path "$env:WINDIR\SysWOW64\TWAINDSM.dll"
$dsm64 = Test-Path "$env:WINDIR\System32\TWAINDSM.dll"

# TWAIN drivers: 32-bit sources live under twain_32, 64-bit under twain_64
foreach ($pair in @(@{Dir="$env:WINDIR\twain_32"; Bits="32-bit"}, @{Dir="$env:WINDIR\twain_64"; Bits="64-bit"})) {
    if (Test-Path $pair.Dir) {
        Get-ChildItem $pair.Dir -Recurse -Filter *.ds | ForEach-Object {
            $v = $_.VersionInfo
            $rows += [pscustomobject]@{
                Site            = $Site
                Computer        = $env:COMPUTERNAME
                OS              = $osText
                Interface       = "TWAIN"
                DriverBits      = $pair.Bits
                DeviceOrDriver  = ($v.ProductName, $v.FileDescription, $_.BaseName | Where-Object { $_ } | Select-Object -First 1)
                Vendor          = $v.CompanyName
                DriverVersion   = $v.FileVersion
                DriverFile      = $_.FullName
                TWAINDSM_32bit  = $dsm32
                TWAINDSM_64bit  = $dsm64
            }
        }
    }
}

# WIA / imaging devices currently known to Windows
Get-PnpDevice -Class Image | ForEach-Object {
    $rows += [pscustomobject]@{
        Site            = $Site
        Computer        = $env:COMPUTERNAME
        OS              = $osText
        Interface       = "WIA/PnP ($($_.Status))"
        DriverBits      = ""
        DeviceOrDriver  = $_.FriendlyName
        Vendor          = $_.Manufacturer
        DriverVersion   = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName DEVPKEY_Device_DriverVersion).Data
        DriverFile      = $_.InstanceId
        TWAINDSM_32bit  = $dsm32
        TWAINDSM_64bit  = $dsm64
    }
}

if (-not $rows) {
    Write-Host "No TWAIN drivers or imaging devices found. Is the scanner driver installed and the scanner plugged in?"
}
$safeSite = ($Site -replace '[^\w\-]', '_')
$out = Join-Path $PSScriptRoot "scanner-inventory_${safeSite}_$($env:COMPUTERNAME).csv"
$rows | Export-Csv -Path $out -NoTypeInformation -Encoding UTF8
$rows | Format-Table Interface, DriverBits, DeviceOrDriver, DriverVersion -AutoSize
Write-Host "`nSaved: $out"
