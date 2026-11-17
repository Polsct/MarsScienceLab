// ModuleMSLEDLSequencer - automatic EDL events on the backshell. It flies the aeroshell part; the Sky Crane's
// ModuleMSLPoweredDescent flies the rest. Camera, active vessel, time warp and saves stay with the player.
// Triggers (defaults):
//     mortar          speed <= mortarSpeed (405 m/s) while Mach <= mortarMachMax, or Mach <= mortarMachMin at any
//                     speed; only after entry was seen, never on the pad
//     heat shield     >= heatShieldDelay s after the mortar and Mach < heatShieldMach
//     backshell sep   >= backshellDelay s after the heat shield and radar altitude <= backshellAgl; the powered
//                     descent is armed first
// The vessel state is read each time, so an event fired by hand is picked up and the sequence carries on.
using System;
using UnityEngine;

namespace MSLAeroshell
{
    public class ModuleMSLEDLSequencer : PartModule
    {
        [KSPField] public float mortarSpeed = 405f;         // m/s planet-relative (MSL velocity trigger)
        [KSPField] public float mortarMachMax = 2.2f;       // [GAME] upper Mach of the DGB deployment envelope
        [KSPField] public float mortarMachMin = 1.4f;       // [GAME] lower Mach: fire here whatever the speed
        [KSPField] public float heatShieldMach = 0.8f;
        [KSPField] public float heatShieldDelay = 15f;      // s after the mortar (MSL: ~24 s)
        [KSPField] public float backshellAgl = 1600f;       // m radar altitude (MSL: ~1.6 km)
        [KSPField] public float backshellDelay = 5f;        // s after the heat shield
        [KSPField] public string heatShieldNode = "top";
        [KSPField] public string payloadNode = "payload";
        [KSPField] public string poweredDescentModule = "ModuleMSLPoweredDescent";

        // Part menu group "Auto EDL": with the payload protection (ModuleMSLAeroshellShield) and the backshell's
        // separation button (ModuleDecouple on the payload node), grouped in OnStart.
        const string Group = "mslEDL", GroupName = "#MSL_Aeroshell_UI_grp_EDL";
        [KSPField(isPersistant = true, guiActive = true, guiActiveEditor = true, guiName = "#MSL_Aeroshell_UI_Auto_EDL", groupName = Group, groupDisplayName = GroupName)]
        [UI_Toggle(enabledText = "#MSL_Aeroshell_UI_ON", disabledText = "#MSL_Aeroshell_UI_OFF")]
        public bool autoEDL = true;
        [KSPField(guiName = "#MSL_Aeroshell_UI_EDL_step", groupName = Group, groupDisplayName = GroupName)] public string status = "";
        // Set once the vessel has been above the atmosphere or descending faster than mortarSpeed; before that (pad,
        // ascent) the mortar never fires (at clamp release the vessel is briefly not landed, Mach near 0).
        [KSPField(isPersistant = true)] public bool entered;
        [KSPField(isPersistant = true)] public double mortarUT = -1;
        [KSPField(isPersistant = true)] public double heatShieldUT = -1;

        PartModule realChute;
        System.Reflection.FieldInfo realChuteDeployed;
        ModuleMSLParachute fallbackChute;
        ModuleMSLEntryGuidance guidance;
        int settleFrames;       // physics frames since the vessel was last unpacked

        static string Text(string key) { return MSL.Localization.Text.Get(key); }

        // What the sequence waits for, with the trigger values;
        // built once, the triggers do not change in flight (status is set every physics frame)
        string waitMortar, waitHeatShield, waitBackshell;
        void BuildWaitTexts()
        {
            waitMortar = KSP.Localization.Localizer.Format("#MSL_Aeroshell_UI_edl_waiting_mortar", mortarSpeed.ToString("F0"), mortarMachMax.ToString("F1"));
            waitHeatShield = KSP.Localization.Localizer.Format("#MSL_Aeroshell_UI_edl_waiting_heat_shield", heatShieldMach.ToString("F1"));
            waitBackshell = KSP.Localization.Localizer.Format("#MSL_Aeroshell_UI_edl_waiting_backshell", backshellAgl.ToString("F0"));
        }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            FindChute();
            guidance = part.FindModuleImplementing<ModuleMSLEntryGuidance>();
            ModuleDecouple sep = Decoupler(part, payloadNode);
            if (sep != null && sep.Events["Decouple"] != null)
            {
                sep.Events["Decouple"].group = new BasePAWGroup(Group, Text(GroupName), false);
                if (sep.Fields["ejectionForcePercent"] != null)       // its VAB force slider joins the group too
                    sep.Fields["ejectionForcePercent"].group = new BasePAWGroup(Group, Text(GroupName), false);
            }
            BuildWaitTexts();
            status = autoEDL ? waitMortar : Text("#MSL_Aeroshell_UI_off");
        }

        // The EDL step stays off the part menu: the toolbar status window lists the steps.

        // The parachute is its own part on the mortar node; looked up again when the vessel changes
        // (the part can be missing, or gone after a collision).
        Part chutePart;
        int chutePartCount = -1;
        void FindChute()
        {
            chutePartCount = vessel != null ? vessel.parts.Count : -1;
            chutePart = MSL.Modules.ChutePart(part);
            realChute = MSL.Modules.Named(chutePart, "RealChuteModule");
            realChuteDeployed = realChute != null ? realChute.GetType().GetField("oneWasDeployed") : null;
            fallbackChute = chutePart != null ? chutePart.FindModuleImplementing<ModuleMSLParachute>() : null;
            chuteLid = chutePart != null ? chutePart.FindModelTransform(lidName) : null;
        }

        bool ChuteOut()
        {
            if (vessel != null && vessel.parts.Count != chutePartCount) FindChute();
            if (fallbackChute != null) return fallbackChute.oneWasDeployed;
            return realChute != null && realChuteDeployed != null && (bool)realChuteDeployed.GetValue(realChute);
        }

        // The game's Mach (FAR's when installed); while that is still 0 after a load, from the vessel's speed of
        // sound; 0 (unknown) until the air data is valid
        double Mach()
        {
            if (vessel.atmDensity <= 0) return 0;
            if (vessel.mach > 0) return vessel.mach;
            double a = vessel.speedOfSound;
            return a > 1 ? vessel.srfSpeed / a : 0;
        }

        bool FireMortar()
        {
            if (fallbackChute != null) fallbackChute.Deploy();
            else if (realChute != null && realChute.Events["GUIDeploy"] != null) realChute.Events["GUIDeploy"].Invoke();
            else return false;
            if (guidance != null && guidance.guidanceEnabled) guidance.DisableGuidance();
            // the canopy holds the attitude now; RCS and SAS would only fight it (as on MSL, RCS off under the chute)
            vessel.ActionGroups.SetGroup(KSPActionGroup.SAS, false);
            vessel.ActionGroups.SetGroup(KSPActionGroup.RCS, false);
            Log("mortar fire: v={0:F1} m/s, Mach {1:F2} (vessel.mach {2:F2}), alt {3:F0} m", vessel.srfSpeed, Mach(), vessel.mach, vessel.altitude);
            return true;
        }

        Part Attached(string node)
        {
            AttachNode n = part.FindAttachNode(node);
            return n != null ? n.attachedPart : null;
        }

        static ModuleDecouple Decoupler(Part p, string node)
        {
            foreach (ModuleDecouple d in p.FindModulesImplementing<ModuleDecouple>())
                if (d.explosiveNodeID == node || node == null) return d;
            return null;
        }

        // The descent stage on the payload node, or the first part below it that carries the powered-descent module.
        PartModule PoweredDescent(Part payload, out Part stage)
        {
            stage = null;
            if (payload == null) return null;
            var scan = new System.Collections.Generic.List<Part> { payload };
            for (int i = 0; i < scan.Count && i < 64; i++)
            {
                PartModule m = MSL.Modules.Named(scan[i], poweredDescentModule);
                if (m != null) { stage = scan[i]; return m; }
                scan.AddRange(scan[i].children);
            }
            return null;
        }

        void SeparateBackshell(Part payload)
        {
            Part stage;
            PartModule pd = PoweredDescent(payload, out stage);
            if (pd != null && pd.Events["ArmAutoLanding"] != null) pd.Events["ArmAutoLanding"].Invoke();
            ModuleDecouple d = Decoupler(part, payloadNode);
            if (d == null || d.isDecoupled) return;
            Log("backshell separation: agl {0:F0} m, v={1:F1} m/s; powered descent {2}", vessel.radarAltitude, vessel.srfSpeed, pd != null ? "armed" : "not found");
            d.Decouple();
        }

        void Log(string fmt, params object[] a)
        {
            Debug.Log("[MSLAeroshell] EDL: " + string.Format(System.Globalization.CultureInfo.InvariantCulture, fmt, a));
        }

        // Mortar bang and canopy inflation on the chute coming out, whoever fired it. The state at the first unpacked frame
        // is taken as is, so a load with the chute out stays quiet.
        [KSPField] public float inflateDelay = 1.135f;      // s, mortar to line stretch (RealChute preDeploymentSpeed)
        // The bang goes with the lid leaving (ModuleMSLParachuteLidFX throws it then): RealChute sets oneWasDeployed
        // 0.1-0.4 s before it hides its cap and starts the canopy, so the lid's own state is the trigger when there is one.
        [KSPField] public string lidName = "PCC_Lid";
        Transform chuteLid;
        bool chuteKnown, chuteWasOut;
        float inflateAt = -1;

        void ChuteSounds()
        {
            bool chute = ChuteOut();
            if (chuteLid != null) chute = !chuteLid.gameObject.activeSelf;
            if (chuteKnown && chute && !chuteWasOut)
            {
                part.Effect("mslMortar");
                inflateAt = Time.time + (fallbackChute != null ? fallbackChute.preDeploymentSpeed : inflateDelay);
                Log("sound: mortar");
            }
            chuteKnown = true;
            chuteWasOut = chute;
            if (inflateAt >= 0 && Time.time >= inflateAt)
            {
                inflateAt = -1;
                part.Effect("mslChuteInflate");
                Log("sound: canopy inflation");
            }
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null) return;
            if (!vessel.packed) ChuteSounds();
            if (!autoEDL) { status = Text("#MSL_Aeroshell_UI_off"); return; }
            if (vessel.packed) { settleFrames = 0; return; }
            // after a load / unpack vessel.mach is 0 for a few frames while density is not: judge only on settled air data.
            if (settleFrames < 25) { settleFrames++; return; }
            double mach = Mach();
            double now = Planetarium.GetUniversalTime();
            bool chute = ChuteOut();
            if (chute && mortarUT < 0) mortarUT = now;              // fired by hand or by RealChute itself
            Part shield = Attached(heatShieldNode);
            if (shield == null && mortarUT >= 0 && heatShieldUT < 0) heatShieldUT = now;
            Part payload = Attached(payloadNode);
            if (payload == null) { status = Text("#MSL_Aeroshell_UI_edl_backshell_separated"); return; }
            bool inAir = vessel.atmDensity > 0 && !vessel.LandedOrSplashed && vessel.verticalSpeed < 0;
            if (!entered && !vessel.LandedOrSplashed &&
                (vessel.altitude >= vessel.mainBody.atmosphereDepth || inAir && vessel.srfSpeed > mortarSpeed))
            {
                entered = true;
                Log("entry seen: alt {0:F0} m, v={1:F1} m/s; mortar armed", vessel.altitude, vessel.srfSpeed);
            }

            if (!chute)
            {
                status = waitMortar;
                if (mortarUT >= 0) return;                          // fired; RealChute opens it on its own conditions
                if (entered && inAir && mach > 0 && vessel.altitude < vessel.mainBody.atmosphereDepth &&
                    ((vessel.srfSpeed <= mortarSpeed && mach <= mortarMachMax) || mach <= mortarMachMin))
                {
                    if (FireMortar()) mortarUT = now;
                }
                return;
            }
            if (shield != null)
            {
                status = waitHeatShield;
                if (inAir && mach > 0 && now - mortarUT >= heatShieldDelay && mach < heatShieldMach)
                {
                    ModuleDecouple d = Decoupler(shield, null);
                    if (d != null && !d.isDecoupled)
                    {
                        Log("heat shield jettison: Mach {0:F2}, v={1:F1} m/s, agl {2:F0} m", mach, vessel.srfSpeed, vessel.radarAltitude);
                        d.Decouple();
                        heatShieldUT = now;
                    }
                }
                return;
            }
            status = waitBackshell;
            if (inAir && now - heatShieldUT >= backshellDelay && vessel.radarAltitude > 0 && vessel.radarAltitude <= backshellAgl)
                SeparateBackshell(payload);
        }

        public override string GetInfo()
        {
            return KSP.Localization.Localizer.Format("#MSL_Aeroshell_UI_Auto_EDL_info", mortarSpeed.ToString("F0"),
                mortarMachMax.ToString("F1"), mortarMachMin.ToString("F1"), heatShieldMach.ToString("F1"), backshellAgl.ToString("F0"));
        }
    }
}
