# Rebuilds the Beckhoff library DLLs this HMI references and copies them next to this script.
# Run it after changing the library, then rebuild Odyssey.Master.Beckhoff.sln.
#
#   powershell -ExecutionPolicy Bypass -File References\Beckhoff\Update-BeckhoffLibrary.ps1

param(
    [string]$LibraryRoot = 'C:\Users\karth\OneDrive\Desktop\Beckhoff_Library\Beckhoff_Library',
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found - Visual Studio with the .NET desktop workload is required.' }

# Teal.Library.Stepper references Contracts, Log and Twincat (Card), so building it builds all four.
$project = Join-Path $LibraryRoot 'Teal.Library.Stepper\Teal.Library.Stepper.csproj'
& $msbuild $project /t:Build "/p:Configuration=$Configuration" /v:minimal /nologo
if ($LASTEXITCODE -ne 0) { throw "Library build failed (MSBuild exit $LASTEXITCODE)." }

$output = Join-Path $LibraryRoot "Teal.Library.Stepper\bin\$Configuration"
foreach ($name in 'Teal.Library.Contracts', 'Teal.Library.Log', 'Teal.Library.Twincat', 'Teal.Library.Stepper') {
    Copy-Item (Join-Path $output "$name.dll") $PSScriptRoot -Force
    $pdb = Join-Path $output "$name.pdb"
    if (Test-Path $pdb) { Copy-Item $pdb $PSScriptRoot -Force }
}

Write-Host "Copied library DLLs from $output"
