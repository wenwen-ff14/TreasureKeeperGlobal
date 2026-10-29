#Requires -Version 7.0
param([switch]$Online)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$taskRepositoryUrl = 'https://raw.githubusercontent.com/wenwen-ff14/TreasureKeeperGlobal/main/pluginmaster.json'
$taskJson = Get-Content -LiteralPath (Join-Path $taskRoot 'pluginmaster.json') -Raw
if ($taskJson.TrimStart()[0] -ne '[') { throw 'Dalamud repository must be a JSON array.' }
$taskEntries = @($taskJson | ConvertFrom-Json)
if ($taskEntries.Count -ne 1) { throw 'Expected exactly one plugin entry.' }
$taskEntry = $taskEntries[0]
if ($taskEntry.InternalName -ne 'TreasureKeeperGlobal' -or $taskEntry.DalamudApiLevel -ne 15 -or
    $taskEntry.AssemblyVersion -notmatch '^\d+\.\d+\.\d+\.0$' -or $taskEntry.IsHide -ne $false -or $taskEntry.IsTestingExclusive -ne $false) {
    throw 'Invalid plugin identity, version, API level or visibility.'
}
$taskVersion = [string]$taskEntry.AssemblyVersion
$taskTag = 'v' + $taskVersion.Substring(0, $taskVersion.Length - 2)
$taskAsset = "TreasureKeeperGlobal-$taskVersion-api15.zip"
$taskExpectedUrl = "https://github.com/wenwen-ff14/TreasureKeeperGlobal/releases/download/$taskTag/$taskAsset"
foreach ($taskKey in @('DownloadLinkInstall','DownloadLinkUpdate','DownloadLinkTesting')) {
    if ($taskEntry.$taskKey -ne $taskExpectedUrl) { throw "Unexpected $taskKey" }
}
$taskPackage = Join-Path $taskRoot "dist/$taskAsset"
$taskZip = [IO.Compression.ZipFile]::OpenRead($taskPackage)
try {
    foreach ($taskName in @('TreasureKeeperGlobal.dll','TreasureKeeperGlobal.Core.dll','TreasureKeeperGlobal.deps.json','TreasureKeeperGlobal.json','LICENSE.txt')) {
        if ($null -eq $taskZip.GetEntry($taskName)) { throw "Missing root package entry: $taskName" }
    }
    foreach ($taskFile in $taskZip.Entries) {
        if ($taskFile.FullName -match '(^|/)(\.sdk|\.git|\.tools)/|\.pdb$|(^|/)\.\.(/|$)|^[/\\]' -or
            ($taskFile.FullName -match '\.dll$' -and $taskFile.FullName -notin @('TreasureKeeperGlobal.dll','TreasureKeeperGlobal.Core.dll'))) {
            throw "Unexpected package entry: $($taskFile.FullName)"
        }
    }
    $taskReader = [IO.StreamReader]::new($taskZip.GetEntry('TreasureKeeperGlobal.json').Open())
    try { $taskPacked = $taskReader.ReadToEnd() | ConvertFrom-Json } finally { $taskReader.Dispose() }
    foreach ($taskKey in @('InternalName','AssemblyVersion','DalamudApiLevel','RepoUrl')) {
        if ($taskPacked.$taskKey -ne $taskEntry.$taskKey) { throw "Repository differs from packaged $taskKey" }
    }
    $taskStream = $taskZip.GetEntry('TreasureKeeperGlobal.dll').Open()
    $taskMemory = [IO.MemoryStream]::new()
    try {
        $taskStream.CopyTo($taskMemory)
        $taskMemory.Position = 0
        $taskPe = [System.Reflection.PortableExecutable.PEReader]::new($taskMemory)
        try {
            $taskMetadata = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($taskPe)
            if ($taskMetadata.GetAssemblyDefinition().Version.ToString() -ne $taskVersion) { throw 'Packaged DLL version mismatch.' }
        } finally { $taskPe.Dispose() }
    } finally { $taskStream.Dispose(); $taskMemory.Dispose() }
} finally { $taskZip.Dispose() }
if ($Online) {
    $taskResponse = Invoke-WebRequest -Uri $taskRepositoryUrl -Headers @{ 'Cache-Control' = 'no-cache' }
    if (([string]$taskResponse.Content).Replace("`r`n", "`n") -cne $taskJson.Replace("`r`n", "`n")) { throw 'Public repository JSON differs from the prepared file.' }
    $taskHttp = [Net.Http.HttpClient]::new()
    try {
        $taskBytes = $taskHttp.GetByteArrayAsync($taskEntry.DownloadLinkInstall).GetAwaiter().GetResult()
        $taskRemoteHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($taskBytes))
        if ($taskRemoteHash -ne (Get-FileHash -LiteralPath $taskPackage -Algorithm SHA256).Hash) { throw 'Public runtime ZIP hash mismatch.' }
        Write-Output "Anonymous public download verified: SHA256 $taskRemoteHash"
    } finally { $taskHttp.Dispose() }
}
Write-Output "Verified TreasureKeeperGlobal ${taskVersion}: API 15, installation/update links and compiled runtime archive."


