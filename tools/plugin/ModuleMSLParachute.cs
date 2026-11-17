// ModuleMSLParachute - the MSL disk-gap-band parachute when RealChute is not installed.
// Stock ModuleParachute cannot take the flown area and CD, and its altitude logic does not fit a mortar fired on a
// speed trigger. Same model as the RealChute setup: F = 0.5 * rho * v^2 * CD0 * A against the surface velocity at
// the canopy transform; A = pi/4 * preDeployedDiameter^2 at line stretch, then a linear ramp to pi/4 * D0^2 over
// the inflation time. Added by Patches/MSL_Parachute.cfg only :NEEDS[!RealChute]. Not modelled: heating, line
// loads, failure, repacking.
// Fired from the part menu, an action group or the EDL sequencer (deploys in atmosphere while descending,
// otherwise arms and fires once that holds). Not staged.
using System;
using UnityEngine;

namespace MSLAeroshell
{
    public class ModuleMSLParachute : PartModule, IPartMassModifier, IPartCostModifier
    {
        [KSPField] public string canopyName = "MSL_DGB_canopy";
        [KSPField] public string capName = "PCC_Lid";
        [KSPField] public string preDeploymentAnimation = "MSL_DGB_line_stretch";
        [KSPField] public string deploymentAnimation = "MSL_DGB_inflate";
        [KSPField] public float preDeployedDiameter = 3f;
        [KSPField] public float deployedDiameter = 21.35f;
        [KSPField] public float dragCoefficient = 0.624f;
        [KSPField] public float preDeploymentSpeed = 1.135f;    // s, mortar fire to line stretch
        [KSPField] public float deploymentSpeed = 0.635f;       // s, line stretch to full inflation
        [KSPField] public float packMass = 0.0575f;             // t, added to the part (RealChute adds the same)
        [KSPField] public float packCost = 27f;
        [KSPField] public float cutSpeed = 0.5f;                // m/s, cut once landed and slower than this
        [KSPField] public bool mustGoDown = true;

        // STOWED, ARMED, PREDEPLOYED, DEPLOYED, CUT
        [KSPField(isPersistant = true)] public string chuteState = "STOWED";
        [KSPField(isPersistant = true)] public double deployUT = -1;
        // Part menu group "Parachute", shared with ModuleMSLParachuteMessage; UpdateMenu shows the one button that applies.
        const string Group = "mslChute", GroupName = "#MSL_Aeroshell_UI_grp_Parachute";
        [KSPField(guiActive = true, guiActiveEditor= false, guiName = "#MSL_Aeroshell_UI_Chute_status", groupName = Group, groupDisplayName = GroupName)] public string status = "";
        [KSPField(guiActive = true, guiActiveEditor= false, guiName = "#MSL_Aeroshell_UI_Drag", guiUnits = " kN", guiFormat = "F1", groupName = Group, groupDisplayName = GroupName)] public float dragKN;

        Transform canopy, cap;
        Animation[] animators;
        bool inflateStarted;

        public bool Deployed { get { return chuteState == "PREDEPLOYED" || chuteState == "DEPLOYED"; } }
        public bool oneWasDeployed { get { return Deployed || chuteState == "CUT"; } }   // same name as RealChute's field

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            // part prefab: keep the canopy out of the generated stock drag cubes
            if (HighLogic.LoadedScene == GameScenes.LOADING) { Transform c = part.FindModelTransform(canopyName); if (c != null) c.gameObject.SetActive(false); }
        }

        public override void OnStart(StartState state)
        {
            canopy = part.FindModelTransform(canopyName);
            cap = part.FindModelTransform(capName);
            animators = part.FindModelAnimators(preDeploymentAnimation);
            if (!HighLogic.LoadedSceneIsFlight) { ShowCanopy(false); ShowCap(true); status = Text("#MSL_Aeroshell_UI_chute_stowed"); return; }
            if (chuteState == "PREDEPLOYED" || chuteState == "DEPLOYED")
            {
                ShowCanopy(true); ShowCap(false);
                if (chuteState == "DEPLOYED") PoseInflated(); else StartStretch(Planetarium.GetUniversalTime() - deployUT);
            }
            else { ShowCanopy(false); ShowCap(chuteState != "CUT"); }
            UpdateStatus();
        }

        static string Text(string key) { return MSL.Localization.Text.Get(key); }
        void ShowCanopy(bool on) { if (canopy != null && canopy.gameObject.activeSelf != on) canopy.gameObject.SetActive(on); }
        void ShowCap(bool on) { if (cap != null && cap.gameObject.activeSelf != on) cap.gameObject.SetActive(on); }

        void Play(string clip, float duration, float normalizedTime)
        {
            if (animators == null) return;
            foreach (Animation a in animators)
            {
                AnimationState s = a[clip];
                if (s == null) continue;
                s.wrapMode = WrapMode.ClampForever;
                s.speed = duration > 0 ? 1f / duration : 0f;
                s.normalizedTime = Mathf.Clamp01(normalizedTime);
                a.Play(clip);
            }
        }
        void StartStretch(double elapsed)
        {
            inflateStarted = false;
            Play(preDeploymentAnimation, preDeploymentSpeed, (float)(elapsed / preDeploymentSpeed));
        }
        void PoseInflated()
        {
            inflateStarted = true;
            Play(deploymentAnimation, 0f, 1f);
        }

        bool CanOpen()
        {
            return vessel != null && vessel.atmDensity > 0 && !vessel.LandedOrSplashed && (!mustGoDown || vessel.verticalSpeed < 0);
        }

        [KSPEvent(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 5, guiName = "#MSL_Aeroshell_UI_Fire_mortar", groupName = Group, groupDisplayName = GroupName)]
        public void Deploy()
        {
            if (!HighLogic.LoadedSceneIsFlight || Deployed || chuteState == "CUT") return;
            if (!CanOpen()) { chuteState = "ARMED"; UpdateStatus(); return; }
            chuteState = "PREDEPLOYED";
            deployUT = Planetarium.GetUniversalTime();
            ShowCap(false); ShowCanopy(true); Aim();
            StartStretch(0);
            Debug.Log("[MSLAeroshell] " + part.partInfo.title + ": mortar fire (fallback parachute), v=" + vessel.srfSpeed.ToString("F1") + " m/s, alt=" + vessel.altitude.ToString("F0") + " m");
            UpdateStatus();
        }
        [KSPAction("#MSL_Aeroshell_UI_Fire_mortar")] public void DeployAction(KSPActionParam p) { Deploy(); }

        [KSPEvent(guiActive = true, guiActiveUnfocused = true, unfocusedRange = 5, guiName = "#MSL_Aeroshell_UI_Cut_parachute", groupName = Group, groupDisplayName = GroupName)]
        public void Cut()
        {
            if (chuteState == "STOWED" || chuteState == "ARMED") { chuteState = "STOWED"; UpdateStatus(); return; }
            if (!Deployed) return;
            chuteState = "CUT"; dragKN = 0;
            ShowCanopy(false);
            UpdateStatus();
        }
        [KSPAction("#MSL_Aeroshell_UI_Cut_parachute")] public void CutAction(KSPActionParam p) { Cut(); }

        void UpdateStatus()
        {
            switch (chuteState)
            {
                case "ARMED": status = Text("#MSL_Aeroshell_UI_chute_armed"); break;
                case "PREDEPLOYED": status = Text("#MSL_Aeroshell_UI_chute_line_stretch"); break;
                case "DEPLOYED": status = Text("#MSL_Aeroshell_UI_chute_deployed"); break;
                case "CUT": status = Text("#MSL_Aeroshell_UI_chute_cut"); break;
                default: status = Text("#MSL_Aeroshell_UI_chute_stowed"); break;
            }
            if (!HighLogic.LoadedSceneIsFlight) return;
            Events["Deploy"].guiActive = chuteState == "STOWED";
            Events["Cut"].guiActive = chuteState == "ARMED" || Deployed;     // armed: cancels the arming
            Fields["dragKN"].guiActive = Deployed;
            Fields["status"].guiActive = chuteState != "STOWED";           // stowed: the mortar button says it
            MonoUtilities.RefreshPartContextWindow(part);
        }

        void Aim()
        {
            if (canopy == null || vessel == null) return;
            Vector3 v = (Vector3)vessel.srf_velocity;
            if (v.sqrMagnitude < 0.01f) return;
            Vector3 aft = -v.normalized;
            Vector3 up = Vector3.ProjectOnPlane(part.transform.forward, aft);
            if (up.sqrMagnitude < 1e-4f) up = Vector3.ProjectOnPlane(part.transform.right, aft);
            canopy.rotation = Quaternion.LookRotation(aft, up);
        }

        static double Area(double d) { return Math.PI * 0.25 * d * d; }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) return;
            if (chuteState == "ARMED" && CanOpen()) Deploy();
            if (!Deployed || part.Rigidbody == null || vessel.packed) { dragKN = 0; return; }

            double t = Planetarium.GetUniversalTime() - deployUT;
            double area;
            if (t < preDeploymentSpeed) area = Area(preDeployedDiameter) * Math.Max(0, t) / preDeploymentSpeed;
            else
            {
                double f = deploymentSpeed > 0 ? Math.Min(1, (t - preDeploymentSpeed) / deploymentSpeed) : 1;
                area = Area(preDeployedDiameter) + (Area(deployedDiameter) - Area(preDeployedDiameter)) * f;
                if (!inflateStarted) { inflateStarted = true; Play(deploymentAnimation, deploymentSpeed, (float)((t - preDeploymentSpeed) / deploymentSpeed)); }
                if (f >= 1 && chuteState == "PREDEPLOYED") { chuteState = "DEPLOYED"; UpdateStatus(); }
            }
            if (vessel.LandedOrSplashed && vessel.srfSpeed < cutSpeed) { Cut(); return; }

            Aim();
            Vector3d v = vessel.srf_velocity;
            double speed = v.magnitude;
            if (speed < 0.01 || vessel.atmDensity <= 0) { dragKN = 0; return; }
            double kN = 0.5 * vessel.atmDensity * speed * speed * dragCoefficient * area / 1000.0;   // rigidbody mass in t
            Vector3 at = canopy != null ? canopy.position : part.transform.position;
            part.Rigidbody.AddForceAtPosition(-(Vector3)(v / speed) * (float)kN, at, ForceMode.Force);
            dragKN = (float)kN;
        }

        void LateUpdate()
        {
            if (HighLogic.LoadedSceneIsFlight && Deployed) Aim();   // keep the drawn canopy on the airflow between physics frames
        }

        public float GetModuleMass(float defaultMass, ModifierStagingSituation sit) { return packMass; }
        public ModifierChangeWhen GetModuleMassChangeWhen() { return ModifierChangeWhen.FIXED; }
        public float GetModuleCost(float defaultCost, ModifierStagingSituation sit) { return packCost; }
        public ModifierChangeWhen GetModuleCostChangeWhen() { return ModifierChangeWhen.FIXED; }

        public override string GetModuleDisplayName() { return Text("#MSL_Aeroshell_UI_Parachute"); }
        public override string GetInfo()
        {
            return KSP.Localization.Localizer.Format("#MSL_Aeroshell_UI_Fallback_parachute_info",
                deployedDiameter.ToString("F2"), dragCoefficient.ToString("F3"), (packMass * 1000).ToString("F1"));
        }
    }
}
