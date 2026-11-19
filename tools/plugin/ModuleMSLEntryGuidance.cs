// Entry guidance: bank steering with a numerical range predictor, crossrange reversals, heading alignment and
// balance-mass release. A game implementation, not the flight gains. The built-in rate controller flies the attitude
// through ModuleRCS only.
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MSLAeroshell
{
    public class ModuleMSLEntryGuidance : PartModule
    {
        [KSPField(isPersistant=true)] public bool guidanceEnabled;
        // No part-menu rows: target, buttons and manual bank are in the toolbar control panel (MSLStatusWindow); the group
        // names stay for the readout fields' metadata.
        const string G="mslGuidance",GN="#MSL_Aeroshell_UI_grp_Guidance",T="mslEntryData",TN="#MSL_Aeroshell_UI_grp_Telemetry";
        [KSPField(guiName="#MSL_Aeroshell_UI_Guidance_status")] public string status=MSL.Localization.Text.Get("#MSL_Aeroshell_UI_off");
        [KSPField(isPersistant=true,guiName="#MSL_Aeroshell_UI_Target_latitude")] public float targetLatitude=-4.5895f;
        [KSPField(isPersistant=true,guiName="#MSL_Aeroshell_UI_Target_longitude")] public float targetLongitude=137.4417f;
        [KSPField] public float trimAoA=16f;
        [KSPField] public float referenceArea=15.904f;
        [KSPField] public bool autoEntryMassRelease=true;
        // Entry balance masses leave below this speed. Earlier release adds lift below 1100 m/s, where only the heading is
        // steered, and lands long.
        [KSPField] public float entryMassReleaseSpeed=800f;
        // Bank steering for range above this speed, heading alignment below.
        [KSPField] public float headingAlignmentSpeed=1100f;
        // Reversal deadband: crossrange central angle a + b v^2 (deg, v in m/s), narrowing as the vehicle slows, tighter by
        // firstReversalScale until the first reversal. [GAME] 0.005 / 5e-9 / 0.5 = 7.7 km at 5000 m/s, 0.65 km at 1100.
        [KSPField] public float reversalDeadbandConst=.005f;
        [KSPField] public float reversalDeadbandQuad=5e-9f;
        [KSPField] public float firstReversalScale=.5f;
        // MSL minimum bank while the crossrange grows, so a saturated vertical L/D still turns the vehicle.
        [KSPField] public float minimumBankGrowing=15f;
        double previousCrossrange;
        // Cruise balance masses leave once the guided entry attitude has settled (bank within 5 deg, rate < 0.5 deg/s
        // for 5 s) or 150 km above the entry interface, as on MSL; only after the cruise stage is gone.
        [KSPField] public bool autoCruiseMassRelease=true;
        double settledSince=-1;
        [KSPField(isPersistant=true,guiName="#MSL_Aeroshell_UI_Manual_bank")] public bool manualBank;
        [KSPField(isPersistant=true,guiName="#MSL_Aeroshell_UI_Manual_bank_angle")] public float manualBankAngle;
        [KSPField(isPersistant=true,guiName="#MSL_Aeroshell_UI_Bank_command",guiFormat="F1",guiUnits="°",groupName=T,groupDisplayName=TN,groupStartCollapsed=true)] public float bankCommand;
        [KSPField(guiName="#MSL_Aeroshell_UI_Measured_bank",guiFormat="F1",guiUnits="°",groupName=T,groupDisplayName=TN,groupStartCollapsed=true)] public float measuredBank;
        [KSPField(guiName="#MSL_Aeroshell_UI_Measured_AoA",guiFormat="F1",guiUnits="°",groupName=T,groupDisplayName=TN,groupStartCollapsed=true)] public float measuredAoA;
        [KSPField(guiName="#MSL_Aeroshell_UI_Range_to_target",guiFormat="F1",guiUnits=" km",groupName=T,groupDisplayName=TN,groupStartCollapsed=true)] public float rangeKm;
        [KSPField(guiName="#MSL_Aeroshell_UI_Predicted_range",guiFormat="F1",guiUnits=" km",groupName=T,groupDisplayName=TN,groupStartCollapsed=true)] public float predictedKm;
        [KSPField(guiName="#MSL_Aeroshell_UI_RCS_command",guiFormat="F2",groupName=T,groupDisplayName=TN,groupStartCollapsed=true)] public float commandMagnitude;
        [KSPField(guiName="#MSL_Aeroshell_UI_Bank_reversals",groupName=T,groupDisplayName=TN,groupStartCollapsed=true)] public int bankReversals;
        [KSPField(guiName="#MSL_Aeroshell_UI_Entry_balance_masses",groupName=T,groupDisplayName=TN,groupStartCollapsed=true)] public int entryMasses;
        [KSPField(isPersistant=true,guiName="#MSL_Aeroshell_UI_Estimated_drag_coefficient",guiFormat="F2",groupName=T,groupDisplayName=TN,groupStartCollapsed=true)] public float estimatedCd=1.6f;
        [KSPField(isPersistant=true,guiName="#MSL_Aeroshell_UI_Measured_lift_drag",guiFormat="F3",groupName=T,groupDisplayName=TN,groupStartCollapsed=true)] public float measuredLiftDrag=.24f;
        [KSPField(isPersistant=true)] public int bankSign=1;
        // Entry attitude: pre-bank at the guidance bank (not lift up) until aerodynamic drag reaches
        // rangeControlDrag, then bank steering for range. [GAME] threshold, not the flight ETPC value.
        [KSPField] public float rangeControlDrag=1.96f;
        // Ground covered after the predictor's 450 m/s end point (mortar, chute, powered descent), m. Range control aims this
        // far short of the landing site. [GAME] RSS: 12.5 km.
        [KSPField] public float terminalRange=12000f;
        // Usable RCS angular acceleration, deg/s^2 ([GAME], weakest axis = roll): the commanded rate is limited
        // to what can still be stopped at the target, so bank changes do not overshoot.
        [KSPField] public float rcsAngularAccel=1f;
        [KSPField] public float maxBodyRate=10f;        // deg/s
        // Above aeroControlQ (kPa) only the bank about the capsule axis is flown; pitch and yaw are left to aerodynamic trim
        // and only damped beyond dampRateDeadband (holding the computed nose angle saturated the RCS). In vacuum all three
        // axes are held, except under physics warp. No firing inside attitudeDeadband (deg) and rateDeadband (deg/s).
        [KSPField] public float aeroControlQ=.3f;
        [KSPField] public float attitudeDeadband=2f;
        [KSPField] public float rateDeadband=.2f;
        [KSPField] public float dampRateDeadband=1.5f;
        [KSPField] public float bankSlewRate=10f;       // deg/s, guidance bank command
        // Fewer RCS pulses: the bank command follows the solution only once it is bankCommandStep (deg) away, and the
        // attitude loop, once started at attitudeDeadband, drives the error down to attitudeSettle.
        [KSPField] public float bankCommandStep=3f;
        [KSPField] public float attitudeSettle=.5f;
        float heldBank;
        bool steeringActive;
        // Range control may bank past 90 deg (lift partly down) when the prediction is long.
        [KSPField] public float maximumBank=100f;
        // Heading alignment: bank = -gain * (heading error + lead * its rate), so the roll-out starts before the
        // heading is reached. [GAME] 3 / 8 s / 60 deg cut the RSS crossrange 2.87 -> 1.57 km.
        [KSPField] public float headingAlignmentGain=3f;
        [KSPField] public float headingAlignmentLead=8f;
        [KSPField] public float headingAlignmentMaxBank=60f;
        float previousHeadingError,headingErrorRate;
        bool haveHeadingError;
        // the status line already names the phase (heading alignment / range control)
        [KSPField(isPersistant=true)] public bool rangeControl;
        Part controlPart;
        // descent-stage RCS: on the stage part itself, or on MSL.SC.* component parts
        readonly List<ModuleRCS> controlRcs=new List<ModuleRCS>();
        Vessel topologyVessel;
        bool topologyDirty=true;
        int topologyPartCount=-1, cachedEntryMasses, cachedCruiseMasses;
        PartModule chuteModule;
        System.Reflection.FieldInfo chuteDeployed;
        ModuleMSLParachute fallbackChute;   // no RealChute: the plugin's own parachute module
        Vessel hooked;
        Quaternion wanted;
        double nextGuidance;
        float bankMagnitude=65f;
        CelestialBody predictionBody;
        Func<double,double> densityAtAltitude;
        Vector3d previousVelocity;
        bool haveVelocity;
        readonly Vector3 balanceAxis=new Vector3(0,0,-1); // EBMD cluster: Backshell -Z

        static bool Is(Part p,string name){return p!=null && p.partInfo!=null && p.partInfo.name==name;}
        void InvalidateTopology(Vessel changed)
        {if(changed==vessel || changed==topologyVessel)topologyDirty=true;}
        void RefreshTopology()
        {
            if(vessel==null){topologyVessel=null;topologyDirty=true;return;}
            if(!topologyDirty && topologyVessel==vessel && topologyPartCount==vessel.parts.Count)return;
            topologyVessel=vessel;topologyPartCount=vessel.parts.Count;topologyDirty=false;
            cachedEntryMasses=cachedCruiseMasses=0;controlPart=null;controlRcs.Clear();
            foreach(Part p in vessel.parts)
            {
                if(Is(p,"MSL.EBMD"))cachedEntryMasses++;
                else if(Is(p,"MSL.CBMD"))cachedCruiseMasses++;
                else if(controlPart==null && Is(p,"MSL.SkyCrane"))controlPart=p;
            }
            if(controlPart==null)return;
            controlRcs.AddRange(controlPart.FindModulesImplementing<ModuleRCS>());
            foreach(Part c in controlPart.children)
                if(c.partInfo!=null && c.partInfo.name.StartsWith("MSL.SC."))controlRcs.AddRange(c.FindModulesImplementing<ModuleRCS>());
        }
        int Count(string name)
        {
            RefreshTopology();
            if(vessel==null)return 0;
            if(name=="MSL.EBMD")return cachedEntryMasses;
            if(name=="MSL.CBMD")return cachedCruiseMasses;
            int n=0;foreach(Part p in vessel.parts)if(Is(p,name))n++;return n;
        }
        public override void OnStart(StartState state)
        {
            base.OnStart(state);wanted=part.transform.rotation;
            if(!HighLogic.LoadedSceneIsFlight)return;
            GameEvents.onVesselWasModified.Add(InvalidateTopology);
            GameEvents.onVesselGoOffRails.Add(InvalidateTopology);
            FindChute();
            RefreshTopology();
        }

        [KSPEvent(guiActive=false,guiName="#MSL_Aeroshell_UI_Enable_guidance")]
        public void EnableGuidance()
        {
            if(Count("MSL.EBMD")!=6){status=MSL.Localization.Text.Get("#MSL_Aeroshell_UI_requires_six_entry_balance_masses");return;}
            guidanceEnabled=true;nextGuidance=0;rangeControl=false;
            // the bank command starts from the attitude flown now, so turning the guidance on does not first roll back to 0
            bankCommand=heldBank=CurrentBank();
        }
        [KSPEvent(guiActive=false,guiName="#MSL_Aeroshell_UI_Disable_guidance")]
        public void DisableGuidance(){guidanceEnabled=false;status=MSL.Localization.Text.Get("#MSL_Aeroshell_UI_off");Unhook();}
        [KSPAction("#MSL_Aeroshell_UI_Enable_MSL_entry_guidance")] public void EnableAction(KSPActionParam p){EnableGuidance();}
        [KSPAction("#MSL_Aeroshell_UI_Disable_MSL_entry_guidance")] public void DisableAction(KSPActionParam p){DisableGuidance();}
        [KSPEvent(guiActive=false,guiName="#MSL_Aeroshell_UI_Jettison_two_cruise_balance_masses")]
        public void JettisonCruiseMasses(){Release("MSL.CBMD");}
        [KSPEvent(guiActive=false,guiName="#MSL_Aeroshell_UI_Jettison_six_entry_balance_masses")]
        public void JettisonEntryMasses(){Release("MSL.EBMD");}
        [KSPAction("#MSL_Aeroshell_UI_Jettison_cruise_balance_masses")] public void CruiseAction(KSPActionParam p){JettisonCruiseMasses();}
        [KSPAction("#MSL_Aeroshell_UI_Jettison_entry_balance_masses")] public void EntryAction(KSPActionParam p){JettisonEntryMasses();}
        void Release(string name)
        {
            if(vessel==null)return;
            foreach(Part p in new List<Part>(vessel.parts))
                if(Is(p,name))
                {
                    ModuleAnchoredDecoupler d=p.FindModuleImplementing<ModuleAnchoredDecoupler>();
                    if(d!=null && !d.isDecoupled)StartCoroutine(ReleaseMass(p,d));
                }
        }
        System.Collections.IEnumerator ReleaseMass(Part mass,ModuleAnchoredDecoupler decoupler)
        {
            // The backshell colliders stay whole over the pockets; the mass may pass through them for 0.4 s, then collisions
            // are restored. Ejection and recoil use impulses, not teleportation.
            Collider[] own=mass.GetComponentsInChildren<Collider>();
            var shell=new List<Collider>();
            foreach(Part p in vessel.parts)if(p!=mass)shell.AddRange(p.GetComponentsInChildren<Collider>());
            foreach(Collider a in own)foreach(Collider b in shell)if(a!=null && b!=null)Physics.IgnoreCollision(a,b,true);
            Vector3 direction=-mass.transform.up;
            decoupler.ejectionForce=0;decoupler.Decouple();
            yield return new WaitForFixedUpdate();
            if(mass!=null && mass.Rigidbody!=null)
            {
                mass.Rigidbody.AddForce(direction*2.5f,ForceMode.VelocityChange);
                if(part.Rigidbody!=null)part.Rigidbody.AddForce(-direction*mass.Rigidbody.mass*2.5f,ForceMode.Impulse);
            }
            yield return new WaitForSeconds(.4f);
            foreach(Collider a in own)foreach(Collider b in shell)if(a!=null && b!=null)Physics.IgnoreCollision(a,b,false);
        }

        // Convenient starting target: predict 45-degree-bank range along the
        // current ground track; users can then edit latitude/longitude.
        [KSPEvent(guiActive=false,guiName="#MSL_Aeroshell_UI_Target_nominal_entry_endpoint")]
        public void SetNominalTarget()
        {
            if(vessel==null || vessel.srfSpeed<100)return;
            Vector3d up=(vessel.CoMD-vessel.mainBody.position).normalized;
            Vector3d along=Vector3d.Exclude(up,vessel.srf_velocity).normalized;
            double distance=PredictRange(45,EntryMass())+terminalRange,angle=distance/vessel.mainBody.Radius;
            Vector3d point=vessel.mainBody.position+vessel.mainBody.Radius*(up*Math.Cos(angle)+along*Math.Sin(angle));
            targetLatitude=(float)vessel.mainBody.GetLatitude(point,false);
            targetLongitude=(float)vessel.mainBody.GetLongitude(point,false);
        }

        // vessel mass without the cruise stage and its balance masses (kg): what enters the atmosphere
        double EntryMass()
        {
            double m=vessel.GetTotalMass()*1000;
            foreach(Part p in vessel.parts)
                if(Is(p,"MSL.CruiseStage") || Is(p,"MSL.CBMD") || (p.partInfo!=null && p.partInfo.name.StartsWith("MSL.CS.")))m-=(p.mass+p.GetResourceMass())*1000;
            return m;
        }
        // bank magnitude (deg) whose predicted range ends terminalRange short of the target
        float SolveBank(EntryRangeState prediction,double range)
        {
            double lo=0,hi=maximumBank;
            for(int i=0;i<7;i++)
            {
                double mid=(lo+hi)/2;
                if(EntryRangePredictor.Predict(prediction,mid,densityAtAltitude)>range-terminalRange)lo=mid;else hi=mid;
            }
            return (float)((lo+hi)/2);
        }
        // signed bank (deg) measured now: 0 = entry balance masses on top (lift up)
        public float CurrentBank()
        {
            if(vessel==null || vessel.srfSpeed<10)return 0;
            Vector3 velocity=((Vector3)vessel.srf_velocity).normalized;
            Vector3 liftUp=Vector3.ProjectOnPlane((Vector3)(vessel.CoMD-vessel.mainBody.position),velocity).normalized;
            return Vector3.SignedAngle(liftUp,Vector3.ProjectOnPlane(part.transform.TransformDirection(balanceAxis),velocity),velocity);
        }
        // Pre-bank (deg, signed) for the current target: the range-control solution for the entry mass, turned towards
        // the target side. Sets bankSign to that side so the guidance starts on it.
        public float PreBank()
        {
            if(vessel==null || vessel.srfSpeed<10)return 0;
            Vector3 velocity=((Vector3)vessel.srf_velocity).normalized;
            Vector3 up=((Vector3)(vessel.CoMD-vessel.mainBody.position)).normalized;
            Vector3 liftRight=Vector3.Cross(velocity,Vector3.ProjectOnPlane(up,velocity).normalized).normalized;
            Vector3d target=vessel.mainBody.GetWorldSurfacePosition(targetLatitude,targetLongitude,0);
            Vector3 targetUp=((Vector3)(target-vessel.mainBody.position)).normalized;
            double range=vessel.mainBody.Radius*Math.Acos(Math.Max(-1,Math.Min(1,Vector3.Dot(up,targetUp))));
            bankSign=Vector3.Dot(Vector3.ProjectOnPlane(targetUp,up).normalized,liftRight)>=0?1:-1;
            bankMagnitude=SolveBank(PredictionState(EntryMass()),range);
            return bankSign*bankMagnitude;
        }

        public double PredictRange(double bank)
        {return PredictRange(bank,vessel.GetTotalMass()*1000);}
        double PredictRange(double bank,double mass)
        {return EntryRangePredictor.Predict(PredictionState(mass),bank,densityAtAltitude);}
        double DensityAtAltitude(double altitude)
        {return predictionBody.GetDensity(predictionBody.GetPressure(altitude),predictionBody.GetTemperature(altitude));}
        EntryRangeState PredictionState(double mass)
        {
            CelestialBody body=vessel.mainBody;
            predictionBody=body;
            if(densityAtAltitude==null)densityAtAltitude=DensityAtAltitude;
            Vector3d up=(vessel.CoMD-body.position).normalized;
            double gamma=Math.Asin(Math.Max(-.99,Math.Min(.99,Vector3d.Dot(vessel.srf_velocity.normalized,up))));
            double ballast=0;
            foreach(Part p in vessel.parts)if(Is(p,"MSL.EBMD"))ballast+=(p.mass+p.GetResourceMass())*1000;
            return new EntryRangeState{Radius=body.Radius,GravityParameter=body.gravParameter,
                AtmosphereDepth=body.atmosphereDepth,Altitude=vessel.altitude,Speed=vessel.srfSpeed,
                Gamma=gamma,Mass=mass,Ballast=ballast,ReferenceArea=referenceArea,Cd=estimatedCd,
                LiftDrag=measuredLiftDrag,AutoRelease=autoEntryMassRelease,ReleaseSpeed=entryMassReleaseSpeed,HeadingSpeed=headingAlignmentSpeed};
        }

        // the parachute part on the mortar node, looked up again when the part count changes
        int chutePartCount=-1;
        void FindChute()
        {
            chutePartCount=vessel!=null?vessel.parts.Count:-1;
            Part c=MSL.Modules.ChutePart(part);
            chuteModule=MSL.Modules.Named(c,"RealChuteModule");
            chuteDeployed=chuteModule!=null?chuteModule.GetType().GetField("oneWasDeployed"):null;
            fallbackChute=c!=null?c.FindModuleImplementing<ModuleMSLParachute>():null;
        }
        bool ChuteOut()
        {
            if(vessel!=null && vessel.parts.Count!=chutePartCount)FindChute();
            if(fallbackChute!=null)return fallbackChute.oneWasDeployed;
            return chuteModule!=null && chuteDeployed!=null && (bool)chuteDeployed.GetValue(chuteModule);
        }

        // Control-panel state: the guidance has no part-menu rows; MSLStatusWindow draws its controls from
        // these. panelEntry: heat shield on, canopy not out (guidance applies); panelCruiseMasses / panelEntryMasses: on board.
        [NonSerialized] public bool panelEntry, panelCruiseMasses, panelEntryMasses;
        void Update()
        {
            if(!HighLogic.LoadedSceneIsFlight || vessel==null)return;
            AttachNode hs=part.FindAttachNode("top");
            panelEntry=hs!=null && hs.attachedPart!=null && !ChuteOut();
            panelEntryMasses=Count("MSL.EBMD")>0;panelCruiseMasses=Count("MSL.CBMD")>0;
        }

        void FixedUpdate()
        {
            if(!HighLogic.LoadedSceneIsFlight || vessel==null)return;
            entryMasses=Count("MSL.EBMD");
            if(!guidanceEnabled){Unhook();return;}
            // The parachute may be deployed by a player or an action group. Stop
            // steering as soon as the canopy is out, including after reload.
            if(ChuteOut())
            {
                DisableGuidance();vessel.ActionGroups.SetGroup(KSPActionGroup.RCS,false);
                status=MSL.Localization.Text.Get("#MSL_Aeroshell_UI_complete_parachute_deployed");return;
            }
            AttachNode hs=part.FindAttachNode("top");
            if(hs==null || hs.attachedPart==null){DisableGuidance();status=MSL.Localization.Text.Get("#MSL_Aeroshell_UI_complete_heat_shield_separated");return;}
            if(controlPart==null){status=MSL.Localization.Text.Get("#MSL_Aeroshell_UI_no_descent_stage_RCS");Unhook();return;}
            if(controlRcs.Count==0){status=MSL.Localization.Text.Get("#MSL_Aeroshell_UI_no_RCS_module");Unhook();return;}
            for(int i=0;i<controlRcs.Count;i++)controlRcs[i].rcsEnabled=true;
            // SetReferenceTransform fires onVesselReferenceTransformSwitch on every call.
            if(vessel.GetReferenceTransformPart()!=controlPart)vessel.SetReferenceTransform(controlPart);
            if(vessel.ActionGroups[KSPActionGroup.SAS])vessel.ActionGroups.SetGroup(KSPActionGroup.SAS,false);
            if(!vessel.ActionGroups[KSPActionGroup.RCS])vessel.ActionGroups.SetGroup(KSPActionGroup.RCS,true);
            if(hooked!=vessel){Unhook();hooked=vessel;hooked.OnFlyByWire+=Steer;}
            if(vessel.srfSpeed<10)return;
            Vector3 velocity=((Vector3)vessel.srf_velocity).normalized;
            Vector3 up=((Vector3)(vessel.CoMD-vessel.mainBody.position)).normalized;
            // Calibrate the predictor from the installed aero system (measured forces only). Cd is taken against the
            // predictor's own density model, so predicted drag equals measured drag here.
            predictionBody=vessel.mainBody;
            double modelQ=.5*DensityAtAltitude(vessel.altitude)*vessel.srfSpeed*vessel.srfSpeed;
            if(haveVelocity && !vessel.packed && vessel.dynamicPressurekPa>.5 && vessel.srfSpeed>1100 && entryMasses>0)
            {
                double gravity=vessel.mainBody.gravParameter/(vessel.CoMD-vessel.mainBody.position).sqrMagnitude;
                Vector3 acceleration=(Vector3)((vessel.srf_velocity-previousVelocity)/TimeWarp.fixedDeltaTime)+up*(float)gravity;
                float drag=-Vector3.Dot(acceleration,velocity);
                if(drag>8)
                {
                    float cd=Mathf.Clamp((float)(drag*vessel.GetTotalMass()*1000/(Math.Max(1,modelQ)*referenceArea)),.6f,3.5f);
                    float ld=Mathf.Clamp(Vector3.ProjectOnPlane(acceleration,velocity).magnitude/drag,.04f,.6f);
                    float blend=Mathf.Clamp01(TimeWarp.fixedDeltaTime/2f);
                    estimatedCd=Mathf.Lerp(estimatedCd,cd,blend);measuredLiftDrag=Mathf.Lerp(measuredLiftDrag,ld,blend);
                }
            }
            previousVelocity=vessel.srf_velocity;haveVelocity=true;
            Vector3 liftUp=Vector3.ProjectOnPlane(up,velocity).normalized;
            Vector3 liftRight=Vector3.Cross(velocity,liftUp).normalized;
            measuredAoA=Vector3.Angle(part.transform.up,velocity);
            measuredBank=Vector3.SignedAngle(liftUp,Vector3.ProjectOnPlane(part.transform.TransformDirection(balanceAxis),velocity),velocity);
            if(autoCruiseMassRelease && Count("MSL.CBMD")>0 && Count("MSL.CruiseStage")==0)
            {
                double now=Planetarium.GetUniversalTime();
                bool calm=Mathf.Abs(Mathf.DeltaAngle(measuredBank,bankCommand))<5 && part.Rigidbody!=null && part.Rigidbody.angularVelocity.magnitude*Mathf.Rad2Deg<.5f;
                if(!calm)settledSince=-1;else if(settledSince<0)settledSince=now;
                double ei=Math.Min(125000,vessel.mainBody.atmosphereDepth);
                if(settledSince>=0 && now-settledSince>=5 || vessel.altitude<ei+150000){JettisonCruiseMasses();settledSince=-1;}
            }
            Vector3d target=vessel.mainBody.GetWorldSurfacePosition(targetLatitude,targetLongitude,0);
            Vector3 targetUp=((Vector3)(target-vessel.mainBody.position)).normalized;
            double range=vessel.mainBody.Radius*Math.Acos(Math.Max(-1,Math.Min(1,Vector3.Dot(up,targetUp))));
            rangeKm=(float)(range/1000);
            Vector3 targetHeading=Vector3.ProjectOnPlane(targetUp,up).normalized;
            Vector3 heading=Vector3.ProjectOnPlane(velocity,up).normalized;
            float headingError=Vector3.SignedAngle(heading,targetHeading,up);
            double crossrange=range*Vector3.Dot(targetHeading,liftRight);
            if(haveHeadingError)
                headingErrorRate=Mathf.Lerp(headingErrorRate,Mathf.DeltaAngle(previousHeadingError,headingError)/TimeWarp.fixedDeltaTime,Mathf.Clamp01(TimeWarp.fixedDeltaTime));
            previousHeadingError=headingError;haveHeadingError=true;
            if(Planetarium.GetUniversalTime()>=nextGuidance && vessel.srfSpeed>headingAlignmentSpeed)
            {
                nextGuidance=Planetarium.GetUniversalTime()+1;
                EntryRangeState prediction=PredictionState(vessel.GetTotalMass()*1000);
                bankMagnitude=SolveBank(prediction,range);
                predictedKm=(float)(EntryRangePredictor.Predict(prediction,bankMagnitude,densityAtAltitude)/1000);
                double corridor=vessel.mainBody.Radius*(reversalDeadbandConst+reversalDeadbandQuad*vessel.srfSpeed*vessel.srfSpeed)*Math.PI/180*(bankReversals==0?firstReversalScale:1);
                int sign=crossrange>=0?1:-1;
                if(Math.Abs(crossrange)>corridor && sign!=bankSign){bankSign=sign;bankReversals++;}
            }
            bool crossrangeGrowing=Math.Abs(crossrange)>Math.Abs(previousCrossrange);previousCrossrange=crossrange;
            float targetBank=bankSign*(crossrangeGrowing?Mathf.Max(bankMagnitude,minimumBankGrowing):bankMagnitude);
            status=MSL.Localization.Text.Get("#MSL_Aeroshell_UI_range_control_RCS");
            if(!rangeControl)
            {
                double dragAccel=modelQ*referenceArea*estimatedCd/(vessel.GetTotalMass()*1000);
                if(dragAccel>=rangeControlDrag)rangeControl=true;
                else status=MSL.Localization.Text.Get("#MSL_Aeroshell_UI_entry_attitude_pre_bank_RCS");
            }
            if(vessel.srfSpeed<=headingAlignmentSpeed){targetBank=Mathf.Clamp(-headingAlignmentGain*(headingError+headingAlignmentLead*headingErrorRate),-headingAlignmentMaxBank,headingAlignmentMaxBank);status=MSL.Localization.Text.Get("#MSL_Aeroshell_UI_heading_alignment_RCS");}
            if(manualBank){targetBank=manualBankAngle;status=MSL.Localization.Text.Get("#MSL_Aeroshell_UI_manual_bank_RCS");}
            if(autoEntryMassRelease && entryMasses>0 && Count("MSL.CBMD")==0 && vessel.srfSpeed<entryMassReleaseSpeed)JettisonEntryMasses();
            if(manualBank || Mathf.Abs(targetBank-heldBank)>=bankCommandStep)heldBank=targetBank;
            // linear, not the shortest arc: a reversal between banks past 90 deg goes over the top (through lift up)
            bankCommand=Mathf.MoveTowards(bankCommand,heldBank,bankSlewRate*TimeWarp.fixedDeltaTime);
            float aoa=entryMasses>0 && Count("MSL.CBMD")==0?trimAoA:0;
            Vector3 lift=Quaternion.AngleAxis(bankCommand,velocity)*liftUp;
            Vector3 nose=velocity*Mathf.Cos(aoa*Mathf.Deg2Rad)-lift*Mathf.Sin(aoa*Mathf.Deg2Rad);
            Quaternion baseRot=Quaternion.FromToRotation(part.transform.up,nose)*part.transform.rotation;
            Vector3 side=Vector3.ProjectOnPlane(baseRot*balanceAxis,nose).normalized;
            Vector3 targetSide=Vector3.ProjectOnPlane(lift,nose).normalized;
            wanted=Quaternion.AngleAxis(Vector3.SignedAngle(side,targetSide,nose),nose)*baseRot;
        }

        void Steer(FlightCtrlState state)
        {
            if(!guidanceEnabled || controlPart==null || part.Rigidbody==null)return;
            Quaternion error=wanted*Quaternion.Inverse(part.transform.rotation);
            float angle;Vector3 axis;error.ToAngleAxis(out angle,out axis);if(angle>180)angle-=360;
            if(float.IsNaN(axis.x))axis=Vector3.zero;
            Vector3 rate=part.Rigidbody.angularVelocity*Mathf.Rad2Deg;
            Vector3 errorVector=axis*angle;
            bool aero=vessel.dynamicPressurekPa>aeroControlQ;
            Vector3 roll=part.transform.up;
            Vector3 damp=Vector3.zero;
            if(aero)
            {
                // bank only: the error and rate about the capsule axis; the rest is damped, not steered
                Vector3 perpRate=Vector3.ProjectOnPlane(rate,roll);
                if(perpRate.magnitude>dampRateDeadband)damp=-perpRate*(1-dampRateDeadband/perpRate.magnitude);
                errorVector=roll*Vector3.Dot(errorVector,roll);
                rate=roll*Vector3.Dot(rate,roll);
            }
            angle=errorVector.magnitude;axis=angle>1e-4f?errorVector/angle:Vector3.zero;
            // vacuum under physics warp: long physics steps make the RCS chatter, so attitude is left alone until 1x warp.
            // Only above the atmosphere: below it the flight path turns away under a held attitude.
            if(!aero && TimeWarp.CurrentRate>1.01f && vessel.altitude>vessel.mainBody.atmosphereDepth){commandMagnitude=0;return;}
            if(angle>=attitudeDeadband)steeringActive=true;else if(angle<attitudeSettle)steeringActive=false;
            if(!steeringActive && rate.magnitude<rateDeadband && damp==Vector3.zero)
            {commandMagnitude=0;return;}
            float stop=Mathf.Sqrt(2*rcsAngularAccel*angle)*.8f;
            Vector3 desiredRate=!steeringActive?Vector3.zero:axis*Mathf.Min(maxBodyRate,Mathf.Min(angle*.7f,stop));
            Vector3 correction=desiredRate-rate+damp;
            if(correction.magnitude<rateDeadband)correction=damp;
            Vector3 command=-(Quaternion.Inverse(vessel.ReferenceTransform.rotation)*correction);
            commandMagnitude=Mathf.Min(1,command.magnitude);
            state.pitch=Mathf.Clamp(command.x,-1,1);state.roll=Mathf.Clamp(command.y,-1,1);state.yaw=Mathf.Clamp(command.z,-1,1);
        }
        void Unhook(){if(hooked!=null)hooked.OnFlyByWire-=Steer;hooked=null;}
        void OnDestroy()
        {
            GameEvents.onVesselWasModified.Remove(InvalidateTopology);
            GameEvents.onVesselGoOffRails.Remove(InvalidateTopology);
            Unhook();
        }
    }
}
