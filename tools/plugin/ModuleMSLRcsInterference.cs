// ModuleMSLRcsInterference - aerodynamic interference of the descent-stage RCS jets with the capsule wake.
// FAR and stock aero do not see thruster plumes. Each physics frame the realised RCS torque T about the centre
// of mass is summed from ModuleRCS.thrustForces and
//     dT = q / (q + qHalf) * ( gainMach(M) * T  +  couplingMach(M) * |T| * a_alpha )
// is applied to this part (a_alpha: unit axis turning the capsule axis toward the flow). The curves in the cfg
// are estimates. The jets' own thrust stays with ModuleRCS.
using System.Collections.Generic;
using UnityEngine;

namespace MSLAeroshell
{
    public class ModuleMSLRcsInterference : PartModule
    {
        // Always on (no part-menu switch); the flight readouts stay off the part menu.
        const string Group = "mslGuidance", GroupName = "#MSL_Aeroshell_UI_grp_Guidance";

        [KSPField] public FloatCurve gainMach = new FloatCurve();
        [KSPField] public FloatCurve couplingMach = new FloatCurve();
        [KSPField] public float qHalf = 0.5f;                      // kPa, interaction needs aftbody pressure
        [KSPField] public string rcsPartPrefix = "MSL.SC.";        // descent-stage RCS parts
        [KSPField] public string stagePartName = "MSL.SkyCrane";

        [KSPField(guiName = "#MSL_Aeroshell_UI_Interference_status", groupName = Group, groupDisplayName = GroupName, groupStartCollapsed = false)]
        public string status = MSL.Localization.Text.Get("#MSL_Aeroshell_UI_standby");
        [KSPField(guiName = "#MSL_Aeroshell_UI_Interference_torque", guiUnits = " kN m", guiFormat = "F3", groupName = Group, groupDisplayName = GroupName, groupStartCollapsed = false)]
        public float torqueKNm;
        [KSPField(guiName = "#MSL_Aeroshell_UI_Interference_RCS", guiFormat = "P0", groupName = Group, groupDisplayName = GroupName, groupStartCollapsed = false)]
        public float ratio;

        readonly List<ModuleRCS> rcs = new List<ModuleRCS>();
        Vessel topologyVessel;
        int topologyPartCount = -1;

        public override void OnStart(StartState state)
        {
            if (gainMach.Curve.length == 0)
            {
                gainMach.Add(0f, 0.05f); gainMach.Add(3f, 0.05f); gainMach.Add(5f, 0f);
                gainMach.Add(10f, 0.08f); gainMach.Add(18f, 0.15f); gainMach.Add(30f, 0.15f);
            }
            if (couplingMach.Curve.length == 0)
            {
                couplingMach.Add(0f, 0f); couplingMach.Add(5f, 0f); couplingMach.Add(10f, 0.03f);
                couplingMach.Add(18f, 0.05f); couplingMach.Add(30f, 0.05f);
            }
        }

        void Refresh()
        {
            if (topologyVessel == vessel && topologyPartCount == vessel.parts.Count) return;
            topologyVessel = vessel;
            topologyPartCount = vessel.parts.Count;
            rcs.Clear();
            foreach (Part p in vessel.parts)
            {
                if (p.partInfo == null) continue;
                string n = p.partInfo.name;
                if (n == stagePartName || n.StartsWith(rcsPartPrefix)) rcs.AddRange(p.FindModulesImplementing<ModuleRCS>());
            }
        }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || part.Rigidbody == null || vessel.packed) return;
            torqueKNm = 0f;
            ratio = 0f;
            Refresh();
            if (rcs.Count == 0) { status = MSL.Localization.Text.Get("#MSL_Aeroshell_UI_no_descent_stage_RCS"); return; }
            double q = vessel.dynamicPressurekPa;
            if (q <= 1e-4 || vessel.srfSpeed < 1) { status = MSL.Localization.Text.Get("#MSL_Aeroshell_UI_no_flow"); return; }
            Vector3 com = vessel.CoM;
            Vector3 T = Vector3.zero;
            for (int i = 0; i < rcs.Count; i++)
            {
                ModuleRCS m = rcs[i];
                if (m.thrustForces == null || !m.rcsEnabled || m.flameout) continue;
                for (int k = 0; k < m.thrusterTransforms.Count && k < m.thrustForces.Length; k++)
                {
                    float f = m.thrustForces[k];
                    if (f <= 0f) continue;
                    Transform t = m.thrusterTransforms[k];
                    Vector3 dir = m.useZaxis ? t.forward : t.up;            // exhaust; thrust is opposite
                    T += Vector3.Cross(t.position - com, -dir * f);
                }
            }
            float mag = T.magnitude;
            if (mag < 1e-6f) { status = MSL.Localization.Text.Get("#MSL_Aeroshell_UI_jets_off"); return; }
            float mach = (float)vessel.mach;
            float ramp = (float)(q / (q + qHalf));
            Vector3 dT = T * (gainMach.Evaluate(mach) * ramp);
            // capsule axis: the heat-shield (top) node side, +Y of the backshell; it should face the flow
            Vector3 axis = part.transform.up;
            Vector3 into = -(Vector3)vessel.srf_velocity.normalized;
            Vector3 k2 = Vector3.Cross(axis, into);
            if (k2.sqrMagnitude > 1e-6f) dT += k2.normalized * (mag * couplingMach.Evaluate(mach) * ramp);
            part.Rigidbody.AddTorque(dT, ForceMode.Force);              // kN m on a tonne rigid body
            torqueKNm = dT.magnitude;
            ratio = torqueKNm / mag;
            status = KSP.Localization.Localizer.Format("#MSL_Aeroshell_UI_active_M_1", mach.ToString("F1"));
        }

        public override string GetInfo()
        {
            return MSL.Localization.Text.Get("#MSL_Aeroshell_UI_RcsInterference_Info");
        }
    }
}
