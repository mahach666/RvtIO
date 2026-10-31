<#
.SYNOPSIS
    Packs release assets into artifacts: binary archives of library and CLI, source zip, NuGet packages and SHA256SUMS.txt.
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$props = [xml](Get-Content -LiteralPath (Join-Path $root 'Directory.Build.props') -Raw)
$versionNode = $props.SelectSingleNode('/Project/PropertyGroup/Version')
if (-not $versionNode) {
    $versionNode = $props.SelectSingleNode('/Project/PropertyGroup/VersionPrefix')
}
$version = if ($versionNode) { $versionNode.InnerText } else { $null }
if (-not $version) { throw "Version or VersionPrefix property not found in Directory.Build.props" }

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path
$stage = Join-Path $Output 'stage'
if (Test-Path -LiteralPath $stage) { Remove-Item -LiteralPath $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

function Invoke-DotNet {
    & dotnet @args
    if ($LASTEXITCODE -ne 0) { throw "dotnet $($args -join ' ') failed with exit code $LASTEXITCODE" }
}

function New-Zip([string]$SourceDir, [string]$ZipPath) {
    if (Test-Path -LiteralPath $ZipPath) { Remove-Item -LiteralPath $ZipPath }
    Compress-Archive -Path (Join-Path $SourceDir '*') -DestinationPath $ZipPath
}

# 1. RvtIO Library assemblies (net8.0 and net10.0 with all dependencies)
Write-Host "Publishing RvtIO library..."
$libStage = Join-Path $stage 'lib'
$libNet8 = Join-Path $libStage 'net8.0'
$libNet10 = Join-Path $libStage 'net10.0'
Invoke-DotNet publish (Join-Path $root 'src\RvtIO\RvtIO.csproj') -c Release -f net8.0 -o $libNet8
Invoke-DotNet publish (Join-Path $root 'src\RvtIO\RvtIO.csproj') -c Release -f net10.0 -o $libNet10
Copy-Item (Join-Path $root 'README.md') $libStage
Copy-Item (Join-Path $root 'LICENSE') $libStage
Copy-Item (Join-Path $root 'THIRD-PARTY-NOTICES.md') $libStage
New-Zip $libStage (Join-Path $Output "RvtIO-$version.zip")

# 2. CLI (.NET 8 Runtime)
Write-Host "Publishing RvtIO.Cli (net8.0)..."
$cliStage = Join-Path $stage 'cli-net8'
Invoke-DotNet publish (Join-Path $root 'src\RvtIO.Cli\RvtIO.Cli.csproj') -c Release -f net8.0 -o $cliStage
if (Test-Path (Join-Path $cliStage 'RvtIO.Cli.exe')) {
    Copy-Item (Join-Path $cliStage 'RvtIO.Cli.exe') (Join-Path $cliStage 'rvtio.exe')
    Copy-Item (Join-Path $cliStage 'RvtIO.Cli.runtimeconfig.json') (Join-Path $cliStage 'rvtio.runtimeconfig.json')
}
Copy-Item (Join-Path $root 'README.md') $cliStage
Copy-Item (Join-Path $root 'LICENSE') $cliStage
New-Zip $cliStage (Join-Path $Output "RvtIO-Cli-$version.zip")

# 3. CLI (.NET 10 Runtime)
Write-Host "Publishing RvtIO.Cli (net10.0)..."
$cli10Stage = Join-Path $stage 'cli-net10'
Invoke-DotNet publish (Join-Path $root 'src\RvtIO.Cli\RvtIO.Cli.csproj') -c Release -f net10.0 -o $cli10Stage
if (Test-Path (Join-Path $cli10Stage 'RvtIO.Cli.exe')) {
    Copy-Item (Join-Path $cli10Stage 'RvtIO.Cli.exe') (Join-Path $cli10Stage 'rvtio.exe')
    Copy-Item (Join-Path $cli10Stage 'RvtIO.Cli.runtimeconfig.json') (Join-Path $cli10Stage 'rvtio.runtimeconfig.json')
}
Copy-Item (Join-Path $root 'README.md') $cli10Stage
Copy-Item (Join-Path $root 'LICENSE') $cli10Stage
New-Zip $cli10Stage (Join-Path $Output "RvtIO-Cli-$version-net10.0.zip")

# 4. NuGet packages
Write-Host "Packing NuGet packages..."
Invoke-DotNet pack (Join-Path $root 'RvtIO.slnx') -c Release --no-build -o $Output

# 5. Source code archive
Write-Host "Creating source code archive..."
$sourceZip = Join-Path $Output "RvtIO-$version-source.zip"
if (Test-Path -LiteralPath $sourceZip) { Remove-Item -LiteralPath $sourceZip }
& git -C $root archive --format=zip --prefix=RvtIO/ --output=$sourceZip HEAD
if ($LASTEXITCODE -ne 0) { throw "git archive failed with exit code $LASTEXITCODE" }

Remove-Item -LiteralPath $stage -Recurse -Force

# 6. Checksums
Write-Host "Computing SHA256..."
$files = @(Get-ChildItem -LiteralPath $Output -File | Where-Object { $_.Extension -in '.zip', '.nupkg', '.snupkg' } | Sort-Object Name)
$sums = $files | ForEach-Object { '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name }
Set-Content -LiteralPath (Join-Path $Output 'SHA256SUMS.txt') -Value $sums -Encoding Ascii
$sums
