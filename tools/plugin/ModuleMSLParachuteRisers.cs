// Triple bridle: three legs from the PSS strut ends to a confluence fitting at the canopy transform, so they follow
// the parachute's aim and deployment scale. Canopy emission is scaled by sun elevation (no glow on the night side).
// Deployment, drag and cut come from the parachute module on MSL_AS_Parachute.
using UnityEngine;

namespace MSLAeroshell
{
    public class ModuleMSLParachuteRisers : PartModule
    {
        [KSPField] public string canopyName="MSL_DGB_canopy";
        [KSPField] public float fittingZ=1.349f;
        Transform[] anchors=new Transform[3];
        LineRenderer[] lines=new LineRenderer[3];
        Transform canopy;
        Transform lid;
        Renderer[] canopyRenderers;
        Material rope;
        Material[] canopyMaterials;
        Color[] canopyEmission;
        float daylight=-1;
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            if(!HighLogic.LoadedSceneIsFlight)return;
            canopy=part.FindModelTransform(canopyName);
            lid=part.FindModelTransform("PCC_Lid");
            if(canopy!=null)canopyRenderers=canopy.GetComponentsInChildren<Renderer>(true);
            if(canopyRenderers!=null)
            {
                canopyMaterials=new Material[canopyRenderers.Length];canopyEmission=new Color[canopyRenderers.Length];
                for(int i=0;i<canopyRenderers.Length;i++)
                    if(canopyRenderers[i].sharedMaterial!=null && canopyRenderers[i].sharedMaterial.HasProperty("_EmissiveColor"))
                    {canopyMaterials[i]=canopyRenderers[i].material;canopyEmission[i]=canopyMaterials[i].GetColor("_EmissiveColor");}
            }
            rope=new Material(Shader.Find("KSP/Diffuse"));
            Texture2D texture=new Texture2D(1,1);texture.SetPixel(0,0,new Color(.27f,.25f,.21f));texture.Apply();
            rope.mainTexture=texture;
            for(int i=0;i<3;i++)
            {
                // the PSS strut ends are on the backshell; the module is on the parachute part mounted on it
                anchors[i]=part.FindModelTransform("parachuteAnchor_"+(i+1));
                if(anchors[i]==null && part.parent!=null)anchors[i]=part.parent.FindModelTransform("parachuteAnchor_"+(i+1));
                var go=new GameObject("MSL parachute riser "+(i+1));go.transform.SetParent(part.transform,false);
                lines[i]=go.AddComponent<LineRenderer>();lines[i].useWorldSpace=true;
                lines[i].sharedMaterial=rope;lines[i].positionCount=2;
                lines[i].startWidth=lines[i].endWidth=.009f;lines[i].numCapVertices=4;
                lines[i].enabled=false;
            }
        }
        void LateUpdate()
        {
            if(!HighLogic.LoadedSceneIsFlight || canopy==null)
            {
                foreach(LineRenderer line in lines)if(line!=null)line.enabled=false;
                return;
            }
            bool visible=lid==null || !lid.gameObject.activeInHierarchy;
            bool canopyVisible=false;
            if(canopyRenderers!=null)foreach(Renderer r in canopyRenderers)
                if(r!=null && r.enabled && r.gameObject.activeInHierarchy){canopyVisible=true;break;}
            visible &= canopyVisible;
            if(canopyVisible)Daylight();
            for(int i=0;i<3;i++)
            {
                if(lines[i]==null)continue;
                lines[i].enabled=visible && anchors[i]!=null;
                if(lines[i].enabled){lines[i].SetPosition(0,anchors[i].position);lines[i].SetPosition(1,FittingPosition);}
            }
        }
        void Daylight()
        {
            if(canopyMaterials==null || Planetarium.fetch==null || vessel==null)return;
            Vector3d up=(part.transform.position-vessel.mainBody.position).normalized;
            Vector3d sun=(Planetarium.fetch.Sun.position-part.transform.position).normalized;
            float f=Mathf.Clamp01(((float)Vector3d.Dot(up,sun)+.05f)/.15f);   // 0 below -3 deg, 1 above +6 deg
            if(Mathf.Abs(f-daylight)<.01f)return;
            daylight=f;
            for(int i=0;i<canopyMaterials.Length;i++)
                if(canopyMaterials[i]!=null){Color c=canopyEmission[i];canopyMaterials[i].SetColor("_EmissiveColor",new Color(c.r*f,c.g*f,c.b*f,c.a));}
        }
        public Vector3 FittingPosition{get{return canopy.TransformPoint(0,0,fittingZ);}}
        void OnDestroy()
        {
            foreach(LineRenderer line in lines)if(line!=null)Destroy(line.gameObject);
            if(rope!=null){Destroy(rope.mainTexture);Destroy(rope);}
            if(canopyMaterials!=null)foreach(Material m in canopyMaterials)if(m!=null)Destroy(m);
        }
    }
}
