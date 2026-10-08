$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot 'src/HollowKnightPet.csproj'
$outputPath = Join-Path $PSScriptRoot 'app'
dotnet publish $projectPath -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -o $outputPath
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host "Built: $outputPath/HollowKnightPet.exe"
