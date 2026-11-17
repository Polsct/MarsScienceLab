// Editor icons: the backshell and the parachute are modelled apex-down, so their icon prefab is turned 180 deg.
using System.Collections.Generic;
using UnityEngine;

namespace MSLAeroshell
{
    [KSPAddon(KSPAddon.Startup.MainMenu, true)]
    public class MSLPartIcons : MonoBehaviour
    {
        // KSP part names ('.' for '_')
        static readonly string[] Flip = { "MSL.Backshell", "MSL.AS.Parachute" };
        // transforms switched off in the icon prefab before the flip pivot is measured
        static readonly string[] HideInIcon = { "MSL_DGB_canopy" };

        void Start()
        {
            foreach (string name in Flip)
            {
                AvailablePart ap = PartLoader.getPartInfoByName(name);
                if (ap == null || ap.iconPrefab == null) { Debug.LogWarning("[MSL] icon flip: no icon for " + name); continue; }
                Transform root = ap.iconPrefab.transform;
                // the packed canopy is not part of the icon (stock chutes show only the case)
                foreach (Transform t in ap.iconPrefab.GetComponentsInChildren<Transform>(true))
                    if (t != root && System.Array.IndexOf(HideInIcon, t.name) >= 0) t.gameObject.SetActive(false);
                bool any = false;
                Bounds b = new Bounds();
                // only what the icon shows: counting the canopy moved the pivot ~0.7 m below the lid and the
                // turn threw the visible model out of its icon cell onto the neighbouring parts
                foreach (MeshFilter f in ap.iconPrefab.GetComponentsInChildren<MeshFilter>(false))
                {
                    if (f.sharedMesh == null) continue;
                    MeshRenderer r = f.GetComponent<MeshRenderer>();
                    if (r == null || !r.enabled) continue;
                    Bounds mb = f.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 p = root.InverseTransformPoint(f.transform.TransformPoint(c));
                        if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
                    }
                }
                if (!any) { Debug.LogWarning("[MSL] icon flip: no meshes in " + name); continue; }
                Vector3 centre = root.TransformPoint(b.center), axis = root.TransformDirection(Vector3.forward);
                var kids = new List<Transform>();
                foreach (Transform c in root) kids.Add(c);
                foreach (Transform c in kids) c.RotateAround(centre, axis, 180f);
                Debug.Log("[MSL] icon flip: " + name + " turned 180 deg about " + b.center.ToString("F3"));
            }
        }
    }
}
