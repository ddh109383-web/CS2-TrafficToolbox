param([string]$ModRoot = [Environment]::GetEnvironmentVariable('CSII_LOCALMODSPATH','User'))
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($ModRoot)) { throw 'Missing CSII_LOCALMODSPATH.' }
$ModRoot = [IO.Path]::GetFullPath($ModRoot)
$dest = Join-Path $ModRoot RoadLaneLock
$source = Join-Path $PSScriptRoot 'bin/Release/net48'
$release = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot release-status.json) | ConvertFrom-Json
$manifestName = 'build-manifest.json'
$manifest = Get-Content -Raw -LiteralPath (Join-Path $source $manifestName) | ConvertFrom-Json
if ($release.Installable -ne $true -or $manifest.OfficialPostProcess -ne $true -or $release.Version -ne $manifest.Version) {
    throw 'Missing approved release or matching official build.'
}
$names = @('RoadLaneLock.dll','RoadLaneLock_win_x86_64.dll','RoadLaneLock.mjs','RoadLaneLock.svg','LaneChange.svg','Junction.svg','Recorder.svg','Transit.svg','Detour.svg','Parking.svg','RoadPreview.png','MonitorPreview.png','RecordPreview.png')
function MatchesBuild([string]$folder) {
    foreach ($name in $names) {
        $file = Join-Path $folder $name
        if (!(Test-Path -LiteralPath $file -PathType Leaf) -or (Get-FileHash -LiteralPath $file).Hash -ne $manifest.Files.$name) { return $false }
    }
    return $true
}
if (!(MatchesBuild $source)) { throw 'Package hash mismatch. Installation stopped.' }
$manifestHash = (Get-FileHash -LiteralPath (Join-Path $source $manifestName)).Hash
function MatchesInstall([string]$folder) {
    $file = Join-Path $folder $manifestName
    return ((Test-Path -LiteralPath $file -PathType Leaf) -and (Get-FileHash -LiteralPath $file).Hash -eq $manifestHash -and (MatchesBuild $folder))
}
if (MatchesInstall $dest) { Write-Output "Already installed and verified: RoadLaneLock $($manifest.Version)"; return }
function CheckGame {
    if (Get-Process Cities2 -ErrorAction SilentlyContinue) { throw 'Save and exit Cities II before installing.' }
}
CheckGame
# The asset database scans the entire game data directory, including siblings of Mods.
$gameData = Split-Path -Parent $ModRoot
$work = Join-Path (Split-Path -Parent $gameData) 'RoadLaneLock-Install'
[IO.Directory]::CreateDirectory($work) | Out-Null
$lock = [IO.File]::Open((Join-Path $work 'install.lock'),'OpenOrCreate','ReadWrite','None')
try {
    if (MatchesInstall $dest) { Write-Output "Already installed and verified: RoadLaneLock $($manifest.Version)"; return }
    $id = (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N')
    $stage = Join-Path $work "stage-$id"
    $backup = Join-Path $work "backup-$id"
    $hadOld = Test-Path -LiteralPath $dest -PathType Container
    [IO.Directory]::CreateDirectory($ModRoot) | Out-Null
    if ($hadOld) { Copy-Item -LiteralPath $dest -Destination $stage -Recurse -Force }
    else { [IO.Directory]::CreateDirectory($stage) | Out-Null }
    foreach ($name in ($names + $manifestName)) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $stage -Force }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot README.md) -Destination $stage -Force
    if (!(MatchesInstall $stage)) { throw 'Staged files failed verification.' }
    CheckGame
    if ($hadOld) { [IO.Directory]::Move($dest,$backup) }
    $promoted = $false
    try {
        [IO.Directory]::Move($stage,$dest)
        $promoted = $true
        if (!(MatchesInstall $dest)) { throw 'Installed files failed verification.' }
    } catch {
        if ($promoted) { [IO.Directory]::Move($dest,(Join-Path $work "failed-$id")) }
        if ($hadOld) { [IO.Directory]::Move($backup,$dest) }
        throw
    }
    Write-Output "Installed and verified: RoadLaneLock $($manifest.Version)"
    if ($hadOld) { Write-Output "Backup: $backup" }
} finally { $lock.Dispose() }


