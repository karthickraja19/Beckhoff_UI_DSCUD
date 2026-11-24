# Rebuilds the Beckhoff library DLLs this HMI references and copies them next to this script.
# Run it after changing the library, then rebuild Odyssey.Master.Beckhoff.sln.
#
#   powershell -ExecutionPolicy Bypass -File References\Beckhoff\Update-BeckhoffLibrary.ps1

param(
    # The main library source (Master.sln). D:\BeckhOffLibrary\BeckhOffLibrary is the older copy.
    [string]$LibraryRoot = 'D:\Beckhoff_Karthi\Latest - Beckhoff\Beckhoff_Lib-class-implementation-m\Beckhoff_Lib-class-implementation-m',
    [string]$Configuration = 'Debug'
)

$ErrorActionPreference = 'Stop'

$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$msbuild = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild not found - Visual Studio with the .NET desktop workload is required.' }

# Each device project references Contracts, Log and Twincat (Card), so building the five device
# projects builds all eight. The Twincat DLL is built from Teal.Library.Twincat\Teal.Library.Card.csproj.
$devices = 'Cylinder', 'Servo', 'Stepper', 'TcpIp', 'Vacuum'
foreach ($device in $devices) {
    $project = Join-Path $LibraryRoot "Teal.Library.$device\Teal.Library.$device.csproj"
    # SolutionDir: the projects' post-build step copies each DLL to $(SolutionDir)Library.
    # (two backslashes: one alone before the closing quote would escape it on the command line)
    & $msbuild $project /t:Build "/p:Configuration=$Configuration" "/p:SolutionDir=$LibraryRoot\\" /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "Library build failed for $device (MSBuild exit $LASTEXITCODE)." }
}

# Assembly name -> project folder.
$assemblies = [ordered]@{
    'Teal.Library.Contracts' = 'Teal.Library.Contracts'
    'Teal.Library.Log'       = 'Teal.Library.Logger'
    'Teal.Library.Twincat'   = 'Teal.Library.Twincat'
    'Teal.Library.Cylinder'  = 'Teal.Library.Cylinder'
    'Teal.Library.Servo'     = 'Teal.Library.Servo'
    'Teal.Library.Stepper'   = 'Teal.Library.Stepper'
    'Teal.Library.TcpIp'     = 'Teal.Library.TcpIp'
    'Teal.Library.Vacuum'    = 'Teal.Library.Vacuum'
}

foreach ($name in $assemblies.Keys) {
    $output = Join-Path $LibraryRoot "$($assemblies[$name])\bin\$Configuration"
    Copy-Item (Join-Path $output "$name.dll") $PSScriptRoot -Force
    $pdb = Join-Path $output "$name.pdb"
    if (Test-Path $pdb) { Copy-Item $pdb $PSScriptRoot -Force }
}

Write-Host "Copied $($assemblies.Count) library DLLs from $LibraryRoot"
