param(
    [ValidateSet('win-x64', 'win-arm64', 'osx-arm64', 'osx-x64')]
    [string]$Runtime = 'win-x64'
)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskOutput = Join-Path $taskRoot "artifacts/$Runtime"
$taskProject = Join-Path $taskRoot 'src/ComTest/ComTest.csproj'

if ($Runtime.StartsWith('osx-')) {
    $taskBundle = Join-Path $taskOutput 'ComTest.app/Contents'
    $taskPublish = Join-Path $taskBundle 'MacOS'
    New-Item -ItemType Directory -Force -Path $taskBundle | Out-Null
    Copy-Item -LiteralPath (Join-Path $taskRoot 'packaging/Info.plist') -Destination $taskBundle
} else {
    $taskPublish = $taskOutput
}

dotnet publish $taskProject -c Release -r $Runtime --self-contained true -o $taskPublish
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE" }
Write-Output "Published ComTest to $taskOutput"
