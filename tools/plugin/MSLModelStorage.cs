// Restores compact .mu storage before PartLoader copies the MSL model prefabs: shared meshes and load-time tangents
// (MSLTangents.cs).
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MSLAeroshell
{
    [KSPAddon(KSPAddon.Startup.Instantly,true)]
    public class MSLModelStorageInstaller : MonoBehaviour
    {
        void Awake()
        {
            List<LoadingSystem> loaders=LoadingScreen.Instance.loaders;
            int index=loaders.FindIndex(loader=>loader is PartLoader);
            GameObject host=new GameObject("MSLModelStorage");DontDestroyOnLoad(host);
            loaders.Insert(index<0?loaders.Count:index,host.AddComponent<MSLModelStorage>());
            if(index<0)Debug.LogError("[MSLModelStorage] PartLoader not found; restoring models last");
        }
    }

    public class MSLModelStorage : LoadingSystem
    {
        public static int SharedLeaves {get;private set;}
        public static int GeneratedTangentMeshes {get;private set;}
        bool ready;

        public override bool IsReady(){return ready;}
        public override float ProgressFraction(){return ready?1:0;}
        public override string ProgressTitle(){return MSL.Localization.Text.Get("#MSL_Aeroshell_MSL_model_storage");}

        public override void StartLoad()
        {
            try
            {
                foreach(ConfigNode node in GameDatabase.Instance.GetConfigNodes("MSL_SHARED_MESHES"))ShareMeshes(node);
                HashSet<Mesh> done=new HashSet<Mesh>();
                foreach(UrlDir.UrlFile file in GameDatabase.Instance.databaseModelFiles)
                {
                    if(!file.url.StartsWith("MarsScienceLab/",StringComparison.Ordinal))continue;
                    GameObject prefab=GameDatabase.Instance.GetModelPrefab(file.url);
                    if(prefab==null)continue;
                    foreach(MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Mesh mesh=filter.sharedMesh;
                        if(mesh==null || !done.Add(mesh) || filter.GetComponent<MeshRenderer>()==null)continue;
                        if(mesh.tangents.Length==0 && mesh.normals.Length>0 && mesh.uv.Length>0)
                        {GenerateTangents(mesh);GeneratedTangentMeshes++;}
                    }
                }
                Debug.Log("[MSLModelStorage] shared leaves "+SharedLeaves+", generated tangents for "+GeneratedTangentMeshes+" meshes");
            }
            catch(Exception exception){Debug.LogError("[MSLModelStorage] "+exception);}
            ready=true;
        }

        static void ShareMeshes(ConfigNode node)
        {
            string url=node.GetValue("model");
            GameObject prefab=GameDatabase.Instance.GetModelPrefab(url);
            if(prefab==null)throw new InvalidOperationException("Missing model: "+url);
            Dictionary<string,Transform> named=new Dictionary<string,Transform>();
            HashSet<string> repeated=new HashSet<string>();
            foreach(Transform t in prefab.GetComponentsInChildren<Transform>(true))
                if(named.ContainsKey(t.name))repeated.Add(t.name);else named[t.name]=t;
            foreach(string value in node.GetValues("share"))
            {
                string[] f=value.Split(',');
                if(f.Length!=5)throw new FormatException("Bad share in "+url+": "+value);
                string target=f[0].Trim(),source=f[1].Trim();
                Transform t,s;
                if(repeated.Contains(target) || repeated.Contains(source) || !named.TryGetValue(target,out t) || !named.TryGetValue(source,out s))
                    throw new InvalidOperationException("Shared mesh names must be unique in "+url+": "+value);
                MeshFilter sourceFilter=s.GetComponent<MeshFilter>();MeshRenderer sourceRenderer=s.GetComponent<MeshRenderer>();
                if(sourceFilter==null || sourceRenderer==null)throw new InvalidOperationException("Shared mesh source has no renderer: "+source);
                if(t.GetComponent<MeshFilter>()!=null)continue;
                t.gameObject.AddComponent<MeshFilter>().sharedMesh=sourceFilter.sharedMesh;
                MeshRenderer renderer=t.gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterials=sourceRenderer.sharedMaterials;
                renderer.shadowCastingMode=sourceRenderer.shadowCastingMode;
                renderer.receiveShadows=sourceRenderer.receiveShadows;
                t.localScale=Vector3.Scale(t.localScale,new Vector3(Parse(f[2]),Parse(f[3]),Parse(f[4])));
                SharedLeaves++;
            }
        }

        static float Parse(string value)
        {return float.Parse(value.Trim(),System.Globalization.CultureInfo.InvariantCulture);}

        public static void GenerateTangents(Mesh mesh)
        {
            Vector3[] vertices=mesh.vertices,normals=mesh.normals;Vector2[] uvs=mesh.uv;
            float[] v=new float[vertices.Length*3],n=new float[vertices.Length*3],uv=new float[vertices.Length*2];
            for(int i=0;i<vertices.Length;i++)
            {
                v[i*3]=vertices[i].x;v[i*3+1]=vertices[i].y;v[i*3+2]=vertices[i].z;
                n[i*3]=normals[i].x;n[i*3+1]=normals[i].y;n[i*3+2]=normals[i].z;
                uv[i*2]=uvs[i].x;uv[i*2+1]=uvs[i].y;
            }
            List<int> tris=new List<int>();
            for(int sub=0;sub<mesh.subMeshCount;sub++)tris.AddRange(mesh.GetTriangles(sub));
            float[] t=MSLTangents.Generate(v,n,uv,tris.ToArray());
            Vector4[] tangents=new Vector4[vertices.Length];
            for(int i=0;i<tangents.Length;i++)tangents[i]=new Vector4(t[i*4],t[i*4+1],t[i*4+2],t[i*4+3]);
            mesh.tangents=tangents;
        }
    }
}
