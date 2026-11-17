#!/usr/bin/env bash
# Compile the MSL_SkyCrane runtime plugin (shipped with the mod) against a KSP install.
# usage: SkyCrane/plugin/build_plugin.sh <KSP install folder>
set -euo pipefail
KSP="${1:?KSP root}"
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
M="$KSP/KSP_x64_Data/Managed"
CSC="/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe"
OUT="$ROOT/GameData/MarsScienceLab/Plugins"
mkdir -p "$OUT"
refs=()
for f in "$M"/mscorlib.dll "$M"/System.dll "$M"/System.Core.dll "$M"/Assembly-CSharp.dll "$M"/Assembly-CSharp-firstpass.dll "$M"/UnityEngine*.dll; do
  refs+=("-r:$(cygpath -w "$f")")
done
"$CSC" -noconfig -nologo -target:library -optimize -nostdlib -out:"$(cygpath -w "$OUT/MSLSkyCrane.dll")" \
  "${refs[@]}" "$(cygpath -w "$HERE/MSLSkyCrane.cs")" "$(cygpath -w "$HERE/ModuleMSLPoweredDescent.cs")" "$(cygpath -w "$HERE/DescentMixer.cs")" "$(cygpath -w "$ROOT/tools/plugin/MSLLocalization.cs")" "$(cygpath -w "$ROOT/tools/plugin/MSLModules.cs")" "$(cygpath -w "$ROOT/tools/plugin/MSLVersion.cs")" 2>&1 | iconv -f GBK -t UTF-8
ls -la "$OUT/MSLSkyCrane.dll"
