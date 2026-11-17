// MMRTG power decay: the generator's output scales by 0.5^(age / 14.1 years) (about 4.8 % a year, thermocouple
// wear on top of Pu-238 decay), age counted from launch. Depends on universal time only, so nothing is caught up.
// Kerbalism replaces the generator with its own _RTG process and removes this module.
using System;
using UnityEngine;
namespace MSLCuriosity
{
    public class ModuleCuriosityRTG : PartModule
    {
        // Half-life of the electrical output, Earth years (365.25 d)
        [KSPField] public double halfLifeYears=14.1;
        // Universal time of the first flight start; -1 in the editor and in craft files
        [KSPField(isPersistant=true)] public double startUT=-1;
        [KSPField(guiActive=true, guiActiveEditor=false, guiName="#MSL_Curiosity_UI_RTG_output")] public string output="";
        const double Year=365.25*86400;
        ModuleGenerator gen;
        double[] baseRate;
        float shown=-1;

        public double Fraction(double ut)
        {
            if(startUT<0 || halfLifeYears<=0)return 1;
            return Math.Pow(0.5,Math.Max(0,ut-startUT)/(halfLifeYears*Year));
        }

        public override void OnStart(StartState state)
        {
            gen=part.FindModuleImplementing<ModuleGenerator>();
            if(gen!=null && gen.resHandler!=null)
            {
                baseRate=new double[gen.resHandler.outputResources.Count];
                for(int i=0;i<baseRate.Length;i++)baseRate[i]=gen.resHandler.outputResources[i].rate;
            }
            if(!HighLogic.LoadedSceneIsFlight){Fields["output"].guiActive=false;return;}
            if(startUT<0)startUT=Planetarium.GetUniversalTime();
            Apply();
        }

        void Apply()
        {
            if(baseRate==null)return;
            double f=Fraction(Planetarium.GetUniversalTime()),ec=0;
            for(int i=0;i<baseRate.Length && i<gen.resHandler.outputResources.Count;i++)
            {
                ModuleResource r=gen.resHandler.outputResources[i];
                r.rate=baseRate[i]*f;
                if(r.name=="ElectricCharge")ec=r.rate;
            }
            float key=(float)Math.Round(ec*10000+f*100);
            if(key!=shown)
            {
                shown=key;
                // 1 EC/s is taken as 1 kW, as in the part's 0.110 EC/s for 110 W
                output=string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:F1} W ({1:F0} %)",ec*1000,f*100);
            }
        }

        public void FixedUpdate()
        {
            if(HighLogic.LoadedSceneIsFlight)Apply();
        }

        public override string GetInfo()
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,"{0:F1} {1}",halfLifeYears,
                KSP.Localization.Localizer.Format("#MSL_Curiosity_UI_RTG_halflife"));
        }
    }
}
