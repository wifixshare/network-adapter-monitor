$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'NetworkCardMonitor\NetworkCardMonitor.csproj'
$output = Join-Path $PSScriptRoot 'publish'

Write-Host 'Building Windows application (offline-compatible)...' -ForegroundColor Cyan
& dotnet publish $project --configuration Release --self-contained false '-p:PublishSingleFile=false' '-p:DebugType=None' '-p:DebugSymbols=false' --output $output

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE. See the compiler errors above."
}

Write-Host ''
Write-Host (Join-Path $output 'NetworkCardMonitor.exe') -ForegroundColor Green
Write-Host 'Build completed. Keep all files in the publish folder together.'
Write-Host 'Run NetworkCardMonitor.exe once to enable startup automatically.'
