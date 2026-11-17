// Mod version, compiled into all three plugins. Keep in step with GameData/MarsScienceLab/MarsScienceLab.version
// and the KSPAssembly attributes (MSLDependencyCheck.cs, MSLSkyCrane.cs, ModuleCuriosityComponent.cs).
using System.Reflection;

[assembly: AssemblyProduct("Mars Science Lab")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Polsct, MIT License")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
[assembly: AssemblyInformationalVersion("1.0.0")]

internal static class MSLVersion
{
    public const string Text = "1.0.0";
}
