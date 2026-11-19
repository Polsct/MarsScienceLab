// ModuleMSLPoweredDescent - automatic powered descent, sky crane and flyaway for the MSL descent stage.
//   vertical    braking parabola vz = -(0.75 + sqrt(2 * 1.6 * (agl - 21))), at most 60 m/s; PI on vz plus gravity;
//               while lowering, the bridle tension fed forward (low-passed, capped at 1.2 rover weights)
//   horizontal  divert away from the backshell at 18 m/s for the first 12 s, then null; tilt <= 18 deg, thrust
//               axis slewed at <= 12 deg/s
//   attitude    PID on the thrust axis through ModuleMSLDescentControl (differential MLE throttle)
//   sky crane   at 21 m AGL, |vz + 0.75| < 0.35 m/s, vh < 1 m/s: ModuleMSLBridle.StartLowering; rover mobility
//               deploys once the bridle is 1 m long; touchdown when the rover is landed or < 0.95 m up; bridles cut
//               when the tension stays under 30 % of the rover weight for 0.3 s (or 6 s after touchdown)
//   flyaway     6 m/s climb for 6 s, then 30 m/s level away from the rover, tilt up to 30 deg, throttle <= 60 %;
//               engines off 650 m from the rover
// Armed by ModuleMSLEDLSequencer just before backshell separation, or from the part menu; it lights the MLEs 1 s
// after the stage has left the backshell. Throttle and attitude go through ModuleMSLDescentControl's overrides, so
// the stage flies whether or not it is the active vessel. Units: tonnes, kN, metres, seconds.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MSLSkyCrane
{
    public class ModuleMSLPoweredDescent : PartModule
    {
        [KSPField] public float skyCraneAgl = 21f;          // m, rover release height
        [KSPField] public float skyCraneSpeed = 0.75f;      // m/s descent rate while lowering
        [KSPField] public float brakingAccel = 1.6f;        // m/s^2 of the braking parabola [GAME]
        [KSPField] public float divertSpeed = 18f;          // m/s away from the backshell [GAME]
        [KSPField] public float divertTime = 12f;           // s
        [KSPField] public float maxTilt = 18f;              // deg [GAME]
        [KSPField] public float axisSlewRate = 12f;         // deg/s [GAME]
        [KSPField] public float flyawayDistance = 650f;     // m from the rover
        [KSPField] public float flyawayThrottle = 0.6f;
        [KSPField] public float ignitionDelay = 1f;         // s after the backshell separation
        [KSPField] public string backshellPart = "MSL.Backshell";
        [KSPField] public string roverModule = "ModuleCuriosityRig";

        // OFF, ARMED, POWERED, SKYCRANE, FLYAWAY, DONE
        [KSPField(isPersistant = true)] public string phase = "OFF";
        [KSPField(isPersistant = true)] public double phaseUT = -1;
        // Part menu: in the stage's one "Sky crane" group; the phase while it flies and the one button
        // that applies (arm or cancel).
        const string Group = "mslSkyCrane", GroupName = "#MSL_SkyCrane_UI_grp_SkyCrane";
        [KSPField(guiActive = true, guiActiveEditor= false, guiName = "#MSL_SkyCrane_UI_Landing_status", groupName = Group, groupDisplayName = GroupName)] public string status = "";

        ModuleMSLDescentControl ctl;
        ModuleMSLBridle bridle;
        Part rover;
        double sepUT = -1, touchdownUT = -1, slackSince = -1, cutUT = -1;
        double vzInt, tensionF;
        Vector3 attInt, dirSlew, divertDir;
        bool rigDeployed;
        float axial = -1;

        static string Text(string key) { return MSL.Localization.Text.Get(key); }
        bool Flying { get { return phase == "POWERED" || phase == "SKYCRANE" || phase == "FLYAWAY"; } }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            ctl = part.FindModuleImplementing<ModuleMSLDescentControl>();
            bridle = part.FindModuleImplementing<ModuleMSLBridle>();
            if (!HighLogic.LoadedSceneIsFlight) return;
            // a reload mid-flight: the bridle joints are not saved, so a lowering in progress ends as a cut
            if (phase == "SKYCRANE") Enter("FLYAWAY");
            UpdateUi();
        }

        void UpdateUi()
        {
            bool idle = phase == "OFF" || phase == "DONE";
            Events["ArmAutoLanding"].guiActive = idle;
            Events["CancelAutoLanding"].guiActive = !idle;
            Fields["status"].guiActive = !idle;
            if (HighLogic.LoadedSceneIsFlight) MonoUtilities.RefreshPartContextWindow(part);
            switch (phase)
            {
                case "ARMED": status = Text("#MSL_SkyCrane_UI_auto_armed"); break;
                case "POWERED": status = Text("#MSL_SkyCrane_UI_auto_powered"); break;
                case "SKYCRANE": status = Text("#MSL_SkyCrane_UI_auto_sky_crane"); break;
                case "FLYAWAY": status = Text("#MSL_SkyCrane_UI_auto_flyaway"); break;
                case "DONE": status = Text("#MSL_SkyCrane_UI_auto_done"); break;
                default: status = Text("#MSL_SkyCrane_UI_Off"); break;
            }
        }

        void Enter(string p)
        {
            phase = p;
            phaseUT = Planetarium.GetUniversalTime();
            // the vertical integral carries over into the sky crane: resetting it dropped the throttle below RealFuels' ignition
            // threshold and the MLEs shut down with no ElectricCharge to re-ignite
            if (p != "SKYCRANE") vzInt = 0;
            attInt = Vector3.zero;
            UpdateUi();
            Log("{0}: agl {1:F1} m, v={2:F2} m/s", p, vessel != null ? vessel.radarAltitude : 0, vessel != null ? vessel.srfSpeed : 0);
        }

        static void Log(string fmt, params object[] a)
        {
            Debug.Log("[MSLSkyCrane] auto landing " + string.Format(System.Globalization.CultureInfo.InvariantCulture, fmt, a));
        }

        [KSPEvent(guiActive = true, guiName = "#MSL_SkyCrane_UI_Arm_auto_landing", groupName = Group, groupDisplayName = GroupName)]
        public void ArmAutoLanding()
        {
            if (!HighLogic.LoadedSceneIsFlight || ctl == null || bridle == null || Flying) return;
            sepUT = -1;
            Enter("ARMED");
        }
        [KSPAction("#MSL_SkyCrane_UI_Arm_auto_landing")] public void ArmAction(KSPActionParam p) { ArmAutoLanding(); }

        [KSPEvent(guiActive = false, guiName = "#MSL_SkyCrane_UI_Cancel_auto_landing", groupName = Group, groupDisplayName = GroupName)]
        public void CancelAutoLanding()
        {
            Release();
            phase = "OFF";
            UpdateUi();
        }

        void Release()
        {
            if (ctl == null) return;
            ctl.overrideThrottle = null;
            ctl.overrideCommand = null;
        }

        bool HasBackshell()
        {
            foreach (Part p in vessel.parts) if (p.partInfo != null && p.partInfo.name == backshellPart) return true;
            return false;
        }

        // the separated backshell nearest to the stage (for the divert direction)
        Vessel Backshell()
        {
            Vessel best = null;
            double d = 2000;
            foreach (Vessel v in FlightGlobals.VesselsLoaded)
            {
                if (v == vessel) continue;
                foreach (Part p in v.parts)
                    if (p.partInfo != null && p.partInfo.name == backshellPart)
                    {
                        double dd = Vector3d.Distance(v.CoMD, vessel.CoMD);
                        if (dd < d) { d = dd; best = v; }
                        break;
                    }
            }
            return best;
        }

        Part FindRover()
        {
            if (bridle != null && bridle.rover != null) return bridle.rover;
            AttachNode node = part.FindAttachNode("bottom");
            if (node != null && node.attachedPart != null) return node.attachedPart;
            foreach (Part c in part.children) if (MSL.Modules.Named(c, roverModule) != null) return c;
            return null;
        }

        float MaxThrust() { float f = 0; foreach (ModuleEngines e in ctl.engines) if (e != null) f += e.maxThrust; return f; }
        float MinThrust() { float f = 0; foreach (ModuleEngines e in ctl.engines) if (e != null) f += e.minThrust; return f; }
        float FinalThrust() { float f = 0; foreach (ModuleEngines e in ctl.engines) if (e != null) f += e.finalThrust; return f; }

        // fraction of the nozzle thrust along the stage axis (the MLEs are canted)
        float AxialFactor()
        {
            if (axial > 0) return axial;
            Vector3 s = Vector3.zero;
            int k = 0;
            foreach (ModuleEngines e in ctl.engines)
                if (e != null) foreach (Transform tr in e.thrustTransforms) { s += -tr.forward; k++; }
            axial = k > 0 ? Vector3.Dot(s / k, part.transform.up) : 1f;
            if (axial <= 0.1f) axial = 1f;
            return axial;
        }

        void Ignite()
        {
            foreach (ModuleEngines e in ctl.engines) if (e != null && !e.EngineIgnited) e.Activate();
            ctl.overrideThrottle = 0.6f;
            rover = FindRover();
            divertDir = AwayFrom(Backshell());
            dirSlew = part.transform.up;
            rigDeployed = false; touchdownUT = -1; slackSince = -1; cutUT = -1; tensionF = 0;
            Enter("POWERED");
        }

        Vector3 Up() { return (Vector3)(vessel.CoMD - vessel.mainBody.position).normalized; }

        // horizontal direction away from another vessel; else along the horizontal velocity; else any horizontal
        Vector3 AwayFrom(Vessel other)
        {
            Vector3 up = Up();
            Vector3 h = Vector3.ProjectOnPlane((Vector3)vessel.srf_velocity, up);
            Vector3 away = other != null && other != vessel ? Vector3.ProjectOnPlane((Vector3)(vessel.CoMD - other.CoMD), up) : Vector3.zero;
            return (away.magnitude > 1 ? away : (h.magnitude > 1 ? h : Vector3.Cross(up, Vector3.forward))).normalized;
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || vessel == null || ctl == null) return;
            if (phase == "OFF" || phase == "DONE") return;
            if (vessel.packed) return;
            double now = Planetarium.GetUniversalTime();
            if (phase == "ARMED")
            {
                if (HasBackshell() || vessel.LandedOrSplashed) { sepUT = -1; return; }
                if (sepUT < 0) sepUT = now;
                if (now - sepUT >= ignitionDelay) Ignite();
                return;
            }
            if (ctl.engines.Count == 0) { Release(); Enter("DONE"); return; }
            if (dirSlew == Vector3.zero) dirSlew = part.transform.up;
            if (rover == null) rover = FindRover();
            // not saved: after a load, divert away from the backshell again, fly away from the rover
            if (divertDir == Vector3.zero)
                divertDir = AwayFrom(phase == "FLYAWAY" ? (rover != null ? rover.vessel : null) : Backshell());
            Fly(now);
        }

        void Fly(double now)
        {
            Vessel v = vessel;
            Vector3 up = Up();
            Vector3 vel = (Vector3)v.srf_velocity;
            double agl = v.radarAltitude;
            double vz = Vector3.Dot(vel, up);
            Vector3 vh = Vector3.ProjectOnPlane(vel, up);
            double g = v.mainBody.gravParameter / (v.CoMD - v.mainBody.position).sqrMagnitude;
            double m = v.GetTotalMass();
            double tension = bridle != null ? bridle.tension : 0;
            bool lowering = bridle != null && bridle.lowering;
            double since = now - phaseUT;
            float dt = TimeWarp.fixedDeltaTime;

            // vertical velocity profile: braking parabola to skyCraneSpeed at the sky-crane height
            double vzT;
            if (phase == "FLYAWAY") vzT = since < 6 ? 6 : 0;            // climb clear, then fly off level
            else if (phase == "SKYCRANE") vzT = -skyCraneSpeed;
            else vzT = -Math.Min(60.0, skyCraneSpeed + Math.Sqrt(Math.Max(0, 2 * brakingAccel * (agl - skyCraneAgl))));
            // horizontal: divert away from the backshell, then null horizontal velocity
            Vector3 vhT = Vector3.zero;
            if (phase == "POWERED" && since < divertTime) vhT = divertDir * divertSpeed;
            if (phase == "FLYAWAY") vhT = divertDir * 30f;
            const double kz = 1.2, kh = 0.35;
            vzInt += (vzT - vz) * dt;
            vzInt = Math.Max(-5, Math.Min(5, vzInt));
            double az = g + kz * (vzT - vz) + 0.8 * vzInt;
            // the bridles pull down on the stage: feed forward the rover weight they carry, low-passed and capped
            // (raw bridle force follows the stage's own acceleration, so feeding it back unfiltered is unstable)
            if (lowering && rover != null)
            {
                double cap = 1.2 * rover.mass * g;
                tensionF += (Math.Min(tension, cap) - tensionF) * Math.Min(1.0, dt / 0.6);
                az += tensionF / m;
            }
            else tensionF = 0;
            // the engines can only push up: a negative vertical command means "throttle down", never "point down"
            az = Math.Max(az, 0.25 * g);
            Vector3 ah = (vhT - vh) * (float)kh;
            float tiltMax = phase == "FLYAWAY" ? Mathf.Clamp(10f * (float)since, 0f, 30f) : maxTilt;
            float maxH = (float)(Math.Max(az, 0.1) * Math.Tan(tiltMax * Math.PI / 180));
            if (ah.magnitude > maxH) ah = ah.normalized * maxH;
            Vector3 acmd = up * (float)az + ah;
            dirSlew = Vector3.RotateTowards(dirSlew, acmd.normalized, axisSlewRate * Mathf.Deg2Rad * dt, 0f).normalized;
            Vector3 dir = dirSlew;

            // throttle from the axial thrust actually available (canted engines)
            float ax = AxialFactor();
            float fmax = MaxThrust() * ax, fmin = MinThrust() * ax;
            float cos = Vector3.Dot(dir, part.transform.up);
            double need = m * acmd.magnitude / Math.Max(1e-3, cos > 0.5f ? cos : 0.5f);
            float thr = Mathf.Clamp01((float)((need - fmin) / Math.Max(1e-3, fmax - fmin)));
            if (phase == "FLYAWAY") thr = Mathf.Min(thr, flyawayThrottle);
            ctl.overrideThrottle = thr;

            // attitude: PID on the thrust-axis error; the integral holds the steady bridle torque (BUD exit off the
            // CoM). Differential-throttle authority grows with thrust, so the command is scaled by 10 kN / thrust.
            Vector3 axis = part.transform.up;
            Vector3 w = part.Rigidbody != null ? part.Rigidbody.angularVelocity : Vector3.zero;
            Vector3 err = Vector3.Cross(axis, dir);
            attInt += err * dt;
            if (attInt.magnitude > 0.4f) attInt = attInt.normalized * 0.4f;
            Vector3 torque = err * 6f + attInt * 3f - w * 3f;
            // reference thrust and mass go with the part scale squared (stock scale without RSS); the torque per unit command
            // grows as 1 / scale (thrust x arm over mass x arm^2), taken out by the last factor
            float s = part.rescaleFactor, s2 = s * s;
            torque *= Mathf.Clamp(10f * s2 / Mathf.Max(s2, FinalThrust()), 0.25f, 2f) * Mathf.Clamp((float)m / (2.0f * s2), 0.35f, 1f) * s;
            Vector3 cmd = -(Quaternion.Inverse(v.ReferenceTransform.rotation) * torque);
            if (cmd.magnitude > 1) cmd.Normalize();
            ctl.overrideCommand = cmd;

            if (phase == "POWERED")
            {
                bool state = agl <= skyCraneAgl + 0.5 * s && Math.Abs(vz + skyCraneSpeed) < 0.35 && vh.magnitude < 1.0;
                // fallback after a long descent: release close to the profile rather than hover until dry
                bool late = since > 90 && agl <= skyCraneAgl + 3 * s && Math.Abs(vz) < 2 && vh.magnitude < 2;
                if ((state || late) && bridle != null)
                {
                    bridle.StartLowering();
                    if (bridle.rover != null) rover = bridle.rover;
                    Enter("SKYCRANE");
                }
                return;
            }
            if (phase == "SKYCRANE")
            {
                double payout = bridle != null ? bridle.payout : 0;
                if (!rigDeployed && payout > 1.0 * s && rover != null)
                {
                    rigDeployed = true;
                    PartModule rig = MSL.Modules.Named(rover, roverModule);
                    if (rig != null && rig.Events["DeployMobility"] != null) rig.Events["DeployMobility"].Invoke();
                }
                Vessel rv = rover != null ? rover.vessel : null;
                if (rv != null && rv != v)
                {
                    bool down = rv.Landed || rv.radarAltitude < 0.95 * s;
                    if (down && touchdownUT < 0) { touchdownUT = now; Log("rover touchdown: v={0:F2} m/s", rv.srfSpeed); }
                    if (touchdownUT >= 0)
                    {
                        if (tension < 0.3 * rover.mass * g) { if (slackSince < 0) slackSince = now; } else slackSince = -1;
                        if (slackSince >= 0 && now - slackSince >= 0.3 || now - touchdownUT > 6) { Cut(now); return; }
                    }
                }
                if (bridle == null || !bridle.lowering || since > 40) Cut(now);   // no touchdown seen: do not hover until dry
                return;
            }
            // FLYAWAY
            if (cutUT < 0) cutUT = now;
            double dist = rover != null && rover.vessel != null ? Vector3d.Distance(v.CoMD, rover.vessel.CoMD) : since * 30;
            if (dist >= flyawayDistance || (v.Landed && since > 3) || since > 60)
            {
                foreach (ModuleEngines e in ctl.engines) if (e != null && e.EngineIgnited) e.Shutdown();
                Release();
                Log("flyaway end: {0:F0} m from the rover, agl {1:F0} m", dist, agl);
                Enter("DONE");
            }
        }

        void Cut(double now)
        {
            if (bridle != null && !bridle.cut) bridle.Cut();
            cutUT = now;
            Enter("FLYAWAY");
        }

        public override string GetInfo()
        {
            return KSP.Localization.Localizer.Format("#MSL_SkyCrane_UI_Auto_landing_info", skyCraneAgl.ToString("F0"),
                skyCraneSpeed.ToString("F2"), flyawayDistance.ToString("F0"));
        }
    }
}
