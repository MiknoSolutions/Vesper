param(
    [string]$Version = "v1.0.0"
)

$ErrorActionPreference = "Stop"

$root = Resolve-Path "$PSScriptRoot\.."
$publishDir = Join-Path $root "src\Vesper\bin\Release\net8.0-windows\win-x64\publish"
$outDir = Join-Path $root "release"
$stagingDir = Join-Path $outDir "Vesper-portable-win-x64"
$zipPath = Join-Path $outDir "Vesper-portable-win-x64-$Version.zip"
$shaPath = "$zipPath.sha256"

if (!(Test-Path $publishDir)) {
    throw "Publish directory not found: $publishDir. Run scripts/build-portable.ps1 first."
}

if (Test-Path $stagingDir) {
    Remove-Item $stagingDir -Recurse -Force
}

New-Item -ItemType Directory -Path $stagingDir -Force | Out-Null
New-Item -ItemType Directory -Path $outDir -Force | Out-Null

Copy-Item (Join-Path $publishDir "Vesper.exe") -Destination $stagingDir -Force

$guideSource = Join-Path $root "docs\END_USER_GUIDE.md"
if (Test-Path $guideSource) {
    Copy-Item $guideSource -Destination (Join-Path $stagingDir "README.txt") -Force
}

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path (Join-Path $stagingDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

$hash = Get-FileHash $zipPath -Algorithm SHA256
"$($hash.Hash)  $(Split-Path -Leaf $zipPath)" | Out-File -FilePath $shaPath -Encoding ascii -Force

Write-Host "Release package created: $zipPath"
Write-Host "SHA256 file created: $shaPath"
