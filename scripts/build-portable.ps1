$ErrorActionPreference = "Stop"

Set-Location "$PSScriptRoot\..\src"

dotnet publish .\Vesper\Vesper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true

Write-Host "Portable build ready: src\Vesper\bin\Release\net8.0-windows\win-x64\publish\Vesper.exe"
