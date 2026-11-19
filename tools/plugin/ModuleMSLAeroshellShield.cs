// Closed aeroshell payload shielding through the stock cargo-bay contract, recomputed on load/unpack and attachment
// changes; the payload is exposed as soon as either shell separates. While closed, every payload mesh leaves the
// re-entry FX layer; with hideEnclosedPayload (off by default) the ones wholly inside the shell are not drawn at all.
using System.Collections.Generic;
using UnityEngine;

namespace MSLAeroshell
{
    public class ModuleMSLAeroshellShield : PartModule, IAirstreamShield
    {
        [KSPField] public string heatShieldNode = "top";
        [KSPField] public string payloadNode = "payload";
        [KSPField] public float maxPayloadRadius = 2.1f;
        [KSPField] public float minPayloadY = -1.33f;
        [KSPField] public float maxPayloadY = 0.55f;
        [KSPField] public bool hideEnclosedPayload = false;
        [KSPField] public bool enclosedNoFlame = true;
        // While the shell is closed the flame proxy draws the whole capsule's flames, and every payload mesh leaves the FX
        // layer, not only the wholly enclosed ones, so no payload flame shows through the closed shell.
        [KSPField] public bool allPayloadNoFlame = true;
        // Backshell OML in this part's frame (y = 0 at the seal) and the inset a mesh must keep to count as enclosed (shell
        // thickness plus clearance, so RCS heads in their holes stay drawn). The heat shield side needs no margin.
        [KSPField] public float sealRadius = 2.2248f;
        [KSPField] public float hipY = -0.9049f;
        [KSPField] public float hipRadius = 1.5429f;
        [KSPField] public float bipY = -1.3585f;
        [KSPField] public float bipRadius = 0.788f;
        [KSPField] public float forebodyCone = 70f;
        [KSPField] public float wallMargin = 0.06f;
        // Inner face of the heat shield, 0.107 m forward of its OML line; payload may come this far forward, as a fraction
        // of the seal radius.
        [KSPField] public float forebodyInset = 0.04f;
        // Shell layers under the TPS: moved to a layer the flight camera draws and the FX cameras do not, or the stock FX
        // draws a second, brighter flame cone inside the outer one.
        [KSPField] public string noFlameMeshes = "HS_Struct_Sandwich,HS_Inner_ThermalBlanket,HS_MEDLI_Harness,HS_Frame_ShoulderCloseoutRing,HS_Interface_SeparationRing,BS_Struct_Sandwich,BS_BIP_Plate_Al,BS_Interface_SeparationRing,BS_TPS_SLA561V,BS_BIP_TPS_AcusilII,BS_PCC_BaseFastenerPorts,BS_PCC_Closeout_Covers,BS_PCC_TLGA_Fasteners,BS_PCC_Lid_TPS_AcusilII,BS_PCC_Lid_InnerFacesheet,BS_PCC_Lid_Fasteners,BS_Mortar_Canister,BS_Mortar_Breech,BS_Mortar_Initiators";
        // Child parts searched for noFlameMeshes too (PCC lid and mortar), and the entry balance masses while the flame
        // proxy stands in for the shell.
        [KSPField] public string noFlameChildParts = "MSL.AS.Parachute,MSL.EBMD";
        // Payload meshes that poke through the backshell holes (Sky Crane RCS heads) stay drawn, so they drew flames on
        // the shell. While the shell is closed they leave the FX layer (prefixes); back on layer 0 once it opens.
        [KSPField] public string noFlamePayloadMeshes = "M_RCS_";
        readonly List<GameObject> payloadNoFlame = new List<GameObject>();
        // Firefly builds envelopes from every renderer except layer 1 and ignores forceRenderingOff, so hidden payload also
        // goes to noFlameLayer and Firefly is asked to rebuild after a layer change.
        readonly Dictionary<MeshRenderer, int> hiddenLayer = new Dictionary<MeshRenderer, int>();
        bool fireflyDirty;
        float nextFirefly;
        [KSPField] public int noFlameLayer = 1;
        // Inward faces of the TPS skins inside splitRadius move to a child renderer on noFlameLayer, or their flames run
        // through the interior. Inward = normals pointing at (0, splitCentreY, 0).
        [KSPField] public string splitFlameMeshes = "HS_TPS_PICA_Shell";
        // Meshes listed here lose every face inside splitRadius, outward ones too: only the rim band draws flames.
        [KSPField] public string splitWholeMeshes = "HS_TPS_PICA_Shell";
        Light glow;
        Camera velCam, fxDepthCam;
        [KSPField] public float splitRadius = 0.93f;
        [KSPField] public float splitCentreY = -0.35f;
        float nextLayerCheck;
        // Flame proxy: while the heat shield is on, every shell mesh goes to noFlameLayer and one smooth body of revolution
        // (proxyTriangles) stands in for the closed shell on the FX layer, drawn only by the stock FX cameras. Firefly uses
        // a transform named "atmofx_envelope" as the part's only envelope, hence the name. Removed with the heat shield.
        // proxyProfile: closed OML as y,r pairs in seal radii, backshell frame (y = 0 at the seal, nose toward +y).
        [KSPField] public bool flameProxy = true;
        [KSPField] public int proxyTriangles = 10000;
        [KSPField] public int proxySegments = 96;
        [KSPField] public string proxyProfile = "0.4088,0.0000;0.4079,0.0294;0.4054,0.0587;0.4011,0.0878;0.3952,0.1166;0.3875,0.1450;0.3783,0.1729;0.0850,0.9784;0.0807,0.9860;0.0754,0.9929;0.0690,0.9989;0.0618,1.0038;0.0539,1.0076;0.0455,1.0101;0.0369,1.0112;0.0281,1.0110;0.0195,1.0095;0.0113,1.0066;0.0036,1.0025;-0.4067,0.6935;-0.6155,0.3457;-0.6497,0.2863;-0.8596,0.1717;-0.8596,0.0000";
        // Proxy scale-up about its middle, so it lies just outside the faceted real skin (0 gives patchy flames).
        [KSPField] public float proxyInflate = 0.02f;
        // The proxy ends at this y (seal radii, backshell frame) and is open behind it; the real backshell still occludes.
        // -1 or less: the whole closed shell.
        [KSPField] public float proxyAftY = -0.2f;
        // proxyAftY when Firefly is installed (Firefly draws the aft band as a flaming ring on the backshell).
        [KSPField] public float proxyAftYFirefly = 0f;
        static bool? fireflyInstalled;
        static bool FireflyInstalled()
        {
            if (fireflyInstalled == null)
            {
                fireflyInstalled = false;
                foreach (AssemblyLoader.LoadedAssembly a in AssemblyLoader.loadedAssemblies)
                    if (a.name == "Firefly") { fireflyInstalled = true; break; }
            }
            return fireflyInstalled.Value;
        }
        // Pockets of the balance masses are left out of the proxy (within proxyPocketRadius of nodes named proxyPocketNodes).
        [KSPField] public string proxyPocketNodes = "ebmd";
        [KSPField] public float proxyPocketRadius = 0.035f;
        GameObject proxy;
        MeshRenderer proxyRenderer;
        Camera fxCam;
        readonly List<GameObject> proxyMoved = new List<GameObject>();
        [KSPField(guiName = "#MSL_Aeroshell_UI_Payload_protection", groupName = "mslEDL", groupDisplayName = "#MSL_Aeroshell_UI_grp_EDL")]
        public string status = MSL.Localization.Text.Get("#MSL_Aeroshell_UI_open");

        readonly HashSet<Part> protectedParts = new HashSet<Part>();
        readonly HashSet<Part> wanted = new HashSet<Part>();
        readonly HashSet<Part> visited = new HashSet<Part>();
        readonly Stack<Part> todo = new Stack<Part>();
        readonly List<MeshRenderer> candidates = new List<MeshRenderer>();
        readonly List<Mesh> candidateMeshes = new List<Mesh>();
        readonly Dictionary<MeshRenderer, Matrix4x4> poses = new Dictionary<MeshRenderer, Matrix4x4>();
        readonly HashSet<MeshRenderer> hidden = new HashSet<MeshRenderer>();
        readonly HashSet<MeshRenderer> known = new HashSet<MeshRenderer>();
        readonly List<MeshRenderer> scan = new List<MeshRenderer>();
        float nextScan;
        float nextPoseCheck;
        static readonly Vector3[] corners = new Vector3[8];
        static readonly List<Vector3> vertices = new List<Vector3>();

        public bool ClosedAndLocked()
        {
            if (part == null || vessel == null) return false;
            AttachNode cap = part.FindAttachNode(heatShieldNode);
            AttachNode payload = part.FindAttachNode(payloadNode);
            Part hs = cap != null ? cap.attachedPart : null;
            return hs != null && hs.vessel == vessel && hs.partInfo != null &&
                hs.partInfo.name == "MSL.HeatShield" && payload != null &&
                payload.attachedPart != null && payload.attachedPart.vessel == vessel;
        }

        public Vessel GetVessel() { return vessel; }
        public Part GetPart() { return part; }

        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if (!HighLogic.LoadedSceneIsFlight) return;
            GameEvents.onVesselWasModified.Add(OnModified);
            GameEvents.onVesselGoOffRails.Add(OnModified);
            Refresh();
        }

        void OnModified(Vessel v) { if (v == vessel) { Refresh(); nextLayerCheck = 0f; } }

        void FixedUpdate()
        {
            if (!HighLogic.LoadedSceneIsFlight) return;
            Refresh();
            if (RenderersChanged()) CollectCandidates();
            CheckPoses();
            NoFlameLayers();
            if (fireflyDirty && Time.unscaledTime >= nextFirefly) FireflyRebuild();
        }

        // Every 2 s (the game puts part models back on layer 0 in places): inner shell layers off the FX layer.
        void NoFlameLayers()
        {
            if (noFlameMeshes.Length == 0) return;
            // the stock upstream-occlusion camera and the FX depth camera see layer 0 only: meshes moved off the
            // FX layer must still shadow what is downstream and hide the flames behind them
            int bit = 1 << noFlameLayer;
            if (velCam != null && (velCam.cullingMask & bit) == 0) velCam.cullingMask |= bit;
            if (fxDepthCam != null && (fxDepthCam.cullingMask & bit) == 0) fxDepthCam.cullingMask |= bit;
            if (Time.unscaledTime < nextLayerCheck) return;
            if (velCam == null) { GameObject g = GameObject.Find("velocity camera"); if (g != null) velCam = g.GetComponent<Camera>(); }
            if (fxDepthCam == null) { GameObject g = GameObject.Find("FXDepthCamera"); if (g != null) fxDepthCam = g.GetComponent<Camera>(); }
            nextLayerCheck = Time.unscaledTime + 2f;
            PayloadNoFlame();
            string[] names = noFlameMeshes.Split(',');
            string[] splits = splitFlameMeshes.Split(',');
            // the re-entry glow light reaches layer 0 only: let it light the meshes moved off the FX layer
            if (glow == null) { GameObject g = GameObject.Find("reentryGlowLight"); if (g != null) glow = g.GetComponent<Light>(); }
            if (glow != null) glow.cullingMask |= 1 << noFlameLayer;
            AttachNode cap = part.FindAttachNode(heatShieldNode);
            Part hs = cap != null ? cap.attachedPart : null;
            string[] childParts = noFlameChildParts.Split(',');
            bool closed = flameProxy && hs != null && hs.vessel == vessel && hs.partInfo != null && hs.partInfo.name == "MSL.HeatShield";
            if (!closed) DropProxy(names);
            else
            {
                if (proxy == null) BuildProxy();
                // a jettisoned part (balance mass) flames on its own again
                for (int i = proxyMoved.Count - 1; i >= 0; i--)
                {
                    GameObject g = proxyMoved[i];
                    Part owner = g != null ? g.GetComponentInParent<Part>() : null;
                    if (g != null && owner != null && owner.vessel == vessel) continue;
                    if (g != null && g.layer == noFlameLayer && System.Array.IndexOf(names, g.name) < 0) g.layer = 0;
                    proxyMoved.RemoveAt(i);
                }
                if (fxCam == null && FXCamera.Instance != null) fxCam = FXCamera.Instance.GetComponent<Camera>();
            }
            for (int k = -2; k < part.children.Count; k++)
            {
                Part p = k == -2 ? part : k == -1 ? hs : part.children[k];
                if (p == null || (k == -1 && (p.partInfo == null || p.partInfo.name != "MSL.HeatShield"))) continue;
                if (k >= 0 && (p.partInfo == null || System.Array.IndexOf(childParts, p.partInfo.name) < 0)) continue;
                if (proxy != null)
                {
                    // the proxy flames for the whole shell: every mesh of these parts leaves the FX layer
                    foreach (MeshRenderer mr in p.transform.GetComponentsInChildren<MeshRenderer>(true))
                        if (mr.gameObject.layer == 0 && mr != proxyRenderer && mr.GetComponent<Collider>() == null &&
                            mr.GetComponentInParent<Part>() == p)
                        { mr.gameObject.layer = noFlameLayer; proxyMoved.Add(mr.gameObject); fireflyDirty = true; }
                    continue;
                }
                foreach (MeshRenderer mr in p.transform.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.gameObject.layer == 0 && System.Array.IndexOf(names, mr.name) >= 0 && mr.GetComponent<Collider>() == null &&
                        mr.GetComponentInParent<Part>() == p)
                    { mr.gameObject.layer = noFlameLayer; fireflyDirty = true; }
                foreach (MeshRenderer mr in p.transform.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.gameObject.layer == 0 && System.Array.IndexOf(splits, mr.name) >= 0 && mr.GetComponentInParent<Part>() == p &&
                        mr.transform.Find(mr.name + "_inner") == null)
                    { SplitInward(mr); fireflyDirty = true; }
            }
        }

        void BuildProxy()
        {
            Mesh mesh = ProxyMesh();
            if (mesh == null) { flameProxy = false; return; }
            Material mat = null;
            foreach (MeshRenderer mr in part.transform.GetComponentsInChildren<MeshRenderer>(true))
                if (mr.sharedMaterial != null && mr.GetComponentInParent<Part>() == part) { mat = mr.sharedMaterial; break; }
            if (mat == null) { flameProxy = false; return; }
            Transform model = part.transform.Find("model");
            if (model == null) model = part.transform;
            proxy = new GameObject("atmofx_envelope");
            proxy.layer = 0;
            proxy.transform.SetParent(model, false);
            proxy.transform.position = part.transform.position;
            proxy.transform.rotation = part.transform.rotation;
            Vector3 s = model.lossyScale;
            proxy.transform.localScale = new Vector3(1f / s.x, 1f / s.y, 1f / s.z);
            proxy.AddComponent<MeshFilter>().sharedMesh = mesh;
            proxyRenderer = proxy.AddComponent<MeshRenderer>();
            // a shell material: the stock FX cameras draw by replacement shader and need its RenderType tag
            proxyRenderer.sharedMaterial = mat;
            proxyRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            proxyRenderer.receiveShadows = false;
            proxyRenderer.enabled = false;
            Camera.onPreCull += ProxyPreCull;
            Camera.onPostRender += ProxyPostRender;
            fireflyDirty = true;
            Debug.Log("[MSLAeroshell] flame proxy: " + mesh.triangles.Length / 3 + " triangles stand in for the closed shell on the re-entry FX layer, cut at y " + (FireflyInstalled() ? proxyAftYFirefly : proxyAftY) + (FireflyInstalled() ? " (Firefly)" : " (stock FX)"));
        }

        void ProxyPreCull(Camera c)
        {
            if (proxyRenderer == null) return;
            proxyRenderer.enabled = c != null && ((object)c == (object)fxCam || (object)c == (object)velCam || (object)c == (object)fxDepthCam);
        }

        void ProxyPostRender(Camera c) { if (proxyRenderer != null) proxyRenderer.enabled = false; }

        // Heat shield gone (or proxy switched off): shell meshes return to the FX layer, except the ones the
        // name lists keep off it.
        void DropProxy(string[] keep)
        {
            if (proxy == null && proxyMoved.Count == 0) return;
            Camera.onPreCull -= ProxyPreCull;
            Camera.onPostRender -= ProxyPostRender;
            if (proxy != null)
            {
                MeshFilter f = proxy.GetComponent<MeshFilter>();
                if (f != null && f.sharedMesh != null) Destroy(f.sharedMesh);
                // Firefly looks the envelope up by name on its next rebuild, before Destroy has taken effect
                proxy.name = "flame_proxy_removed";
                proxy.transform.SetParent(null, false);
                Destroy(proxy);
            }
            proxy = null;
            proxyRenderer = null;
            foreach (GameObject g in proxyMoved)
                if (g != null && g.layer == noFlameLayer && (keep == null || System.Array.IndexOf(keep, g.name) < 0)) g.layer = 0;
            proxyMoved.Clear();
            fireflyDirty = true;
        }

        // Closed body of revolution through proxyProfile. Profile corners are kept; the remaining rings go to the
        // longest spans, so the triangles are close to uniform along the outline.
        Mesh ProxyMesh()
        {
            var prof = new List<Vector2>();
            foreach (string pair in proxyProfile.Split(';'))
            {
                string[] yr = pair.Split(',');
                float y, r;
                if (yr.Length != 2 || !float.TryParse(yr[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y) ||
                    !float.TryParse(yr[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r)) continue;
                Vector2 pt = new Vector2(y * sealRadius, Mathf.Max(0f, r) * sealRadius);
                float cut = (FireflyInstalled() ? proxyAftYFirefly : proxyAftY) * sealRadius;
                if (pt.x < cut && prof.Count > 0)
                {
                    // the profile runs nose to lid (y falling): end it on the cut plane
                    Vector2 last = prof[prof.Count - 1];
                    if (last.x > cut + 1e-5f) prof.Add(Vector2.Lerp(last, pt, (last.x - cut) / (last.x - pt.x)));
                    break;
                }
                prof.Add(pt);
            }
            int seg = Mathf.Clamp(proxySegments, 8, 256);
            if (prof.Count < 3) return null;
            // triangles = 2 * seg * (stations - 2) with a pole at each end
            int stations = Mathf.Max(prof.Count, proxyTriangles / (2 * seg) + 2);
            var cuts = new int[prof.Count - 1];
            for (int extra = stations - prof.Count; extra > 0; extra--)
            {
                int best = 0; float bestLen = -1f;
                for (int i = 0; i < cuts.Length; i++)
                {
                    float len = (prof[i + 1] - prof[i]).magnitude / (cuts[i] + 1);
                    if (len > bestLen) { bestLen = len; best = i; }
                }
                cuts[best]++;
            }
            var line = new List<Vector2>();
            for (int i = 0; i < cuts.Length; i++)
                for (int k = 0; k <= cuts[i]; k++) line.Add(Vector2.Lerp(prof[i], prof[i + 1], k / (float)(cuts[i] + 1)));
            line.Add(prof[prof.Count - 1]);
            float eps = 1e-4f * sealRadius;
            var verts = new List<Vector3>();
            var first = new int[line.Count];
            for (int i = 0; i < line.Count; i++)
            {
                first[i] = verts.Count;
                if (line[i].y <= eps) { verts.Add(new Vector3(0f, line[i].x, 0f)); continue; }
                for (int k = 0; k < seg; k++)
                {
                    float a = 2f * Mathf.PI * k / seg;
                    verts.Add(new Vector3(line[i].y * Mathf.Cos(a), line[i].x, line[i].y * Mathf.Sin(a)));
                }
            }
            var tris = new List<int>();
            for (int i = 0; i + 1 < line.Count; i++)
            {
                bool poleA = line[i].y <= eps, poleB = line[i + 1].y <= eps;
                if (poleA && poleB) continue;
                for (int k = 0; k < seg; k++)
                {
                    int k1 = (k + 1) % seg;
                    int a0 = first[i] + (poleA ? 0 : k), a1 = first[i] + (poleA ? 0 : k1);
                    int b0 = first[i + 1] + (poleB ? 0 : k), b1 = first[i + 1] + (poleB ? 0 : k1);
                    if (!poleB) { tris.Add(a0); tris.Add(b1); tris.Add(b0); }
                    if (!poleA) { tris.Add(a0); tris.Add(a1); tris.Add(b1); }
                }
            }
            CutPockets(verts, tris);
            if (proxyInflate != 0f)
            {
                Vector3 mid = new Vector3(0f, 0.5f * (prof[0].x + prof[prof.Count - 1].x), 0f);
                for (int i = 0; i < verts.Count; i++) verts[i] = mid + (verts[i] - mid) * (1f + proxyInflate);
            }
            var mesh = new Mesh();
            mesh.name = "MSL_FlameProxy";
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            // normals must face outward: flip the winding if they came out facing the axis
            Vector3[] n = mesh.normals;
            float outward = 0f;
            for (int i = 0; i < verts.Count; i++) outward += n[i].x * verts[i].x + n[i].z * verts[i].z;
            if (outward < 0f)
            {
                for (int i = 0; i < tris.Count; i += 3) { int t = tris[i]; tris[i] = tris[i + 1]; tris[i + 1] = t; }
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
            }
            mesh.RecalculateBounds();
            return mesh;
        }

        void CutPockets(List<Vector3> verts, List<int> tris)
        {
            if (proxyPocketNodes.Length == 0 || proxyPocketRadius <= 0f) return;
            string[] prefixes = proxyPocketNodes.Split(',');
            float rad = proxyPocketRadius * sealRadius;
            int cut = 0, pockets = 0;
            foreach (AttachNode node in part.attachNodes)
            {
                bool match = false;
                foreach (string pre in prefixes) if (pre.Length > 0 && node.id.StartsWith(pre)) match = true;
                if (!match) continue;
                pockets++;
                Vector3 axis = node.orientation.normalized;
                for (int i = tris.Count - 3; i >= 0; i -= 3)
                {
                    Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                    bool hit = false;
                    // the node sits in the pocket, a little off the OML: points along its axis
                    for (int k = -2; k <= 2 && !hit; k++)
                    {
                        Vector3 q = node.position + axis * (k * 0.5f * rad);
                        hit = (ClosestOnTriangle(q, a, b, c) - q).sqrMagnitude < rad * rad;
                    }
                    if (hit) { tris.RemoveRange(i, 3); cut++; }
                }
            }
            if (pockets > 0) Debug.Log("[MSLAeroshell] flame proxy: " + cut + " triangles left out over " + pockets + " balance-mass pockets");
        }

        // Closest point of triangle abc to p (barycentric Voronoi regions).
        static Vector3 ClosestOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, ap = p - a;
            float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0f && d2 <= 0f) return a;
            Vector3 bp = p - b;
            float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0f && d4 <= d3) return b;
            float vc = d1 * d4 - d3 * d2;
            if (vc <= 0f && d1 >= 0f && d3 <= 0f) return a + ab * (d1 / (d1 - d3));
            Vector3 cp = p - c;
            float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0f && d5 <= d6) return c;
            float vb = d5 * d2 - d1 * d6;
            if (vb <= 0f && d2 >= 0f && d6 <= 0f) return a + ac * (d2 / (d2 - d6));
            float va = d3 * d6 - d5 * d4;
            if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
            float den = 1f / (va + vb + vc);
            return a + ab * (vb * den) + ac * (vc * den);
        }

        void PayloadNoFlame()
        {
            if (protectedParts.Count == 0)
            {
                foreach (GameObject g in payloadNoFlame) if (g != null && g.layer == noFlameLayer) g.layer = 0;
                if (payloadNoFlame.Count > 0) fireflyDirty = true;
                payloadNoFlame.Clear();
                return;
            }
            if (noFlamePayloadMeshes.Length == 0) return;
            string[] prefixes = noFlamePayloadMeshes.Split(',');
            foreach (Part p in protectedParts)
            {
                if (p == null) continue;
                foreach (MeshRenderer mr in p.transform.GetComponentsInChildren<MeshRenderer>(true))
                {
                    if (mr.gameObject.layer != 0 || mr.GetComponent<Collider>() != null) continue;
                    foreach (string pre in prefixes)
                        if (pre.Length > 0 && mr.name.StartsWith(pre))
                        {
                            mr.gameObject.layer = noFlameLayer;
                            payloadNoFlame.Add(mr.gameObject);
                            fireflyDirty = true;
                            break;
                        }
                }
            }
        }

        readonly List<Mesh> splitMeshes = new List<Mesh>();
        void SplitInward(MeshRenderer mr)
        {
            MeshFilter f = mr.GetComponent<MeshFilter>();
            if (f == null || f.sharedMesh == null || !f.sharedMesh.isReadable) return;
            Mesh src = f.sharedMesh;
            Vector3[] v = src.vertices, n = src.normals;
            if (n.Length != v.Length) return;
            Matrix4x4 m = part.transform.worldToLocalMatrix * mr.transform.localToWorldMatrix;
            Vector3 centre = new Vector3(0f, splitCentreY * sealRadius, 0f);
            float r2 = splitRadius * sealRadius * splitRadius * sealRadius;
            bool whole = System.Array.IndexOf(splitWholeMeshes.Split(','), mr.name) >= 0;
            var inward = new bool[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 q = m.MultiplyPoint3x4(v[i]);
                inward[i] = q.x * q.x + q.z * q.z < r2 && (whole || Vector3.Dot(m.MultiplyVector(n[i]), q - centre) < 0f);
            }
            Mesh outer = Instantiate(src), inner = Instantiate(src);
            splitMeshes.Add(outer); splitMeshes.Add(inner);   // runtime copies: destroyed with the module
            int moved = 0;
            var a = new List<int>(); var b = new List<int>();
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                int[] t = src.GetTriangles(sm);
                a.Clear(); b.Clear();
                for (int i = 0; i < t.Length; i += 3)
                {
                    bool inw = inward[t[i]] && inward[t[i + 1]] && inward[t[i + 2]];
                    List<int> to = inw ? b : a;
                    to.Add(t[i]); to.Add(t[i + 1]); to.Add(t[i + 2]);
                    if (inw) moved++;
                }
                outer.SetTriangles(a, sm);
                inner.SetTriangles(b, sm);
            }
            var go = new GameObject(mr.name + "_inner");
            go.transform.SetParent(mr.transform, false);
            go.layer = noFlameLayer;
            go.AddComponent<MeshFilter>().sharedMesh = inner;
            go.AddComponent<MeshRenderer>().sharedMaterials = mr.sharedMaterials;
            f.sharedMesh = outer;
            Debug.Log("[MSLAeroshell] " + mr.name + ": " + moved + " inward triangles off the re-entry FX layer");
        }

        void Refresh()
        {
            wanted.Clear();
            visited.Clear();
            todo.Clear();
            if (ClosedAndLocked())
            {
                // Walk only the payload attachment branch. Never protect the
                // external cruise stage, heat shield or the backshell itself.
                visited.Add(part);
                todo.Push(part.FindAttachNode(payloadNode).attachedPart);
                while (todo.Count > 0)
                {
                    Part p = todo.Pop();
                    if (p == null || p.vessel != vessel || !visited.Add(p)) continue;
                    // A physicsless part goes with its parent: the rover's arm/mast components report a CoM 1 m
                    // ahead of the heat shield (model origin off the node), yet sit wholly inside the shell.
                    bool rides = p.physicalSignificance == Part.PhysicalSignificance.NONE && p.parent != null && wanted.Contains(p.parent);
                    Vector3 local = part.transform.InverseTransformPoint(p.transform.TransformPoint(p.CoMOffset));
                    if (!rides && (local.y < minPayloadY || local.y > maxPayloadY ||
                        local.x * local.x + local.z * local.z > maxPayloadRadius * maxPayloadRadius)) continue;
                    wanted.Add(p);
                    if (p.parent != null) todo.Push(p.parent);
                    foreach (Part child in p.children) todo.Push(child);
                }
            }
            bool changed = !protectedParts.SetEquals(wanted);
            foreach (Part p in protectedParts)
                if (p != null && !wanted.Contains(p)) p.RemoveShield(this);
            foreach (Part p in wanted)
                if (!protectedParts.Contains(p)) p.AddShield(this);
            // The PAW text only changes with membership count. Keep the
            // per-physics-tick geometry check without allocating new sets/text.
            if (protectedParts.Count != wanted.Count)
                status = wanted.Count > 0 ? KSP.Localization.Localizer.Format("#MSL_Aeroshell_UI_closed_1_payload_parts_shielded", wanted.Count.ToString()) : MSL.Localization.Text.Get("#MSL_Aeroshell_UI_open");
            protectedParts.Clear();
            protectedParts.UnionWith(wanted);
            wanted.Clear();
            visited.Clear();
            if (changed) CollectCandidates();
        }

        // forceRenderingOff, not Renderer.enabled (other modules own enabled). Renderers come from the live transform tree,
        // because the rover rig rebuilds component models after the shell closes. Each renderer is judged once.
        void CollectCandidates()
        {
            Show();
            candidates.Clear();
            candidateMeshes.Clear();
            poses.Clear();
            nextPoseCheck = 0f;
            Scan();
            known.Clear();
            known.UnionWith(scan);
            if (!hideEnclosedPayload && !enclosedNoFlame) return;
            foreach (MeshRenderer mr in scan)
            {
                if (mr.forceRenderingOff) continue;
                MeshFilter f = mr.GetComponent<MeshFilter>();
                if (f == null || f.sharedMesh == null) continue;
                candidates.Add(mr);
                candidateMeshes.Add(f.sharedMesh);
            }
            scan.Clear();
        }

        void Scan()
        {
            scan.Clear();
            foreach (Part p in protectedParts)
            {
                if (p == null) continue;
                foreach (MeshRenderer mr in p.transform.GetComponentsInChildren<MeshRenderer>(true))
                    if (mr.GetComponentInParent<Part>() == p) scan.Add(mr);
            }
        }

        // Every 2 s while payload is shielded: a renderer destroyed or added since the last collection (model rebuilt).
        bool RenderersChanged()
        {
            if (protectedParts.Count == 0 || Time.unscaledTime < nextScan) return false;
            nextScan = Time.unscaledTime + 2f;
            Scan();
            bool changed = scan.Count != known.Count;
            if (!changed) foreach (MeshRenderer mr in scan) if (!known.Contains(mr)) { changed = true; break; }
            scan.Clear();
            return changed;
        }

        // Twice a second: re-judge only the meshes that moved against the shell.
        void CheckPoses()
        {
            if (candidates.Count == 0 || Time.unscaledTime < nextPoseCheck) return;
            nextPoseCheck = Time.unscaledTime + 0.5f;
            var clock = System.Diagnostics.Stopwatch.StartNew();
            Matrix4x4 toPart = part.transform.worldToLocalMatrix;
            int before = hidden.Count, moved = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                MeshRenderer mr = candidates[i];
                if (mr == null) continue;
                Matrix4x4 m = toPart * mr.transform.localToWorldMatrix, old;
                if (poses.TryGetValue(mr, out old) && Same(old, m)) continue;
                poses[mr] = m;
                moved++;
                bool hide = (allPayloadNoFlame && !hideEnclosedPayload) || Enclosed(m, candidateMeshes[i]);
                if (hide == hidden.Contains(mr)) continue;
                if (hideEnclosedPayload) mr.forceRenderingOff = hide;
                if (hide) { hidden.Add(mr); HideLayer(mr); } else { hidden.Remove(mr); RestoreLayer(mr); }
                fireflyDirty = true;
            }
            if (moved == 0) return;
            vertices.Clear();
            vertices.TrimExcess();
            if (hidden.Count == before && poses.Count > moved) return;
            long tris = 0;
            for (int i = 0; i < candidates.Count; i++)
                if (candidates[i] != null && hidden.Contains(candidates[i]))
                    for (int k = 0; k < candidateMeshes[i].subMeshCount; k++) tris += candidateMeshes[i].GetIndexCount(k) / 3;
            Debug.Log("[MSLAeroshell] closed shell: " + hidden.Count + " of " + candidates.Count + " payload meshes (" + tris +
                " tris) " + (allPayloadNoFlame && !hideEnclosedPayload ? "in the closed shell" : "enclosed") + " and " + (hideEnclosedPayload ? "not drawn" : "off the FX layer") + "; " + moved + " re-judged in " + clock.ElapsedMilliseconds + " ms");
        }

        static bool Same(Matrix4x4 a, Matrix4x4 b)
        {
            for (int c = 0; c < 3; c++)
                for (int r = 0; r < 3; r++)
                    if (Mathf.Abs(a[r, c] - b[r, c]) > 0.01f) return false;
            return Mathf.Abs(a.m03 - b.m03) < 0.02f && Mathf.Abs(a.m13 - b.m13) < 0.02f && Mathf.Abs(a.m23 - b.m23) < 0.02f;
        }

        void Show()
        {
            foreach (MeshRenderer r in hidden) if (r != null) { if (hideEnclosedPayload) r.forceRenderingOff = false; RestoreLayer(r); }
            if (hidden.Count > 0) fireflyDirty = true;
            hidden.Clear();
            hiddenLayer.Clear();
        }

        void HideLayer(MeshRenderer mr)
        {
            // a collider on the same object would change its physics/raycast layer
            if (mr.gameObject.layer == noFlameLayer || hiddenLayer.ContainsKey(mr) || mr.GetComponent<Collider>() != null) return;
            hiddenLayer[mr] = mr.gameObject.layer;
            mr.gameObject.layer = noFlameLayer;
        }

        void RestoreLayer(MeshRenderer mr)
        {
            int old;
            if (!hiddenLayer.TryGetValue(mr, out old)) return;
            hiddenLayer.Remove(mr);
            if (mr != null && mr.gameObject.layer == noFlameLayer) mr.gameObject.layer = old;
        }

        // Firefly is optional: found by name. Only a loaded effect is rebuilt; an unloaded one builds itself from the
        // current layers when the vessel enters the atmosphere.
        void FireflyRebuild()
        {
            nextFirefly = Time.unscaledTime + 1f;
            fireflyDirty = false;
            if (vessel == null || vessel.vesselModules == null) return;
            foreach (VesselModule vm in vessel.vesselModules)
            {
                if (vm == null || vm.GetType().Name != "AtmoFxModule") continue;
                try
                {
                    System.Type t = vm.GetType();
                    const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                    object loaded = null;
                    var f = t.GetField("isLoaded", all);
                    if (f != null) loaded = f.GetValue(vm);
                    else { var pr = t.GetProperty("isLoaded", all); if (pr != null) loaded = pr.GetValue(vm, null); }
                    if (loaded is bool && !(bool)loaded) return;
                    // CreateVesselFx returns at once when the effect is already loaded; OnVesselPartCountChanged
                    // drops the envelopes and rebuilds them on the next frame
                    var m = t.GetMethod("OnVesselPartCountChanged", all, null, System.Type.EmptyTypes, null) ??
                        t.GetMethod("CreateVesselFx", all, null, System.Type.EmptyTypes, null);
                    if (m == null) return;
                    m.Invoke(vm, null);
                    Debug.Log("[MSLAeroshell] Firefly envelopes rebuilt (" + hidden.Count + " hidden payload meshes, layer " + noFlameLayer + " skipped)");
                }
                catch (System.Exception e) { Debug.LogWarning("[MSLAeroshell] Firefly rebuild failed: " + e.Message); }
                return;
            }
        }

        // Allowed radius at height y (negative when y is past the forebody cone tip or behind the BIP).
        float Wall(float y)
        {
            if (y >= 0f) return Mathf.Min(sealRadius, sealRadius - (y - forebodyInset * sealRadius) * Mathf.Tan(forebodyCone * Mathf.Deg2Rad));
            if (y >= hipY) return Mathf.Lerp(sealRadius, hipRadius, y / hipY) - wallMargin;
            if (y >= bipY) return Mathf.Lerp(hipRadius, bipRadius, (y - hipY) / (bipY - hipY)) - wallMargin;
            return -1f;
        }

        // Bounds corners first (cheap, conservative); a mesh whose box pokes out is
        // tested vertex by vertex, once per shell closure.
        bool Enclosed(Matrix4x4 m, Mesh mesh)
        {
            Bounds b = mesh.bounds;
            Vector3 c = b.center, e = b.extents;
            for (int i = 0; i < 8; i++)
                corners[i] = c + new Vector3((i & 1) != 0 ? e.x : -e.x, (i & 2) != 0 ? e.y : -e.y, (i & 4) != 0 ? e.z : -e.z);
            bool inside = true;
            for (int i = 0; i < 8 && inside; i++) inside = Inside(m.MultiplyPoint3x4(corners[i]));
            if (inside) return true;
            if (!mesh.isReadable) return false;
            mesh.GetVertices(vertices);
            for (int i = 0; i < vertices.Count; i++)
                if (!Inside(m.MultiplyPoint3x4(vertices[i]))) return false;
            return true;
        }

        bool Inside(Vector3 p)
        {
            float wall;
            if (p.y >= 0f) wall = Mathf.Min(sealRadius, sealRadius - (p.y - forebodyInset * sealRadius) * Mathf.Tan(forebodyCone * Mathf.Deg2Rad));
            else if (p.y >= hipY) wall = Mathf.Lerp(sealRadius, hipRadius, p.y / hipY) - wallMargin;
            else if (p.y >= bipY) wall = Mathf.Lerp(hipRadius, bipRadius, (p.y - hipY) / (bipY - hipY)) - wallMargin;
            else return false;
            return wall > 0f && p.x * p.x + p.z * p.z <= wall * wall;
        }

        void OnDestroy()
        {
            GameEvents.onVesselWasModified.Remove(OnModified);
            GameEvents.onVesselGoOffRails.Remove(OnModified);
            foreach (Part p in protectedParts) if (p != null) p.RemoveShield(this);
            protectedParts.Clear();
            DropProxy(noFlameMeshes.Split(','));
            Show();
            wanted.Clear();
            visited.Clear();
            todo.Clear();
            foreach (Mesh m in splitMeshes) if (m != null) Destroy(m);
            splitMeshes.Clear();
        }
    }
}
