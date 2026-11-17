// ModuleMSLCruiseRCS - balanced thruster allocation for the cruise stage REAs (MSL_CS_REACluster x2).
// Stock ModuleRCS throttles each thruster on its own with no regard to net force; the REAs sit at different
// heights along the stack axis, so every SAS turn left a few newtons of net force on the trajectory.
// The eight REAs are allocated together once per physics frame: throttles c in [0,1] minimise
// |T(c) - T*|^2 + |F(c) - F*|^2 + mu sum(c), T*/F* being the pilot/SAS command scaled to 0.8 of the group's
// authority. Couples and TCM pairs come out force-free. Fuel, Isp, FX and force still go through ModuleRCS
// (CalculateThrust, UpdatePowerFX, AddForceAtPosition), so Waterfall and RealFuels see an ordinary ModuleRCSFX.
// fullThrust is ignored; precision mode scales the command like stock.
// The hidden FixedUpdate below replaces ModuleRCS.FixedUpdate (not virtual); inputs are read by reflection.
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace MSLAeroshell
{
    public class ModuleMSLCruiseRCS : ModuleRCSFX
    {
        [KSPField] public bool balanced = true;       // false: plain stock ModuleRCS allocation (A/B tests)
        [KSPField] public float forceWeight = 1f;     // 1/N in the cost, against 1/(N m) for torque
        [KSPField] public float throttleCost = 3e-3f;  // linear fuel cost (x torqueRef^2): no zero-wrench counter-firing
        [KSPField] public float commandScale = 0.8f;   // full stick = 0.8 of one full couple, so the target stays reachable
        [KSPField] public int sweeps = 40;            // coordinate-descent sweeps per physics frame (warm started)

        const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
        static readonly FieldInfo fInputRot = typeof(ModuleRCS).GetField("inputRot", Priv);
        static readonly FieldInfo fInputLin = typeof(ModuleRCS).GetField("inputLin", Priv);
        static readonly FieldInfo fUsePrecision = typeof(ModuleRCS).GetField("usePrecision", Priv);
        static readonly FieldInfo fExhaustVel = typeof(ModuleRCS).GetField("exhaustVel", Priv);
        static readonly FieldInfo fTotalThrust = typeof(ModuleRCS).GetField("totalThrustForce", Priv);
        static readonly FieldInfo fIsOperating = typeof(ModuleRCS).GetField("isOperating", Priv);
        static readonly MethodInfo mPropStatus = typeof(ModuleRCS).GetMethod("UpdatePropellantStatus", Priv);
        static bool reflectionLogged;

        // one shared solution per vessel and physics step
        class Group
        {
            public double stamp = double.NaN;
            public readonly List<ModuleMSLCruiseRCS> modules = new List<ModuleMSLCruiseRCS>();
            public readonly Dictionary<Transform, float> throttle = new Dictionary<Transform, float>();
            public readonly Dictionary<Transform, float> warm = new Dictionary<Transform, float>();
            public int partCount = -1;
        }
        static readonly Dictionary<Guid, Group> groups = new Dictionary<Guid, Group>();

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!reflectionLogged)
            {
                reflectionLogged = true;
                bool ok = fInputRot != null && fInputLin != null && fUsePrecision != null && fExhaustVel != null
                    && fTotalThrust != null && fIsOperating != null && mPropStatus != null;
                Debug.Log("[MSL] ModuleMSLCruiseRCS: balanced REA allocation " + (ok ? "ready" : "MISSING ModuleRCS members, stock allocation used"));
            }
        }

        bool ReflectionOk
        {
            get { return fInputRot != null && fInputLin != null && fUsePrecision != null && fExhaustVel != null && fTotalThrust != null && fIsOperating != null && mPropStatus != null; }
        }

        // same gate as ModuleRCS.FixedUpdate before it allocates
        bool Firing()
        {
            if (!moduleIsEnabled || vessel == null || !rcsEnabled || IsAdjusterBreakingRCS()) return false;
            if (part.ShieldedFromAirstream && !shieldedCanThrust) return false;
            rcs_active = vessel.ActionGroups[KSPActionGroup.RCS];
            return rcs_active;
        }

        static bool Usable(Transform t)
        {
            return t != null && t.position != Vector3.zero && t.gameObject.activeInHierarchy;
        }

        Vector3 Exhaust(Transform t) { return useZaxis ? t.forward : t.up; }

        public new void FixedUpdate()
        {
            if (!ReflectionOk || !balanced) { base.FixedUpdate(); return; }
            fIsOperating.SetValue(this, false);
            if (!HighLogic.LoadedSceneIsFlight) return;
            if (TimeWarp.CurrentRate > 1f && TimeWarp.WarpMode == TimeWarp.Modes.HIGH) { DeactivatePowerFX(); return; }
            tC = thrusterTransforms.Count;
            if (thrustForces.Length != tC) thrustForces = new float[tC];
            for (int k = 0; k < tC; k++) thrustForces[k] = 0f;
            fTotalThrust.SetValue(this, 0f);
            realISP = atmosphereCurve.Evaluate((float)part.staticPressureAtm);
            fExhaustVel.SetValue(this, (double)realISP * G * ispMult);
            if (!Firing()) { DeactivateFX(); return; }
            Vector3 rot = (Vector3)fInputRot.GetValue(this), lin = (Vector3)fInputLin.GetValue(this);
            if (rot == Vector3.zero && lin == Vector3.zero) { DeactivateFX(); return; }

            Group g = Solve();
            mPropStatus.Invoke(this, null);
            float recip = 1f / thrusterPower, total = 0f;
            bool operating = false;
            for (int i = 0; i < tC; i++)
            {
                Transform t = thrusterTransforms[i];
                if (!Usable(t)) continue;
                float c;
                if (!g.throttle.TryGetValue(t, out c)) c = 0f;
                bool success;
                float force = CalculateThrust(c, out success);
                thrustForces[i] = force;
                bool on = force > 0f && success;
                operating |= on;
                UpdatePowerFX(on, i, Mathf.Clamp(force * recip, 0.1f, 1f));
                if (!on || isJustForShow) continue;
                total += force;
                part.AddForceAtPosition(-Exhaust(t) * force, t.position);
            }
            fTotalThrust.SetValue(this, total);
            fIsOperating.SetValue(this, operating);
        }

        Group Solve()
        {
            Group g;
            if (!groups.TryGetValue(vessel.id, out g)) { DropStaleGroups(); g = new Group(); groups[vessel.id] = g; }
            double stamp = Time.fixedTime;
            if (g.stamp == stamp) return g;
            g.stamp = stamp;
            if (g.partCount != vessel.parts.Count || g.modules.Count == 0 || g.modules.Exists(m => m == null || m.vessel != vessel))
            {
                g.modules.Clear();
                g.modules.AddRange(vessel.FindPartModulesImplementing<ModuleMSLCruiseRCS>());
                g.partCount = vessel.parts.Count;
            }
            g.throttle.Clear();

            // columns: every usable thruster of every module that may fire this frame
            List<Transform> ts = new List<Transform>();
            List<Vector3> fs = new List<Vector3>(), ps = new List<Vector3>();
            Vector3 com = vessel.CurrentCoM;
            for (int m = 0; m < g.modules.Count; m++)
            {
                ModuleMSLCruiseRCS mod = g.modules[m];
                if (!mod.balanced || !mod.Firing()) continue;
                float fmax = mod.thrusterPower * mod.thrustPercentage * 0.01f;
                for (int i = 0; i < mod.thrusterTransforms.Count; i++)
                {
                    Transform t = mod.thrusterTransforms[i];
                    if (!Usable(t)) continue;
                    ts.Add(t); fs.Add(-mod.Exhaust(t) * fmax); ps.Add(t.position - com);
                }
            }
            int n = ts.Count;
            if (n == 0) return g;

            // commanded wrench: stock fires a thruster when exhaust . (rot x r) > 0 or exhaust . lin > 0,
            // i.e. it wants torque along -rot and force along -lin
            Vector3 rot = (Vector3)fInputRot.GetValue(this), lin = (Vector3)fInputLin.GetValue(this);
            double[,] a = new double[6, n];
            double torqueRef = 0, forceRef = 0, lever = 0;
            int leverN = 0;
            for (int j = 0; j < n; j++)
            {
                Vector3 tq = Vector3.Cross(ps[j], fs[j]);
                a[0, j] = tq.x; a[1, j] = tq.y; a[2, j] = tq.z;
                a[3, j] = fs[j].x * forceWeight; a[4, j] = fs[j].y * forceWeight; a[5, j] = fs[j].z * forceWeight;
                torqueRef += tq.magnitude; forceRef += fs[j].magnitude;
                float l = fs[j].sqrMagnitude > 0f ? tq.magnitude / fs[j].magnitude : 0f;
                if (l > 1f) { lever += l; leverN++; }
            }
            // full stick = one full couple / one full thruster pair along the axis
            torqueRef *= commandScale / Math.Max(1, n / 2); forceRef *= commandScale / Math.Max(1, n / 2);
            float scale = 1f;
            if ((bool)fUsePrecision.GetValue(this))
                scale = useLever ? (leverN > 0 ? (float)(leverN / lever) : 1f) : precisionFactor;
            Vector3 tStar = -rot * (float)torqueRef * scale, fStar = -lin * (float)forceRef * scale * forceWeight;
            double[] b = { tStar.x, tStar.y, tStar.z, fStar.x, fStar.y, fStar.z };

            // box-constrained least squares by coordinate descent on H c = q
            double[,] h = new double[n, n];
            double[] q = new double[n], c = new double[n];
            double pen = throttleCost * torqueRef * torqueRef;
            for (int i = 0; i < n; i++)
            {
                for (int k = 0; k < 6; k++) q[i] += a[k, i] * b[k];
                q[i] -= pen;
                for (int j = i; j < n; j++)
                {
                    double s = 0;
                    for (int k = 0; k < 6; k++) s += a[k, i] * a[k, j];
                    h[i, j] = h[j, i] = s;
                }
                if (h[i, i] < 1e-9) h[i, i] = 1e-9;
                float w;
                c[i] = g.warm.TryGetValue(ts[i], out w) ? w : 0.0;
            }
            for (int sweep = 0; sweep < sweeps; sweep++)
            {
                double moved = 0;
                for (int i = 0; i < n; i++)
                {
                    double grad = -q[i];
                    for (int j = 0; j < n; j++) grad += h[i, j] * c[j];
                    double v = c[i] - grad / h[i, i];
                    v = v < 0 ? 0 : v > 1 ? 1 : v;
                    moved = Math.Max(moved, Math.Abs(v - c[i]));
                    c[i] = v;
                }
                if (moved < 1e-5) break;
            }
            g.warm.Clear();
            for (int i = 0; i < n; i++)
            {
                float v = c[i] < 1e-4 ? 0f : (float)c[i];
                g.throttle[ts[i]] = v; g.warm[ts[i]] = v;
            }
            return g;
        }

        // no OnDestroy here: ModuleRCS has a private one that Unity would then skip
        static void DropStaleGroups()
        {
            List<Guid> dead = new List<Guid>();
            foreach (KeyValuePair<Guid, Group> kv in groups)
                if (kv.Value.modules.TrueForAll(m => m == null || m.vessel == null || m.vessel.id != kv.Key)) dead.Add(kv.Key);
            for (int i = 0; i < dead.Count; i++) groups.Remove(dead[i]);
        }
    }
}
