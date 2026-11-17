// Editor part list: the PCC lid + mortar part (MSL_AS_Parachute) is listed with the parachutes. RealChute's
// "Parachutes" filter picks parts by module info, so the part gets a "Parachute" module info once loaded.
using UnityEngine;

namespace MSLAeroshell
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class MSLPartCategory : MonoBehaviour
    {
        static readonly string[] Parachutes = { "MSL.AS.Parachute" };

        void Start()
        {
            foreach (string name in Parachutes)
            {
                AvailablePart ap = PartLoader.getPartInfoByName(name);
                if (ap == null) { Debug.LogWarning("[MSL] parachute category: no part " + name); continue; }
                if (ap.moduleInfos.Exists(m => m.moduleName == "Parachute" || m.moduleName == "RealChute")) continue;
                ap.moduleInfos.Add(new AvailablePart.ModuleInfo
                {
                    moduleName = "Parachute",
                    moduleDisplayName = MSL.Localization.Text.Get("#MSL_Aeroshell_UI_Parachute_info_title"),
                    info = MSL.Localization.Text.Get("#MSL_Aeroshell_UI_Parachute_info")
                });
            }
        }
    }
}
