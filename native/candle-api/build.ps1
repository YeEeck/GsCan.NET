# Build candle_api.dll for win-x64 and (when possible) win-x86.
param(
    [string]$Configuration = "Release",
    [string]$OutRoot = "$PSScriptRoot\..\..\src\GsCan\runtimes"
)

$ErrorActionPreference = "Stop"
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (-not (Test-Path $vswhere)) { throw "vswhere.exe not found" }

$vs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
if (-not $vs) { throw "Visual Studio C++ tools not found" }

function Invoke-ArchBuild([string]$Arch) {
    $buildDir = Join-Path $PSScriptRoot "build-$Arch"
    $rid = if ($Arch -eq "x64") { "win-x64" } else { "win-x86" }
    $dest = Join-Path $OutRoot "$rid\native"
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    if (Test-Path $buildDir) { Remove-Item -Recurse -Force $buildDir }

    Write-Host "=== Configuring $Arch ==="
    cmake -S $PSScriptRoot -B $buildDir -A $Arch -DCMAKE_BUILD_TYPE=$Configuration
    if ($LASTEXITCODE -ne 0) { throw "cmake configure failed for $Arch" }

    Write-Host "=== Building $Arch ==="
    cmake --build $buildDir --config $Configuration
    if ($LASTEXITCODE -ne 0) { throw "cmake build failed for $Arch" }

    $dll = Join-Path $buildDir "$Configuration\candle_api.dll"
    if (-not (Test-Path $dll)) {
        $dll = Get-ChildItem -Path $buildDir -Filter candle_api.dll -Recurse | Select-Object -First 1 -ExpandProperty FullName
    }
    if (-not $dll -or -not (Test-Path $dll)) { throw "candle_api.dll not found after $Arch build" }
    Copy-Item $dll (Join-Path $dest "candle_api.dll") -Force
    Write-Host "Installed: $(Join-Path $dest 'candle_api.dll')"
}

Invoke-ArchBuild "x64"
try {
    Invoke-ArchBuild "Win32"
} catch {
    Write-Warning "x86 (Win32) build failed: $_"
}
