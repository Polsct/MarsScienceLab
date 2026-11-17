#!/usr/bin/env bash
# Compile the MSL_Aeroshell runtime plugin (shipped with the mod) against a KSP install.
# usage: tools/plugin/build_plugin.sh <KSP install folder>
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
sources=()
for f in "$HERE"/Module*.cs; do sources+=("$(cygpath -w "$f")"); done
sources+=("$(cygpath -w "$HERE/EntryRangePredictor.cs")")
sources+=("$(cygpath -w "$HERE/MSLModelStorage.cs")")
sources+=("$(cygpath -w "$HERE/MSLTangents.cs")")
sources+=("$(cygpath -w "$HERE/MSLLocalization.cs")")
sources+=("$(cygpath -w "$HERE/MSLModules.cs")")
sources+=("$(cygpath -w "$HERE/MSLStatusWindow.cs")")
sources+=("$(cygpath -w "$HERE/MSLContractText.cs")")
sources+=("$(cygpath -w "$HERE/MSLScale.cs")")
sources+=("$(cygpath -w "$HERE/MSLDependencyCheck.cs")")
sources+=("$(cygpath -w "$HERE/MSLPartIcons.cs")")
sources+=("$(cygpath -w "$HERE/MSLPartCategory.cs")")
sources+=("$(cygpath -w "$HERE/MSLVersion.cs")")
"$CSC" -noconfig -nologo -target:library -optimize -nostdlib -out:"$(cygpath -w "$OUT/MSLAeroshell.dll")" \
  "${refs[@]}" "${sources[@]}" 2>&1 | iconv -f GBK -t UTF-8
ls -la "$OUT/MSLAeroshell.dll"
