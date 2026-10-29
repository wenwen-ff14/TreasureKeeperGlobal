$ErrorActionPreference = 'Stop'
Push-Location (Split-Path $PSScriptRoot -Parent)
try {
    dotnet build TreasureKeeperGlobal/TreasureKeeperGlobal.csproj -c Release
    if ($LASTEXITCODE) { throw 'Build failed' }
    dotnet run --project tests/TreasureKeeperGlobal.Validation -c Release
    if ($LASTEXITCODE) { throw 'Validation failed' }
    $output = 'TreasureKeeperGlobal/bin/Release'
    $manifest = Get-Content "$output/TreasureKeeperGlobal.json" -Raw | ConvertFrom-Json
    if ($manifest.DalamudApiLevel -ne 15) { throw 'Expected API 15' }
    Copy-Item LICENSE.txt,README.md -Destination $output
    Copy-Item third-party -Destination $output -Recurse -Force
    New-Item dist -ItemType Directory -Force | Out-Null
    Compress-Archive -Path @("$output/TreasureKeeperGlobal.dll", "$output/TreasureKeeperGlobal.Core.dll", "$output/TreasureKeeperGlobal.deps.json", "$output/TreasureKeeperGlobal.json", "$output/LICENSE.txt", "$output/README.md", "$output/third-party") -DestinationPath "dist/TreasureKeeperGlobal-$($manifest.AssemblyVersion)-api15.zip" -Force
    & ./scripts/Prepare-PluginRepository.ps1 -PackagePath (Join-Path $PWD "dist/TreasureKeeperGlobal-$($manifest.AssemblyVersion)-api15.zip")
} finally { Pop-Location }


