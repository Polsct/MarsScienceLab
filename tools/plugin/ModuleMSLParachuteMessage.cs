// Custom parachute message: optional text sewn into the disk panels as orange (1) and white (0) gores. Default is
// the plain pattern. Rings run from the innermost disk ring out to the band; per ring 8 characters of 10 gores
// (7-bit ASCII, high bit first) plus 3 white gores. '|' separates rings, otherwise whole words are packed.
// Each panel strip has its own vertices, so recolouring only moves a strip's u to another atlas column.
using System.Collections.Generic;
using UnityEngine;
using MSL.Localization;

namespace MSLAeroshell
{
    public class ModuleMSLParachuteMessage : PartModule
    {
        [KSPField] public string canopyName="MSL_DGB_canopy";
        [KSPField] public int columns=16;
        [KSPField] public int diskWhite=0;
        [KSPField] public int diskOrange=7;
        [KSPField] public int splitOrangeWhite=8;
        [KSPField] public int splitWhiteOrange=9;
        [KSPField] public int bandWhite=2;
        [KSPField] public int bandOrange=6;
        [KSPField] public float vTile=2.4f;
        [KSPField] public float panel=1.2f;
        [KSPField] public float slant=.45f;
        [KSPField] public int gores=80;
        [KSPField] public int firstPanel=1;
        [KSPField] public int ringPanels=1;
        [KSPField] public int diskRings=3;
        [KSPField] public int rings=4;
        [KSPField] public int ringChars=8;
        [KSPField(isPersistant=true)] public string message="";
        [KSPField(guiActiveEditor=true,guiName="#MSL_Aeroshell_UI_Parachute_message",groupName="mslChute",groupDisplayName="#MSL_Aeroshell_UI_grp_Parachute")] public string messageDisplay="";
        const string LockId="MSLParachuteMessage";
        MeshFilter[] filters;
        Mesh[] instances;
        Vector2[][] originalUv;
        PopupDialog dialog;
        public override void OnStart(StartState state)
        {
            base.OnStart(state);
            UpdateDisplay();
            if(!HighLogic.LoadedSceneIsFlight)return;
            Transform canopy=part.FindModelTransform(canopyName);
            if(canopy==null)return;
            filters=canopy.GetComponentsInChildren<MeshFilter>(true);
            instances=new Mesh[filters.Length];originalUv=new Vector2[filters.Length][];
            Apply();
        }
        [KSPEvent(guiActiveEditor=true,guiName="#MSL_Aeroshell_UI_Set_parachute_message",groupName="mslChute",groupDisplayName="#MSL_Aeroshell_UI_grp_Parachute")]
        public void SetMessageEvent()
        {
            if(dialog!=null)return;
            string draft=message;
            InputLockManager.SetControlLock(ControlTypes.KEYBOARDINPUT,LockId);
            dialog=PopupDialog.SpawnPopupDialog(new Vector2(.5f,.5f),new Vector2(.5f,.5f),
                new MultiOptionDialog(LockId,Text.Get("#MSL_Aeroshell_UI_Parachute_message_help"),
                    Text.Get("#MSL_Aeroshell_UI_Parachute_message"),HighLogic.UISkin,380f,
                    new DialogGUITextInput(message,false,rings*(ringChars+1),s=>{draft=s;return s;},30f),
                    new DialogGUIHorizontalLayout(
                        new DialogGUIButton(Text.Get("#MSL_Aeroshell_UI_Apply"),()=>{SetMessage(draft);CloseDialog();},true),
                        new DialogGUIButton(Text.Get("#MSL_Aeroshell_UI_Clear"),()=>{SetMessage("");CloseDialog();},true),
                        new DialogGUIButton(Text.Get("#MSL_Aeroshell_UI_Cancel"),CloseDialog,true))),
                false,HighLogic.UISkin);
        }
        void CloseDialog(){dialog=null;InputLockManager.RemoveControlLock(LockId);}
        void Update(){if(dialog==null && InputLockManager.GetControlLock(LockId)!=ControlTypes.None)InputLockManager.RemoveControlLock(LockId);}
        void SetMessage(string text)
        {
            message=Clean(text);
            UpdateDisplay();
            Apply();
        }
        void UpdateDisplay()
        {
            List<string> r=Rings(message);
            messageDisplay=r.Count==0?Text.Get("#MSL_Aeroshell_UI_Parachute_message_none"):string.Join(" | ",r.ToArray());
        }
        // ConfigNode cannot hold braces or "//" in a value; other characters outside printable ASCII become '?'.
        static string Clean(string text)
        {
            var sb=new System.Text.StringBuilder();
            foreach(char c in (text??"").Trim())
            {
                char d=c=='{'?'(':c=='}'?')':(c<32||c>126)?'?':c;
                if(d=='/' && sb.Length>0 && sb[sb.Length-1]=='/')continue;
                sb.Append(d);
            }
            return sb.ToString();
        }
        List<string> Rings(string text)
        {
            var result=new List<string>();
            text=Clean(text);
            if(text.Length==0)return result;
            if(text.IndexOf('|')>=0)
                foreach(string p in text.Split('|'))result.Add(p.Trim());
            else
            {
                string cur="";
                foreach(string word in text.Split(new[]{' '},System.StringSplitOptions.RemoveEmptyEntries))
                {
                    string w=word;
                    while(w.Length>ringChars){if(cur.Length>0){result.Add(cur);cur="";}result.Add(w.Substring(0,ringChars));w=w.Substring(ringChars);}
                    if(w.Length==0)continue;
                    if(cur.Length==0)cur=w;
                    else if(cur.Length+1+w.Length<=ringChars)cur+=" "+w;
                    else{result.Add(cur);cur=w;}
                }
                if(cur.Length>0)result.Add(cur);
            }
            if(result.Count>rings)result.RemoveRange(rings,result.Count-rings);
            for(int i=0;i<result.Count;i++)if(result[i].Length>ringChars)result[i]=result[i].Substring(0,ringChars);
            return result;
        }
        // 1 orange, 0 white, -1 outside the message rings (keep the default pattern); p<0 is the band
        int Bit(List<string> r,int gore,int p)
        {
            int ring;
            if(p<0)ring=diskRings;
            else if(p>=firstPanel && p<firstPanel+diskRings*ringPanels)ring=diskRings-1-(p-firstPanel)/ringPanels;
            else return -1;
            if(ring>=r.Count)return 0;
            int k=gore/10,b=gore%10;
            if(k>=r[ring].Length || b>=7)return 0;
            return (r[ring][k]>>(6-b))&1;
        }
        int DiskColumn(int below,int above){return below==1?(above==1?diskOrange:splitOrangeWhite):(above==1?splitWhiteOrange:diskWhite);}
        void Apply()
        {
            if(filters==null)return;
            List<string> r=Rings(message);
            for(int m=0;m<filters.Length;m++)
            {
                if(filters[m]==null)continue;
                if(instances[m]==null)
                {
                    if(r.Count==0)continue;                     // default pattern: keep the shared mesh
                    instances[m]=filters[m].mesh;               // per-part copy
                    originalUv[m]=instances[m].uv;
                }
                Vector2[] orig=originalUv[m],uv=(Vector2[])orig.Clone();
                if(r.Count>0)
                {
                    Vector3[] v=instances[m].vertices;int[] t=instances[m].triangles;
                    for(int i=0;i<t.Length;i+=3)
                    {
                        int a=t[i],b=t[i+1],c=t[i+2];
                        int col=(int)((orig[a].x+orig[b].x+orig[c].x)/3f*columns);
                        bool band=col==bandWhite || col==bandOrange;
                        if(!band && col!=diskWhite && col!=diskOrange && col!=splitOrangeWhite && col!=splitWhiteOrange)continue;
                        float x=(v[a].x+v[b].x+v[c].x)/3f,y=(v[a].y+v[b].y+v[c].y)/3f;
                        float arc=(orig[a].y+orig[b].y+orig[c].y)/3f*vTile;
                        int gore=((Mathf.FloorToInt(Mathf.Atan2(y,x)/(2*Mathf.PI/gores))%gores)+gores)%gores;
                        int j=Mathf.FloorToInt(arc/panel);
                        int next;
                        if(band)next=Bit(r,gore,-1)==1?bandOrange:bandWhite;
                        else if(j>=1 && arc-j*panel<slant)                   // between the two ends of seam j
                        {
                            int below=Bit(r,gore,j-1),above=Bit(r,gore,j);
                            if(below<0 && above<0)continue;
                            if(below<0)below=(col==diskOrange||col==splitOrangeWhite)?1:0;
                            if(above<0)above=(col==diskOrange||col==splitWhiteOrange)?1:0;
                            next=DiskColumn(below,above);
                        }
                        else
                        {
                            int bit=Bit(r,gore,j);
                            if(bit<0)continue;
                            next=DiskColumn(bit,bit);
                        }
                        float du=(float)(next-col)/columns;
                        uv[a].x=orig[a].x+du;uv[b].x=orig[b].x+du;uv[c].x=orig[c].x+du;
                    }
                }
                instances[m].uv=uv;
            }
        }
        void OnDestroy()
        {
            if(dialog!=null){dialog.Dismiss();CloseDialog();}
            if(instances!=null)foreach(Mesh m in instances)if(m!=null)Destroy(m);
        }
    }
}
