// Robotic-arm science sequences on the articulated rig:
//   unstow / stow
//   MAHLI     survey image at 25 cm, close-up at 5 cm standoff (no contact)
//   APXS      contact placement, integration
//   DRT       contact placement, brushing
//   drill     preload, rotary-percussive drilling, retract, CHIMRA sieving, portion drop into CheMin, then SAM
// Every tool is placed from one elbow-up pose family, the turret turning the tool to point down. The approach stops
// 10 cm above the ground; the descent is straight down at fixed tool pitch and ends on contact (or the MAHLI
// standoff), so it follows the real terrain. Times are GAME.
using System.Collections;
using UnityEngine;
namespace MSLCuriosity
{
    public partial class ModuleCuriosityRig
    {
        static readonly string[] armJoints={"Arm_Azimuth","Arm_Elevation","Arm_Elbow","Arm_Wrist","Arm_Turret"};
        const int AZ=0,EL=1,EB=2,WR=3,TU=4;
        // shoulder azimuth, shoulder elevation, elbow, wrist, turret (hinge deg; 0 = working pose = ready)
        static readonly float[] PoseReady={0,0,0,0,0};
        // part.cfg touchRotation, elbow 3 deg open (at -70 the wrist tube sits inside the body)
        static readonly float[] PoseStow={-90,-15,-67,89,-32};
        // stow <-> ready waypoint, so the upper arm swings clear of the body's front face
        static readonly float[] PoseStowVia={-15,-15,0,4,-2};
        class Placement{public float[] pose;public float[] perCm;public Placement(float[] p,float[] d){pose=p;perCm=d;}}
        // tool down, contact point 10 cm (MAHLI 25 cm) above flat ground 2.25 m ahead of the rover origin;
        // perCm: elevation, elbow, wrist per cm straight down (pitch el+elbow+wrist unchanged)
        static readonly Placement PlaceMahli=new Placement(new float[]{0,-30,167.5f,125,-58.9f},new float[]{.524f,-.148f,-.375f});
        static readonly Placement PlaceApxs=new Placement(new float[]{0,-20,160,127.5f,50.4f},new float[]{.566f,-.299f,-.267f});
        static readonly Placement PlaceDrt=new Placement(new float[]{0,-25,165,122.5f,-109.9f},new float[]{.55f,-.229f,-.321f});
        static readonly Placement PlaceDrill=new Placement(new float[]{0,-20,162.5f,125,-175},new float[]{.577f,-.314f,-.263f});
        // CHIMRA portion end pointing down 8 cm over the deck inlets
        static readonly float[] PoseCheMin={28,-77,-8,-27.5f,108.9f};
        static readonly float[] PoseSam={47.8f,-79.8f,-10,-3,110.4f};
        // CHIMRA sieving: wrist / turret orientations from the ready pose (GAME sequence; ground and body clear)
        static readonly float[][] PoseSieve={new float[]{0,0,0,90,90},new float[]{0,0,0,90,-90},new float[]{0,0,0,30,175},new float[]{0,0,0,120,-175}};
        // joint groups moved one after the other (off the target the elbow lifts first)
        static readonly int[][] AllTogether={new[]{AZ,EL,EB,WR,TU}};
        static readonly int[][] OffTarget={new[]{EB},new[]{EL},new[]{AZ,WR,TU}};
        static readonly int[][] OffCheMin={new[]{EL},new[]{WR,TU},new[]{EB},new[]{AZ}};

        [KSPField(guiActive=true,guiActiveEditor=false,groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true,guiName="#MSL_Curiosity_UI_Arm_task")] public string armTask="";
        [KSPField] public float armTolerance=1.5f;      // deg: joint at its target
        [KSPField] public float armMoveSlack=8f;        // s on top of the travel time before a move counts as blocked
        [KSPField] public float descentSpeed=1.5f;      // cm/s [GAME]
        [KSPField] public float descentMax=30f;         // cm below the approach pose without contact: no target there
        [KSPField] public float mahliStandoff=.05f;     // m, model scale (MAHLI close-up standoff)
        [KSPField] public float apxsTime=20f,brushTime=10f,sieveDwell=2f,deliverDwell=3f;   // s [GAME]
        [KSPField] public bool armAutoScience=true;     // MAHLI / APXS run themselves at their step
        IEnumerator armOp;
        string armOpName,armFail;
        public bool ArmBusy{get{return armOp!=null;}}

        float ArmField(int j){switch(j){case AZ:return shoulderAz;case EL:return shoulderEl;case EB:return elbow;case WR:return wrist;default:return turret;}}
        void SetArmField(int j,float v)
        {
            Link l;if(byName.TryGetValue(armJoints[j],out l))v=Mathf.Clamp(v,l.lo,l.hi);
            switch(j){case AZ:shoulderAz=v;break;case EL:shoulderEl=v;break;case EB:elbow=v;break;case WR:wrist=v;break;default:turret=v;break;}
        }
        Link ArmLink(int j){Link l;return byName.TryGetValue(armJoints[j],out l)?l:null;}
        // hold every arm joint where it is now
        void FreezeArm(){for(int j=0;j<5;j++){Link l=ArmLink(j);if(l!=null)SetArmField(j,l.angle);}}
        void ResetArmRates(){foreach(Link l in links)l.rateScale=1f;}

        bool StartArmOp(string name,IEnumerator op)
        {
            if(!running||armOp!=null||ArmLink(TU)==null)return false;
            if(vessel.srfSpeed>.05){ScreenMessages.PostScreenMessage(MSL.Localization.Text.Get("#MSL_Curiosity_UI_arm_need_still"),5f);return false;}
            armOpName=name;armFail=null;armOp=op;return true;
        }
        void ArmOps()
        {
            if(armOp==null){armTask=MSL.Localization.Text.Get("#MSL_Curiosity_UI_arm_idle");return;}
            bool more;
            try{more=armOp.MoveNext();}
            catch(System.Exception e){Debug.LogException(e);armFail="#MSL_Curiosity_UI_arm_blocked";more=false;}
            if(more)return;
            string done=armFail==null?KSP.Localization.Localizer.Format("#MSL_Curiosity_UI_arm_done",armOpName):MSL.Localization.Text.Get(armFail);
            if(armFail!=null)FreezeArm();
            ScreenMessages.PostScreenMessage(done,5f);
            EndArmOp();
        }
        void EndArmOp(){armOp=null;drillT=-1f;drillAnim=DRILL_OFF;ResetArmRates();}
        bool Fail(string key){armFail=key;return false;}

        // Move to a pose, one joint group after the other; the joints of a group are rate-scaled so they arrive together
        // (the planned path). The ground stopping the turret, or a joint not there in time, aborts.
        IEnumerator Move(float[] pose,int[][] order,string where)
        {
            armTask=KSP.Localization.Localizer.Format("#MSL_Curiosity_UI_step_move",MSL.Localization.Text.Get(where));
            foreach(int[] g in order)
            {
                float max=0;
                foreach(int j in g){Link l=ArmLink(j);if(l!=null)max=Mathf.Max(max,Mathf.Abs(Mathf.Clamp(pose[j],l.lo,l.hi)-l.angle));}
                foreach(int j in g){Link l=ArmLink(j);if(l==null)continue;float d=Mathf.Abs(Mathf.Clamp(pose[j],l.lo,l.hi)-l.angle);l.rateScale=max>.01f?Mathf.Max(.05f,d/max):1f;SetArmField(j,pose[j]);}
                float limit=max/jointRate+armMoveSlack;
                for(float t=0;;t+=Time.fixedDeltaTime)
                {
                    bool there=true;
                    foreach(int j in g)
                    {
                        Link l=ArmLink(j);if(l==null)continue;
                        if(Mathf.Abs(l.angle-ArmField(j))>armTolerance)there=false;
                    }
                    if(there)break;
                    if(t>limit||armBlockedT>.5f){Fail("#MSL_Curiosity_UI_arm_blocked");yield break;}
                    yield return null;
                }
            }
            ResetArmRates();
        }
        IEnumerator Dwell(float seconds,string key)
        {
            for(float t=0;t<seconds;t+=Time.fixedDeltaTime){armTask=KSP.Localization.Localizer.Format(key,Mathf.CeilToInt(seconds-t));yield return null;}
        }
        // Straight down at fixed tool pitch until the ground stops the turret (standoff < 0) or the turret-to-ground gap
        // is the standoff (model m).
        IEnumerator Descend(Placement p,float standoff,string tool)
        {
            float scale=Mathf.Max(.01f,part.rescaleFactor),down=0;
            float[] start={ArmField(EL),ArmField(EB),ArmField(WR)};
            while(true)
            {
                armTask=KSP.Localization.Localizer.Format("#MSL_Curiosity_UI_step_descend",tool,down.ToString("F1"));
                if(armBlocked)break;
                if(standoff>=0){float c=TurretClearance();if(c>=0 && c<=standoff*scale)break;}
                if(vessel.srfSpeed>.05){Fail("#MSL_Curiosity_UI_arm_need_still");yield break;}
                if(down>=descentMax){armFail="#MSL_Curiosity_UI_arm_no_ground";yield break;}
                float lag=0;
                for(int j=EL;j<=WR;j++){Link l=ArmLink(j);if(l!=null)lag=Mathf.Max(lag,Mathf.Abs(l.angle-ArmField(j)));}
                if(lag<armTolerance)
                {
                    down=Mathf.Min(descentMax,down+descentSpeed*Time.fixedDeltaTime);
                    for(int j=EL;j<=WR;j++)SetArmField(j,start[j-EL]+p.perCm[j-EL]*down/scale);
                }
                yield return null;
            }
            FreezeArm();
            for(int i=0;i<25;i++)yield return null;   // settle on the target
        }
        IEnumerator GoReady()
        {
            if(AtPose(PoseReady))yield break;
            Link eb=ArmLink(EB),az=ArmLink(AZ);
            IEnumerator m;
            // from the stow (shoulder turned toward the body) through the waypoint
            if(az!=null&&az.angle<-45f){m=Move(PoseStowVia,AllTogether,"#MSL_Curiosity_UI_pose_ready");while(m.MoveNext())yield return null;if(armFail!=null)yield break;}
            // elbow-up placement family: lift the elbow off the target first
            m=Move(PoseReady,eb!=null&&eb.angle>100f?OffTarget:AllTogether,"#MSL_Curiosity_UI_pose_ready");
            while(m.MoveNext())yield return null;
        }
        bool AtPose(float[] pose){for(int j=0;j<5;j++){Link l=ArmLink(j);if(l!=null&&Mathf.Abs(l.angle-Mathf.Clamp(pose[j],l.lo,l.hi))>armTolerance)return false;}return true;}
        void RunExperiment(string id)
        {
            if(!armAutoScience)return;
            foreach(ModuleCuriosityExperiment m in vessel.FindPartModulesImplementing<ModuleCuriosityExperiment>())
                if(m.experimentID==id){if(m.GetData().Length==0)m.DeployExperiment();return;}
        }

        IEnumerator OpStow()
        {
            IEnumerator m=GoReady();while(m.MoveNext())yield return null;if(armFail!=null)yield break;
            m=Move(PoseStowVia,AllTogether,"#MSL_Curiosity_UI_pose_stow");while(m.MoveNext())yield return null;if(armFail!=null)yield break;
            m=Move(PoseStow,AllTogether,"#MSL_Curiosity_UI_pose_stow");while(m.MoveNext())yield return null;
        }
        IEnumerator OpUnstow(){IEnumerator m=GoReady();while(m.MoveNext())yield return null;}
        // ready -> approach -> descend -> work -> back to the approach pose -> ready
        IEnumerator Placed(Placement p,float standoff,string tool,IEnumerator work)
        {
            IEnumerator m=GoReady();while(m.MoveNext())yield return null;if(armFail!=null)yield break;
            m=Move(p.pose,AllTogether,"#MSL_Curiosity_UI_pose_target");while(m.MoveNext())yield return null;if(armFail!=null)yield break;
            m=Descend(p,standoff,tool);while(m.MoveNext())yield return null;
            if(armFail==null)while(work.MoveNext())yield return null;
            string failed=armFail;armFail=null;
            m=Move(p.pose,AllTogether,"#MSL_Curiosity_UI_pose_target");while(m.MoveNext())yield return null;
            if(armFail==null){m=Move(PoseReady,OffTarget,"#MSL_Curiosity_UI_pose_ready");while(m.MoveNext())yield return null;}
            if(failed!=null)armFail=failed;
        }
        IEnumerator OpMahli()
        {
            // survey at the approach height (25 cm), then the close-up standoff
            IEnumerator m=GoReady();while(m.MoveNext())yield return null;if(armFail!=null)yield break;
            m=Move(PlaceMahli.pose,AllTogether,"#MSL_Curiosity_UI_pose_target");while(m.MoveNext())yield return null;if(armFail!=null)yield break;
            m=Dwell(3f,"#MSL_Curiosity_UI_step_survey");while(m.MoveNext())yield return null;
            m=Descend(PlaceMahli,mahliStandoff,"MAHLI");while(m.MoveNext())yield return null;
            if(armFail==null)
            {
                RunExperiment("MSL_MAHLI");
                m=Dwell(3f,"#MSL_Curiosity_UI_step_closeup");while(m.MoveNext())yield return null;
            }
            string failed=armFail;armFail=null;
            m=Move(PlaceMahli.pose,AllTogether,"#MSL_Curiosity_UI_pose_target");while(m.MoveNext())yield return null;
            if(armFail==null){m=Move(PoseReady,OffTarget,"#MSL_Curiosity_UI_pose_ready");while(m.MoveNext())yield return null;}
            if(failed!=null)armFail=failed;
        }
        IEnumerator ApxsWork()
        {
            IEnumerator m=Dwell(apxsTime,"#MSL_Curiosity_UI_step_apxs");
            while(m.MoveNext()){if(!ArmPlaced){armFail="#MSL_Curiosity_UI_arm_lost";yield break;}yield return null;}
            RunExperiment("MSL_APXS");
        }
        IEnumerator BrushWork()
        {
            IEnumerator m=Dwell(brushTime,"#MSL_Curiosity_UI_step_brush");
            drillT=0f;   // the DRT motor sound
            while(m.MoveNext())yield return null;
            drillT=-1f;
        }
        // drilled biome, delivered to the inlets at the end of the campaign
        string drilledBody,drilledBiome;
        IEnumerator DrillWork()
        {
            drillT=0f;drillAnim=DRILL_RUN;
            while(drillT<drillTime)
            {
                double need=drillECs*Time.fixedDeltaTime;
                if(!ArmPlaced||vessel.srfSpeed>.1||part.RequestResource("ElectricCharge",need)<need*.95){drillT=-1f;armFail="#MSL_Curiosity_UI_drill_lost";drillAnim=DRILL_OFF;yield break;}
                drillT+=Time.fixedDeltaTime;
                armTask=KSP.Localization.Localizer.Format("#MSL_Curiosity_UI_drilling",Mathf.CeilToInt(drillTime-drillT));
                yield return null;
            }
            drillT=-1f;drillAnim=DRILL_OFF;drilledBody=vessel.mainBody.name;drilledBiome=Biome(vessel);
        }
        IEnumerator OpDrill()
        {
            drilledBody=null;
            IEnumerator m;
            // the arm already on the target (placed by hand): drill there
            if(ArmPlaced)
            {
                m=DrillWork();while(m.MoveNext())yield return null;if(armFail!=null)yield break;
                m=Move(PoseReady,OffTarget,"#MSL_Curiosity_UI_pose_ready");while(m.MoveNext())yield return null;   // elbow first: off the surface before the rest swings
            }
            else{m=Placed(PlaceDrill,-1f,"Drill",DrillWork());while(m.MoveNext())yield return null;}
            if(armFail!=null||drilledBody==null)yield break;
            for(int k=0;k<PoseSieve.Length;k++)
            {
                m=Move(PoseSieve[k],AllTogether,"#MSL_Curiosity_UI_pose_sieve");while(m.MoveNext())yield return null;if(armFail!=null)yield break;
                m=Dwell(sieveDwell,"#MSL_Curiosity_UI_step_sieve");drillT=0f;drillAnim=DRILL_HAMMER;while(m.MoveNext())yield return null;drillT=-1f;drillAnim=DRILL_OFF;
            }
            m=Move(PoseReady,AllTogether,"#MSL_Curiosity_UI_pose_ready");while(m.MoveNext())yield return null;if(armFail!=null)yield break;
            m=Move(PoseCheMin,AllTogether,"#MSL_Curiosity_UI_pose_chemin");while(m.MoveNext())yield return null;if(armFail!=null)yield break;
            m=Dwell(deliverDwell,"#MSL_Curiosity_UI_step_deliver_chemin");while(m.MoveNext())yield return null;
            m=Move(PoseReady,OffCheMin,"#MSL_Curiosity_UI_pose_ready");while(m.MoveNext())yield return null;if(armFail!=null)yield break;
            m=Move(PoseSam,AllTogether,"#MSL_Curiosity_UI_pose_sam");while(m.MoveNext())yield return null;if(armFail!=null)yield break;
            m=Dwell(deliverDwell,"#MSL_Curiosity_UI_step_deliver_sam");while(m.MoveNext())yield return null;
            sampleBody=drilledBody;sampleBiome=drilledBiome;
            ScreenMessages.PostScreenMessage(KSP.Localization.Localizer.Format("#MSL_Curiosity_UI_drill_done",sampleBiome),5f);
            m=Move(PoseReady,AllTogether,"#MSL_Curiosity_UI_pose_ready");while(m.MoveNext())yield return null;
        }

        [KSPEvent(guiActive=true,guiName="#MSL_Curiosity_UI_Arm_unstow",groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true)]
        public void ArmUnstow(){StartArmOp(MSL.Localization.Text.Get("#MSL_Curiosity_UI_Arm_unstow"),OpUnstow());}
        [KSPEvent(guiActive=true,guiName="#MSL_Curiosity_UI_Arm_stow",groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true)]
        public void ArmStow(){StartArmOp(MSL.Localization.Text.Get("#MSL_Curiosity_UI_Arm_stow"),OpStow());}
        [KSPEvent(guiActive=true,guiName="#MSL_Curiosity_UI_Arm_mahli",groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true)]
        public void ArmMahli(){StartArmOp(MSL.Localization.Text.Get("#MSL_Curiosity_UI_Arm_mahli"),OpMahli());}
        [KSPEvent(guiActive=true,guiName="#MSL_Curiosity_UI_Arm_apxs",groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true)]
        public void ArmApxs(){StartArmOp(MSL.Localization.Text.Get("#MSL_Curiosity_UI_Arm_apxs"),Placed(PlaceApxs,-1f,"APXS",ApxsWork()));}
        [KSPEvent(guiActive=true,guiName="#MSL_Curiosity_UI_Arm_drt",groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true)]
        public void ArmDrt(){StartArmOp(MSL.Localization.Text.Get("#MSL_Curiosity_UI_Arm_drt"),Placed(PlaceDrt,-1f,"DRT",BrushWork()));}
        [KSPEvent(guiActive=true,guiName="#MSL_Curiosity_UI_Drill_sample",groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true)]
        public void DrillSample(){StartArmOp(MSL.Localization.Text.Get("#MSL_Curiosity_UI_Drill_sample"),OpDrill());}
        [KSPEvent(guiActive=true,guiName="#MSL_Curiosity_UI_Arm_abort",groupName="arm",groupDisplayName="#MSL_Curiosity_UI_grp_Arm",groupStartCollapsed=true)]
        public void ArmAbort(){if(armOp==null)return;EndArmOp();FreezeArm();ScreenMessages.PostScreenMessage(MSL.Localization.Text.Get("#MSL_Curiosity_UI_arm_aborted"),5f);}
        static readonly string[] armOpEvents={"ArmUnstow","ArmStow","ArmMahli","ArmApxs","ArmDrt","DrillSample"};
        void UpdateArmUI()
        {
            bool arm=running&&byName.ContainsKey("Arm_Turret");
            foreach(string e in armOpEvents)Events[e].guiActive=arm&&armOp==null;
            Events["ArmAbort"].guiActive=arm&&armOp!=null;
            Fields["armTask"].guiActive=arm&&armOp!=null;   // idle: the buttons say it
        }

        // ---- turret contact geometry: a core sphere plus one sphere on each tool's contact point (the box hull made placed
        // tools hover). Turret frame (x, y, z) of the model is (x, z, y) in Unity; model metres.
        static readonly Vector3 turretCore=new Vector3(0,.155f,0);
        const float turretCoreR=.2f,turretToolR=.04f;
        static readonly Vector3[] turretTools={U(0,-.25f,.149f),U(.192f,.159f,.174f),U(-.207f,.125f,.178f),U(-.272f,-.098f,.192f),U(.268f,-.090f,.145f)};
        static Vector3 U(float x,float y,float z){return new Vector3(x,z,y);}
    }
}
