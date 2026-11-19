// Load-time tangent rule shared by MSLModelStorage: per-triangle accumulation over all
// triangles, Gram-Schmidt against the normal; singular UVs get a stable perpendicular tangent.
namespace MSLAeroshell
{
    public static class MSLTangents
    {
        // v,n: xyz per vertex; uv: xy per vertex; tris: all submesh indices.
        public static float[] Generate(float[] v,float[] n,float[] uv,int[] tris)
        {
            int count=v.Length/3;
            double[] tan1=new double[count*3],tan2=new double[count*3];
            int triangles=tris.Length/3;
            double[] sdir=new double[triangles*3],tdir=new double[triangles*3];
            // Promote before subtracting: float32 differences lose the tiny UV determinants.
            for(int f=0;f<triangles;f++)
            {
                int a=tris[f*3],b=tris[f*3+1],c=tris[f*3+2];
                double e1x=(double)v[b*3]-v[a*3],e1y=(double)v[b*3+1]-v[a*3+1],e1z=(double)v[b*3+2]-v[a*3+2];
                double e2x=(double)v[c*3]-v[a*3],e2y=(double)v[c*3+1]-v[a*3+1],e2z=(double)v[c*3+2]-v[a*3+2];
                double d1x=(double)uv[b*2]-uv[a*2],d1y=(double)uv[b*2+1]-uv[a*2+1];
                double d2x=(double)uv[c*2]-uv[a*2],d2y=(double)uv[c*2+1]-uv[a*2+1];
                double det=d1x*d2y-d2x*d1y,r=det==0?0:1/det;
                sdir[f*3]=(e1x*d2y-e2x*d1y)*r;sdir[f*3+1]=(e1y*d2y-e2y*d1y)*r;sdir[f*3+2]=(e1z*d2y-e2z*d1y)*r;
                tdir[f*3]=(e2x*d1x-e1x*d2x)*r;tdir[f*3+1]=(e2y*d1x-e1y*d2x)*r;tdir[f*3+2]=(e2z*d1x-e1z*d2x)*r;
            }
            // Accumulate corner by corner (all first corners, then second, third); keep this order, the float rounding depends on it.
            for(int corner=0;corner<3;corner++)
                for(int f=0;f<triangles;f++)
                {
                    int k=tris[f*3+corner]*3;
                    tan1[k]+=sdir[f*3];tan1[k+1]+=sdir[f*3+1];tan1[k+2]+=sdir[f*3+2];
                    tan2[k]+=tdir[f*3];tan2[k+1]+=tdir[f*3+1];tan2[k+2]+=tdir[f*3+2];
                }
            float[] result=new float[count*4];
            for(int i=0;i<count;i++)
            {
                double nx=n[i*3],ny=n[i*3+1],nz=n[i*3+2];
                double normalLength2=nx*nx+ny*ny+nz*nz;
                double dot=(nx*tan1[i*3]+ny*tan1[i*3+1]+nz*tan1[i*3+2])/System.Math.Max(normalLength2,1e-30);
                double x=tan1[i*3]-nx*dot,y=tan1[i*3+1]-ny*dot,z=tan1[i*3+2]-nz*dot;
                double length=System.Math.Sqrt(x*x+y*y+z*z);
                if(length<=1e-12)
                {
                    double ax=1,ay=0,az=0;
                    if(System.Math.Abs(ny)<System.Math.Abs(nx)) {ax=0;ay=1;}
                    if(System.Math.Abs(nz)<(ay==1?System.Math.Abs(ny):System.Math.Abs(nx))) {ax=0;ay=0;az=1;}
                    double nn=nx*nx+ny*ny+nz*nz;
                    double proj=nn>1e-30?(ax*nx+ay*ny+az*nz)/nn:0;
                    x=ax-nx*proj;y=ay-ny*proj;z=az-nz*proj;
                    length=System.Math.Sqrt(x*x+y*y+z*z);
                    result[i*4]=(float)(x/length);result[i*4+1]=(float)(y/length);result[i*4+2]=(float)(z/length);
                    result[i*4+3]=1;continue;
                }
                x/=length;y/=length;z/=length;
                // cross(n, t) . tan2
                double hand=(ny*z-nz*y)*tan2[i*3]+(nz*x-nx*z)*tan2[i*3+1]+(nx*y-ny*x)*tan2[i*3+2];
                result[i*4]=(float)x;result[i*4+1]=(float)y;result[i*4+2]=(float)z;result[i*4+3]=hand<0?-1:1;
            }
            return result;
        }
    }
}
