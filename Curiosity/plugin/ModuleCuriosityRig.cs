// Curiosity articulated rig: convex wheel contacts and hinged rigid links, no WheelCollider springs.
// Joint stops and mass split are estimates.
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
namespace MSLCuriosity
{
    public partial class ModuleCuriosityRig : PartModule
    {
        // Pose flags; the status line says the same in words.
        [KSPField(isPersistant=true)] public bool deployed=true;
        // Cruise stow: wheels folded as packed in the aeroshell; mobility deploys to the touchdown pose over deployTime.
        [KSPField(isPersistant=true)] public bool stowed=false;
        [KSPField] public float deployTime=6f; // s [GAME]; flight mobility deploy duration not modelled
        float deployT=-1f;
        [KSPField(guiActive=true,guiName="#MSL_Curiosity_UI_Rig",groupName="rig",groupDisplayName="#MSL_Curiosity_UI_grp_Rig")] public string status=MSL.Localization.Text.Get("#MSL_Curiosity_UI_offline_build_physics_off");
        // VAB: one pose selector in place of three pose buttons; not saved, read back
        // from stowed/deployed in OnStart.
        [KSPField(guiActiveEditor=true,guiName="#MSL_Curiosity_UI_Pose",groupName="rig",groupDisplayName="#MSL_Curiosity_UI_grp_Rig")]
        [UI_ChooseOption(options=new[]{"cruise","touchdown","surface"})] public string editorPose="surface";
        // Parking brake. Zero wheel throttle holds the wheels anyway (non-backdrivable drive actuators), so W/S drive
        // straight away; the B key (Brakes action group) also brakes.
        [KSPField(isPersistant=true,guiActive=true,guiActiveEditor=false,groupName="drive",groupDisplayName="#MSL_Curiosity_UI_grp_Drive",guiName="#MSL_Curiosity_UI_Wheel_brake")] [UI_Toggle(scene=UI_Scene.Flight,enabledText="#MSL_Curiosity_UI_ON",disabledText="#MSL_Curiosity_UI_OFF")] public bool brakes=false;
        // Set by the player enabling the rig; the rig restarts by itself after the vessel was packed (warp, switch, reload).
        [KSPField(isPersistant=true)] public bool rigOn=false;
        [KSPField(isPersistant=true,guiActive=true,guiActiveEditor=false,groupName="drive",groupDisplayName="#MSL_Curiosity_UI_grp_Drive",guiName="#MSL_Curiosity_UI_Manual_corner_steering")] [UI_Toggle(scene=UI_Scene.Flight,enabledText="#MSL_Curiosity_UI_ON",disabledText="#MSL_Curiosity_UI_OFF")] public bool manualSteering=false;
        // Joint slider ranges here are placeholders; OnStart sets them from the LINK stops.
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true,guiName="#MSL_Curiosity_UI_Shoulder_azimuth",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-160,maxValue=160,stepIncrement=1)] public float shoulderAz;
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true,guiName="#MSL_Curiosity_UI_Shoulder_elevation",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-150,maxValue=150,stepIncrement=1)] public float shoulderEl;
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true,guiName="#MSL_Curiosity_UI_Elbow",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-160,maxValue=160,stepIncrement=1)] public float elbow;
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true,guiName="#MSL_Curiosity_UI_Wrist",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-180,maxValue=180,stepIncrement=1)] public float wrist;
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true,guiName="#MSL_Curiosity_UI_Turret",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-180,maxValue=180,stepIncrement=1)] public float turret;
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="mast",groupDisplayName="#MSL_Curiosity_UI_grp_Mast",groupStartCollapsed=true,guiName="#MSL_Curiosity_UI_Mast_fold",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-100,maxValue=100,stepIncrement=1)] public float mastFold;
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="mast",groupDisplayName="#MSL_Curiosity_UI_grp_Mast",groupStartCollapsed=true,guiName="#MSL_Curiosity_UI_Mast_azimuth",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-180,maxValue=180,stepIncrement=1)] public float mastAz;
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="mast",groupDisplayName="#MSL_Curiosity_UI_grp_Mast",groupStartCollapsed=true,guiName="#MSL_Curiosity_UI_Mast_elevation",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-90,maxValue=90,stepIncrement=1)] public float mastEl;
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="drive",groupDisplayName="#MSL_Curiosity_UI_grp_Drive",guiName="#MSL_Curiosity_UI_FL_steering",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-90,maxValue=90,stepIncrement=1)] public float steerFL;
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="drive",groupDisplayName="#MSL_Curiosity_UI_grp_Drive",guiName="#MSL_Curiosity_UI_FR_steering",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-90,maxValue=90,stepIncrement=1)] public float steerFR;
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="drive",groupDisplayName="#MSL_Curiosity_UI_grp_Drive",guiName="#MSL_Curiosity_UI_RL_steering",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-90,maxValue=90,stepIncrement=1)] public float steerRL;
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="drive",groupDisplayName="#MSL_Curiosity_UI_grp_Drive",guiName="#MSL_Curiosity_UI_RR_steering",guiFormat="F0",guiUnits="°")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=-90,maxValue=90,stepIncrement=1)] public float steerRR;
        // Real top speed 4 cm/s; the slider allows faster driving.
        [KSPField(isPersistant=true,guiActive=true,guiActiveEditor=false,groupName="drive",groupDisplayName="#MSL_Curiosity_UI_grp_Drive",guiName="#MSL_Curiosity_UI_Drive_speed",guiUnits=" m/s",guiFormat="F2")] [UI_FloatRange(scene=UI_Scene.Flight,minValue=.04f,maxValue=.5f,stepIncrement=.01f)] public float maxSpeed=.04f;
        [KSPField] public float wheelRadius=.25f;
        [KSPField] public float motorTorqueKNm=.2f; // GAME: 6 wheels climb ~25 deg on Mars
        [KSPField] public float brakeTorqueKNm=.6f; // GAME: holding torque at zero throttle / brake
        // Rocker differential as hinge springs solved inside PhysX: each rocker is pulled to (aL-aR)/2, so aL+aR -> 0
        // while the antisymmetric (terrain following) motion stays free. GAME stiffness, kN m per degree.
        [KSPField] public float differentialStiffness=50f;
        [KSPField] public float differentialDamper=2f;
        [KSPField] public float maxJointMassRatio=10f;
        // Roll the wheels on a sphere of wheelRadius (world metres) instead of the faceted hull, whose jumping contact point
        // makes PhysX drop friction anchors and the rover creep downhill.
        [KSPField] public bool sphereWheels=true;
        // Wheel-train links are a few kg against a ~900 kg body; PhysX converges badly at that ratio and the wheel motors
        // track with a steady ~5 deg/s error. Give the solver heavier links (taken off the body, total mass unchanged).
        [KSPField] public float minDriveLinkMass=.025f;
        // Ground-speed loop on top of the wheel motors: the integral cancels what the motors still lose (creep on slopes).
        [KSPField] public float speedKp=1f,speedKi=1.5f,speedTrimMax=.1f;
        // m/s^2; a step change in wheel speed kicks the light links apart, worst at the long physics-warp step
        [KSPField] public float maxAcceleration=.2f;
        float speedTrim,lastCommand;
        // Under physics warp the light arm/mast chains sag and pull apart, so they ride rigidly on the body until warp ends.
        // The rig is rebuilt on each switch; the resume path waits until KSP has settled the rigid rover.
        bool warpRig;
        static bool PhysicsWarp(){return TimeWarp.WarpMode==TimeWarp.Modes.LOW && TimeWarp.CurrentRate>1.01f;}
        // The arm and mast are never rigid bodies: their joints turn kinematically (JointServo).
        bool Dynamic(Link l){return l.mass>0 && !l.Kinematic;}
        [KSPField] public float powerECs=.12f; // GAME 1 EC = 1 kJ approximation
        // Rocker differential residual, for tests; not shown.
        [KSPField] public float differentialError;
        // Rocker / bogie hinge rate for tests: sums of squared rate and of its per-step change ((deg/s)^2), sample count.
        public float suspRateSq,suspJerkSq;public int suspSamples;
        class Link
        {
            public string name,parent,kind;
            public Transform t,originalParent;
            public GameObject roll;
            public Vector3 axis,pos,touchPos,cruisePos;
            public Quaternion rot,touchRot,cruiseRot;
            public float mass,lo,hi,cmd,angle,lastRaw,prev,rateScale=1f,lastVel;
            public bool parked; // drive wheel held by its hinge limits (ParkWheel)
            public bool Kinematic{get{return kind=="arm"||kind=="mast";}}
            public Rigidbody rb;
            public HingeJoint hinge;
            public List<Collider> colliders=new List<Collider>();
        }
        // all: the LINK config; links / byName: those whose transform is on the vessel. The wheels, arm and mast are
        // component parts (ModuleCuriosityComponent) mounted in the rover model; one that is missing is left out.
        readonly List<Link> all=new List<Link>(),links=new List<Link>();
        readonly Dictionary<string,Link> allByName=new Dictionary<string,Link>(),byName=new Dictionary<string,Link>();
        Transform body;
        bool dirty=true;
        public void MarkDirty(){dirty=true;}
        bool running;
        int unpackedFrames;
        float movingMass;
        Vector3 lastPosition;
        static float Number(string s) { return float.Parse(s,CultureInfo.InvariantCulture); }
        static Vector3 V(string s) { string[] a=s.Split(',');return new Vector3(Number(a[0]),Number(a[1]),Number(a[2])); }
        static Quaternion Q(string s) { string[] a=s.Split(',');return new Quaternion(Number(a[0]),Number(a[1]),Number(a[2]),Number(a[3])); }
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            // Packing can be undone in the same frame (vessel switch, warp start), before FixedUpdate sees vessel.packed.
            if(HighLogic.LoadedSceneIsFlight){GameEvents.onVesselGoOnRails.Add(OnRails);GameEvents.onTimeWarpRateChanged.Add(OnWarpRate);}
            ConfigNode config=null;
            foreach(ConfigNode m in part.partInfo.partConfig.GetNodes("MODULE"))
                if(m.GetValue("name")=="ModuleCuriosityRig")config=m;
            if(config==null) { status=MSL.Localization.Text.Get("#MSL_Curiosity_UI_missing_rig_config");return; }
            foreach(ConfigNode n in config.GetNodes("LINK"))
            {
                Link l=new Link();l.name=n.GetValue("name");l.parent=n.GetValue("parent");l.kind=n.GetValue("kind");
                l.axis=V(n.GetValue("axis"));l.pos=V(n.GetValue("position"));l.rot=Q(n.GetValue("rotation"));
                l.touchPos=V(n.GetValue("touchPosition"));l.touchRot=Q(n.GetValue("touchRotation"));
                l.cruisePos=n.HasValue("cruisePosition")?V(n.GetValue("cruisePosition")):l.touchPos;
                l.cruiseRot=n.HasValue("cruiseRotation")?Q(n.GetValue("cruiseRotation")):l.touchRot;
                l.mass=Number(n.GetValue("mass"));l.lo=Number(n.GetValue("min"));l.hi=Number(n.GetValue("max"));
                all.Add(l);allByName[l.name]=l;
            }
            Transform rocker=part.FindModelTransform("Rocker_L");
            if(rocker==null){status=KSP.Localization.Localizer.Format("#MSL_Curiosity_UI_missing_transform_1","Rocker_L");return;}
            body=rocker.parent;
            foreach(KeyValuePair<string,string> s in sliders)
                if(allByName.ContainsKey(s.Key))
                {
                    Link l=allByName[s.Key];UI_FloatRange r=(UI_FloatRange)Fields[s.Value].uiControlFlight;
                    r.minValue=l.lo;r.maxValue=l.hi;Fields[s.Value].SetValue(Mathf.Clamp((float)Fields[s.Value].GetValue(this),l.lo,l.hi),this);
                }
            Rebuild();
            UpdateUI(true);
            if(HighLogic.LoadedSceneIsEditor)SetupPoseOption();
        }
        void SetupPoseOption()
        {
            BaseField f=Fields["editorPose"];
            UI_ChooseOption o=(UI_ChooseOption)f.uiControlEditor;
            o.display=new[]{MSL.Localization.Text.Get("#MSL_Curiosity_UI_Pose_cruise"),MSL.Localization.Text.Get("#MSL_Curiosity_UI_Pose_touchdown"),MSL.Localization.Text.Get("#MSL_Curiosity_UI_Pose_surface")};
            editorPose=stowed?"cruise":deployed?"surface":"touchdown";
            o.onFieldChanged=(field,old)=>{if(editorPose=="cruise")SetCruise();else SetPose(editorPose=="surface");};
        }
        List<ModuleCuriosityComponent> Components()
        {
            List<ModuleCuriosityComponent> found=new List<ModuleCuriosityComponent>();
            foreach(Part c in part.children){ModuleCuriosityComponent m=c.FindModuleImplementing<ModuleCuriosityComponent>();if(m!=null)found.Add(m);}
            return found;
        }
        static Transform Find(Transform t,string name)
        {
            if(t.name==name)return t;
            foreach(Transform c in t){Transform f=Find(c,name);if(f!=null)return f;}
            return null;
        }
        // Mount the component parts under their rig frames, find the links again and put them in the current pose.
        void Rebuild()
        {
            if(running||body==null)return;dirty=false;
            foreach(ModuleCuriosityComponent c in Components())
            {
                Transform at=null;Link cfg;string id=c.Instance;
                if(c.link=="")at=body;
                else if(id!=null && allByName.TryGetValue(id,out cfg))at=cfg.parent=="Body"?body:Find(body,cfg.parent);
                if(at!=null)c.Mount(at);
            }
            links.Clear();byName.Clear();
            foreach(Link l in all)
            {
                l.t=Find(body,l.name);if(l.t==null)continue;
                l.originalParent=l.t.parent;links.Add(l);byName[l.name]=l;
            }
            FindDrill();
            if(stowed)SetCruise();else if(!MobilityDeploying)SetPose(deployed);
        }
        // Joint sliders take their ranges from the LINK min/max (curiosity_spec.LIMITS), not the attribute defaults.
        static readonly Dictionary<string,string> sliders=new Dictionary<string,string>{
            {"Arm_Azimuth","shoulderAz"},{"Arm_Elevation","shoulderEl"},{"Arm_Elbow","elbow"},{"Arm_Wrist","wrist"},{"Arm_Turret","turret"},
            {"Mast_Fold","mastFold"},{"Mast_Yaw","mastAz"},{"Mast_Pitch","mastEl"},
            {"Steer_FL","steerFL"},{"Steer_FR","steerFR"},{"Steer_RL","steerRL"},{"Steer_RR","steerRR"}};
        static readonly string[] steerSliders={"steerFL","steerFR","steerRL","steerRR"};
        static readonly string[] rigControls={"brakes","maxSpeed","manualSteering","shoulderAz","shoulderEl","elbow","wrist","turret","mastFold","mastAz","mastEl","sampleInfo","armTask"};
        int uiState=-1;
        // In flight the panel shows the status and the one button that applies; driving, arm and mast controls only
        // while the rig runs, corner steering sliders only in manual steering.
        void UpdateUI(bool force)
        {
            bool flight=HighLogic.LoadedSceneIsFlight;
            int s=(flight?1:0)|(running?2:0)|(manualSteering?4:0)|(stowed?8:0)|(MobilityDeploying?16:0)|(ArmBusy?32:0)|(sampleBody!=""?64:0);
            if(s==uiState && !force)return;uiState=s;
            Events["EnableRig"].guiActive=!running;
            Events["DeployMobility"].guiActive=stowed && !MobilityDeploying && !running;
            foreach(string n in rigControls)Fields[n].guiActive=running;
            UpdateArmUI();
            foreach(string n in steerSliders)Fields[n].guiActive=running && manualSteering;
            Fields["sampleInfo"].guiActive=running && sampleBody!="";   // no "none" row before the first drill
            if(flight)MonoUtilities.RefreshPartContextWindow(part);
        }
        void SetPose(bool ground)
        {
            if(running)return;
            deployed=ground;stowed=false;deployT=-1f;SetLinkColliders(true);
            foreach(Link l in links){l.t.localPosition=ground?l.pos:l.touchPos;l.t.localRotation=ground?l.rot:l.touchRot;l.angle=l.cmd=l.prev=0;}
            status=ground?MSL.Localization.Text.Get("#MSL_Curiosity_UI_deployed_physics_off"):MSL.Localization.Text.Get("#MSL_Curiosity_UI_touchdown_stowed_physics_off");
        }
        void SetCruise()
        {
            if(running)return;
            stowed=true;deployed=false;deployT=-1f;
            foreach(Link l in links){l.t.localPosition=l.cruisePos;l.t.localRotation=l.cruiseRot;}
            SetLinkColliders(false);
            status=MSL.Localization.Text.Get("#MSL_Curiosity_UI_cruise_stowed_physics_off");
        }
        // While packed in the aeroshell the wheel, suspension, arm, mast and body-fixed component hulls are off (they would
        // overlap the neighbouring vessels at separation); they come back when mobility deploys. On in the VAB for picking.
        void SetLinkColliders(bool on)
        {
            if(!HighLogic.LoadedSceneIsFlight)on=true;
            foreach(Link l in links)foreach(Collider c in l.t.GetComponentsInChildren<Collider>(true))c.enabled=on;
            foreach(ModuleCuriosityComponent m in Components())if(m.link=="")foreach(Collider c in m.part.GetComponentsInChildren<Collider>(true))c.enabled=on;
        }
        // Pose events are hidden (the VAB uses editorPose); in flight the stack sequence, DeployMobility and EnableRig set the pose.
        [KSPEvent(guiActiveEditor=false,guiName="#MSL_Curiosity_UI_Set_cruise_stowed_pose_physics_off",groupName="rig",groupDisplayName="#MSL_Curiosity_UI_grp_Rig")]
        public void CruisePose(){SetCruise();}
        [KSPEvent(guiActive=true,guiName="#MSL_Curiosity_UI_Deploy_mobility_cruise_to_touchdown",groupName="rig",groupDisplayName="#MSL_Curiosity_UI_grp_Rig")]
        public void DeployMobility(){if(running||!stowed||deployT>=0f)return;deployT=0f;SetLinkColliders(true);status=MSL.Localization.Text.Get("#MSL_Curiosity_UI_deploying_mobility");}
        [KSPAction("#MSL_Curiosity_UI_Deploy_mobility_cruise_to_touchdown")]
        public void DeployMobilityAction(KSPActionParam p){DeployMobility();}
        public bool MobilityDeploying{get{return deployT>=0f;}}
        public void Update()
        {
            if(dirty && !running)Rebuild();
            if(HighLogic.LoadedSceneIsFlight)UpdateUI(false);
            if(HighLogic.LoadedSceneIsFlight)DrillAnimate();
            if(deployT<0f||running)return;
            deployT+=Time.deltaTime;
            float u=Mathf.SmoothStep(0f,1f,Mathf.Clamp01(deployT/Mathf.Max(.1f,deployTime)));
            foreach(Link l in links){l.t.localPosition=Vector3.Lerp(l.cruisePos,l.touchPos,u);l.t.localRotation=Quaternion.Slerp(l.cruiseRot,l.touchRot,u);}
            part.Effect("mslServo",u>=1f?0f:.6f);
            if(u>=1f)SetPose(false);
        }
        [KSPEvent(guiActiveEditor=false,guiName="#MSL_Curiosity_UI_Set_surface_pose_physics_off",groupName="rig",groupDisplayName="#MSL_Curiosity_UI_grp_Rig")]
        public void SurfacePose(){SetPose(true);}
        [KSPEvent(guiActiveEditor=false,guiName="#MSL_Curiosity_UI_Set_touchdown_pose_physics_off",groupName="rig",groupDisplayName="#MSL_Curiosity_UI_grp_Rig")]
        public void TouchdownPose(){SetPose(false);}
        [KSPEvent(guiActive=true,guiName="#MSL_Curiosity_UI_Enable_experimental_articulated_physics",groupName="rig",groupDisplayName="#MSL_Curiosity_UI_grp_Rig")]
        public void EnableRig()
        {
            if(running || !HighLogic.LoadedSceneIsFlight || vessel.packed || part.rb==null || links.Count==0)return;
            if(stowed || MobilityDeploying){status=MSL.Localization.Text.Get("#MSL_Curiosity_UI_deploy_surface_pose_before_enabling");return;}
            if(!deployed)
            {
                // From the touchdown stow the arm and mast start where they are: the arm stays stowed (ArmUnstow), the mast
                // rises at the actuator rate (RaiseMast), nothing jumps to the surface pose.
                Dictionary<Link,float> from=new Dictionary<Link,float>();
                foreach(Link l in links)if(l.Kinematic)from[l]=AngleOf(l,l.touchRot);
                SetPose(true);
                foreach(KeyValuePair<Link,float> k in from)
                {
                    k.Key.angle=k.Key.cmd=k.Key.prev=k.Value;Pose(k.Key);
                    string f;if(sliders.TryGetValue(k.Key.name,out f))Fields[f].SetValue(k.Key.kind=="arm"||k.Key.name!="Mast_Fold"?k.Value:0f,this);
                }
                mastRaising=true;
            }
            movingMass=0;warpRig=PhysicsWarp();
            // Capture all original chains BEFORE detaching the dynamic links.
            foreach(Link l in links)
            {
                if(l.mass<=0)continue;
                l.originalParent=l.t.parent;l.colliders.Clear();
                foreach(Collider c in l.t.GetComponentsInChildren<Collider>(true))
                {
                    bool owned=true;
                    foreach(Link child in links)if(child!=l && child.mass>0 && c.transform.IsChildOf(child.t) && child.t.IsChildOf(l.t)){owned=false;break;}
                    if(owned)l.colliders.Add(c);
                }
            }
            foreach(Link l in links)if(Dynamic(l)){l.t.SetParent(part.transform,true);MakeBody(l);}
            foreach(Link l in links)if(l.rb!=null){MakeHinge(l);l.cmd=0;l.angle=0;l.lastRaw=0;l.lastVel=0;l.parked=false;}
            // arm and mast: no hulls while they turn (they would push the rover); the turret's ground gap is geometric
            foreach(Link l in links)if(l.Kinematic){l.cmd=l.prev=l.angle;Pose(l);foreach(Collider c in l.colliders)c.enabled=false;}
            armBlocked=false;armBlockedT=0;
            lastPosition=part.rb.position;speedTrim=0;lastCommand=Vector3.Dot((Vector3)vessel.srf_velocity,part.transform.right);running=true;rigOn=true;status=MSL.Localization.Text.Get("#MSL_Curiosity_UI_EXPERIMENTAL_rigid_links_differential");
        }
        void MakeBody(Link l)
        {
            l.rb=l.t.gameObject.AddComponent<Rigidbody>();
            l.rb.mass=l.kind=="drive"||l.kind=="steer"||l.kind=="bogie"||l.kind=="rocker"?Mathf.Max(l.mass,minDriveLinkMass):l.mass;
            // drawn with the body's interpolation (None), or the link jitters on screen against the body
            l.rb.useGravity=false;l.rb.interpolation=part.rb.interpolation;
            l.rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;
            l.rb.solverIterations=24*Passes();l.rb.solverVelocityIterations=12*Passes();
            l.rb.velocity=part.rb.GetPointVelocity(l.rb.worldCenterOfMass);l.rb.angularVelocity=part.rb.angularVelocity;
            l.rb.maxAngularVelocity=20;
            foreach(Collider c in l.colliders){c.enabled=true;MeshCollider mc=c as MeshCollider;if(mc!=null)mc.convex=true;
                PhysicMaterial pm=new PhysicMaterial("CuriosityContact");pm.dynamicFriction=.65f;pm.staticFriction=.75f;pm.bounciness=0;c.material=pm;}
            if(sphereWheels && l.kind=="drive" && l.colliders.Count>0)
            {
                l.roll=new GameObject("COL_WheelRoll");l.roll.layer=l.colliders[0].gameObject.layer;l.roll.transform.SetParent(l.t,false);
                SphereCollider sc=l.roll.AddComponent<SphereCollider>();sc.radius=wheelRadius/Mathf.Max(l.roll.transform.lossyScale.x,1e-4f);sc.material=l.colliders[0].sharedMaterial;
                foreach(Collider c in l.colliders)c.enabled=false;
                l.colliders.Add(sc);
            }
            movingMass+=l.rb.mass;
        }
        void MakeHinge(Link l)
        {
            string pn=l.parent;Rigidbody parentBody=part.rb;
            while(pn!="Body" && byName.ContainsKey(pn)){Link p=byName[pn];if(p.rb!=null){parentBody=p.rb;break;}pn=p.parent;}
            l.hinge=l.t.gameObject.AddComponent<HingeJoint>();l.hinge.connectedBody=parentBody;
            l.hinge.anchor=Vector3.zero;l.hinge.axis=l.axis;l.hinge.autoConfigureConnectedAnchor=false;
            l.hinge.connectedAnchor=parentBody.transform.InverseTransformPoint(l.t.position);
            l.hinge.enableCollision=false;
            // PhysX loses joint stiffness at large mass ratios (5 kg wheel on a 900 kg body): the rover creeps
            // downhill on held wheels and the chains drift. Let the solver see the parent as at most N x the link.
            float ratio=parentBody.mass/l.rb.mass;if(ratio>maxJointMassRatio)l.hinge.connectedMassScale=maxJointMassRatio/ratio;
            if(l.kind!="drive"){JointLimits lim=l.hinge.limits;lim.min=l.lo;lim.max=l.hi;l.hinge.limits=lim;l.hinge.useLimits=true;}
            foreach(Collider c in l.colliders)foreach(Collider p in parentBody.GetComponentsInChildren<Collider>())
                if(p.attachedRigidbody==parentBody)Physics.IgnoreCollision(c,p,true);
        }
        // The physicsless component parts weigh on the rover body.
        float BodyMass()
        {
            float m=part.mass+(float)part.GetResourceMass();
            foreach(Part c in part.children)if(c.physicalSignificance==Part.PhysicalSignificance.NONE)m+=c.mass+(float)c.GetResourceMass();
            return m;
        }
        // the island takes the highest count; a 4x warp step needs proportionally more passes to converge
        int Passes(){return warpRig?Mathf.Clamp(Mathf.RoundToInt(TimeWarp.CurrentRate),1,4):1;}
        void Differential()
        {
            Link l=byName["Rocker_L"],r=byName["Rocker_R"];
            float al=l.hinge.angle,ar=r.hinge.angle,half=(al-ar)*.5f;
            differentialError=Mathf.DeltaAngle(0,al+ar);
            Spring(l,half,differentialStiffness,differentialDamper);Spring(r,-half,differentialStiffness,differentialDamper);
            Link bar=byName["Differential_Crossbar"];bar.t.localRotation=bar.rot*Quaternion.AngleAxis(al,bar.axis);
        }
        static void Spring(Link l,float target,float k,float d)
        {
            JointSpring s=l.hinge.spring;s.spring=k;s.damper=d;s.targetPosition=Mathf.Clamp(target,l.lo,l.hi);l.hinge.spring=s;l.hinge.useSpring=true;
        }
        [KSPField] public float steerStiffness=3f,steerDamper=.25f; // steering hinge spring and damper
        void Servo(Link l,float target){Spring(l,target,steerStiffness,steerDamper);}
        // Arm and mast joints turn kinematically at the actuator rate: the link is set to its pose rotation turned by the
        // joint angle about the hinge axis. The arm rides the body (a motor-driven arm fell to the ground).
        // The ground stops the turret: a step that would press its tools into the ground is taken back (GroundGuard).
        [KSPField] public float jointRate=9f;
        void TrackAngle(Link l)
        {
            float raw=l.hinge.angle;l.angle+=Mathf.DeltaAngle(l.lastRaw,raw);l.lastRaw=raw;
        }
        void Pose(Link l){l.t.localRotation=l.rot*Quaternion.AngleAxis(l.angle,l.axis);}
        // joint angle of a link rotation (the inverse of Pose)
        static float AngleOf(Link l,Quaternion q)
        {
            float a;Vector3 ax;(Quaternion.Inverse(l.rot)*q).ToAngleAxis(out a,out ax);
            if(a>180f)a-=360f;
            return Vector3.Dot(ax,l.axis)<0f?-a:a;
        }
        // Mast deployment after the first enable: the mast stands up first, then the head turns to its working pose.
        bool mastRaising;
        void RaiseMast()
        {
            if(!mastRaising)return;
            Link l;if(byName.TryGetValue("Mast_Fold",out l)&&Mathf.Abs(l.angle-mastFold)>.1f)return;
            mastAz=0f;mastEl=0f;mastRaising=false;
        }
        void JointServo(Link l,float target)
        {
            l.prev=l.angle;
            l.angle=l.cmd=Mathf.MoveTowards(l.angle,Mathf.Clamp(target,l.lo,l.hi),jointRate*l.rateScale*Time.fixedDeltaTime);
            if(l.angle!=l.prev)Pose(l);
        }
        // Parking brake: a zero-velocity motor creeps downhill, so below parkSpeed each drive hinge is locked by limits at
        // its current angle; throttle releases it. A wheel near the +-180 wrap stays on the motor until away from it.
        [KSPField] public float parkSpeed=.05f; // m/s
        void ParkWheel(Link l,bool park)
        {
            if(park==l.parked)return;
            if(park)
            {
                float a=l.hinge.angle;if(Mathf.Abs(a)>175f)return;
                JointLimits lim=l.hinge.limits;lim.min=a-.01f;lim.max=a+.01f;lim.bounciness=0;l.hinge.limits=lim;l.hinge.useLimits=true;
            }
            else l.hinge.useLimits=false;
            l.parked=park;
        }
        // the ground stopped the arm this step / for this long (s)
        bool armBlocked;float armBlockedT;
        // for tests: joint angle (NaN without the link), the ground holding the arm
        public float JointAngle(string name){Link l;return byName.TryGetValue(name,out l)?l.angle:float.NaN;}
        public bool ArmGrounded{get{return armBlockedT>0f;}}
        void GroundGuard(float before)
        {
            float after=TurretGap();
            armBlocked=!float.IsNaN(after) && after<0f && after<before-1e-4f;
            if(armBlocked){foreach(Link l in links)if(l.kind=="arm" && l.angle!=l.prev){l.angle=l.cmd=l.prev;Pose(l);}armBlockedT+=Time.fixedDeltaTime;}
            else armBlockedT=0;
        }
        // Joint projection: a link more than projectDistance off its parent's anchor is put back on it (parent-first order,
        // so children follow in the same pass).
        [KSPField] public float projectDistance=.005f;
        public float projected;
        void Project()
        {
            foreach(Link l in links)
            {
                if(l.rb==null||l.hinge==null||l.hinge.connectedBody==null)continue;
                Rigidbody p=l.hinge.connectedBody;
                Vector3 want=p.position+p.rotation*Vector3.Scale(p.transform.lossyScale,l.hinge.connectedAnchor);
                Vector3 e=want-l.rb.position;float d=e.magnitude;
                if(d<=projectDistance)continue;
                l.rb.position+=e*(1-projectDistance/d);projected=Mathf.Max(projected,d);
            }
        }
        public void FixedUpdate()
        {
            // a component part went (crash): the rig restarts without it
            if(running && dirty)StopRig();
            if(!running)
            {
                if(dirty)Rebuild();
                // Resume after the vessel was packed: let KSP settle it on the ground for half a second first.
                if(rigOn && HighLogic.LoadedSceneIsFlight && vessel!=null && !vessel.packed && vessel.LandedOrSplashed && part.rb!=null)
                {if(++unpackedFrames>25){unpackedFrames=0;EnableRig();}}
                else unpackedFrames=0;
                return;
            }
            if(vessel==null || vessel.packed || part.rb==null){StopRig();return;}
            if(PhysicsWarp()!=warpRig){StopRig();return;}
            // The link bodies carry their own mass; the rover body keeps the rest (its part, components and resources).
            part.rb.mass=Mathf.Max(.001f,BodyMass()-movingMass);
            Vector3 jump=part.rb.position-lastPosition;
            if(jump.magnitude>50)foreach(Link l in links)if(l.rb!=null){l.rb.position+=jump;l.rb.velocity=part.rb.GetPointVelocity(l.rb.worldCenterOfMass);}
            lastPosition=part.rb.position;
            foreach(Link l in links)if(l.rb!=null)
            {
                l.rb.AddForce((Vector3)FlightGlobals.getGeeForceAtPosition(l.rb.worldCenterOfMass)*l.rb.mass,ForceMode.Force);
                if(l.rb.interpolation!=part.rb.interpolation)l.rb.interpolation=part.rb.interpolation; // KSP may switch the body's
            }
            Project();
            foreach(Link l in links)if(l.hinge!=null)TrackAngle(l);
            foreach(Link l in links)if(l.hinge!=null&&(l.kind=="rocker"||l.kind=="bogie"))
            {float v=l.hinge.velocity,dv=v-l.lastVel;l.lastVel=v;suspRateSq+=v*v;suspJerkSq+=dv*dv;suspSamples++;}
            Differential();
            UpdateDrill();
            RaiseMast();
            ArmOps();
            float throttle=vessel.ctrlState.wheelThrottle,turn=vessel.ctrlState.wheelSteer;
            double required=powerECs*(Mathf.Abs(throttle)+.1f)*Time.fixedDeltaTime;
            if(part.RequestResource("ElectricCharge",required)<required*.95)throttle=0;
            bool brake=brakes || vessel.ActionGroups[KSPActionGroup.Brakes];
            bool hold=brake || Mathf.Abs(throttle)<.01f;
            float radius=Mathf.Abs(turn)<.001f?100000:4*part.rescaleFactor/turn;
            float want=hold?0:throttle*maxSpeed,have=Vector3.Dot((Vector3)vessel.srf_velocity,part.transform.right);
            float err=want-have,step=maxAcceleration*Time.fixedDeltaTime;
            float raw=want+speedTrim+Mathf.Clamp(err*speedKp,-speedTrimMax,speedTrimMax);
            // no integration while the ramp limits, or the trim winds up and overshoots
            if(Mathf.Abs(raw-lastCommand)<=step)speedTrim=Mathf.Clamp(speedTrim+err*speedKi*Time.fixedDeltaTime,-speedTrimMax,speedTrimMax);
            float command=Mathf.MoveTowards(lastCommand,raw,step);lastCommand=command;
            float gapBefore=TurretGap();
            foreach(Link l in links)
            {
                if(l.hinge==null && !l.Kinematic)continue;
                if(l.kind=="drive")
                {
                    JointMotor m=l.hinge.motor;m.force=hold?brakeTorqueKNm:motorTorqueKNm;
                    Vector3 p=part.transform.InverseTransformPoint(l.t.position);
                    float factor=Mathf.Sqrt(p.x*p.x+(radius-p.z)*(radius-p.z))/Mathf.Abs(radius);
                    // +throttle (W) drives toward +x, the arm / front of the rover
                    m.targetVelocity=command*factor/wheelRadius*Mathf.Rad2Deg;
                    m.freeSpin=false;l.hinge.motor=m;l.hinge.useMotor=true;
                    ParkWheel(l,hold && Mathf.Abs(have)<parkSpeed);
                }
                else if(l.kind=="steer")
                {
                    float angle;
                    if(manualSteering)angle=l.name=="Steer_FL"?steerFL:l.name=="Steer_FR"?steerFR:l.name=="Steer_RL"?steerRL:steerRR;
                    else {Vector3 p=part.transform.InverseTransformPoint(l.t.position);angle=Mathf.Atan(p.x/(radius-p.z))*Mathf.Rad2Deg;}
                    Servo(l,angle);
                }
                else if(l.name=="Arm_Azimuth")JointServo(l,shoulderAz);
                else if(l.name=="Arm_Elevation")JointServo(l,shoulderEl);
                else if(l.name=="Arm_Elbow")JointServo(l,elbow);
                else if(l.name=="Arm_Wrist")JointServo(l,wrist);
                else if(l.name=="Arm_Turret")JointServo(l,turret);
                else if(l.name=="Mast_Fold")JointServo(l,mastFold);
                else if(l.name=="Mast_Yaw")JointServo(l,mastAz);
                else if(l.name=="Mast_Pitch")JointServo(l,mastEl);
            }
            GroundGuard(float.IsNaN(gapBefore)?float.PositiveInfinity:gapBefore);
            Sounds(command);
        }
        // Patches/MSL_Curiosity_Sounds.cfg: the drive motors with the commanded speed, the actuators with the fastest arm /
        // mast joint (hinge jitter at rest stays under the floor), the drill while it runs
        void Sounds(float command)
        {
            float servo=0;
            foreach(Link l in links)if(l.Kinematic)servo=Mathf.Max(servo,Mathf.Abs(l.angle-l.prev)/Time.fixedDeltaTime/jointRate);
            part.Effect("mslDrive",Mathf.Clamp01(Mathf.Abs(command)/Mathf.Max(.001f,maxSpeed)));
            part.Effect("mslServo",servo>.15f?Mathf.Clamp01(servo):0f);
            part.Effect("mslDrill",Drilling?1f:0f);
        }
        void Quiet(){if(part==null||gone)return;part.Effect("mslDrive",0f);part.Effect("mslServo",0f);part.Effect("mslDrill",0f);}
        bool gone;
        // ---- Science gates (ModuleCuriosityExperiment): contact science needs the arm placed, remote sensing the mast up,
        // SAM / CheMin a drilled sample from the current biome.
        // m [GAME]: gap between the turret (its tools) and the ground below it
        [KSPField] public float armContactRange=.15f;
        [KSPField] public float drillTime=30f,drillECs=.15f; // s, EC/s [GAME]
        [KSPField(isPersistant=true)] public string sampleBody="",sampleBiome="";
        [KSPField(guiActive=true,guiActiveEditor=false,groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true,guiName="#MSL_Curiosity_UI_Sample")] public string sampleInfo="";
        float drillT=-1f;
        public bool Drilling{get{return drillT>=0f;}}
        public static string Biome(Vessel v){return ScienceUtil.GetExperimentBiome(v.mainBody,v.latitude,v.longitude);}
        public bool HasSample{get{return vessel!=null && sampleBody==vessel.mainBody.name && sampleBiome==Biome(vessel);}}
        // Gap from the turret's tools to the ground below the turret (signed: < 0 pressed in), to the plane of the ground
        // hit; NaN without a running rig or ground within 5 m. Tool spheres: ModuleCuriosityArmOps.
        float TurretGap()
        {
            Link l;if(!running||!byName.TryGetValue("Arm_Turret",out l))return float.NaN;
            Vector3 c=l.t.TransformPoint(turretCore),down=((Vector3)(vessel.mainBody.position-(Vector3d)c)).normalized;
            RaycastHit hit;
            if(!Physics.Raycast(c-down,down,out hit,6f,1<<15))return float.NaN;
            float scale=l.t.lossyScale.x,gap=Vector3.Dot(c-hit.point,hit.normal)-turretCoreR*scale;
            foreach(Vector3 b in turretTools)gap=Mathf.Min(gap,Vector3.Dot(l.t.TransformPoint(b)-hit.point,hit.normal)-turretToolR*scale);
            return gap;
        }
        // m [GAME]: gap between the turret (its tools) and the ground below it; -1 without a running rig or ground
        public float TurretClearance(){float g=TurretGap();return float.IsNaN(g)?-1f:Mathf.Max(0f,g);}
        public bool ArmPlaced{get{float c=TurretClearance();return c>=0f && c<armContactRange;}}
        // Mast standing: hinge 0 is the working pose (mast up).
        public bool MastUp
        {
            get
            {
                if(stowed||MobilityDeploying)return false;
                Link l;if(!running)return deployed;
                return !byName.TryGetValue("Mast_Fold",out l)||Mathf.Abs(l.angle)<15f;
            }
        }
        // ---- Drill animation (visual only): Drill_Feed moves along the bit axis, its child Drill_Bit spins; local -Z is out
        // of the drill. Feed to the rock, then rotary-percussive down to drillDepth; sieving runs the percussor alone.
        // Model metres, scaled with the part.
        [KSPField] public float drillRpm=120f;          // ESTIMATE
        [KSPField] public float drillContact=.045f;     // m: stroke from rest to the stabilizer plane (model probe)
        [KSPField] public float drillDepth=.05f;        // m: hole depth
        [KSPField] public float drillFeedSpeed=.03f;    // m/s [GAME] approach / retract
        [KSPField] public float drillHammer=.0008f;     // m: percussion jitter
        const int DRILL_OFF=0,DRILL_RUN=1,DRILL_HAMMER=2;
        int drillAnim;
        Transform drillFeed,drillBit;
        Vector3 drillFeedRest;Quaternion drillBitRest;
        float drillFeedPos,drillSpin,drillAngle;
        void FindDrill()
        {
            Transform f=body==null?null:Find(body,"Drill_Feed");
            if(f==drillFeed)return;
            drillFeed=f;drillBit=f==null?null:Find(f,"Drill_Bit");
            if(f!=null)drillFeedRest=f.localPosition;
            if(drillBit!=null)drillBitRest=drillBit.localRotation;
            drillFeedPos=drillSpin=drillAngle=0f;
        }
        void DrillAnimate()
        {
            if(drillFeed==null||drillBit==null)return;
            float dt=Time.deltaTime;if(dt<=0f)return;
            float want=0f;
            if(drillAnim==DRILL_RUN)
            {
                float u=Mathf.Clamp01(drillT/Mathf.Max(.1f,drillTime));
                want=drillContact+drillDepth*Mathf.Clamp01((u-.1f)/.85f);
            }
            drillFeedPos=Mathf.MoveTowards(drillFeedPos,want,(drillAnim==DRILL_RUN&&drillFeedPos>=drillContact?drillDepth/Mathf.Max(.1f,drillTime*.85f):drillFeedSpeed)*dt);
            bool contact=drillAnim==DRILL_RUN&&drillFeedPos>=drillContact-.002f;
            drillSpin=Mathf.MoveTowards(drillSpin,contact?drillRpm*6f:0f,drillRpm*6f*dt);   // deg/s, 1 s spin-up
            drillAngle=Mathf.Repeat(drillAngle+drillSpin*dt,360f);
            float hammer=contact||drillAnim==DRILL_HAMMER?drillHammer*UnityEngine.Random.value:0f;
            drillFeed.localPosition=drillFeedRest+Vector3.back*(drillFeedPos+hammer);
            drillBit.localRotation=drillBitRest*Quaternion.AngleAxis(drillAngle,Vector3.forward);
        }
        // DrillSample (ModuleCuriosityArmOps): drill, sieve and deliver to the inlets; the sample counts once delivered.
        void UpdateDrill()
        {
            sampleInfo=sampleBiome==""?MSL.Localization.Text.Get("#MSL_Curiosity_UI_sample_none"):sampleBiome+(HasSample?"":" ("+sampleBody+")");
        }
        // After a stop the arm and mast stay where they were (their hulls off away from the surface pose); EnableRig
        // carries on from there.
        void KeepPose()
        {
            Dictionary<Link,float> kept=new Dictionary<Link,float>();foreach(Link l in links)if(l.Kinematic)kept[l]=l.angle;
            SetPose(deployed);if(!deployed)return;
            foreach(KeyValuePair<Link,float> k in kept)
            {
                k.Key.angle=k.Key.cmd=k.Key.prev=k.Value;Pose(k.Key);
                if(Mathf.Abs(k.Value)>.5f)foreach(Collider c in k.Key.colliders)c.enabled=false;
            }
        }
        void StopRig()
        {
            if(!running)return;running=false;EndArmOp();Quiet();
            // Immediate: AddComponent<Rigidbody> fails while a Destroy()ed body is still attached, so a deferred destroy
            // breaks any rebuild in the same frame.
            foreach(Link l in links){if(l.hinge!=null)DestroyImmediate(l.hinge);if(l.rb!=null)DestroyImmediate(l.rb);if(l.roll!=null)DestroyImmediate(l.roll);l.hinge=null;l.rb=null;l.roll=null;}
            foreach(Link l in links)if(l.t!=null)l.t.SetParent(l.originalParent,true);
            if(part.rb!=null)part.rb.mass=BodyMass();
            if(dirty)Rebuild();else KeepPose();status=MSL.Localization.Text.Get(rigOn?"#MSL_Curiosity_UI_paused_resumes_on_unpack":"#MSL_Curiosity_UI_physics_off_packed_reload");
        }
        // No menu button: the rig stays on; warp/rails still pause it (StopRig). Kept for code and tests.
        [KSPEvent(guiActive=false,guiName="#MSL_Curiosity_UI_Disable_articulated_physics",groupName="rig",groupDisplayName="#MSL_Curiosity_UI_grp_Rig")]
        public void DisableRig(){rigOn=false;StopRig();}
        void OnRails(Vessel v){if(v==vessel)StopRig();}
        // Go rigid before the first long step runs, not one step late; FixedUpdate rebuilds for the new rate.
        void OnWarpRate(){if(running && !vessel.packed && PhysicsWarp()!=warpRig)StopRig();}
        public void OnDestroy(){gone=true;GameEvents.onVesselGoOnRails.Remove(OnRails);GameEvents.onTimeWarpRateChanged.Remove(OnWarpRate);bool keep=rigOn;StopRig();rigOn=keep;}
    }
}
