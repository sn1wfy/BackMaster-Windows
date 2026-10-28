<#
  Builds, tests and packages BackMaster.
    .\build.ps1                 tests + self-contained executables + artifacts\BackMaster.msi
    .\build.ps1 -SkipTests      the same without running tests
    .\build.ps1 -SkipInstaller  stop after publishing the executables
  Needs only the .NET 8 SDK or later; WiX comes from NuGet.
#>
param(
    [string]$Configuration = "Release",
    [string]$Version = "0.3.0",
    [switch]$SkipTests,
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$publish = Join-Path $root "artifacts\publish"

function Invoke-Step([string]$name, [scriptblock]$block) {
    Write-Host "==> $name" -ForegroundColor Cyan
    & $block
    if ($LASTEXITCODE -ne 0) { throw "$name failed (exit code $LASTEXITCODE)." }
}

if (-not $SkipTests) {
    Invoke-Step "Tests" { dotnet test (Join-Path $root "BackMaster.sln") -c $Configuration }
}

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
$common = @("-c", $Configuration, "-r", "win-x64", "--self-contained", "true",
            "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true",
            "-p:EnableCompressionInSingleFile=true", "-p:Version=$Version", "-o", $publish)
Invoke-Step "Publish service" { dotnet publish (Join-Path $root "src\BackMaster.Service\BackMaster.Service.csproj") @common }
Invoke-Step "Publish tray" { dotnet publish (Join-Path $root "src\BackMaster.Tray\BackMaster.Tray.csproj") @common }

if (-not $SkipInstaller) {
    Invoke-Step "Installer" {
        dotnet build (Join-Path $root "installer\BackMaster.Installer.wixproj") -c $Configuration `
            "-p:Version=$Version" "-p:PublishDir=$publish" -o (Join-Path $root "artifacts")
    }
    Write-Host "Installer: $(Join-Path $root 'artifacts\BackMaster.msi')" -ForegroundColor Green
}
