param(
    [string]$RuntimeIdentifier = "win-x64",
    [string]$TauriTarget = "x86_64-pc-windows-msvc"
)

$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot "tools\EamAssetExtractor\EamAssetExtractor.csproj"
$publishRoot = Join-Path $projectRoot "src-tauri\binaries\build\$TauriTarget"
$targetDirectory = Join-Path $projectRoot "src-tauri\binaries"
$extension = if ($RuntimeIdentifier.StartsWith("win", [System.StringComparison]::OrdinalIgnoreCase)) { ".exe" } else { "" }
$publishedBinary = Join-Path $publishRoot ("EamAssetExtractor" + $extension)
$targetBinary = Join-Path $targetDirectory ("eam-asset-extractor-$TauriTarget" + $extension)

New-Item -ItemType Directory -Force -Path $publishRoot, $targetDirectory | Out-Null

dotnet publish $project `
    --configuration Release `
    --framework net8.0 `
    --runtime $RuntimeIdentifier `
    --self-contained true `
    -p:PublishSingleFile=true `
    --output $publishRoot

if ($LASTEXITCODE -ne 0) {
    throw "The EAM asset extractor publish failed."
}

if (-not (Test-Path -LiteralPath $publishedBinary)) {
    throw "The published asset extractor was not found at $publishedBinary."
}

Copy-Item -LiteralPath $publishedBinary -Destination $targetBinary -Force
Write-Output "Prepared Tauri sidecar: $targetBinary"
