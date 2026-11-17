// ModuleMSLShieldAero - drag of the jettisoned heat shield.
// FAR voxelizes the lone shield coarsely and fills the open dish, which gives non-physical forces that throw it
// back into the backshell. Once the shield's decoupler has fired, the part is shielded from FAR (reflection, no
// compile-time dependency) and gets F = 0.5 * rho * v^2 * CD(Mach) * referenceArea at its centre of mass, against
// the surface velocity. No lift or torque. Without FAR it does nothing unless onlyWithFAR = false.
using System;
using System.Reflection;
using UnityEngine;

namespace MSLAeroshell
{
    public class ModuleMSLShieldAero : PartModule
    {
        [KSPField] public float referenceArea = 15.904f;          // pi * 2.25^2 (D = 4.5 m)
        [KSPField] public FloatCurve cdMach = new FloatCurve();
        [KSPField] public bool onlyWithFAR = true;

        // Readouts kept for debugging, not shown on the part menu.
        const string Group = "mslShieldAero", GroupName = "#MSL_Aeroshell_UI_grp_ShieldAero";
        [KSPField(guiName = "#MSL_Aeroshell_UI_Shield_aero_status", groupName = Group, groupDisplayName = GroupName, groupStartCollapsed = true)]
        public string status = MSL.Localization.Text.Get("#MSL_Aeroshell_UI_standby");

        [KSPField(guiName = "#MSL_Aeroshell_UI_Shield_drag", guiUnits = " kN", guiFormat = "F2", groupName = Group, groupDisplayName = GroupName, groupStartCollapsed = true)]
        public float dragKN;

        ModuleDecouple decoupler;
        PartModule farModule;
        MethodInfo farSetShielded;
        bool active;
        readonly object[] shieldedArgs = new object[] { true };
        static readonly Func<Part, Vector3, Vector3> ZeroForce = (p, f) => Vector3.zero;

        public override void OnStart(StartState state)
        {
            decoupler = part.FindModuleImplementing<ModuleDecouple>();
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (cdMach.Curve.length == 0)
            {
                cdMach.Add(0f, 1.05f);                                  // fallback, see cfg
                cdMach.Add(2.09f, 1.368f);
                cdMach.Add(5.83f, 1.588f);
            }
            foreach (PartModule pm in part.Modules)
                if (pm.moduleName == "FARAeroPartModule") farModule = pm;
            if (farModule != null)
                farSetShielded = farModule.GetType().GetMethod("SetShielded", BindingFlags.Public | BindingFlags.Instance);
        }

        void Activate()
        {
            active = true;
            if (farModule != null)
            {
                PropertyInfo mod = farModule.GetType().GetProperty("AeroForceModifier", BindingFlags.Public | BindingFlags.Instance);
                if (mod != null && mod.CanWrite && mod.PropertyType == typeof(Func<Part, Vector3, Vector3>))
                    mod.SetValue(farModule, ZeroForce, null);
            }
            status = farModule != null ? MSL.Localization.Text.Get("#MSL_Aeroshell_UI_active_FAR_bypassed") : MSL.Localization.Text.Get("#MSL_Aeroshell_UI_active");
            Debug.Log("[MSLAeroshell] " + part.partInfo.title + ": jettisoned-shield aero " + status);
        }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || part.Rigidbody == null) return;
            if (!active)
            {
                if (decoupler == null || !decoupler.isDecoupled) return;
                if (onlyWithFAR && farModule == null) { status = MSL.Localization.Text.Get("#MSL_Aeroshell_UI_inactive_no_FAR"); enabled = false; return; }
                Activate();
            }
            if (farSetShielded != null) farSetShielded.Invoke(farModule, shieldedArgs);   // re-assert every frame
            Vector3d v = vessel.srf_velocity;
            double speed = v.magnitude;
            if (speed < 0.1 || vessel.atmDensity <= 0) { dragKN = 0; return; }
            double q = 0.5 * vessel.atmDensity * speed * speed;
            double kN = q * cdMach.Evaluate((float)vessel.mach) * referenceArea / 1000.0;   // rigidbody mass is in tonnes
            part.Rigidbody.AddForce(-(Vector3)(v / speed) * (float)kN, ForceMode.Force);
            dragKN = (float)kN;
        }

        public override string GetInfo()
        {
            return KSP.Localization.Localizer.Format("#MSL_Aeroshell_UI_After_jettison_replaces_FAR_aerodynamics_on_this_shield_with_a_CD_Mach_drag_model_reference_area_1_m2", referenceArea.ToString("F2"));
        }
    }
}
