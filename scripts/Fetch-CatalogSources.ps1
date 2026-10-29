param([string]$Commit = 'f43013b5f535e5b54bcb6f6f2fc4f646ea77696f')
$ErrorActionPreference = 'Stop'
if ($Commit -notmatch '^[0-9a-f]{40}$') { throw 'Supply a full Teamcraft commit SHA.' }
$root = Split-Path $PSScriptRoot -Parent
$cache = Join-Path $root '.cache/teamcraft'
$wiki = Join-Path $root '.cache/wiki'
New-Item -ItemType Directory -Force -Path $cache,$wiki | Out-Null
foreach ($file in @('libs/data/src/lib/json/instances.json', 'libs/data/src/lib/json/loot-sources.json', 'libs/data/src/lib/extracts/extracts.json', 'libs/data/src/lib/json/items.json')) {
    Invoke-WebRequest "https://raw.githubusercontent.com/ffxiv-teamcraft/ffxiv-teamcraft/$Commit/$file" -OutFile (Join-Path $cache (Split-Path $file -Leaf))
}
Set-Content -LiteralPath (Join-Path $cache 'commit.txt') -Value $Commit
Invoke-WebRequest "https://raw.githubusercontent.com/ffxiv-teamcraft/ffxiv-teamcraft/$Commit/LICENSE" -OutFile (Join-Path $root 'third-party/Teamcraft-LICENSE')
$sources = Get-Content -LiteralPath (Join-Path $root 'docs/catalog-sources.json') -Raw | ConvertFrom-Json
foreach ($source in $sources.Wiki) {
    Invoke-WebRequest $source.RevisionUrl -OutFile (Join-Path $wiki ($source.Page + '.html'))
}
foreach ($source in $sources.Historical) {
    $target = Join-Path $root $source.CachePath
    New-Item -ItemType Directory -Force -Path (Split-Path $target -Parent) | Out-Null
    Invoke-WebRequest $source.Url -OutFile $target
}
Write-Output 'Downloaded pinned sources. Run CatalogBuilder against the local game sqpack directory.'
