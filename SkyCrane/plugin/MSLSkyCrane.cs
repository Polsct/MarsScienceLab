// MSL Sky Crane part modules.
// ModuleMSLDescentControl  Attitude control by differential throttling of the eight canted MLEs (no reaction wheel).
//                          Each physics frame every engine gets its own throttle: the mean follows the main throttle
//                          and the split produces the commanded torque. A single part with several thrust
//                          transforms is driven through thrustTransformMultipliers.
// ModuleMSLMLEMount        Editor helper on MSL_SC_MLE: picks the Outboard / Inboard head variant from the stage node.
// ModuleMSLBridle          Sky crane: fires the rover bolts, then holds the rover on three bridles paid out at
//                          payoutRate up to bridleLength; "Cut bridles" releases them.
// Units: tonnes, kN, metres, seconds. [GAME] marks tuning values that are not flight data.
using System;
using System.Collections.Generic;
using UnityEngine;

[assembly: KSPAssembly("MSLSkyCrane", 1, 0)]

namespace MSLSkyCrane
{
    public class ModuleMSLDescentControl : PartModule, ITorqueProvider
    {
        [KSPField] public string engineID = "";
        [KSPField] public float maxSplit = 0.85f;           // [GAME] largest per-engine deviation from the even split
        [KSPField] public float authority = 1.0f;           // [GAME] fraction of the available torque used at full stick
        // MLE differential-throttle attitude control: always on, no part-menu switch. Kept as a
        // plain field for code (the auto landing sets it); forced on at start so an old save with it off cannot strand it.
        [NonSerialized] public bool controlEnabled = true;
        [KSPField(guiName = "#MSL_SkyCrane_UI_MLE_split", guiFormat = "F2", groupName = "mslSkyCrane", groupDisplayName = "#MSL_SkyCrane_UI_grp_SkyCrane")] public float splitDisplay;
        // The stage's RCS (MSL_SC_RCS pairs) is the entry control system; it is disarmed while the MLEs burn under this
        // module (it would fight the throttle split) and re-armed when they stop.
        [KSPField] public bool holdRcsInPoweredFlight = true;

        // External command: torque in vessel axes as a fraction [-1, 1] of the authority; replaces ctrlState for this frame.
        [NonSerialized] public Vector3? overrideCommand;
        // External throttle (ModuleMSLPoweredDescent): replaces mainThrottle as the mean of the split while set, so the
        // stage flies whether or not it is the active vessel. Ignited engines count as burning even at zero thrust.
        [NonSerialized] public float? overrideThrottle;

        // Nozzles under control: engines[i] owns thrust transform nozzle[i] (one per MSL_SC_MLE part, or all
        // transforms of a single multi-nozzle engine on this part).
        public readonly List<ModuleEngines> engines = new List<ModuleEngines>();
        readonly List<Transform> nozzles = new List<Transform>();
        readonly List<Part> scan = new List<Part>();
        readonly List<ModuleRCS> heldRcs = new List<ModuleRCS>();
        bool singlePart;
        int n;
        Vector3[] unitArms;
        DescentMixer mixer;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            controlEnabled = true;
            Collect();
            if (HighLogic.LoadedSceneIsFlight) GameEvents.onVesselWasModified.Add(OnModified);
        }

        void OnDestroy() { GameEvents.onVesselWasModified.Remove(OnModified); }

        void OnModified(Vessel v) { if (v == vessel) Collect(); }

        bool Matches(ModuleEngines e) { return engineID == "" || e.engineID == engineID; }

        // Engines on this part, else the matching engines in the subtree below it (the stage's own parts:
        // a separated rover takes nothing with it, and the backshell above is never searched).
        void Collect()
        {
            // under the auto-landing override the main throttle is 0: releasing here would leave the MLEs at 0 for a frame and
            // RealFuels would shut them down with no ElectricCharge to re-ignite. The next FixedUpdate re-mixes them.
            if (!overrideThrottle.HasValue) ReleaseThrottles();
            engines.Clear();
            nozzles.Clear();
            singlePart = false;
            foreach (ModuleEngines e in part.FindModulesImplementing<ModuleEngines>())
                if (Matches(e) && e.thrustTransforms.Count > 0)
                {
                    singlePart = true;
                    foreach (Transform t in e.thrustTransforms) { engines.Add(e); nozzles.Add(t); }
                    break;
                }
            if (!singlePart)
            {
                scan.Clear();
                scan.AddRange(part.children);
                for (int k = 0; k < scan.Count; k++)
                {
                    Part p = scan[k];
                    foreach (ModuleEngines e in p.FindModulesImplementing<ModuleEngines>())
                        if (Matches(e) && e.thrustTransforms.Count > 0) { engines.Add(e); nozzles.Add(e.thrustTransforms[0]); }
                    scan.AddRange(p.children);
                }
            }
            if (engines.Count != n)
            {
                n = engines.Count;
                unitArms = new Vector3[n];
                mixer = n > 0 ? new DescentMixer(n) : null;
            }
        }

        // Back to the main throttle (control off, or engines leaving the stage).
        void ReleaseThrottles()
        {
            if (singlePart) return;
            foreach (ModuleEngines e in engines)
                if (e != null) { e.independentThrottle = false; e.independentThrottlePercentage = 0f; }
        }

        bool Burning()
        {
            for (int i = 0; i < n; i++)
                if (engines[i] != null && engines[i].EngineIgnited && engines[i].finalThrust > 0f) return true;
            return false;
        }

        bool Ignited()
        {
            for (int i = 0; i < n; i++)
                if (engines[i] != null && engines[i].EngineIgnited && !engines[i].flameout) return true;
            return false;
        }

        float FinalThrust()
        {
            float f = 0f;
            if (singlePart) return n > 0 && engines[0] != null ? engines[0].finalThrust : 0f;
            for (int i = 0; i < n; i++) if (engines[i] != null) f += engines[i].finalThrust;
            return f;
        }

        // Unit torque (per kN of total thrust, even split) of each nozzle about the vessel CoM, world frame.
        Vector3[] UnitArms()
        {
            Vector3 com = vessel.CoM;
            Vector3[] a = unitArms;
            for (int i = 0; i < n; i++)
            {
                Transform t = nozzles[i];
                a[i] = t != null ? Vector3.Cross(t.position - com, -t.forward) : Vector3.zero;
            }
            return a;
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight || n == 0 || vessel == null) return;
            List<float> m = singlePart ? engines[0].thrustTransformMultipliers : null;
            if (singlePart && (m == null || m.Count != n)) return;
            bool burning = overrideThrottle.HasValue ? Ignited() : Burning();
            HoldRcs(holdRcsInPoweredFlight && controlEnabled && burning);
            if (!controlEnabled || !burning)
            {
                if (singlePart) for (int i = 0; i < n; i++) m[i] = 1f / n;
                else ReleaseThrottles();
                splitDisplay = 0f;
                return;
            }
            Transform rt = vessel.ReferenceTransform;
            Vector3 cmd;
            if (overrideCommand.HasValue) cmd = overrideCommand.Value;
            else
            {
                FlightCtrlState c = vessel.ctrlState;
                cmd = new Vector3(c.pitch, c.roll, c.yaw);               // same axis order as ModuleReactionWheel
            }
            overrideCommand = null;
            // Desired torque direction in world space (reaction-wheel sign convention).
            Vector3 want = rt.rotation * -cmd;
            Vector3[] a = UnitArms();
            for (int i = 0; i < n; i++) mixer.SetArm(i, a[i].x, a[i].y, a[i].z);
            mixer.Mix(want.x, want.y, want.z, want.magnitude, authority, maxSplit);
            splitDisplay = (float)(maxSplit * Math.Min(1.0, want.magnitude) * authority);
            if (singlePart)
            {
                for (int i = 0; i < n; i++) m[i] = mixer.Weights[i];
                return;
            }
            // Per-engine throttle = main throttle x n x weight. Near full throttle the largest share would
            // exceed 100 %: scale all engines down together so the torque direction survives.
            float main = overrideThrottle.HasValue ? Mathf.Clamp01(overrideThrottle.Value) : vessel.ctrlState.mainThrottle, top = 0f;
            for (int i = 0; i < n; i++) top = Mathf.Max(top, main * n * mixer.Weights[i]);
            float scale = top > 1f ? 1f / top : 1f;
            for (int i = 0; i < n; i++)
            {
                ModuleEngines e = engines[i];
                if (e == null) continue;
                e.independentThrottle = true;
                e.independentThrottlePercentage = Mathf.Clamp(100f * main * n * mixer.Weights[i] * scale, 0f, 100f);
            }
        }

        // Disarm the stage's RCS while powered (remembering which modules were armed), re-arm them after. The thrusters
        // are the MSL_SC_RCS pairs on the stage nodes; a stage of older saves carries them itself.
        void HoldRcs(bool hold)
        {
            if (hold)
            {
                foreach (ModuleRCS r in part.FindModulesImplementing<ModuleRCS>())
                    if (r.rcsEnabled) { r.rcsEnabled = false; heldRcs.Add(r); }
                foreach (Part c in part.children)
                    if (c.partInfo != null && c.partInfo.name == "MSL.SC.RCS")
                        foreach (ModuleRCS r in c.FindModulesImplementing<ModuleRCS>())
                            if (r.rcsEnabled) { r.rcsEnabled = false; heldRcs.Add(r); }
            }
            else if (heldRcs.Count > 0)
            {
                foreach (ModuleRCS r in heldRcs) if (r != null) r.rcsEnabled = true;
                heldRcs.Clear();
            }
        }

        // Torque available from redistribution at the current thrust (kN*m), for SAS tuning.
        public void GetPotentialTorque(out Vector3 pos, out Vector3 neg)
        {
            pos = neg = Vector3.zero;
            if (n == 0 || vessel == null || !controlEnabled || !Burning()) return;
            Vector3[] a = UnitArms();
            Transform rt = vessel.ReferenceTransform;
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                Vector3 l = rt.InverseTransformDirection(a[i]);
                sum += new Vector3(Mathf.Abs(l.x), Mathf.Abs(l.y), Mathf.Abs(l.z));
            }
            pos = neg = sum * (FinalThrust() / n) * maxSplit * 0.5f;
        }
    }

    public class ModuleMSLMLEMount : PartModule
    {
        // "node:variant" pairs from the measured engine identities.
        [KSPField] public string mountVariants = "";
        // Per-variant local poses of the transforms named poseTransform, "VARIANT:px py pz qx qy qz qw;...|...", in model
        // order. The RCS pair's two rcsTransforms are shared by both hands.
        [KSPField] public string poseTransform = "";
        [KSPField] public string variantPoses = "";
        Transform[] poseTs;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (HighLogic.LoadedSceneIsEditor) part.OnEditorAttach += Match;
            if (poseTransform != "" && variantPoses != "")
            {
                poseTs = part.FindModelTransforms(poseTransform);
                GameEvents.onVariantApplied.Add(OnVariantApplied);
                ApplyPoses();
            }
        }

        public override void OnStartFinished(StartState state) { base.OnStartFinished(state); ApplyPoses(); }

        void OnDestroy()
        {
            if (part != null) part.OnEditorAttach -= Match;
            if (poseTs != null) GameEvents.onVariantApplied.Remove(OnVariantApplied);
        }

        void OnVariantApplied(Part p, PartVariant v) { if (p == part) ApplyPoses(v != null ? v.Name : null); }

        public void ApplyPoses(string variant = null)
        {
            if (poseTs == null || poseTs.Length == 0) return;
            if (variant == null)
            {
                ModulePartVariants mv = part.FindModuleImplementing<ModulePartVariants>();
                if (mv == null || mv.SelectedVariant == null) return;
                variant = mv.SelectedVariant.Name;
            }
            foreach (string block in variantPoses.Split('|'))
            {
                int c = block.IndexOf(':');
                if (c < 0 || block.Substring(0, c).Trim() != variant) continue;
                string[] poses = block.Substring(c + 1).Split(';');
                if (poses.Length != poseTs.Length)
                {
                    Debug.LogWarning(string.Format("[MSL] {0}: {1} poses for {2} {3} transforms", part.partInfo.name, poses.Length, poseTs.Length, poseTransform));
                    return;
                }
                for (int i = 0; i < poses.Length; i++)
                {
                    string[] f = poses[i].Trim().Split(' ');
                    float[] v = new float[7];
                    for (int k = 0; k < 7; k++) v[k] = float.Parse(f[k], System.Globalization.CultureInfo.InvariantCulture);
                    poseTs[i].localPosition = new Vector3(v[0], v[1], v[2]);
                    poseTs[i].localRotation = new Quaternion(v[3], v[4], v[5], v[6]);
                }
                return;
            }
        }

        // Variant for the parent node this engine sits on, or null (not on a Sky Crane MLE node).
        public string VariantFor(string nodeId)
        {
            foreach (string pair in mountVariants.Split(','))
            {
                string[] kv = pair.Split(':');
                if (kv.Length == 2 && kv[0].Trim() == nodeId) return kv[1].Trim();
            }
            return null;
        }

        void Match()
        {
            if (part.parent == null) return;
            AttachNode node = part.parent.FindAttachNodeByPart(part);
            string variant = node != null ? VariantFor(node.id) : null;
            ModulePartVariants variants = part.FindModuleImplementing<ModulePartVariants>();
            if (variant == null || variants == null || variants.SelectedVariant == null || variants.SelectedVariant.Name == variant) return;
            variants.SetVariant(variant);
            ApplyPoses(variant);
            // keep symmetry counterparts on their own nodes' variants
            foreach (Part p in part.symmetryCounterparts)
            {
                ModuleMSLMLEMount m = p.FindModuleImplementing<ModuleMSLMLEMount>();
                if (m != null) m.Match();
            }
            if (EditorLogic.fetch != null && EditorLogic.fetch.ship != null) GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }
    }

    public class ModuleMSLBridle : PartModule
    {
        [KSPField] public string exitTransform = "bridleExit";
        [KSPField] public string tipTransforms = "bridleTip_1,bridleTip_2,bridleTip_3";
        [KSPField] public float bridleLength = 7.5f;        // m
        [KSPField] public float payoutRate = 0.75f;         // m/s, ESTIMATE (skycrane_spec bridle.payout_rate)
        [KSPField] public float spring = 120f;              // kN/m per bridle [GAME] (braided nylon stretch not modelled)
        [KSPField] public float damper = 12f;               // kN*s/m per bridle [GAME]
        [KSPField] public float lineWidth = 0.014f;         // m, bridle diameter (skycrane_spec)
        // Umbilical from the device exit (umbilicalExit) to the rover deck (umbilicalTip): a coiled cable that keeps its curl
        // when paid out. Drawn only, no load. After the cut a break-tie holds the rover end until the stage pulls it out,
        // then it retracts to the device.
        [KSPField] public string umbilicalExitTransform = "umbilicalExit";
        [KSPField] public string umbilicalTipTransform = "umbilicalTip";
        [KSPField] public float umbilicalWidth = 0.0127f;   // m
        [KSPField] public float umbilicalExtra = 0.3f;      // m longer than the bridles, ESTIMATE ("slightly longer")
        [KSPField] public float curlRadius = 0.05f;         // m, least curl radius (coil set), ESTIMATE
        [KSPField] public float curlPitch = 0.3f;           // m per curl, ESTIMATE
        // All three bridles meet at one exit, so the hanging rover has no yaw stiffness of its own; a twist spring about the
        // bridle axis stands in for it: period s, damping ratio [GAME].
        [KSPField] public float twistPeriod = 4f;
        [KSPField] public float twistDamping = 0.8f;
        // Part menu group "Sky crane": release before, cut while the bridles hold the rover, the payout once out.
        const string Group = "mslSkyCrane", GroupName = "#MSL_SkyCrane_UI_grp_SkyCrane";
        [KSPField(guiActive = true, guiActiveEditor= false, guiName = "#MSL_SkyCrane_UI_Bridle", groupName = Group, groupDisplayName = GroupName)] public string status = MSL.Localization.Text.Get("#MSL_SkyCrane_UI_stowed");
        [KSPField(guiActive = true, guiActiveEditor= false, guiName = "#MSL_SkyCrane_UI_Bridle_out", guiUnits = " m", guiFormat = "F2", groupName = Group, groupDisplayName = GroupName)] public float payout;

        public Part rover;
        public bool lowering, cut;
        public float tension;                               // kN, sum over the three bridles
        Transform exit;
        readonly List<Transform> tips = new List<Transform>();
        readonly List<Vector3> roverAnchors = new List<Vector3>();
        readonly List<ConfigurableJoint> joints = new List<ConfigurableJoint>();
        readonly List<LineRenderer> lines = new List<LineRenderer>();
        Transform umbExit, umbTip;
        Vector3 roverUmbilical, umbLoose;
        LineRenderer umbLine;
        int umbPhase;                                       // 0 off, 1 hanging, 2 cut: break-tie holds, 3 retracting
        float umbPhaseTime;
        const int UmbPoints = 160;
        float startLength, startTime;
        Vector3 twistRef;                                   // stage forward in rover axes at release
        bool pending, destroying, fullLength;

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            exit = part.FindModelTransform(exitTransform);
            foreach (string s in tipTransforms.Split(',')) { Transform t = part.FindModelTransform(s.Trim()); if (t != null) tips.Add(t); }
            umbExit = part.FindModelTransform(umbilicalExitTransform);
            umbTip = part.FindModelTransform(umbilicalTipTransform);
            UpdateMenu();
        }

        void UpdateMenu()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            bool hanging = lowering || pending;
            Events["StartLowering"].guiActive = !hanging && !cut;
            Events["Cut"].guiActive = hanging;
            Fields["payout"].guiActive = lowering;
            Fields["status"].guiActive = hanging || cut;     // stowed: the release button says it
            MonoUtilities.RefreshPartContextWindow(part);
        }

        [KSPEvent(guiActive = true, guiName = "#MSL_SkyCrane_UI_Release_and_lower_rover", groupName = Group, groupDisplayName = GroupName)]
        public void StartLowering()
        {
            if (lowering || pending || cut || exit == null || tips.Count == 0) return;
            AttachNode node = part.FindAttachNode("bottom");
            rover = node != null ? node.attachedPart : null;
            // fallback: the first child that is not one of the stage's own components (engines, RCS, tanks)
            if (rover == null)
                foreach (Part c in part.children)
                    if (c.partInfo == null || !c.partInfo.name.StartsWith("MSL.SC.")) { rover = c; break; }
            if (rover == null) { status = MSL.Localization.Text.Get("#MSL_SkyCrane_UI_no_rover_attached"); return; }
            roverAnchors.Clear();
            foreach (Transform t in tips) roverAnchors.Add(rover.transform.InverseTransformPoint(t.position));
            if (umbTip != null) roverUmbilical = rover.transform.InverseTransformPoint(umbTip.position);
            ModuleDecouple dec = null;
            foreach (ModuleDecouple d in part.FindModulesImplementing<ModuleDecouple>())
                if (d.explosiveNodeID == "bottom") dec = d;
            if (dec != null && !dec.isDecoupled) dec.Decouple();
            else rover.decouple();
            pending = true;
            status = MSL.Localization.Text.Get("#MSL_SkyCrane_UI_rover_released");
            UpdateMenu();
        }

        [KSPAction("#MSL_SkyCrane_UI_Sky_crane_release_and_lower_rover")]
        public void StartLoweringAction(KSPActionParam p) { StartLowering(); }

        [KSPEvent(guiActive = true, guiName = "#MSL_SkyCrane_UI_Cut_bridles", groupName = Group, groupDisplayName = GroupName)]
        public void Cut()
        {
            // reel and cutter sounds: Patches/MSL_SkyCrane_Sounds.cfg (none while the part is being destroyed)
            if (part != null && !destroying && (lowering || pending))
            {
                part.Effect("mslBridleReel", 0f);
                part.Effect("mslBridleCut");
            }
            foreach (ConfigurableJoint j in joints) if (j != null) Destroy(j);
            joints.Clear();
            foreach (LineRenderer l in lines) if (l != null) Destroy(l.gameObject);
            lines.Clear();
            // the umbilical is not destroyed with the bridles: break-tie, then retract (LateUpdate)
            if (umbLine != null && umbPhase == 1 && !destroying && rover != null && part != null) { umbPhase = 2; umbPhaseTime = Time.time; }
            else DropUmbilical();
            lowering = false; cut = true; pending = false; tension = 0;
            status = MSL.Localization.Text.Get("#MSL_SkyCrane_UI_cut");
            if (part != null) UpdateMenu();
        }

        [KSPAction("#MSL_SkyCrane_UI_Sky_crane_cut_bridles")]
        public void CutAction(KSPActionParam p) { Cut(); }

        void MakeJoints()
        {
            if (rover == null || rover.rb == null || part.rb == null) return;
            startLength = 0;
            for (int i = 0; i < tips.Count; i++)
            {
                Vector3 tip = rover.transform.TransformPoint(roverAnchors[i]);
                startLength = Mathf.Max(startLength, (tip - exit.position).magnitude);
            }
            for (int i = 0; i < tips.Count; i++)
            {
                ConfigurableJoint j = part.gameObject.AddComponent<ConfigurableJoint>();
                j.connectedBody = rover.rb;
                j.autoConfigureConnectedAnchor = false;
                j.anchor = part.transform.InverseTransformPoint(exit.position);
                j.connectedAnchor = roverAnchors[i];
                j.xMotion = j.yMotion = j.zMotion = ConfigurableJointMotion.Limited;
                j.angularXMotion = j.angularYMotion = j.angularZMotion = ConfigurableJointMotion.Free;
                j.linearLimitSpring = new SoftJointLimitSpring { spring = spring, damper = damper };
                j.linearLimit = new SoftJointLimit { limit = startLength, contactDistance = 0.05f };
                j.enableCollision = true;
                j.breakForce = j.breakTorque = float.PositiveInfinity;
                joints.Add(j);
            }
            for (int i = 0; i < tips.Count; i++)                // the three load-bearing bridles
                lines.Add(NewLine("MSL_Bridle_" + i, 2, lineWidth, new Color(0.10f, 0.10f, 0.09f)));   // dark
            if (umbTip != null)                                 // amber Kapton jacket, as the coil stowed in the device
            {
                DropUmbilical();
                umbLine = NewLine("MSL_Umbilical", UmbPoints, umbilicalWidth, new Color(0.62f, 0.42f, 0.10f));
                umbPhase = 1;
            }
            twistRef = rover.transform.InverseTransformDirection(part.transform.forward);
            startTime = Time.time;
            lowering = true;
            fullLength = false;
            part.Effect("mslBridleStart");
            status = MSL.Localization.Text.Get("#MSL_SkyCrane_UI_lowering");
            UpdateMenu();
        }

        static LineRenderer NewLine(string name, int points, float width, Color colour)
        {
            GameObject go = new GameObject(name);
            LineRenderer l = go.AddComponent<LineRenderer>();
            l.useWorldSpace = true;
            l.positionCount = points;
            l.startWidth = l.endWidth = width;
            l.numCornerVertices = 2;
            l.material = new Material(Shader.Find("KSP/Particles/Alpha Blended") ?? Shader.Find("Sprites/Default"));
            l.startColor = l.endColor = colour;
            return l;
        }

        void DropUmbilical()
        {
            if (umbLine != null) Destroy(umbLine.gameObject);
            umbLine = null;
            umbPhase = 0;
        }

        Vector3 UmbilicalStart() { return (umbExit != null ? umbExit : exit).position; }

        // Umbilical from a (device exit) to b (rover end), cable length len: a curl about the chord whose radius takes up
        // the slack, at least curlRadius, faded out over 0.25 m at both ends; the phase counts from the rover end.
        void DrawUmbilical(Vector3 a, Vector3 b, float len)
        {
            Vector3 d = b - a;
            float D = d.magnitude;
            if (D < 1e-3f) { for (int i = 0; i < UmbPoints; i++) umbLine.SetPosition(i, a); return; }
            Vector3 ax = d / D;
            Vector3 u = Vector3.Cross(ax, part.transform.forward);
            if (u.sqrMagnitude < 1e-4f) u = Vector3.Cross(ax, part.transform.right);
            u.Normalize();
            Vector3 v = Vector3.Cross(ax, u);
            float k = 2 * Mathf.PI / Mathf.Max(curlPitch, 0.01f);
            float ratio = Mathf.Max(len / D, 1f);
            float r = Mathf.Clamp(Mathf.Sqrt(ratio * ratio - 1) / k * 1.3f, curlRadius, 6 * curlRadius);
            r = Mathf.Min(r, 0.25f * D);                        // short spans (stowed, early payout): small curl
            float taper = Mathf.Max(curlPitch * 0.8f, 1e-3f);   // 0.24 m at 1:1, scales with the part
            for (int i = 0; i < UmbPoints; i++)
            {
                float s = i / (float)(UmbPoints - 1);
                float fromRover = D * (1 - s);
                float e = Mathf.SmoothStep(0, 1, Mathf.Clamp01(D * s / taper)) * Mathf.SmoothStep(0, 1, Mathf.Clamp01(fromRover / taper));
                // irregular curls: radius and phase wander along the cable (fixed along it, so they do not flicker)
                float q = fromRover / Mathf.Max(curlPitch, 0.01f);
                float rr = r * e * (0.7f + 0.6f * Mathf.PerlinNoise(q * 0.5f, 0.37f));
                float ph = k * fromRover + 1.5f * (Mathf.PerlinNoise(q * 0.27f, 4.1f) - 0.5f);
                umbLine.SetPosition(i, a + d * s + (u * Mathf.Cos(ph) + v * Mathf.Sin(ph)) * rr);
            }
        }

        // after the cut: the break-tie holds the rover end until the stage has pulled the cable straight (or 1.5 s),
        // then the retract mechanism winds the loose end back to the device in 0.8 s
        void RetractUmbilical()
        {
            if (umbLine == null || part == null) { DropUmbilical(); return; }
            Vector3 a = UmbilicalStart();
            float full = bridleLength + umbilicalExtra;
            if (umbPhase == 2)
            {
                if (rover != null) umbLoose = rover.transform.TransformPoint(roverUmbilical);
                float D = (umbLoose - a).magnitude;
                if (rover == null || D >= full || Time.time - umbPhaseTime > 1.5f) { umbPhase = 3; umbPhaseTime = Time.time; }
                DrawUmbilical(a, umbLoose, Mathf.Max(full, D));
                return;
            }
            float f = Mathf.Clamp01((Time.time - umbPhaseTime) / 0.8f);
            if (f >= 1) { DropUmbilical(); return; }
            Vector3 b = Vector3.Lerp(umbLoose, a, f * f * (3 - 2 * f));
            DrawUmbilical(a, b, (b - a).magnitude * (1 + 0.04f * (1 - f)));
        }

        public void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (pending && rover != null && rover.vessel != vessel && rover.rb != null)
            {
                pending = false;
                MakeJoints();
            }
            if (!lowering) return;
            if (rover == null || rover.rb == null || part.rb == null) { Cut(); return; }
            float L = Mathf.Min(bridleLength, startLength + payoutRate * (Time.time - startTime));
            payout = L;
            tension = 0;
            for (int i = 0; i < joints.Count; i++)
            {
                ConfigurableJoint j = joints[i];
                if (j == null) continue;
                j.linearLimit = new SoftJointLimit { limit = L, contactDistance = 0.05f };
                tension += j.currentForce.magnitude;           // PhysX reaction force of this bridle (kN)
            }
            HoldTwist();
            bool full = L >= bridleLength - 1e-3f;
            if (full) status = MSL.Localization.Text.Get("#MSL_SkyCrane_UI_full_length");
            part.Effect("mslBridleReel", full ? 0f : 1f);
            if (full && !fullLength) part.Effect("mslBridleStop");
            fullLength = full;
        }

        // twist spring-damper between rover and stage about the bridle axis, equal and opposite on both bodies
        void HoldTwist()
        {
            Rigidbody rb = rover.rb, sb = part.rb;
            Vector3 axis = exit.position - rb.worldCenterOfMass;
            if (axis.sqrMagnitude < 1e-4f || twistPeriod <= 0) return;
            axis.Normalize();
            Vector3 a = Vector3.ProjectOnPlane(part.transform.forward, axis);
            Vector3 b = Vector3.ProjectOnPlane(rover.transform.TransformDirection(twistRef), axis);
            if (a.sqrMagnitude < 1e-4f || b.sqrMagnitude < 1e-4f) return;
            float theta = Vector3.SignedAngle(b, a, axis) * Mathf.Deg2Rad;
            float rate = Vector3.Dot(rb.angularVelocity - sb.angularVelocity, axis);
            // rover yaw inertia about the axis from its principal moments
            Vector3 la = Quaternion.Inverse(rb.rotation * rb.inertiaTensorRotation) * axis;
            Vector3 I = rb.inertiaTensor;
            float inertia = la.x * la.x * I.x + la.y * la.y * I.y + la.z * la.z * I.z;
            float w = 2 * Mathf.PI / twistPeriod;
            Vector3 torque = axis * (inertia * (w * w * theta - 2 * twistDamping * w * rate));
            rb.AddTorque(torque, ForceMode.Force);
            sb.AddTorque(-torque, ForceMode.Force);
        }

        public void LateUpdate()
        {
            if (umbPhase >= 2) { RetractUmbilical(); return; }
            if (!lowering || rover == null || exit == null) return;
            for (int i = 0; i < lines.Count; i++)
            {
                lines[i].SetPosition(0, exit.position);
                lines[i].SetPosition(1, rover.transform.TransformPoint(roverAnchors[i]));
            }
            if (umbLine != null)
            {
                Vector3 a = UmbilicalStart(), b = rover.transform.TransformPoint(roverUmbilical);
                // paid out passively behind the bridles, the extra length coming out by full bridle length
                float len = payout * (bridleLength + umbilicalExtra) / Mathf.Max(bridleLength, 0.01f);
                DrawUmbilical(a, b, Mathf.Max(len, (b - a).magnitude));
            }
        }

        public void OnDestroy() { destroying = true; if (lowering) Cut(); DropUmbilical(); }
    }
}
