<#
  Emergency removal: puts the PC back the way it was before BackMaster, even if the MSI
  is missing or the uninstall fails half-way. Run from an elevated PowerShell:

    powershell -ExecutionPolicy Bypass -File .\scripts\Remove-BackMaster.ps1            # remove everything
    powershell -ExecutionPolicy Bypass -File .\scripts\Remove-BackMaster.ps1 -KeepData  # keep policy.json + phrase
    powershell -ExecutionPolicy Bypass -File .\scripts\Remove-BackMaster.ps1 -DryRun    # only report (no admin needed)

  Every step is safe to repeat. The hosts file is backed up before it is changed.
#>
param(
    [switch]$KeepData,
    [switch]$DryRun
)

$ErrorActionPreference = "Continue"
$UpgradeCode = "{485687A7-624F-4B4C-A25D-328CCE31939B}"
$Hosts = Join-Path $env:SystemRoot "System32\drivers\etc\hosts"
$DataDir = Join-Path $env:ProgramData "BackMaster"
$BeginMarker = "# BEGIN BackMaster (managed automatically - do not edit)"
$EndMarker = "# END BackMaster"

function Step([string]$text) { Write-Host "==> $text" -ForegroundColor Cyan }
function Act([string]$text, [scriptblock]$action) {
    if ($DryRun) { Write-Host "    would: $text" -ForegroundColor Yellow }
    else { Write-Host "    $text"; & $action }
}

$elevated = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $elevated -and -not $DryRun) {
    Write-Host "Run this from an elevated PowerShell (right-click > Run as administrator)." -ForegroundColor Red
    exit 1
}

Step "Tray"
Get-Process BackMaster.Tray -ErrorAction SilentlyContinue | ForEach-Object {
    Act "stop tray process $($_.Id)" { Stop-Process -Id $_.Id -Force }
}

Step "Windows Installer package"
$installer = New-Object -ComObject WindowsInstaller.Installer
$products = @($installer.GetType().InvokeMember("RelatedProducts", "GetProperty", $null, $installer, @($UpgradeCode)))
if ($products.Count -eq 0) { Write-Host "    not installed" }
foreach ($code in $products) {
    Act "msiexec /x $code" {
        $p = Start-Process msiexec.exe -ArgumentList "/x", $code, "/qb", "/norestart" -Wait -PassThru
        Write-Host "    msiexec exit code $($p.ExitCode) (0 or 3010 = OK)"
    }
}

Step "Service (in case the uninstall did not remove it)"
$svc = Get-Service BackMaster -ErrorAction SilentlyContinue
if ($svc) {
    Act "stop and delete the BackMaster service" {
        Stop-Service BackMaster -Force -ErrorAction SilentlyContinue
        sc.exe delete BackMaster | Out-Null
    }
} else { Write-Host "    not present" }
Get-Process BackMaster.Service -ErrorAction SilentlyContinue | ForEach-Object {
    Act "kill leftover service process $($_.Id)" { Stop-Process -Id $_.Id -Force }
}

Step "Hosts file"
$text = [IO.File]::ReadAllText($Hosts, [Text.Encoding]::GetEncoding(28591))
if ($text.Contains($BeginMarker)) {
    $lines = $text -split "`r?`n"
    $inside = $false
    $kept = foreach ($line in $lines) {
        if (-not $inside -and $line.Trim() -eq $BeginMarker) { $inside = $true; continue }
        if ($inside) { if ($line.Trim() -eq $EndMarker) { $inside = $false }; continue }
        $line
    }
    $removed = ($lines.Count - @($kept).Count)
    Act "remove BackMaster section ($removed lines), backup at hosts.backmaster-backup" {
        Copy-Item $Hosts "$Hosts.backmaster-backup" -Force
        Set-ItemProperty $Hosts -Name IsReadOnly -Value $false
        [IO.File]::WriteAllText($Hosts, ($kept -join "`r`n"), [Text.Encoding]::GetEncoding(28591))
        ipconfig /flushdns | Out-Null
    }
} else { Write-Host "    no BackMaster section" }

Step "Registry (autostart + browser DNS policies set by the installer)"
$values = @(
    @("HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run", "BackMasterTray"),
    @("HKLM:\SOFTWARE\Policies\Google\Chrome", "DnsOverHttpsMode"),
    @("HKLM:\SOFTWARE\Policies\Microsoft\Edge", "DnsOverHttpsMode"),
    @("HKLM:\SOFTWARE\Policies\Google\Chrome", "BuiltInDnsClientEnabled"),
    @("HKLM:\SOFTWARE\Policies\Microsoft\Edge", "BuiltInDnsClientEnabled"),
    @("HKLM:\SOFTWARE\Policies\BraveSoftware\Brave", "DnsOverHttpsMode"),
    @("HKLM:\SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS", "Enabled"),
    @("HKLM:\SOFTWARE\Policies\Mozilla\Firefox\DNSOverHTTPS", "Locked")
)
foreach ($v in $values) {
    if (Get-ItemProperty -Path $v[0] -Name $v[1] -ErrorAction SilentlyContinue) {
        Act "delete $($v[0])\$($v[1])" { Remove-ItemProperty -Path $v[0] -Name $v[1] -Force }
    }
}
if (Test-Path "HKLM:\SOFTWARE\BackMaster") { Act "delete HKLM\SOFTWARE\BackMaster" { Remove-Item "HKLM:\SOFTWARE\BackMaster" -Recurse -Force } }

Step "DNS lookup log (turned on by BackMaster for the fox)"
if (Test-Path (Join-Path $DataDir "dnslog-enabled-by-backmaster")) {
    Act "turn the Microsoft-Windows-DNS-Client/Operational log off again" {
        wevtutil.exe sl Microsoft-Windows-DNS-Client/Operational /e:false
    }
} else { Write-Host "    not changed by BackMaster" }

Step "Data folder $DataDir"
if (-not (Test-Path $DataDir)) { Write-Host "    not present" }
elseif ($KeepData) { Write-Host "    kept (-KeepData)" }
else {
    Act "take ownership and delete $DataDir" {
        takeown.exe /f $DataDir /r /d y | Out-Null
        icacls.exe $DataDir /reset /t /q | Out-Null
        Remove-Item $DataDir -Recurse -Force
    }
}

Step "Program files"
$programDir = Join-Path $env:ProgramFiles "BackMaster"
if (Test-Path $programDir) { Act "delete $programDir" { Remove-Item $programDir -Recurse -Force } } else { Write-Host "    not present" }

Write-Host ""
Write-Host $(if ($DryRun) { "Dry run finished; nothing was changed." } else { "Done. Restart the browsers (chrome://policy should no longer list DnsOverHttpsMode)." }) -ForegroundColor Green
