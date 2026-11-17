# usage: build_plugin.ps1 -KspRoot <KSP install folder>   (or set the KSP_ROOT environment variable)
param([string]$KspRoot=$env:KSP_ROOT, [switch]$Force)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\tools\plugin\csc_cached.ps1')
$managed=Resolve-MSLManaged $KspRoot
$dest=Join-Path $PSScriptRoot '..\GameData\MarsScienceLab\Plugins\MSLCuriosity.dll'
$refs=@('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll')
$argsList=@('-nologo','-noconfig','-nostdlib','-target:library','-optimize',('-out:'+$dest))
foreach($ref in $refs){$argsList+=('-r:'+(Join-Path $managed $ref))}
Get-ChildItem -LiteralPath $managed -Filter 'UnityEngine*.dll' | ForEach-Object {$argsList+=('-r:'+$_.FullName)}
$argsList+=(Join-Path $PSScriptRoot 'plugin\ModuleCuriosityRig.cs')
$argsList+=(Join-Path $PSScriptRoot 'plugin\ModuleCuriosityArmOps.cs')
$argsList+=(Join-Path $PSScriptRoot 'plugin\ModuleCuriosityComponent.cs')
$argsList+=(Join-Path $PSScriptRoot 'plugin\ModuleCuriosityExperiment.cs')
$argsList+=(Join-Path $PSScriptRoot 'plugin\ModuleCuriosityRTG.cs')
$argsList+=(Join-Path $PSScriptRoot '..\tools\plugin\MSLLocalization.cs')
$argsList+=(Join-Path $PSScriptRoot '..\tools\plugin\MSLVersion.cs')
Invoke-CscCached $dest $argsList -Force:$Force
Get-Item -LiteralPath $dest | Select-Object FullName,Length
