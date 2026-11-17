// Rover component part: physicsless, attached to a rover node, its transform mounted inside the rover model under
// its rig frame, so ModuleCuriosityRig drives it and clicks still resolve to this part. Put back each frame, since
// KSP may move it (vessel load, pack/unpack, editor).
// A part with one MODEL per variant (the wheel: the geometry and its mirror image) shows the model of the selected
// ModulePartVariants variant; attaching it to a rover wheel node selects that node's variant.
using System;
using System.Collections.Generic;
using UnityEngine;

[assembly: KSPAssembly("MSLCuriosity", 1, 0)]

namespace MSLCuriosity
{
    public class ModuleCuriosityComponent : PartModule
    {
        // Top rig link of the model ("Wheel" for the wheel models: renamed to the rover wheel node it is attached to);
        // empty for body-fixed geometry.
        [KSPField] public string link="";
        // Body-fixed geometry: the part origin in the rover model frame (the model is shifted by it).
        [KSPField] public Vector3 mountPosition=Vector3.zero;
        // "variant:model" pairs in MODEL order, and "node:variant" pairs: the variant a rover node takes on attach.
        [KSPField] public string variantModels="";
        [KSPField] public string nodeVariants="";
        string[] variantNames,variantStems;
        string activeVariant;
        Transform mount;
        Part seen;
        Transform[] owned;
        public bool IsWheel{get{return link=="Wheel";}}
        // The rover link this part provides: the wheel link of the node it sits on (node WheelFL -> Wheel_FL; KSP node
        // ids cannot hold '_', rover_components.node_id), else the link itself.
        public string Instance
        {
            get
            {
                if(!IsWheel)return link;
                if(part.parent==null)return null;
                AttachNode n=part.parent.FindAttachNodeByPart(part);
                if(n!=null && IsWheelNode(n.id))return WheelLink(n.id);
                // not on a wheel node (surface attached in the VAB): the nearest wheel node
                float best=float.MaxValue;string id=null;
                Vector3 p=part.parent.transform.InverseTransformPoint(part.transform.position);
                foreach(AttachNode a in part.parent.attachNodes)
                    if(IsWheelNode(a.id) && (a.attachedPart==null||a.attachedPart==part) && (a.position-p).sqrMagnitude<best)
                    {best=(a.position-p).sqrMagnitude;id=a.id;}
                return id==null?null:WheelLink(id);
            }
        }
        static bool IsWheelNode(string id){return id.Length==7 && id.StartsWith("Wheel");}
        static string WheelLink(string id){return "Wheel_"+id.Substring(5);}
        public Transform Top
        {
            get
            {
                if(link=="")return null;
                Transform root=ActiveModel();
                return root!=null?Find(root,IsWheel?(Named??"Wheel"):link):null;
            }
        }
        static Transform Find(Transform t,string name)
        {
            if(t.name==name)return t;
            foreach(Transform c in t){Transform f=Find(c,name);if(f!=null)return f;}
            return null;
        }
        void ParseVariants()
        {
            if(variantNames!=null||string.IsNullOrEmpty(variantModels))return;
            List<string> names=new List<string>(),stems=new List<string>();
            foreach(string pair in variantModels.Split(','))
            {
                string[] kv=pair.Split(':');
                if(kv.Length==2){names.Add(kv[0].Trim());stems.Add(kv[1].Trim());}
            }
            if(names.Count>1){variantNames=names.ToArray();variantStems=stems.ToArray();}
        }
        // The instantiated MODEL of variant i: the child of "model" named after its model file, else by MODEL order.
        Transform VariantModel(Transform model,int i)
        {
            foreach(Transform c in model)if(c.name.IndexOf(variantStems[i],StringComparison.Ordinal)>=0)return c;
            return model.childCount==variantNames.Length?model.GetChild(i):null;
        }
        // The model holding this part's rig links: the selected variant's MODEL, or the whole model.
        Transform ActiveModel()
        {
            Transform model=part.transform.Find("model");
            if(model==null||variantNames==null)return model;
            int i=Array.IndexOf(variantNames,activeVariant);
            Transform m=i>=0?VariantModel(model,i):null;
            return m??model;
        }
        string VariantFor(string nodeId)
        {
            foreach(string pair in nodeVariants.Split(','))
            {
                string[] kv=pair.Split(':');
                if(kv.Length==2 && kv[0].Trim()==nodeId)return kv[1].Trim();
            }
            return null;
        }
        // Show only the variant's MODEL. A wheel mounted on the rover hands its link name to the new model's "Wheel".
        void ApplyVariant(string v)
        {
            ParseVariants();
            if(variantNames==null||v==null)return;
            int idx=Array.IndexOf(variantNames,v);
            if(idx<0)return;
            Transform model=part.transform.Find("model");
            if(model==null)return;
            bool changed=v!=activeVariant;
            Transform oldTop=changed&&IsWheel&&Named!=null?Top:null;
            activeVariant=v;
            for(int i=0;i<variantNames.Length;i++){Transform m=VariantModel(model,i);if(m!=null)m.gameObject.SetActive(i==idx);}
            if(!changed)return;
            if(oldTop!=null){oldTop.name="Wheel";Named=null;if(mount!=null)Mount(mount);}
            Notify(part.parent);
        }
        void OnVariantApplied(Part p,PartVariant v){if(p==part && v!=null)ApplyVariant(v.Name);}
        // Editor: a wheel attached to a rover wheel node takes that node's variant (the player may switch it later).
        void MatchNode()
        {
            if(variantNames==null||part.parent==null)return;
            string inst=Instance;
            string want=inst!=null?VariantFor(inst.Replace("_","")):null;
            if(want==null||want==activeVariant)return;
            ModulePartVariants mv=part.FindModuleImplementing<ModulePartVariants>();
            if(mv!=null && (mv.SelectedVariant==null||mv.SelectedVariant.Name!=want))mv.SetVariant(want);
            ApplyVariant(want);
            if(EditorLogic.fetch!=null && EditorLogic.fetch.ship!=null)GameEvents.onEditorShipModified.Fire(EditorLogic.fetch.ship);
        }
        // Part prefab (loading): only the base variant's MODEL is shown, so the editor icon draws one wheel.
        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node);
            if(HighLogic.LoadedScene!=GameScenes.LOADING)return;
            ParseVariants();
            if(variantNames!=null){activeVariant=null;ApplyVariant(variantNames[0]);}
        }
        string Named;
        // The prefab's model transforms, in the same order as owned: the pose the part has when it is not on a rover.
        Transform[] rest;
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            ParseVariants();
            if(variantNames!=null)
            {
                ModulePartVariants mv=part.FindModuleImplementing<ModulePartVariants>();
                activeVariant=null;
                ApplyVariant(mv!=null&&mv.SelectedVariant!=null?mv.SelectedVariant.Name:variantNames[0]);
                GameEvents.onVariantApplied.Add(OnVariantApplied);
            }
            Transform model=part.transform.Find("model");
            owned=model!=null?model.GetComponentsInChildren<Transform>(true):new Transform[0];
            Transform pm=part.partInfo!=null&&part.partInfo.partPrefab!=null?part.partInfo.partPrefab.transform.Find("model"):null;
            Transform[] p=pm!=null?pm.GetComponentsInChildren<Transform>(true):null;
            rest=p!=null&&p.Length==owned.Length?p:null;
            if(mount==null)Rest();
            seen=part.parent;Notify(seen);
        }
        // Off the rover: the model goes back to its prefab pose and names (the rig moved and renamed links), and an arm or
        // mast stands at its own attach node in the surface pose, so the next attach node is not offset.
        void Rest()
        {
            mount=null;Named=null;
            if(!HighLogic.LoadedSceneIsEditor)return;
            if(rest!=null)
                for(int i=0;i<owned.Length;i++)
                {
                    Transform t=owned[i],r=rest[i];if(t==null||r==null||!t.IsChildOf(part.transform))continue;
                    t.name=r.name;t.localPosition=r.localPosition;t.localRotation=r.localRotation;t.localScale=r.localScale;
                }
            if(link!="" && !IsWheel)
            {
                Transform top=Top;Vector3 p;Quaternion q;
                if(top!=null && SurfacePose(link,out p,out q)){top.localPosition=p;top.localRotation=q;}
            }
        }
        // The link's surface pose from the rover part's LINK table (rover body frame = this model's frame).
        static readonly Dictionary<string,KeyValuePair<Vector3,Quaternion>> poses=new Dictionary<string,KeyValuePair<Vector3,Quaternion>>();
        static bool SurfacePose(string name,out Vector3 p,out Quaternion q)
        {
            KeyValuePair<Vector3,Quaternion> v;
            if(!poses.TryGetValue(name,out v))
            {
                bool found=false;
                if(PartLoader.LoadedPartsList!=null)
                    foreach(AvailablePart ap in PartLoader.LoadedPartsList)
                    {
                        if(found||ap==null||ap.partConfig==null)continue;
                        foreach(ConfigNode m in ap.partConfig.GetNodes("MODULE"))
                        {
                            if(found||m.GetValue("name")!="ModuleCuriosityRig")continue;
                            foreach(ConfigNode l in m.GetNodes("LINK"))
                                if(l.GetValue("name")==name && l.HasValue("position") && l.HasValue("rotation"))
                                {v=new KeyValuePair<Vector3,Quaternion>(ConfigNode.ParseVector3(l.GetValue("position")),ConfigNode.ParseQuaternion(l.GetValue("rotation")));found=true;break;}
                        }
                    }
                if(!found){p=Vector3.zero;q=Quaternion.identity;return false;}
                poses[name]=v;
            }
            p=v.Key;q=v.Value;return true;
        }
        // Called by the rover rig, never while it runs.
        public void Mount(Transform at)
        {
            mount=at;
            if(IsWheel)
            {
                Transform top=Top;string id=Instance;
                if(top!=null && id!=null){top.name=id;Named=id;}
            }
            Place();
        }
        void Place()
        {
            if(mount==null)return;
            if(part.transform.parent!=mount)part.transform.SetParent(mount,false);
            part.transform.localPosition=mountPosition;part.transform.localRotation=Quaternion.identity;
            // the mount sits in the rover's model, scaled by its rescaleFactor (stock scale without RSS); this part's own
            // model carries the same factor, so the part transform cancels the rover's (1 at 1:1)
            Part rover=mount.GetComponentInParent<Part>();
            float k=rover!=null&&rover!=part?Mathf.Max(rover.rescaleFactor,1e-4f):1f;
            part.transform.localScale=Vector3.one/k;
        }
        public void LateUpdate()
        {
            if(part.parent!=seen)
            {
                // detached or moved in the VAB: both rovers find their links again
                Part old=seen;seen=part.parent;
                if(mount!=null)
                {
                    // out of the old rover's model, at unit scale (Place set 1/rescaleFactor under it)
                    Transform keep=part.parent!=null?part.parent.transform:null;
                    if(part.transform.parent!=keep && part.transform.IsChildOf(mount))part.transform.SetParent(keep,true);
                    if(part.transform.IsChildOf(mount)==false)part.transform.localScale=Vector3.one;
                }
                Rest();Notify(old);Notify(seen);
                if(HighLogic.LoadedSceneIsEditor)MatchNode();
                return;
            }
            Place();
        }
        static void Notify(Part p)
        {
            if(p==null)return;
            ModuleCuriosityRig rig=p.FindModuleImplementing<ModuleCuriosityRig>();
            if(rig!=null)rig.MarkDirty();
        }
        public void OnDestroy()
        {
            if(variantNames!=null)GameEvents.onVariantApplied.Remove(OnVariantApplied);
            // The running rig takes the links out to the rover part transform; they go with this part.
            if(owned!=null)foreach(Transform t in owned)if(t!=null && !t.IsChildOf(part.transform))Destroy(t.gameObject);
            Notify(seen);
        }
    }
}
