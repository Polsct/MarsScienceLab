// Rover science experiment that needs the rig in the right state:
//   requires = arm     MAHLI, APXS: arm turret placed on the surface (ModuleCuriosityRig.ArmPlaced)
//   requires = mast    ChemCam, Mastcam: remote sensing mast standing
//   requires = sample  SAM, CheMin: a drill sample from the biome the rover is in (ModuleCuriosityRig.DrillSample)
// Otherwise it is the stock ModuleScienceExperiment. Its DeployExperiment / DeployAction are not virtual, so OnStart
// points the part-menu event and the action-group action (the stock KSPEvent / KSPAction, names and flags unchanged)
// at the gated methods below. Other mods calling DeployExperimentExternal bypass the gate.
using System.Reflection;
using UnityEngine;
namespace MSLCuriosity
{
    public class ModuleCuriosityExperiment : ModuleScienceExperiment
    {
        [KSPField] public string requires="";
        ModuleCuriosityRig Rig()
        {
            if(vessel==null)return null;
            return vessel.FindPartModuleImplementing<ModuleCuriosityRig>();
        }
        // null when the experiment may run, else the message
        public string Blocked()
        {
            if(requires==""||!HighLogic.LoadedSceneIsFlight)return null;
            ModuleCuriosityRig rig=Rig();
            string title=experiment!=null?experiment.experimentTitle:experimentID;
            bool ok=rig!=null && (requires=="arm"?rig.ArmPlaced:requires=="mast"?rig.MastUp:requires=="sample"?rig.HasSample:true);
            if(ok)return null;
            return KSP.Localization.Localizer.Format("#MSL_Curiosity_UI_need_"+requires,title);
        }
        bool Gate()
        {
            string why=Blocked();
            if(why==null)return true;
            ScreenMessages.PostScreenMessage(why,5f,ScreenMessageStyle.UPPER_CENTER);
            return false;
        }
        public new void DeployExperiment(){if(Gate())base.DeployExperiment();}
        public new void DeployAction(KSPActionParam p){if(Gate())base.DeployAction(p);}
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if(requires=="")return;
            const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic;
            FieldInfo ev=typeof(BaseEvent).GetField("<onEvent>k__BackingField",F),ac=typeof(BaseAction).GetField("<onEvent>k__BackingField",F);
            if(ev!=null && Events.Contains("DeployExperiment"))ev.SetValue(Events["DeployExperiment"],new BaseEventDelegate(DeployExperiment));
            BaseAction a=Actions["DeployAction"];
            if(ac!=null && a!=null)ac.SetValue(a,new BaseActionDelegate(DeployAction));
            if(ev==null||ac==null)Debug.LogWarning("[MSLCuriosity] "+experimentID+": could not gate the stock deploy event/action");
        }
        public override string GetInfo()
        {
            string info=base.GetInfo();
            if(requires!="")info+="\n<color=#ffa500>"+MSL.Localization.Text.Get("#MSL_Curiosity_UI_req_"+requires)+"</color>";
            return info;
        }
    }
}
