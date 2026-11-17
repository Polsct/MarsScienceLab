// Module lookup by name without KSP's "Cannot find module" log line (the status window scans every part).
namespace MSL
{
    internal static class Modules
    {
        // The part's module whose moduleName (the class name) is `name`, else null; never logs.
        internal static PartModule Named(Part part, string name)
        {
            if (part == null || string.IsNullOrEmpty(name)) return null;
            PartModuleList list = part.Modules;
            for (int i = 0; i < list.Count; i++)
                if (list[i] != null && list[i].moduleName == name) return list[i];
            return null;
        }

        // The part that carries the parachute module (RealChute, or the plugin's own without it): `part` itself, or
        // the parachute part mounted on it (MSL_AS_Parachute on the backshell's mortar node).
        internal static Part ChutePart(Part part)
        {
            if (part == null) return null;
            if (HasChute(part)) return part;
            for (int i = 0; i < part.children.Count; i++)
                if (HasChute(part.children[i])) return part.children[i];
            return null;
        }

        static bool HasChute(Part p)
        {
            return p != null && (Named(p, "RealChuteModule") != null || Named(p, "ModuleMSLParachute") != null);
        }
    }
}
