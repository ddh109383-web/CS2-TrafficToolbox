param([switch]$PostProcess)
$ErrorActionPreference='Stop'
$env:MSBuildEnableWorkloadResolver='false'
$manifest=Join-Path $PSScriptRoot 'bin/Release/net48/build-manifest.json'
if(Test-Path -LiteralPath $manifest){Remove-Item -LiteralPath $manifest}
& dotnet build (Join-Path $PSScriptRoot 'RoadLaneLock.csproj') -c Release --nologo
if($LASTEXITCODE -ne 0){throw '编译失败'}
if($PostProcess){
 $processor=[Environment]::GetEnvironmentVariable('CSII_MODPOSTPROCESSORPATH','User')
 $unityProject=[Environment]::GetEnvironmentVariable('CSII_UNITYMODPROJECTPATH','User')
 $managed=[Environment]::GetEnvironmentVariable('CSII_MANAGEDPATH','User')
 New-Item -ItemType Directory -Force (Join-Path $PSScriptRoot 'bin/Release/net48/Temp')|Out-Null
 $arguments=@('PostProcess',(Join-Path $PSScriptRoot 'bin/Release/net48/RoadLaneLock.dll'),'-u',$unityProject)
 foreach($reference in Get-ChildItem -LiteralPath $managed -Filter '*.dll'){$arguments+=@('-r',$reference.FullName)}
 $arguments+=@('-p','Windows','-d')
 & $processor @arguments
 if($LASTEXITCODE -ne 0){throw '官方后处理失败'}
}
Copy-Item (Join-Path $PSScriptRoot 'ui/RoadLaneLock.mjs') (Join-Path $PSScriptRoot 'bin/Release/net48/RoadLaneLock.mjs') -Force
Copy-Item (Join-Path $PSScriptRoot 'ui/RoadLaneLock.svg') (Join-Path $PSScriptRoot 'bin/Release/net48/RoadLaneLock.svg') -Force
foreach($asset in @('LaneChange.svg','Junction.svg','Recorder.svg','Transit.svg','Detour.svg','Parking.svg','RoadPreview.png','MonitorPreview.png','RecordPreview.png')){Copy-Item (Join-Path $PSScriptRoot ('ui/'+$asset)) (Join-Path $PSScriptRoot 'bin/Release/net48') -Force}
if($PostProcess){
 $files=@{}
 foreach($name in @('RoadLaneLock.dll','RoadLaneLock_win_x86_64.dll','RoadLaneLock.mjs','RoadLaneLock.svg','LaneChange.svg','Junction.svg','Recorder.svg','Transit.svg','Detour.svg','Parking.svg','RoadPreview.png','MonitorPreview.png','RecordPreview.png')){
  $files[$name]=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot ('bin/Release/net48/'+$name)) -Algorithm SHA256).Hash
 }
 @{Version='0.3.8-alpha';OfficialPostProcess=$true;LiveVerified=$false;BuiltAt=(Get-Date -Format o);Files=$files}|ConvertTo-Json -Depth 4|Set-Content -LiteralPath $manifest -Encoding utf8
}







