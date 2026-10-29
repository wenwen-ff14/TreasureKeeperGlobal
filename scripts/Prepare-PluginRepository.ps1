#Requires -Version 7.0
param([Parameter(Mandatory)][string]$PackagePath)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskZip = [IO.Compression.ZipFile]::OpenRead($PackagePath)
try {
    $taskManifestEntry = $taskZip.GetEntry('TreasureKeeperGlobal.json')
    if ($null -eq $taskManifestEntry) { throw 'Runtime archive is missing its root manifest.' }
    $taskReader = [IO.StreamReader]::new($taskManifestEntry.Open())
    try { $taskManifest = $taskReader.ReadToEnd() | ConvertFrom-Json -AsHashtable } finally { $taskReader.Dispose() }
} finally { $taskZip.Dispose() }
$taskVersion = [string]$taskManifest.AssemblyVersion
if ($taskVersion -notmatch '^\d+\.\d+\.\d+\.0$' -or $taskManifest.InternalName -ne 'TreasureKeeperGlobal' -or $taskManifest.DalamudApiLevel -ne 15) {
    throw 'Invalid plugin version, identity or Dalamud API level.'
}
$taskTag = 'v' + $taskVersion.Substring(0, $taskVersion.Length - 2)
$taskRepo = 'https://github.com/wenwen-ff14/TreasureKeeperGlobal'
if ($taskManifest.RepoUrl -ne $taskRepo) { throw 'Packaged repository URL does not match.' }
$taskAsset = "TreasureKeeperGlobal-$taskVersion-api15.zip"
if ([IO.Path]::GetFileName($PackagePath) -ne $taskAsset) { throw 'Unexpected runtime package filename.' }
$taskUrl = "$taskRepo/releases/download/$taskTag/$taskAsset"
$taskManifest.DownloadLinkInstall = $taskUrl
$taskManifest.DownloadLinkUpdate = $taskUrl
$taskManifest.DownloadLinkTesting = $taskUrl
$taskManifest.IsHide = $false
$taskManifest.IsTestingExclusive = $false
$taskManifest.LastUpdate = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
$taskManifest.Changelog = '國際服首次發布：API 15 / .NET 10，獨立設定、國際服寶物庫辨識；保留丟棄、補錄及寶圖解讀。尚待遊戲內驗收。'
$taskJson = ConvertTo-Json -InputObject @($taskManifest) -Depth 10
[IO.File]::WriteAllText((Join-Path $taskRoot 'pluginmaster.json'), $taskJson + "`n", [Text.UTF8Encoding]::new($false))
& (Join-Path $PSScriptRoot 'Verify-PluginRepository.ps1')

