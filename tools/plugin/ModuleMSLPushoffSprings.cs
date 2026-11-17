// Cruise-stage push-off springs. The stock decoupler carries no ejection force; once the part on the separation
// node has left the vessel, three springs push the two bodies apart at their plungers, force falling linearly to
// zero over the stroke, so a turning stack gets an uneven push rather than one centred impulse.
// [GAME] springForce and stroke give ~0.4 m/s between cruise stage and entry vehicle.
using System;
using System.Globalization;
using UnityEngine;

namespace MSLAeroshell
{
    public class ModuleMSLPushoffSprings : PartModule
    {
        [KSPField] public string nodeID = "top";
        [KSPField] public float springForce = 0.5f;      // kN per spring, fully compressed
        [KSPField] public float stroke = 0.05f;          // m, at 1:1
        [KSPField] public float radius = 0.607f;         // m, plunger axis from the part axis, at 1:1
        [KSPField] public float height = -0.1815f;       // m, plunger tip along the part's up axis, at 1:1
        [KSPField] public string azimuths = "90,210,330"; // deg about the up axis, from +X toward +Z
        [KSPField(isPersistant = true)] public bool released;

        Part mate;                 // the part on the separation node while it is still attached
        Vector3[] tip, seat;       // plunger tips in this part's frame, their seats in the mate's frame
        bool pushing;
        float pushTime;

        public override void OnStart(StartState state)
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            string[] a = azimuths.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
            float s = part.rescaleFactor;
            tip = new Vector3[a.Length];
            for (int i = 0; i < a.Length; i++)
            {
                float az = float.Parse(a[i].Trim(), CultureInfo.InvariantCulture) * Mathf.Deg2Rad;
                tip[i] = new Vector3(radius * Mathf.Cos(az), height, radius * Mathf.Sin(az)) * s;
            }
        }

        void FindMate()
        {
            AttachNode n = part.FindAttachNode(nodeID);
            Part p = n != null ? n.attachedPart : null;
            if (p == null || p.vessel != vessel) return;
            mate = p;
            seat = new Vector3[tip.Length];
            for (int i = 0; i < tip.Length; i++)
                seat[i] = mate.transform.InverseTransformPoint(part.transform.TransformPoint(tip[i]));
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || released || tip == null || vessel == null || vessel.packed) return;
            if (!pushing)
            {
                if (mate == null) { FindMate(); return; }
                if (mate.vessel == vessel) return;                    // still joined
                pushing = true; pushTime = 0f;
            }
            if (mate == null || mate.vessel == null || mate.vessel.packed) { released = true; return; }
            float len = stroke * part.rescaleFactor, percent = 1f;
            ModuleDecouple sep = part.FindModuleImplementing<ModuleDecouple>();
            if (sep != null) percent = sep.ejectionForcePercent / 100f;   // the decoupler's VAB slider scales the springs
            Vector3 up = part.transform.up;
            bool any = false;
            for (int i = 0; i < tip.Length; i++)
            {
                Vector3 a = part.transform.TransformPoint(tip[i]), b = mate.transform.TransformPoint(seat[i]);
                float x = Vector3.Dot(b - a, up);
                if (x >= len) continue;                               // this plunger has left its seat
                any = true;
                Vector3 f = up * (springForce * percent * (1f - Mathf.Max(0f, x) / len));
                mate.AddForceAtPosition(f, b);
                part.AddForceAtPosition(-f, a);
            }
            pushTime += TimeWarp.fixedDeltaTime;
            if (!any || pushTime > 5f) { released = true; pushing = false; }
        }

        public override string GetInfo()
        {
            return "";
        }
    }
}
