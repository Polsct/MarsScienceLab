# usage: build_plugin.ps1 -KspRoot <KSP install folder>   (or set the KSP_ROOT environment variable)
param([string]$KspRoot=$env:KSP_ROOT, [switch]$Force)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\..\tools\plugin\csc_cached.ps1')
$managed=Resolve-MSLManaged $KspRoot
$dest=Join-Path $PSScriptRoot '..\..\GameData\MarsScienceLab\Plugins\MSLSkyCrane.dll'
$compilerArgs=@('-nologo','-noconfig','-nostdlib','-target:library','-optimize',('-out:'+$dest))
foreach($ref in @('mscorlib.dll','System.dll','System.Core.dll','Assembly-CSharp.dll')){$compilerArgs+=('-r:'+(Join-Path $managed $ref))}
Get-ChildItem -LiteralPath $managed -Filter 'UnityEngine*.dll' | ForEach-Object {$compilerArgs+=('-r:'+$_.FullName)}
Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' | ForEach-Object {$compilerArgs+=$_.FullName}
$compilerArgs+=(Join-Path $PSScriptRoot '..\..\tools\plugin\MSLLocalization.cs')
$compilerArgs+=(Join-Path $PSScriptRoot '..\..\tools\plugin\MSLModules.cs')
$compilerArgs+=(Join-Path $PSScriptRoot '..\..\tools\plugin\MSLVersion.cs')
Invoke-CscCached $dest $compilerArgs -Force:$Force
