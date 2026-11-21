# Builds Release|x64 into an empty folder, stages the files and compiles the installer.
# Also writes the portable zip next to the installer.
#
#   powershell -ExecutionPolicy Bypass -File installer\build-installer.ps1 [-PsExec <path>]
#
# PsExec.exe is not in the repository (Sysinternals licence). By default it is taken
# from AsusFanControlGUI\PsExec.exe or bin\x64\Release\PsExec.exe.
param(
    [string]$PsExec
)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$here = $PSScriptRoot

function Find-Tool($candidates, $name) {
    foreach ($c in $candidates) { if ($c -and (Test-Path $c)) { return (Resolve-Path $c).Path } }
    throw "$name not found. Looked in:`n  " + ($candidates -join "`n  ")
}

$msbuild = Find-Tool @(
    (& "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe" -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null | Select-Object -First 1)
) 'MSBuild.exe'
$iscc = Find-Tool @("${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe", "$env:ProgramFiles\Inno Setup 6\ISCC.exe", "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe") 'Inno Setup 6 (ISCC.exe)'
$psexecPath = Find-Tool @($PsExec, "$root\AsusFanControlGUI\PsExec.exe", "$root\bin\x64\Release\PsExec.exe") 'PsExec.exe'

$work = Join-Path $here 'work'
$stage = Join-Path $here 'stage'
$output = Join-Path $here 'Output'
foreach ($d in $work, $stage) { if (Test-Path $d) { Remove-Item $d -Recurse -Force } }
New-Item -ItemType Directory $stage, $output -Force | Out-Null

Write-Host "Building Release|x64..."
& $msbuild "$root\AsusFanControl.sln" -t:Rebuild -p:Configuration=Release -p:Platform=x64 `
    "-p:OutDir=$work\out\" "-p:BaseIntermediateOutputPath=$work\obj\" -v:m -nologo
if ($LASTEXITCODE -ne 0) { throw "Build failed." }

$files = 'AsusFanControlGUI.exe', 'AsusFanControlGUI.exe.config', 'AsusFanControl.exe',
         'AsusFanControl.exe.config', 'AsusWinIO64.dll', 'run.bat'
foreach ($f in $files) { Copy-Item (Join-Path "$work\out" $f) $stage }
Copy-Item $psexecPath (Join-Path $stage 'PsExec.exe')
Copy-Item (Join-Path $here 'README.txt') $stage

$version = (Get-Item (Join-Path $stage 'AsusFanControlGUI.exe')).VersionInfo.FileVersion
$short = ($version -split '\.')[0..2] -join '.'

Write-Host "Compiling installer for v$short..."
& $iscc /Q (Join-Path $here 'AsusFanControl.iss')
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed." }

$zip = Join-Path $output "AsusFanControl-x64-Release-v$short.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path "$stage\*" -DestinationPath $zip

Remove-Item $work -Recurse -Force
Write-Host ""
Get-ChildItem $output | Where-Object Name -like "*v$short*" | ForEach-Object { "{0}  {1:N0} bytes" -f $_.FullName, $_.Length }
