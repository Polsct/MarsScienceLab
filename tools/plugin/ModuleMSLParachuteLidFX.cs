// Mortar fire seen from outside: when the parachute module hides the PCC lid, a visual copy of the lid is thrown
// aft and tumbles away, with a short flash, flame and smoke puff at the muzzle, and a small recoil push on the
// vessel. The copy has no collider or rigid body and moves relative to the part (safe under floating origin and
// Krakensbane). Fires on the lid's active flag going true -> false while unpacked; a state found on load never
// fires. Thin CO2 air: brief flash, smoke swept aft quickly.
// After the first peak force the canopy partly collapses and re-inflates once; that cycle is drawn on the canopy
// scale in LateUpdate (visual only, depth and timing estimated, faded out below Mach 1.7 and gone below 0.8).
using System.Collections.Generic;
using UnityEngine;

namespace MSLAeroshell
{
    public class ModuleMSLParachuteLidFX : PartModule
    {
        [KSPField] public string lidName = "PCC_Lid";
        [KSPField] public Vector3 ejectAxis = new Vector3(0, -1, 0);   // part frame, out of the mortar (canopy aft)
        [KSPField] public Vector3 sideAxis = new Vector3(1, 0, 0);     // part frame, the fixed sideways push
        [KSPField] public float ejectSpeed = 39;      // m/s relative to the part: mortar muzzle velocity
        [KSPField] public float ejectDelay = 0.1f;    // s the lid stays seated after the bang and flash, then leaves
        [KSPField] public float sideSpeed = 6;        // m/s
        [KSPField] public float dragPerMass = 0.115f; // Cd * A / m, m^2/kg: Cd 1, D 0.764 m, 4 kg
        [KSPField] public float maxAccel = 400;       // m/s^2
        [KSPField] public float spinRate = 540;       // deg/s
        [KSPField] public float lifetime = 6;         // s
        [KSPField] public float maxDistance = 400;    // m
        [KSPField] public float flashTime = 0.08f;    // s: no afterburning in CO2, the glow ends with the gas exit
        [KSPField] public float flashIntensity = 2.5f;
        [KSPField] public float flashRange = 8;       // m
        [KSPField] public float fxTime = 1.5f;        // s, flame and smoke objects removed after this
        [KSPField] public float recoilDeltaV = 0.743f;   // m/s on the vessel, opposite the eject axis
        [KSPField] public float recoilTime = 0.04f;      // s over which the push is spread (an impulse the joints take)
        [KSPField] public float smokeDragPerQ = 0.5f;    // m/s^2 per Pa of dynamic pressure on the smoke, along the airflow
        [KSPField] public float smokeMaxAccel = 400;     // m/s^2
        [KSPField] public string canopyName = "MSL_DGB_canopy";
        [KSPField] public float firstPeakTime = 1.77f;   // s from mortar fire: line stretch 1.135 + inflation 0.635
        [KSPField] public float collapseTime = 0.8f;     // s, the partial collapse and re-inflation after the first peak
        [KSPField] public float collapseDepth = 0.14f;   // canopy width lost at the deepest point (fraction)

        Transform lid;
        Vector3 muzzleLocal;      // lid centre in the part frame
        bool lastActive, primed;
        float unpackedTime;
        float scale = 1;

        GameObject pivot;         // the lid copy hangs under this pivot at the lid centre
        Vector3 offset, velocity, spinAxis;
        Quaternion seatedRotation;   // lid copy in the part frame while it is still seated
        float flightTime;
        GameObject fx;
        Light flash;
        float fxAge;
        float recoilLeft;         // s of recoil push still to apply
        Vector3 recoilDir;
        Transform canopy;
        float canopyAge = -1;     // s since the mortar fire while the collapse cycle is still to draw, else -1
        Vector3 canopyBase;       // canopy scale at the first peak (the end of the inflation clip)
        bool canopyHeld;
        float collapseScale;      // 0 subsonic .. 1 at Mach 1.7 and above: the cycle is a supersonic inflation effect
        readonly List<Object> owned = new List<Object>();   // materials and texture of the effects

        public override string GetInfo() { return ""; }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;
            lid = part.FindModelTransform(lidName);
            if (lid == null) return;
            canopy = part.FindModelTransform(canopyName);
            scale = lid.lossyScale.x;
            if (scale <= 0) scale = 1;
            // the lid transform's origin is not the lid (it sits at the model origin): use the meshes' centre
            Bounds b = new Bounds(); bool any = false;
            foreach (MeshFilter mf in lid.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                Bounds m = mf.sharedMesh.bounds;
                Vector3 c = part.transform.InverseTransformPoint(mf.transform.TransformPoint(m.center));
                if (any) b.Encapsulate(c); else { b = new Bounds(c, Vector3.zero); any = true; }
            }
            muzzleLocal = any ? b.center : part.transform.InverseTransformPoint(lid.position);
        }

        void Update()
        {
            if (lid == null || !HighLogic.LoadedSceneIsFlight || vessel == null) return;
            bool active = lid.gameObject.activeSelf;
            if (vessel.packed)
            {
                // a change while packed (or on load) is taken as it is
                lastActive = active; unpackedTime = 0; primed = false;
                Clear();
                return;
            }
            unpackedTime += Time.deltaTime;
            if (!primed)
            {
                lastActive = active;
                if (unpackedTime > 0.5f) primed = true;
                return;
            }
            if (lastActive && !active) Fire();
            lastActive = active;
        }

        void Fire()
        {
            Clear();
            Vector3 axis = part.transform.TransformDirection(ejectAxis).normalized;
            Vector3 side = part.transform.TransformDirection(Vector3.ProjectOnPlane(sideAxis, ejectAxis)).normalized;
            Vector3 muzzle = part.transform.TransformPoint(muzzleLocal);
            Vector3 airflow = vessel.srf_velocity.sqrMagnitude > 1 ? -(Vector3)vessel.srf_velocity.normalized : axis;
            double q = 0.5 * vessel.atmDensity * vessel.srfSpeed * vessel.srfSpeed;
            Debug.Log(string.Format("[MSL] PCC lid ejected: axis . airflow {0:F2}, speed {1:F0} m/s, density {2:F4}, q {3:F0} Pa",
                Vector3.Dot(axis, airflow), vessel.srfSpeed, vessel.atmDensity, q));
            recoilDir = -axis;
            recoilLeft = recoilDeltaV > 0 && recoilTime > 0 ? recoilTime : 0;
            collapseScale = Mathf.Clamp01(((float)vessel.mach - 0.8f) / 0.9f);
            canopyAge = canopy != null && collapseTime > 0 && collapseScale > 0 ? 0 : -1;

            // the copy
            pivot = new GameObject("MSL PCC lid (ejected)");
            pivot.transform.position = muzzle;
            pivot.transform.rotation = lid.rotation;
            GameObject copy = (GameObject)Instantiate(lid.gameObject, lid.position, lid.rotation);
            copy.name = "MSL ejected lid";
            foreach (Collider c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (Component c in copy.GetComponentsInChildren<Component>(true))
                if (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer)) DestroyImmediate(c);
            copy.transform.localScale = lid.lossyScale;
            copy.transform.SetParent(pivot.transform, true);
            // reflections as at the part: away from the vessel the copy would fall outside the reflection probe and the
            // metallic surface set renders black
            foreach (Renderer r in copy.GetComponentsInChildren<Renderer>(true))
            {
                r.enabled = true; r.forceRenderingOff = false;
                r.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Simple;
                r.probeAnchor = part.transform;
            }
            copy.SetActive(true);
            offset = muzzle - part.transform.position;
            velocity = (axis * ejectSpeed + side * sideSpeed) * scale;
            spinAxis = (side + 0.6f * axis + 0.4f * Vector3.Cross(axis, side)).normalized;
            flightTime = -ejectDelay;
            seatedRotation = Quaternion.Inverse(part.transform.rotation) * lid.rotation;

            // flash, flame, smoke at the muzzle (local to the part: short-lived)
            fx = new GameObject("MSL mortar flash");
            fx.transform.SetParent(part.transform, false);
            fx.transform.localPosition = muzzleLocal;
            fx.transform.localRotation = Quaternion.FromToRotation(Vector3.forward, ejectAxis.normalized);
            flash = fx.AddComponent<Light>();
            flash.type = LightType.Point;
            flash.color = new Color(1f, .9f, .75f);
            flash.range = flashRange * scale;
            flash.intensity = flashIntensity;
            fxAge = 0;
            Texture2D soft = SoftTexture(); owned.Add(soft);
            Material flame = ParticleMaterial(soft, "KSP/Particles/Additive", "Legacy Shaders/Particles/Additive", "Particles/Additive", "Particles/Standard Unlit");
            Material smoke = ParticleMaterial(soft, "KSP/Particles/Alpha Blended", "Legacy Shaders/Particles/Alpha Blended", "Particles/Alpha Blended", "Particles/Standard Unlit");
            // the gas leaving the tube: brief, white-yellow, no orange afterburn
            if (flame != null)
                Burst("flame", flame, 10, 0.04f, 0.09f, 14, 30, 0.25f, 1.1f, 12, 0.10f,
                    Fade(new Color(1f, .98f, .92f), new Color(1f, .9f, .72f), .5f, .5f, 0f), Vector3.zero);
            // the propellant-gas cloud: expands wide and thin in the low pressure and is blown aft by the flow
            float drift = Mathf.Min((float)(smokeDragPerQ * q), smokeMaxAccel);
            if (smoke != null)
                Burst("smoke", smoke, 32, 0.45f, 1.0f, 6, 20, 1.0f, 6.0f, 35, 0.25f,
                    Fade(new Color(.95f, .94f, .92f), new Color(.86f, .85f, .83f), .45f, .45f, 0f), airflow * drift);
        }

        static Gradient Fade(Color a, Color b, float mid, float alpha0, float alpha1)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0), new GradientColorKey(b, mid), new GradientColorKey(b, 1) },
                new[] { new GradientAlphaKey(alpha0, 0), new GradientAlphaKey(alpha0 * .8f, mid), new GradientAlphaKey(alpha1, 1) });
            return g;
        }

        void Burst(string name, Material mat, int count, float life0, float life1, float speed0, float speed1,
            float size0, float size1, float coneAngle, float radius, Gradient colour, Vector3 worldAccel)
        {
            var go = new GameObject("MSL mortar " + name);
            go.SetActive(false);
            go.transform.SetParent(fx.transform, false);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life0, life1);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed0 * scale, speed1 * scale);
            main.startSize = new ParticleSystem.MinMaxCurve(size0 * scale * .7f, size0 * scale);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            main.startColor = Color.white;
            main.gravityModifier = 0;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = count;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = radius * scale;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(colour);
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(size1 / size0, AnimationCurve.EaseInOut(0, size0 / size1, 1, 1));
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = speed1 * scale;
            limit.dampen = 0.15f;
            if (worldAccel.sqrMagnitude > 0)
            {
                // the airflow carries the cloud away from the vessel; its speed in the vessel frame is not capped then
                var force = ps.forceOverLifetime;
                force.enabled = true;
                force.space = ParticleSystemSimulationSpace.World;
                force.x = new ParticleSystem.MinMaxCurve(worldAccel.x * scale);
                force.y = new ParticleSystem.MinMaxCurve(worldAccel.y * scale);
                force.z = new ParticleSystem.MinMaxCurve(worldAccel.z * scale);
                limit.enabled = false;
            }
            var pr = go.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = mat;
            pr.renderMode = ParticleSystemRenderMode.Billboard;
            pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            pr.receiveShadows = false;
            go.SetActive(true);
            ps.Play();
        }

        Material ParticleMaterial(Texture2D tex, params string[] shaders)
        {
            foreach (string s in shaders)
            {
                Shader sh = Shader.Find(s);
                if (sh == null) continue;
                var m = new Material(sh);
                m.mainTexture = tex;
                if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", new Color(.5f, .5f, .5f, .5f));
                owned.Add(m);
                return m;
            }
            return null;
        }

        static Texture2D SoftTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false);
            t.wrapMode = TextureWrapMode.Clamp;
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + .5f) / n * 2 - 1, dy = (y + .5f) / n * 2 - 1;
                    float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    float a = 1 - r; a = a * a * (3 - 2 * a);
                    px[y * n + x] = new Color(1, 1, 1, a);
                }
            t.SetPixels(px); t.Apply(false, true);
            return t;
        }

        void LateUpdate()
        {
            if (pivot == null && fx == null && canopyAge < 0) return;
            if (vessel == null || vessel.packed) { Clear(); return; }
            Collapse();
            float dt = Time.deltaTime;
            if (pivot != null)
            {
                flightTime += dt;
                if (flightTime < 0)
                {
                    // the mortar has fired (flash, bang); the lid is still on the cone
                    pivot.transform.position = part.transform.TransformPoint(muzzleLocal);
                    pivot.transform.rotation = part.transform.rotation * seatedRotation;
                    offset = pivot.transform.position - part.transform.position;
                    dt = 0;
                }
                double rho = vessel.atmDensity;
                if (rho > 0 && dt > 0)
                {
                    Vector3 air = -((Vector3)vessel.srf_velocity + velocity);   // air velocity seen by the lid
                    float v = air.magnitude;
                    if (v > 0.01f)
                    {
                        float a = Mathf.Min((float)(0.5 * rho * v * v * dragPerMass), maxAccel);
                        velocity += air / v * Mathf.Min(a * dt, v);
                    }
                }
                // the part frame decelerates with the vessel (aeroshell drag, canopy pull): the lid gains that
                if (dt > 0) velocity -= Vector3.ClampMagnitude((Vector3)vessel.perturbation, maxAccel) * dt;
                offset += velocity * dt;
                pivot.transform.position = part.transform.position + offset;
                if (dt > 0) pivot.transform.Rotate(spinAxis, spinRate * dt, Space.World);
                if (flightTime > lifetime || offset.magnitude > maxDistance * scale)
                {
                    Debug.Log(string.Format("[MSL] PCC lid copy removed after {0:F1} s, {1:F0} m from the part", flightTime, offset.magnitude));
                    Destroy(pivot); pivot = null;
                }
            }
            if (fx != null)
            {
                fxAge += Time.deltaTime;   // the flash and smoke age from the shot, also while the lid is still seated
                if (flash != null)
                {
                    float f = 1 - fxAge / flashTime;
                    if (f <= 0) { Destroy(flash); flash = null; }
                    else flash.intensity = flashIntensity * f * f;
                }
                if (fxAge > fxTime) ClearFx();
            }
        }

        // the single partial collapse after the first peak: width down and back with a slight overshoot, length up a little
        void Collapse()
        {
            if (canopyAge < 0) return;
            canopyAge += Time.deltaTime;
            if (canopy == null || !canopy.gameObject.activeInHierarchy) { EndCollapse(); return; }
            float s = (canopyAge - firstPeakTime) / collapseTime;
            if (s < 0) return;
            if (!canopyHeld) { canopyBase = canopy.localScale; canopyHeld = true; }
            if (s >= 1) { EndCollapse(); return; }
            // 0 -> deepest at 0.3 -> small overshoot at 0.7 -> 1
            float w = s < 0.3f ? -Mathf.Sin(s / 0.3f * Mathf.PI * 0.5f)
                : s < 0.7f ? -Mathf.Cos((s - 0.3f) / 0.4f * Mathf.PI) * 0.5f - 0.5f + (s - 0.3f) / 0.4f * 0.15f
                : 0.15f * (1 - (s - 0.7f) / 0.3f) * (1 - (s - 0.7f) / 0.3f);
            float d = collapseDepth * collapseScale;
            float xy = 1 + d * w;
            float z = 1 - 0.3f * d * w;
            canopy.localScale = new Vector3(canopyBase.x * xy, canopyBase.y * xy, canopyBase.z * z);
        }

        void EndCollapse()
        {
            if (canopyHeld && canopy != null) canopy.localScale = canopyBase;
            canopyHeld = false;
            canopyAge = -1;
        }

        void FixedUpdate()
        {
            if (recoilLeft <= 0) return;
            if (vessel == null || vessel.packed || part.Rigidbody == null) { recoilLeft = 0; return; }
            float dt = Mathf.Min(TimeWarp.fixedDeltaTime, recoilLeft);
            recoilLeft -= dt;
            // the vessel's delta-v as a force on this part (mass in t, force in kN), through the joint into the stack;
            // the last step is shortened so the whole push is recoilDeltaV
            float kN = (float)vessel.totalMass * recoilDeltaV / recoilTime * dt / TimeWarp.fixedDeltaTime;
            part.Rigidbody.AddForce(recoilDir * kN, ForceMode.Force);
        }

        void ClearFx()
        {
            if (fx != null) Destroy(fx);
            fx = null; flash = null;
            foreach (Object o in owned) if (o != null) Destroy(o);
            owned.Clear();
        }

        void Clear()
        {
            if (pivot != null) Destroy(pivot);
            pivot = null;
            recoilLeft = 0;
            EndCollapse();
            ClearFx();
        }

        void OnDestroy() { Clear(); }
    }
}
