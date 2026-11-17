# Dot-sourced by the three build_plugin.ps1 scripts.  The .NET 4 csc.exe has no -deterministic switch (MVID and
# timestamp change on every compile), so a rebuild with unchanged sources used to leave a binary diff on every shipped
# DLL.  Invoke-CscCached compiles only when the C# sources or the compiler switches changed since the DLL was built, or
# when the DLL is not the one that was recorded; tools/plugin_stamps.json records both hashes (missing file = always compile).
# -Force (or MSL_PLUGIN_FORCE=1) always compiles, e.g. after a KSP update changed the reference assemblies.
$script:MSLPluginStamps = Join-Path $PSScriptRoot '..\plugin_stamps.json'

function Resolve-MSLManaged([string]$KspRoot) {
    # KSP install folder from -KspRoot or the KSP_ROOT environment variable; returns its KSP_x64_Data\Managed folder
    if (-not $KspRoot) { throw 'KSP folder not given: pass -KspRoot <KSP install folder> or set the KSP_ROOT environment variable' }
    $managed = Join-Path $KspRoot 'KSP_x64_Data\Managed'
    if (-not (Test-Path -LiteralPath (Join-Path $managed 'Assembly-CSharp.dll'))) { throw "Assembly-CSharp.dll not found under $managed (is $KspRoot a KSP install?)" }
    return $managed
}

function Get-MSLSourceKey([string[]]$CscArgs) {
    $lines = @()
    foreach ($a in ($CscArgs | Where-Object { $_ -like '*.cs' } | Sort-Object { Split-Path $_ -Leaf })) {
        # line endings normalised: *.cs is 'text' in .gitattributes, so checkouts may differ in CRLF/LF
        $text = [IO.File]::ReadAllText($a).Replace("`r`n", "`n")
        $sha = [Security.Cryptography.SHA256]::Create()
        $lines += (Split-Path $a -Leaf) + ':' + [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text))).Replace('-', '')
    }
    $lines += (($CscArgs | Where-Object { $_ -notlike '*.cs' -and $_ -notlike '-r:*' -and $_ -notlike '-out:*' }) -join ' ')
    $lines += (($CscArgs | Where-Object { $_ -like '-r:*' } | ForEach-Object { Split-Path $_ -Leaf } | Sort-Object) -join ' ')
    $sha = [Security.Cryptography.SHA256]::Create()
    return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($lines -join "`n"))).Replace('-', '')
}

function Read-MSLStamps {
    $map = [ordered]@{}
    if (Test-Path -LiteralPath $script:MSLPluginStamps) {
        $obj = Get-Content -Raw -LiteralPath $script:MSLPluginStamps | ConvertFrom-Json
        foreach ($p in $obj.PSObject.Properties) { $map[$p.Name] = @{ source = $p.Value.source; dll = $p.Value.dll } }
    }
    return $map
}

function Write-MSLStamps($map) {
    $rows = foreach ($k in ($map.Keys | Sort-Object)) { '  "{0}": {{"source": "{1}", "dll": "{2}"}}' -f $k, $map[$k].source, $map[$k].dll }
    [IO.File]::WriteAllText($script:MSLPluginStamps, "{`n" + ($rows -join ",`n") + "`n}`n")
}

function Invoke-CscCached([string]$Dest, [string[]]$CscArgs, [switch]$Force) {
    $name = Split-Path $Dest -Leaf
    $key = Get-MSLSourceKey $CscArgs
    $stamps = Read-MSLStamps
    $force = $Force -or $env:MSL_PLUGIN_FORCE -eq '1'
    if (-not $force -and (Test-Path -LiteralPath $Dest) -and $stamps.Contains($name) -and $stamps[$name].source -eq $key -and
        $stamps[$name].dll -eq (Get-FileHash -Algorithm SHA256 -LiteralPath $Dest).Hash) {
        Write-Host "$name up to date (sources unchanged), not recompiled"
        return
    }
    if ($env:MSL_PLUGIN_ADOPT -eq '1' -and (Test-Path -LiteralPath $Dest)) {
        # record the DLL that is already there as built from the current sources (seeding the stamps; no compile)
        Write-Host "$name adopted as built from the current sources"
    } else {
        & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') @CscArgs
        if ($LASTEXITCODE) { throw "$name compilation failed" }
        Write-Host "$name compiled"
    }
    $stamps[$name] = @{ source = $key; dll = (Get-FileHash -Algorithm SHA256 -LiteralPath $Dest).Hash }
    Write-MSLStamps $stamps
}
