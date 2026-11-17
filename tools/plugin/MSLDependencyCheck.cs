// Install check, shown once in the main menu when something is missing or misplaced: ModuleManager or Textures
// Unlimited not loaded, MSLSkyCrane.dll / MSLCuriosity.dll not loaded, mod not in GameData/MarsScienceLab,
// folders of older versions still present, or KSP not 1.12. Only lists problems; the log gets the same lines.
using System;
using System.Collections.Generic;
using System.IO;
using KSP.Localization;
using UnityEngine;

[assembly: KSPAssembly("MSLAeroshell", 1, 0)]

namespace MSLAeroshell
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class MSLDependencyCheck : MonoBehaviour
    {
        static readonly string[] OldFolders = { "MSL_Aeroshell", "MSL_CruiseStage", "MSL_SkyCrane", "MSL_Curiosity" };

        void Start()
        {
            Debug.Log("[MSL] Mars Science Lab " + MSLVersion.Text);
            List<string> problems = new List<string>();
            try { Collect(problems); }
            catch (Exception e) { Debug.LogWarning("[MSL] dependency check failed: " + e.Message); return; }
            if (problems.Count == 0) { Debug.Log("[MSL] dependency check: OK"); return; }
            foreach (string p in problems) Debug.LogWarning("[MSL] dependency check: " + p);
            PopupDialog.SpawnPopupDialog(new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), "MSLDependencyCheck",
                Localizer.Format("#MSL_Aeroshell_UI_Dep_title"),
                Localizer.Format("#MSL_Aeroshell_UI_Dep_intro") + "\n\n- " + string.Join("\n- ", problems.ToArray()),
                Localizer.Format("#MSL_Aeroshell_UI_Dep_ok"), false, HighLogic.UISkin);
        }

        static bool Loaded(string assemblyName)
        {
            foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                if (a.assembly != null && string.Equals(a.assembly.GetName().Name, assemblyName, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }

        static void Collect(List<string> problems)
        {
            if (!Loaded("ModuleManager")) problems.Add(Localizer.Format("#MSL_Aeroshell_UI_Dep_mm"));
            if (!Loaded("TexturesUnlimited")) problems.Add(Localizer.Format("#MSL_Aeroshell_UI_Dep_tu"));
            foreach (string dll in new[] { "MSLSkyCrane", "MSLCuriosity" })
                if (!Loaded(dll)) problems.Add(Localizer.Format("#MSL_Aeroshell_UI_Dep_dll", dll + ".dll"));

            string gameData = Path.GetFullPath(Path.Combine(KSPUtil.ApplicationRootPath, "GameData"));
            string here = Path.GetDirectoryName(typeof(MSLDependencyCheck).Assembly.Location);   // .../Plugins
            string expected = Path.Combine(Path.Combine(gameData, "MarsScienceLab"), "Plugins");
            if (here != null && !string.Equals(Path.GetFullPath(here).TrimEnd('\\', '/'), expected.TrimEnd('\\', '/'),
                                               StringComparison.OrdinalIgnoreCase))
                problems.Add(Localizer.Format("#MSL_Aeroshell_UI_Dep_path", here));

            List<string> old = new List<string>();
            foreach (string f in OldFolders)
                if (Directory.Exists(Path.Combine(gameData, f)) ||
                    Directory.Exists(Path.Combine(Path.Combine(gameData, "MarsScienceLab"), f)))
                    old.Add(f);
            if (old.Count > 0) problems.Add(Localizer.Format("#MSL_Aeroshell_UI_Dep_old", string.Join(", ", old.ToArray())));

            if (Versioning.version_major != 1 || Versioning.version_minor != 12)
                problems.Add(Localizer.Format("#MSL_Aeroshell_UI_Dep_ksp",
                    Versioning.version_major + "." + Versioning.version_minor + "." + Versioning.Revision));
        }
    }
}
