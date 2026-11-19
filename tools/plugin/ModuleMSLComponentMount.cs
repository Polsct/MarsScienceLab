// Editor assembly of the MSL component parts (MLEs, tanks, REA clusters, balance masses).
// ModuleMSLComponentMount  Puts the part in its mount pose on attach (KSP aligns only the node axis, not the roll).
//                          A balance mass surface-attached near a free pocket is moved into it; with
//                          decouplerFollowsAttachment the decoupler fires on the node actually used.
// MSLMountNodeIcons        Node icons of taken mounts are hidden. A mount node takes only its own component; another
//                          part stacked on it is moved to the stage node next to it (KeepOffMount).
// MSLEditorPickThrough     Components inside the parent's convex collider cannot be clicked; while nothing is held,
//                          the parent colliders around the pointer are switched off so KSP's pick finds them.
// MSLFlightPickThrough     The same in flight without touching colliders: the hovered part moves from the parent
//                          shell to the component inside it, so it can be highlighted and right-clicked.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;

namespace MSLAeroshell
{
    public class ModuleMSLComponentMount : PartModule
    {
        // part name of the stage the mounts belong to, e.g. MSL_SkyCrane
        [KSPField] public string parentPart = "";
        // "node:x y z w" pairs, the part rotation in the parent part frame; written by the part generators from the
        // poses the delivered crafts are built with
        [KSPField] public string mounts = "";
        // a surface attachment closer than this (m) to a free mount node is moved onto the nearest one; 0 = stack nodes
        // only. The balance masses use the whole backshell, so wherever they are dropped they go into a pocket.
        [KSPField] public float snapRadius = 0f;
        [KSPField] public bool decouplerFollowsAttachment = false;

        Dictionary<string, Quaternion> table;
        static readonly FieldInfo explosiveNode = typeof(ModuleDecouplerBase).GetField("explosiveNode", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        Dictionary<string, Quaternion> Table
        {
            get
            {
                if (table != null) return table;
                table = new Dictionary<string, Quaternion>();
                foreach (string pair in mounts.Split(','))
                {
                    string[] kv = pair.Split(':');
                    if (kv.Length != 2) continue;
                    string[] q = kv[1].Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (q.Length != 4) continue;
                    float[] v = new float[4];
                    for (int i = 0; i < 4; i++) v[i] = float.Parse(q[i], CultureInfo.InvariantCulture);
                    table[kv[0].Trim()] = new Quaternion(v[0], v[1], v[2], v[3]);
                }
                return table;
            }
        }

        // Runs before the decoupler's OnStart (this module is listed first), which resolves explosiveNodeID once.
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            FollowAttachment();
            if (!HighLogic.LoadedSceneIsEditor) return;
            part.OnEditorAttach += Attached;
            GameEvents.onEditorPartEvent.Add(PartEvent);
            // a craft saved with the part off its mount (older crafts, gizmo offsets) is put back once it has loaded
            StartCoroutine(SnapLoaded());
        }

        void OnDestroy()
        {
            if (part != null) part.OnEditorAttach -= Attached;
            GameEvents.onEditorPartEvent.Remove(PartEvent);
        }

        System.Collections.IEnumerator SnapLoaded()
        {
            yield return null;
            Snap();
            FollowAttachment();
        }

        // the offset and rotate gizmos would move the part out of its mount: put it back, so the pose stays the measured one
        void PartEvent(ConstructionEventType type, Part p)
        {
            if (p != part || EditorLogic.SelectedPart == part) return;
            if (type == ConstructionEventType.PartOffsetting || type == ConstructionEventType.PartOffset ||
                type == ConstructionEventType.PartRotating || type == ConstructionEventType.PartRotated)
            {
                Snap();
                SnapCounterparts();
            }
        }

        void Attached()
        {
            Snap();
            FollowAttachment();
            SnapCounterparts();
        }

        void SnapCounterparts()
        {
            foreach (Part p in part.symmetryCounterparts)
            {
                ModuleMSLComponentMount m = p.FindModuleImplementing<ModuleMSLComponentMount>();
                if (m != null) { m.Snap(); m.FollowAttachment(); }
            }
        }

        bool OnStage(Part p) { return p != null && p.partInfo != null && p.partInfo.name == parentPart.Replace('_', '.'); }

        bool SurfaceAttached(Part p) { return part.srfAttachNode != null && part.srfAttachNode.attachedPart == p; }

        public void Snap()
        {
            Part p = part.parent;
            if (!OnStage(p)) return;
            AttachNode node = p.FindAttachNodeByPart(part);
            Quaternion rel;
            if (node == null || !Table.TryGetValue(node.id, out rel))
            {
                AttachNode mount = node != null && !SurfaceAttached(p) ? FreeMountAt(p, node) : null;
                if (mount != null)
                {
                    // dropped on a stage node next to a free mount node: move the link over to the mount node, so the pose and the
                    // saved attN are the mount's
                    node.attachedPart = null;
                    node.attachedPartId = 0;
                    mount.attachedPart = part;
                    mount.attachedPartId = part.flightID;
                    node = mount;
                }
                else if (snapRadius <= 0 || !SurfaceAttached(p) || (node = FreeMountNear(p)) == null) return;
                rel = Table[node.id];
            }
            AttachNode own = SurfaceAttached(p) ? part.srfAttachNode : part.FindAttachNodeByPart(p);
            Vector3 ownPos = own != null ? own.position : Vector3.zero;
            Quaternion rot = p.transform.rotation * rel;
            part.transform.rotation = rot;
            part.transform.position = p.transform.TransformPoint(node.position) - rot * ownPos;
            // as the delivered crafts store them: attPos0/attRot0 = pose in the parent frame, no gizmo offset
            part.attPos = Vector3.zero;
            part.attRotation = Quaternion.identity;
            part.attPos0 = p.transform.InverseTransformPoint(part.transform.position);
            part.attRotation0 = rel;
        }

        // a stack node this close (m, parent part frame) to a free mount node of this part counts as that mount node
        const float StackRedirect = 0.05f;

        // nearest free mount node of this part within StackRedirect of the stage node it was stacked on
        AttachNode FreeMountAt(Part p, AttachNode on)
        {
            AttachNode best = null;
            float bestDist = StackRedirect * p.rescaleFactor;
            foreach (string id in Table.Keys)
            {
                AttachNode n = p.FindAttachNode(id);
                if (n == null || n == on || (n.attachedPart != null && n.attachedPart != part)) continue;
                float d = (n.position - on.position).magnitude;
                if (d < bestDist) { best = n; bestDist = d; }
            }
            return best;
        }

        // nearest mount node within snapRadius that carries no part and has no other surface-attached part on it
        AttachNode FreeMountNear(Part p)
        {
            AttachNode best = null;
            float bestDist = snapRadius;
            foreach (string id in Table.Keys)
            {
                AttachNode n = p.FindAttachNode(id);
                if (n == null || (n.attachedPart != null && n.attachedPart != part)) continue;
                Vector3 at = p.transform.TransformPoint(n.position);
                bool taken = false;
                foreach (Part c in p.children)
                    if (c != part && (c.transform.position - at).sqrMagnitude < 1e-4f) { taken = true; break; }
                float d = (part.transform.position - at).magnitude;
                if (!taken && d < bestDist) { best = n; bestDist = d; }
            }
            return best;
        }

        public void FollowAttachment()
        {
            if (!decouplerFollowsAttachment || part.parent == null) return;
            ModuleDecouplerBase d = part.FindModuleImplementing<ModuleDecouplerBase>();
            if (d == null) return;
            AttachNode n = SurfaceAttached(part.parent) ? part.srfAttachNode : part.FindAttachNodeByPart(part.parent);
            if (n == null) return;
            d.explosiveNodeID = n == part.srfAttachNode ? "srf" : n.id;
            if (explosiveNode != null) explosiveNode.SetValue(d, n);
        }
    }

    // Mount nodes mostly sit inside the stage, where KSP's depth-tested node icons are hidden. A copy of the icon mesh is
    // drawn on top only where the icon is behind geometry (ZTest Greater), in KSP's icon colour at reduced alpha.
    // A node with a part on it shows no icon: on MSL parts every icon within NodeShare of an attachment point is shrunk
    // to nothing, since components carry co-located top/bottom pairs and stages may have a second node next to a mount.
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class MSLMountNodeIcons : MonoBehaviour
    {
        const float HiddenAlpha = 0.45f;
        // m: a free node this close to an attachment point counts as taken
        const float NodeShare = 0.03f;
        // part name (KSP form, '.' for '_') -> mount node ids on that stage; null = every node (a component part)
        static Dictionary<string, HashSet<string>> nodes;
        // "stage part name/node id" -> names of the component parts that mount on that node
        static Dictionary<string, HashSet<string>> owners;
        // m: a mount node's neighbour this close, on the same axis direction, takes a part that is not the mount's own
        const float StackRedirect = 0.05f;
        Shader shader;
        class Rec { public Part part; public AttachNode node; public Renderer icon, overlay; public Vector3 scale; public bool shrunk; }
        readonly Dictionary<GameObject, Rec> recs = new Dictionary<GameObject, Rec>();
        readonly List<GameObject> dead = new List<GameObject>();
        readonly List<Vector3> taken = new List<Vector3>();

        void Start()
        {
            shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null) Debug.LogWarning("[MSL] node icons: Hidden/Internal-Colored not found, no overlays");
            if (nodes == null) nodes = Collect();
            GameEvents.onEditorPartEvent.Add(PartEvent);
        }

        void PartEvent(ConstructionEventType type, Part p)
        {
            if (type != ConstructionEventType.PartAttached || p == null) return;
            if (KeepOffMount(p)) Debug.Log("[MSL] " + p.partInfo.name + " moved off a component mount node to the stage node next to it");
        }

        // A mount node is for its component only. The link of any other part on it is moved to the nearest free stage node
        // within StackRedirect facing the same way, and the part shifted by the node distance. c = the part just attached.
        public static bool KeepOffMount(Part c)
        {
            Part p = c != null ? c.parent : null;
            if (p == null || p.partInfo == null || c.partInfo == null) return false;
            if (nodes == null) nodes = Collect();
            return Redirect(p, c, c, 1f) || Redirect(c, p, c, -1f);
        }

        // stage: the part with the mount nodes; other: the part on one of them; moved: the part to shift (the child)
        static bool Redirect(Part stage, Part other, Part moved, float sign)
        {
            HashSet<string> ids, own;
            if (!nodes.TryGetValue(stage.partInfo.name, out ids) || ids == null) return false;
            AttachNode on = stage.FindAttachNodeByPart(other);
            if (on == null || !ids.Contains(on.id)) return false;
            if (owners.TryGetValue(stage.partInfo.name + "/" + on.id, out own) && own.Contains(other.partInfo.name)) return false;
            AttachNode best = null;
            float bestDist = StackRedirect * stage.rescaleFactor;
            foreach (AttachNode n in stage.attachNodes)
            {
                if (n == on || ids.Contains(n.id) || n.attachedPart != null) continue;
                if (Vector3.Dot(n.orientation.normalized, on.orientation.normalized) < 0.99f) continue;
                float d = (n.position - on.position).magnitude;
                if (d < bestDist) { best = n; bestDist = d; }
            }
            if (best == null) return false;
            on.attachedPart = null;
            on.attachedPartId = 0;
            best.attachedPart = other;
            best.attachedPartId = other.flightID;
            // the stage node 'best' goes where 'on' was against the other part
            Vector3 shift = stage.transform.TransformPoint(on.position) - stage.transform.TransformPoint(best.position);
            moved.transform.position += shift * -sign;
            moved.attPos0 = moved.parent.transform.InverseTransformPoint(moved.transform.position);
            return true;
        }

        static Dictionary<string, HashSet<string>> Collect()
        {
            var d = new Dictionary<string, HashSet<string>>();
            owners = new Dictionary<string, HashSet<string>>();
            if (PartLoader.LoadedPartsList == null) return d;
            foreach (AvailablePart ap in PartLoader.LoadedPartsList)
            {
                if (ap == null || ap.partConfig == null) continue;
                foreach (ConfigNode m in ap.partConfig.GetNodes("MODULE"))
                {
                    string mod = m.GetValue("name");
                    // the rover and its parts: every node (wheels, arm, mast, antennas, MMRTG)
                    if (mod == "ModuleCuriosityComponent" || mod == "ModuleCuriosityRig") { d[ap.name] = null; continue; }
                    if (mod != "ModuleMSLComponentMount") continue;
                    d[ap.name] = null;
                    string stage = (m.GetValue("parentPart") ?? "").Replace('_', '.');
                    if (stage == "") continue;
                    HashSet<string> ids;
                    if (!d.TryGetValue(stage, out ids)) d[stage] = ids = new HashSet<string>();
                    if (ids == null) continue;
                    foreach (string pair in (m.GetValue("mounts") ?? "").Split(','))
                    {
                        string id = pair.Split(':')[0].Trim();
                        if (id == "") continue;
                        ids.Add(id);
                        HashSet<string> own;
                        if (!owners.TryGetValue(stage + "/" + id, out own)) owners[stage + "/" + id] = own = new HashSet<string>();
                        own.Add(ap.name);
                    }
                }
            }
            return d;
        }

        void LateUpdate()
        {
            EditorLogic e = EditorLogic.fetch;
            if (e == null) return;
            if (e.ship != null) foreach (Part p in e.ship.parts) Fix(p);
            if (EditorLogic.SelectedPart != null) FixTree(EditorLogic.SelectedPart);
            Sync();
        }

        void FixTree(Part p)
        {
            Fix(p);
            foreach (Part c in p.children) FixTree(c);
        }

        void Fix(Part p)
        {
            HashSet<string> ids;
            if (p == null || p.partInfo == null || !nodes.TryGetValue(p.partInfo.name, out ids)) return;
            foreach (AttachNode n in p.attachNodes)
            {
                if (n.icon == null || recs.ContainsKey(n.icon)) continue;
                Renderer r = n.icon.GetComponent<Renderer>();
                var rec = new Rec { part = p, node = n, icon = r, scale = n.icon.transform.localScale };
                recs[n.icon] = rec;
                MeshFilter f = n.icon.GetComponent<MeshFilter>();
                if (shader == null || (ids != null && !ids.Contains(n.id)) || r == null || f == null || f.sharedMesh == null) continue;
                var go = new GameObject("MSL_NodeIconHidden");
                go.layer = n.icon.layer;
                go.transform.SetParent(n.icon.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = f.sharedMesh;
                MeshRenderer o = go.AddComponent<MeshRenderer>();
                o.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                o.receiveShadows = false;
                Material m = new Material(shader);
                m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                m.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Back);
                m.SetInt("_ZWrite", 0);
                m.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Greater);
                m.renderQueue = 3100;
                o.sharedMaterial = m;
                rec.overlay = o;
            }
        }

        static Vector3 World(Part p, AttachNode n) { return p.transform.TransformPoint(n.position); }

        // attachment points on p: its attached nodes, its surface node when attached, where the parts attached to it sit
        void Taken(Part p)
        {
            taken.Clear();
            foreach (AttachNode m in p.attachNodes)
                if (m.attachedPart != null) taken.Add(World(p, m));
            if (p.srfAttachNode != null && p.srfAttachNode.attachedPart != null) taken.Add(World(p, p.srfAttachNode));
            foreach (Part q in p.children) Other(q, p);
            if (p.parent != null) Other(p.parent, p);
        }

        void Other(Part q, Part p)
        {
            if (q == null) return;
            foreach (AttachNode m in q.attachNodes)
                if (m.attachedPart == p) taken.Add(World(q, m));
            if (q.srfAttachNode != null && q.srfAttachNode.attachedPart == p) taken.Add(World(q, q.srfAttachNode));
        }

        bool Occupied(Rec r)
        {
            if (r.node.attachedPart != null) return true;
            Vector3 at = World(r.part, r.node);
            float d2 = NodeShare * NodeShare;
            foreach (Vector3 t in taken)
            {
                float s = (t - at).sqrMagnitude;
                if (s < d2 && Nearest(r, t, Mathf.Sqrt(s))) return true;
            }
            return false;
        }

        // an attachment point only takes the part's node(s) closest to it (ties within 1 mm all count): the parachute on
        // the backshell 'mortar' must not hide 'bottom' 1.2 cm away, or the cruise stage has no node to snap to
        static bool Nearest(Rec r, Vector3 t, float dist)
        {
            foreach (AttachNode m in r.part.attachNodes)
                if (m != r.node && (t - World(r.part, m)).magnitude < dist - 0.001f) return false;
            return true;
        }

        // follow KSP's icon colour (node type, hover highlight) and visibility every frame; shrink taken nodes' icons
        void Sync()
        {
            Part last = null;
            foreach (var kv in recs)
            {
                Rec rec = kv.Value;
                if (kv.Key == null || rec.part == null) { dead.Add(kv.Key); continue; }
                if (rec.part != last) { Taken(rec.part); last = rec.part; }
                Transform t = kv.Key.transform;
                bool hide = Occupied(rec);
                if (hide && !rec.shrunk) { rec.scale = t.localScale; t.localScale = Vector3.zero; rec.shrunk = true; }
                else if (!hide && rec.shrunk) { t.localScale = rec.scale; rec.shrunk = false; }
                else if (!hide) rec.scale = t.localScale;
                Renderer r = rec.icon, o = rec.overlay;
                if (o == null) continue;
                o.enabled = r != null && r.enabled && !hide;
                Material src = r != null ? r.sharedMaterial : null;
                Color c = src != null && src.HasProperty("_Color") ? src.color
                    : src != null && src.HasProperty("_TintColor") ? src.GetColor("_TintColor") : Color.green;
                c.a *= HiddenAlpha;
                o.sharedMaterial.color = c;
            }
            foreach (GameObject g in dead)
            {
                Rec rec;
                if (g != null && recs.TryGetValue(g, out rec) && rec.overlay != null) { Destroy(rec.overlay.sharedMaterial); Destroy(rec.overlay.gameObject); }
                recs.Remove(g);
            }
            dead.Clear();
        }

        void OnDestroy()
        {
            GameEvents.onEditorPartEvent.Remove(PartEvent);
            foreach (var kv in recs)
            {
                if (kv.Key != null && kv.Value.shrunk) kv.Key.transform.localScale = kv.Value.scale;
                if (kv.Value.overlay != null) { Destroy(kv.Value.overlay.sharedMaterial); Destroy(kv.Value.overlay.gameObject); }
            }
            recs.Clear();
        }
    }

    // Shared by the editor and flight pick-through: which physicsless MSL component the pointer ray reaches through
    // its parent's own shell (the Sky Crane tanks sit inside the hollow core hulls, the MLEs in the pod hulls).
    static class MSLPickThrough
    {
        // parent shell this far (m) in front of a component still lets the pick through; the Sky Crane tanks sit
        // about 0.3-0.6 m behind the core deck and side shells, so the stage itself stays pickable from further off
        internal const float Reach = 0.9f;

        internal static bool Component(Part p)
        {
            return p != null && p.parent != null && p.partInfo != null && p.partInfo.name.StartsWith("MSL.") &&
                   (p.PhysicsSignificance == 1 || p.FindModuleImplementing<ModuleMSLComponentMount>() != null);
        }

        static bool InMask(Collider c, int mask) { return (mask & (1 << c.gameObject.layer)) != 0; }

        // triggers count: KSP's pick (Physics.Raycast, triggers hit by default) lands on them too, and the component
        // colliders are triggers in the editor
        static bool Owns(Part p, Collider c, int mask) { return c.enabled && InMask(c, mask) && c.GetComponentInParent<Part>() == p; }

        static bool Convex(Collider c)
        {
            MeshCollider m = c as MeshCollider;
            return m == null ? c is BoxCollider || c is SphereCollider || c is CapsuleCollider : m.convex;
        }

        // The component nearest along the ray whose parent has colliders around it (holding the hit point, or crossed
        // within Reach in front of it), with those colliders in enclosing; null when no component is on the ray,
        // nothing of the parent covers it, or another part stands in front. switchedOff: parent colliders the caller
        // has turned off that still count as they were.
        internal static Part Find(IEnumerable<Part> parts, Ray ray, int mask, QueryTriggerInteraction triggers,
                                  ICollection<Collider> switchedOff, List<Collider> enclosing, out string why)
        {
            enclosing.Clear();
            Part hitPart = null;
            // a finite length: Collider.Raycast with float.MaxValue never hits (origin + dir * max overflows), so the
            // pick-through would find no component at all
            float hitDist = 1000f;
            Vector3 hitPoint = Vector3.zero;
            RaycastHit h;
            int seen = 0;
            foreach (Part p in parts)
            {
                if (!Component(p)) continue;
                seen++;
                foreach (Collider c in p.GetComponentsInChildren<Collider>())
                    if (Owns(p, c, mask) && c.Raycast(ray, out h, hitDist)) { hitPart = p; hitDist = h.distance; hitPoint = h.point; }
            }
            why = "no component on ray (" + seen + " components, ray " + ray.origin.ToString("F2") + ")";
            if (hitPart == null) return null;
            foreach (Collider c in hitPart.parent.GetComponentsInChildren<Collider>(true))
            {
                bool off = switchedOff != null && switchedOff.Contains(c);
                if (!(c.enabled || off) || !InMask(c, mask) || c.GetComponentInParent<Part>() != hitPart.parent) continue;
                bool was = c.enabled;
                c.enabled = true;
                if (Convex(c) && (c.ClosestPoint(hitPoint) - hitPoint).sqrMagnitude < 1e-8f) enclosing.Add(c);
                else if (c.Raycast(ray, out h, hitDist) && h.distance > hitDist - Reach) enclosing.Add(c);
                c.enabled = was;
            }
            why = "hit " + hitPart.partInfo.name + " at " + hitDist.ToString("F2") + ", no parent shell";
            if (enclosing.Count == 0) return null;
            // only when nothing else stands in front: another part seen first keeps the normal pick
            foreach (RaycastHit r in Physics.RaycastAll(ray, hitDist, mask, triggers))
            {
                Part owner = r.collider.GetComponentInParent<Part>();
                if (owner != null && owner != hitPart && !enclosing.Contains(r.collider))
                {
                    why = "hit " + hitPart.partInfo.name + ", in front " + r.collider.name + "@" + owner.partInfo.name + " at " + r.distance.ToString("F2");
                    enclosing.Clear();
                    return null;
                }
            }
            why = "hit " + hitPart.partInfo.name + ", through " + enclosing.Count;
            return hitPart;
        }
    }

    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class MSLEditorPickThrough : MonoBehaviour
    {
        // when set, a pointer ray used instead of the mouse
        public static Ray? TestRay;
        // why the last Find did or did not switch colliders off (diagnostic)
        public static string Why = "";
        readonly HashSet<Collider> off = new HashSet<Collider>();
        readonly List<Collider> want = new List<Collider>();
        readonly List<Collider> enclosing = new List<Collider>();

        void Update()
        {
            want.Clear();
            Find();
            foreach (Collider c in new List<Collider>(off))
                if (!want.Contains(c)) { if (c != null) c.enabled = true; off.Remove(c); }
            foreach (Collider c in want)
                if (off.Add(c)) c.enabled = false;
        }

        void Find()
        {
            EditorLogic e = EditorLogic.fetch;
            Why = "no editor/held";
            if (e == null || e.ship == null || e.editorCamera == null || EditorLogic.SelectedPart != null) return;
            if (TestRay == null && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            Ray ray = TestRay ?? e.editorCamera.ScreenPointToRay(Input.mousePosition);
            if (MSLPickThrough.Find(e.ship.parts, ray, ~0, QueryTriggerInteraction.Collide, off, enclosing, out Why) != null)
                want.AddRange(enclosing);
        }

        void OnDestroy()
        {
            foreach (Collider c in off) if (c != null) c.enabled = true;
            off.Clear();
        }
    }

    // In flight the parent's colliders stay on (switching hulls of a live rigid body off changes its inertia and
    // contacts); the part under the pointer is retargeted instead. KSP's Mouse.Update sets Mouse.HoveredPart to the
    // nearest hit, which is the parent shell around a component; once that has run this frame the hovered part is
    // set to the component, so the hover highlight and the right-click part window (both read later in the frame)
    // get the component. Mouse.Update has no known slot among the timing stages, so the check runs in each of them;
    // the ray test itself runs once per frame and parent.
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class MSLFlightPickThrough : MonoBehaviour
    {
        static readonly TimingManager.TimingStage[] Stages =
        {
            TimingManager.TimingStage.Early, TimingManager.TimingStage.Earlyish, TimingManager.TimingStage.Normal,
            TimingManager.TimingStage.FashionablyLate, TimingManager.TimingStage.Late, TimingManager.TimingStage.BetterLateThanNever,
        };
        // when set, a pointer ray used instead of the mouse
        public static Ray? TestRay;
        // why the last ray test did or did not retarget the pick (diagnostic)
        public static string Why = "";
        readonly List<Collider> enclosing = new List<Collider>();
        readonly List<Part> components = new List<Part>();
        int frame = -1;
        Part testedParent, pick;

        void Start() { foreach (TimingManager.TimingStage s in Stages) TimingManager.UpdateAdd(s, Retarget); }
        void OnDestroy() { foreach (TimingManager.TimingStage s in Stages) TimingManager.UpdateRemove(s, Retarget); }

        void Retarget()
        {
            Part hovered = Mouse.HoveredPart;
            if (hovered == null) return;
            // before Mouse.Update this is last frame's (possibly retargeted) part: test its parent, which is what
            // Mouse.Update reports again while the pointer stays on the shell
            Part parent = MSLPickThrough.Component(hovered) ? hovered.parent : hovered;
            if (frame != Time.frameCount || testedParent != parent)
            {
                frame = Time.frameCount;
                testedParent = parent;
                pick = Test(parent);
            }
            if (pick != null && hovered == pick.parent) Mouse.HoveredPart = pick;
        }

        Part Test(Part parent)
        {
            Why = "no components under " + parent.partInfo.name;
            components.Clear();
            foreach (Part c in parent.children) if (MSLPickThrough.Component(c)) components.Add(c);
            if (components.Count == 0) return null;
            Ray ray;
            if (TestRay != null) ray = TestRay.Value;
            else
            {
                Why = "no camera";
                Camera cam = CameraManager.GetCurrentCamera();
                CameraManager m = CameraManager.Instance;
                if (cam == null || !cam.enabled || m == null || m.currentCameraMode == CameraManager.CameraMode.IVA ||
                    m.currentCameraMode == CameraManager.CameraMode.Internal) return null;
                ray = cam.ScreenPointToRay(Input.mousePosition);
            }
            return MSLPickThrough.Find(components, ray, Part.layerMask.value, QueryTriggerInteraction.UseGlobal, null, enclosing, out Why);
        }
    }

    // The parachute and the backshell come out of the parts list apex up: KSP sets the held part to vesselRotation *
    // initRotation * attRotation, so a 180 deg attRotation equals pressing the pitch key twice; part frame, nodes and
    // saved crafts are unchanged. A root part is turned the frame after it is added; subassemblies keep their pose.
    [KSPAddon(KSPAddon.Startup.EditorAny, false)]
    public class MSLEditorSpawnFlip : MonoBehaviour
    {
        static readonly HashSet<string> Names = new HashSet<string> { "MSL.Backshell", "MSL.AS.Parachute" };
        static readonly Quaternion Flip = Quaternion.AngleAxis(180f, Vector3.right);

        void Start() { GameEvents.onEditorPartEvent.Add(PartEvent); }
        void OnDestroy() { GameEvents.onEditorPartEvent.Remove(PartEvent); }

        static bool Flipped(Part p) { return p != null && p.partInfo != null && Names.Contains(p.partInfo.name); }

        void PartEvent(ConstructionEventType type, Part p)
        {
            if (type != ConstructionEventType.PartCreated || !Flipped(p) || p.children.Count > 0) return;
            EditorLogic e = EditorLogic.fetch;
            if (e == null) return;
            if (p == EditorLogic.RootPart && (e.ship == null || e.ship.parts.Count == 0)) { StartCoroutine(FlipRoot(p)); return; }
            // PartCreated can fire twice for one spawn; turn it once
            if (EditorLogic.SelectedPart != p || p.attRotation != Quaternion.identity) return;
            p.attRotation = Flip;
            p.transform.rotation = e.vesselRotation * p.initRotation * p.attRotation;
        }

        System.Collections.IEnumerator FlipRoot(Part p)
        {
            yield return null;
            EditorLogic e = EditorLogic.fetch;
            if (p == null || e == null || EditorLogic.RootPart != p || e.ship == null || e.ship.parts.Count != 1) yield break;
            p.transform.rotation = e.vesselRotation * Flip;
            p.attRotation0 = p.transform.rotation;
            GameEvents.onEditorShipModified.Fire(e.ship);
        }
    }
}
