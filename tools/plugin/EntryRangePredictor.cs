// Numerical range predictor; vessel invariants are snapshotted once per bank search.
using System;

namespace MSLAeroshell
{
    public struct EntryRangeState
    {
        public double Radius, GravityParameter, AtmosphereDepth, Altitude, Speed, Gamma;
        public double Mass, Ballast, ReferenceArea, Cd, LiftDrag;
        public bool AutoRelease;
        public double ReleaseSpeed;   // m/s, entry balance masses leave below this (ModuleMSLEntryGuidance.entryMassReleaseSpeed)
        public double HeadingSpeed;   // m/s, bank held below this is the heading-alignment one (headingAlignmentSpeed; 0 = 1100)
    }

    public static class EntryRangePredictor
    {
        static readonly double HeadingBankCos=Math.Cos(30*Math.PI/180);
        public static double Predict(EntryRangeState state,double bank,Func<double,double> densityAtAltitude)
        {
            double h=state.Altitude,v=Math.Max(1,state.Speed),r=state.Radius+h,gamma=state.Gamma;
            double distance=0,mass=state.Mass,ballast=state.Ballast;
            double cosBank=Math.Cos(bank*Math.PI/180),dt=.5,headingSpeed=state.HeadingSpeed>0?state.HeadingSpeed:1100;
            for(int i=0;i<1600 && v>450 && h>1000;i++)
            {
                double density=densityAtAltitude(h);
                if(state.AutoRelease && v<state.ReleaseSpeed && ballast>0){mass-=ballast;ballast=0;}
                double drag=.5*density*v*v*state.ReferenceArea*state.Cd/mass;
                double g=state.GravityParameter/(r*r);
                double sinGamma=Math.Sin(gamma),cosGamma=Math.Cos(gamma);
                double dv=-drag-g*sinGamma;
                double liftDrag=ballast>0?state.LiftDrag:0;
                double verticalBank=v>headingSpeed?cosBank:HeadingBankCos;
                double dg=liftDrag*drag*verticalBank/v+(v/r-g/v)*cosGamma;
                distance+=v*cosGamma*dt*state.Radius/r;
                h+=v*sinGamma*dt;v=Math.Max(1,v+dv*dt);gamma+=dg*dt;r=state.Radius+h;
                if(h>state.AtmosphereDepth*2 && gamma>0)break;
            }
            return distance;
        }
    }
}
