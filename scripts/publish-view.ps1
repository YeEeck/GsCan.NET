# Publish GsCan View as a self-contained win-x64 zip.
# Native candle_api.dll stays a replaceable DLL next to the exe (LGPL).
# Does not produce an installer, a single-file bundle, or a win-x86 package.
$ErrorActionPreference = "Stop"

$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "src\GsCan.View\GsCan.View.csproj"
$NativeDll = Join-Path $RepoRoot "src\GsCan\runtimes\win-x64\native\candle_api.dll"
$ArtifactsDir = Join-Path $RepoRoot "artifacts"
$PublishDir = Join-Path $ArtifactsDir "GsCan.View-win-x64"
$ZipPath = Join-Path $ArtifactsDir "GsCan.View-win-x64.zip"

New-Item -ItemType Directory -Force -Path $ArtifactsDir | Out-Null
if (Test-Path $PublishDir) { Remove-Item -Recurse -Force $PublishDir }
if (Test-Path $ZipPath) { Remove-Item -Force $ZipPath }

dotnet publish $Project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $PublishDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

$exe = Join-Path $PublishDir "GsCan.View.exe"
if (-not (Test-Path $exe)) { throw "GsCan.View.exe missing from publish output: $PublishDir" }

$publishedDll = Join-Path $PublishDir "candle_api.dll"
if (-not (Test-Path $publishedDll)) {
    if (-not (Test-Path $NativeDll)) { throw "candle_api.dll missing at $NativeDll" }
    Copy-Item $NativeDll $publishedDll
}
if (-not (Test-Path $publishedDll)) { throw "candle_api.dll is not next to GsCan.View.exe" }

Copy-Item (Join-Path $RepoRoot "licenses\GPL-3.0.txt") (Join-Path $PublishDir "GPL-3.0.txt")
Copy-Item (Join-Path $RepoRoot "licenses\LGPL-3.0.txt") (Join-Path $PublishDir "LGPL-3.0.txt")
Copy-Item (Join-Path $RepoRoot "native\candle-api\SOURCE.txt") (Join-Path $PublishDir "SOURCE.txt")
Copy-Item (Join-Path $RepoRoot "src\GsCan.View\README.md") (Join-Path $PublishDir "README.md")

Compress-Archive -Path $PublishDir -DestinationPath $ZipPath

Write-Host "Published $ZipPath"
Write-Host "candle_api.dll next to GsCan.View.exe: $publishedDll"
