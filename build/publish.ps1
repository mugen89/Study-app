<#
.SYNOPSIS
  Builds the release package: a portable folder + zip with StudyApp.exe (self-contained, single file),
  the SQLite native library and StudyApp.Updater.exe.

.EXAMPLE
  ./build/publish.ps1 -Version 1.2.0
  # → artifacts/StudyApp-1.2.0-win-x64.zip (+ .sha256) and artifacts/StudyApp/ (the unzipped folder)
#>
param(
    [string]$Version = "",
    [string]$Output = "artifacts",
    [string]$Dotnet = "dotnet"
)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

if (-not $Version) {
    $props = [xml](Get-Content (Join-Path $root "Directory.Build.props"))
    $Version = $props.Project.PropertyGroup.Version
}
$Version = $Version.TrimStart('v')
Write-Host "Publishing Study App $Version"

$out = Join-Path $root $Output
$folder = Join-Path $out "StudyApp"
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $folder | Out-Null

& $Dotnet publish src/StudyApp.App/StudyApp.App.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=false `
    -p:DebugType=embedded -p:Version=$Version -o $folder
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$updaterOut = Join-Path $out "updater"
& $Dotnet build src/StudyApp.Updater/StudyApp.Updater.csproj -c Release -p:Version=$Version -o $updaterOut
if ($LASTEXITCODE -ne 0) { throw "updater build failed" }
Copy-Item (Join-Path $updaterOut "StudyApp.Updater.exe") $folder
if (Test-Path (Join-Path $updaterOut "StudyApp.Updater.exe.config")) { Copy-Item (Join-Path $updaterOut "StudyApp.Updater.exe.config") $folder }
Remove-Item $updaterOut -Recurse -Force

# Nothing besides the program belongs in the package (never ship a data folder).
Get-ChildItem $folder -Filter *.pdb | Remove-Item -Force
if (Test-Path (Join-Path $folder "data")) { Remove-Item (Join-Path $folder "data") -Recurse -Force }

$zip = Join-Path $out "StudyApp-$Version-win-x64.zip"
Compress-Archive -Path (Join-Path $folder "*") -DestinationPath $zip -CompressionLevel Optimal
$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "$zip.sha256" -Value "$hash  $(Split-Path $zip -Leaf)" -NoNewline -Encoding ascii

Write-Host ""
Write-Host "Package contents:"
Get-ChildItem $folder | ForEach-Object { "  {0,-32} {1,8:N1} MB" -f $_.Name, ($_.Length / 1MB) }
Write-Host ("Zip: {0} ({1:N1} MB)" -f $zip, ((Get-Item $zip).Length / 1MB))
Write-Host "SHA256: $hash"
