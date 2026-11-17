// Restores compact .mu storage before PartLoader copies the MSL model prefabs: shared meshes, quantized meshes
// (<model>.msq) and load-time tangents (MSLTangents.cs).
using System;
using System.Collections.Generic;
using System.IO;
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
        public static int QuantizedMeshes {get;private set;}
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
                    string quantized=Path.ChangeExtension(file.fullPath,".msq");
                    if(File.Exists(quantized))
                        try{LoadQuantized(quantized,prefab);}
                        catch(Exception exception){Debug.LogError("[MSLModelStorage] "+file.url+": "+exception);}
                    foreach(MeshFilter filter in prefab.GetComponentsInChildren<MeshFilter>(true))
                    {
                        Mesh mesh=filter.sharedMesh;
                        if(mesh==null || !done.Add(mesh) || filter.GetComponent<MeshRenderer>()==null)continue;
                        if(mesh.tangents.Length==0 && mesh.normals.Length>0 && mesh.uv.Length>0)
                        {GenerateTangents(mesh);GeneratedTangentMeshes++;}
                    }
                }
                Debug.Log("[MSLModelStorage] shared leaves "+SharedLeaves+", quantized meshes "+QuantizedMeshes+", generated tangents for "+GeneratedTangentMeshes+" meshes");
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

        // Decodes in double precision and rounds to float once.
        static void LoadQuantized(string path,GameObject prefab)
        {
            byte[] d=File.ReadAllBytes(path);
            if(d.Length<8 || d[0]!='M' || d[1]!='S' || d[2]!='Q' || d[3]!='1')throw new FormatException("Bad quantized mesh file: "+path);
            int count=BitConverter.ToInt32(d,4),at=8;
            for(int m=0;m<count;m++)
            {
                int depth=BitConverter.ToUInt16(d,at);at+=2;
                Transform t=prefab.transform;
                for(int i=0;i<depth;i++,at+=2)
                {
                    int child=BitConverter.ToUInt16(d,at);
                    if(child>=t.childCount)throw new InvalidOperationException("Quantized mesh path leaves the model: "+path);
                    t=t.GetChild(child);
                }
                int nameLength=BitConverter.ToUInt16(d,at);at+=2;
                string name=System.Text.Encoding.UTF8.GetString(d,at,nameLength);at+=nameLength;
                int flags=d[at++],nv=BitConverter.ToInt32(d,at),nsub=BitConverter.ToInt32(d,at+4);at+=8;
                int[] counts=new int[nsub];
                for(int i=0;i<nsub;i++,at+=4)counts[i]=BitConverter.ToInt32(d,at);
                MeshFilter filter=t.GetComponent<MeshFilter>();
                Mesh mesh=filter==null?null:filter.sharedMesh;
                if((depth>0 && t.name!=name) || mesh==null || mesh.subMeshCount!=nsub)
                    throw new InvalidOperationException("Quantized mesh does not match the model at "+name+": "+path);
                Vector3[] vertices=new Vector3[nv];
                if((flags&16)!=0)          // positions stored as floats
                    for(int i=0;i<nv;i++,at+=12)
                        vertices[i]=new Vector3(BitConverter.ToSingle(d,at),BitConverter.ToSingle(d,at+4),BitConverter.ToSingle(d,at+8));
                else
                {
                    double[] lo=new double[3],step=new double[3];
                    for(int i=0;i<3;i++){lo[i]=BitConverter.ToSingle(d,at+i*4);step[i]=(double)BitConverter.ToSingle(d,at+12+i*4)/2097151.0;}
                    at+=24;
                    for(int i=0;i<nv;i++,at+=8)
                    {
                        ulong packed=BitConverter.ToUInt64(d,at);          // three 21-bit numbers
                        vertices[i]=new Vector3((float)(lo[0]+(packed&0x1FFFFF)*step[0]),
                            (float)(lo[1]+((packed>>21)&0x1FFFFF)*step[1]),(float)(lo[2]+((packed>>42)&0x1FFFFF)*step[2]));
                    }
                }
                Vector3[] normals=new Vector3[nv];
                for(int i=0;i<nv;i++,at+=4)
                {
                    int qx=BitConverter.ToUInt16(d,at),qy=BitConverter.ToUInt16(d,at+2);
                    if(qx==0 && qy==0)continue;
                    double x=qx/65535.0*2.0-1.0,y=qy/65535.0*2.0-1.0,z=1.0-(Math.Abs(x)+Math.Abs(y)),fold=Math.Max(-z,0.0);
                    x+=x>=0?-fold:fold;y+=y>=0?-fold:fold;
                    double length=Math.Sqrt(x*x+y*y+z*z);
                    normals[i]=new Vector3((float)(x/length),(float)(y/length),(float)(z/length));
                }
                Vector2[] uv=null;
                if((flags&1)!=0)
                {
                    uv=new Vector2[nv];
                    if((flags&2)!=0)
                        for(int i=0;i<nv;i++,at+=8)uv[i]=new Vector2(BitConverter.ToSingle(d,at),BitConverter.ToSingle(d,at+4));
                    else
                    {
                        double u0=BitConverter.ToSingle(d,at),v0=BitConverter.ToSingle(d,at+4);
                        double us=(double)BitConverter.ToSingle(d,at+8)/65535.0,vs=(double)BitConverter.ToSingle(d,at+12)/65535.0;
                        at+=16;
                        for(int i=0;i<nv;i++,at+=4)
                            uv[i]=new Vector2((float)(u0+BitConverter.ToUInt16(d,at)*us),(float)(v0+BitConverter.ToUInt16(d,at+2)*vs));
                    }
                }
                if((flags&8)!=0)          // vertices stored exactly so their load-time tangents do not change
                {
                    int exact=BitConverter.ToInt32(d,at),index=at+4,p=index+exact*4,n=p+exact*12,u=n+exact*12;
                    for(int i=0;i<exact;i++)
                    {
                        int k=BitConverter.ToInt32(d,index+i*4);
                        vertices[k]=new Vector3(BitConverter.ToSingle(d,p+i*12),BitConverter.ToSingle(d,p+i*12+4),BitConverter.ToSingle(d,p+i*12+8));
                        normals[k]=new Vector3(BitConverter.ToSingle(d,n+i*12),BitConverter.ToSingle(d,n+i*12+4),BitConverter.ToSingle(d,n+i*12+8));
                        uv[k]=new Vector2(BitConverter.ToSingle(d,u+i*8),BitConverter.ToSingle(d,u+i*8+4));
                    }
                    at=u+exact*8;
                }
                bool wide=(flags&4)!=0;
                if(mesh.vertexCount!=3)          // written back in full since it was quantized: keep the .mu mesh
                {
                    for(int sub=0;sub<nsub;sub++)at+=counts[sub]*(wide?4:2);
                    Debug.LogWarning("[MSLModelStorage] "+name+" is not a quantized placeholder; kept as stored: "+path);
                    continue;
                }
                mesh.Clear();
                mesh.indexFormat=nv>65535?UnityEngine.Rendering.IndexFormat.UInt32:UnityEngine.Rendering.IndexFormat.UInt16;
                mesh.vertices=vertices;mesh.normals=normals;
                if(uv!=null)mesh.uv=uv;
                mesh.subMeshCount=nsub;
                for(int sub=0;sub<nsub;sub++)
                {
                    int[] triangles=new int[counts[sub]];
                    if(wide)for(int i=0;i<triangles.Length;i++,at+=4)triangles[i]=BitConverter.ToInt32(d,at);
                    else for(int i=0;i<triangles.Length;i++,at+=2)triangles[i]=BitConverter.ToUInt16(d,at);
                    mesh.SetTriangles(triangles,sub);
                }
                mesh.RecalculateBounds();
                QuantizedMeshes++;
            }
            if(at!=d.Length)throw new FormatException("Trailing bytes in quantized mesh file: "+path);
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
