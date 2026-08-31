param(
    [Parameter(Mandatory, Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$propsPath = Join-Path $repo 'Directory.Build.props'
$manifestPath = Join-Path $repo 'manifest.yml'

$props = [IO.File]::ReadAllText($propsPath)
$manifest = [IO.File]::ReadAllText($manifestPath)

if ($props -notmatch '<Version>[^<]+</Version>') {
    throw 'Directory.Build.props does not contain a Version element.'
}
if ($manifest -notmatch '(?m)^version:') {
    throw 'manifest.yml does not contain a version field.'
}

[IO.File]::WriteAllText(
    $propsPath,
    ($props -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"))
[IO.File]::WriteAllText(
    $manifestPath,
    ($manifest -replace '(?m)^version:[^\r\n]*', "version: $Version"))

$propsVersion = ([xml][IO.File]::ReadAllText($propsPath)).Project.PropertyGroup.Version
$manifestVersion = [regex]::Match(
    [IO.File]::ReadAllText($manifestPath),
    '(?m)^version:[ \t]*(\S+)[ \t]*\r?$').Groups[1].Value
if ($propsVersion -ne $Version -or $manifestVersion -ne $Version) {
    throw 'Version synchronization failed.'
}

$dist = Join-Path $repo 'dist'
$zipPath = Join-Path $dist "EasyRhinoIFC-v$Version.zip"
New-Item -ItemType Directory -Path $dist -Force | Out-Null
Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue

Push-Location $repo
try {
    & .\build.bat
    if ($LASTEXITCODE -ne 0) {
        throw "build.bat failed with exit code $LASTEXITCODE."
    }

    dotnet run --project .\RhinoIfc.Tests\RhinoIfc.Tests.csproj -c Release --no-build
    if ($LASTEXITCODE -ne 0) {
        throw "Tests failed with exit code $LASTEXITCODE."
    }

    Compress-Archive -Path (Join-Path $repo 'yak_stage\*') -DestinationPath $zipPath -Force
    Write-Host "Release ready: $zipPath"
}
finally {
    Pop-Location
}
