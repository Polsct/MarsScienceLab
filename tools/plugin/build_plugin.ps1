# usage: build_plugin.ps1 -KspRoot <KSP install folder>   (or set the KSP_ROOT environment variable)
param([string]$KspRoot=$env:KSP_ROOT, [switch]$Force)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'csc_cached.ps1')
$managed=Resolve-MSLManaged $KspRoot
$dest=Join-Path $PSScriptRoot '..\..\GameData\MarsScienceLab\Plugins\MSLAeroshell.dll'
$refs=@('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll','Assembly-CSharp-firstpass.dll')
$argsList=@('-nologo','-noconfig','-nostdlib','-target:library','-optimize',('-out:'+$dest))
foreach($ref in $refs){$argsList+=('-r:'+(Join-Path $managed $ref))}
Get-ChildItem -LiteralPath $managed -Filter 'UnityEngine*.dll' | ForEach-Object {$argsList+=('-r:'+$_.FullName)}
Get-ChildItem -LiteralPath $PSScriptRoot -Filter 'Module*.cs' | ForEach-Object {$argsList+=$_.FullName}
$argsList+=(Join-Path $PSScriptRoot 'EntryRangePredictor.cs')
$argsList+=(Join-Path $PSScriptRoot 'MSLModelStorage.cs')
$argsList+=(Join-Path $PSScriptRoot 'MSLTangents.cs')
$argsList+=(Join-Path $PSScriptRoot 'MSLLocalization.cs')
$argsList+=(Join-Path $PSScriptRoot 'MSLModules.cs')
$argsList+=(Join-Path $PSScriptRoot 'MSLStatusWindow.cs')
$argsList+=(Join-Path $PSScriptRoot 'MSLContractText.cs')
$argsList+=(Join-Path $PSScriptRoot 'MSLScale.cs')
$argsList+=(Join-Path $PSScriptRoot 'MSLDependencyCheck.cs')
$argsList+=(Join-Path $PSScriptRoot 'MSLPartIcons.cs')
$argsList+=(Join-Path $PSScriptRoot 'MSLPartCategory.cs')
$argsList+=(Join-Path $PSScriptRoot 'MSLVersion.cs')
Invoke-CscCached $dest $argsList -Force:$Force
