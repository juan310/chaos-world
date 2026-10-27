using UnityEngine;
using System.Collections.Generic;

namespace FH.Game
{
// The ten hairstyles per body, as 3D strand clumps built in code (Codex combat prototype's HunterHair, carried over
// unchanged but for the material helper). Worn by the slim hunters (HairFit); names as Appearance.MaleHair /
// FemaleHair, same order.
public static class HunterHair {
 public static readonly string[] Male={"Tousled Crop","Side-Swept","Short Curls","Undercut","Shoulder-Length","Low Ponytail","Topknot","Warrior Braid","Slicked Back","Buzz Cut"};
 public static readonly string[] Female={"Shoulder Waves","Pixie Cut","Straight Bob","Long Straight","High Ponytail","Low Ponytail","Side Braid","Twin Braids","Messy Bun","Half-Up Braid"};
 static Material Material(string name,Color color){var m=new Material(Shader.Find("Standard")){name=name,color=color};m.SetFloat("_Glossiness",.18f);return m;}
 sealed class Builder {
  public List<Vector3> v=new List<Vector3>();public List<int>[] tri={new List<int>(),new List<int>(),new List<int>()};public Vector3 size;
  Vector3 P(Vector3 p)=>Vector3.Scale(p,size);
  public void Ellipsoid(Vector3 c,Vector3 s,int material,int rings=12,int sides=32){int start=v.Count;for(int j=0;j<=rings;j++){float a=Mathf.PI*j/rings;for(int i=0;i<=sides;i++){float b=2*Mathf.PI*i/sides;v.Add(P(c+Vector3.Scale(s,new Vector3(Mathf.Sin(a)*Mathf.Sin(b),Mathf.Cos(a),Mathf.Sin(a)*Mathf.Cos(b)))));}}Grid(start,rings,sides,material);}
  void Grid(int start,int rows,int cols,int material){for(int j=0;j<rows;j++)for(int i=0;i<cols;i++){int a=start+j*(cols+1)+i,b=a+cols+1;tri[material].AddRange(new[]{a,b,a+1,a+1,b,b+1});}}
  public void Cap(bool cropped=false){int start=v.Count;const int rings=14,sides=48;for(int j=0;j<=rings;j++)for(int i=0;i<=sides;i++){float a=2*Mathf.PI*i/sides,front=Mathf.Max(0,Mathf.Cos(a));float bottom=Mathf.Lerp(cropped?1.55f:1.86f,cropped?1.18f:1.02f,front);float b=bottom*j/rings;v.Add(P(new Vector3(Mathf.Sin(a)*Mathf.Sin(b)*1.03f,Mathf.Cos(b)*1.02f,Mathf.Cos(a)*Mathf.Sin(b)*1.04f)));}Grid(start,rings,sides,0);}
  public void Strand(Vector3 a,Vector3 b,Vector3 c,Vector3 d,float width,int mat){const int steps=12,sides=8;int start=v.Count;for(int j=0;j<=steps;j++){float t=j/(float)steps,u=1-t;var point=u*u*u*a+3*u*u*t*b+3*u*t*t*c+t*t*t*d;var tangent=(3*u*u*(b-a)+6*u*t*(c-b)+3*t*t*(d-c)).normalized;var normal=Vector3.ProjectOnPlane(point.normalized,tangent).normalized;if(normal.sqrMagnitude<.1f)normal=Vector3.ProjectOnPlane(Vector3.forward,tangent).normalized;var across=Vector3.Cross(tangent,normal).normalized;float taper=Mathf.Max(.015f,Mathf.Sin(Mathf.PI*(.12f+.88f*t)));for(int k=0;k<=sides;k++){float angle=k*Mathf.PI*2/sides;v.Add(P(point+across*(Mathf.Cos(angle)*width*taper)+normal*(Mathf.Sin(angle)*width*.3f*taper)));}}Grid(start,steps,sides,mat);}
  public void Braid(Vector3 start,Vector3 end,float radius){var axis=(end-start).normalized;var right=Vector3.Cross(axis,Vector3.forward).normalized;if(right.sqrMagnitude<.1f)right=Vector3.right;var forward=Vector3.Cross(right,axis);const int count=96,sides=10;for(int strand=0;strand<3;strand++){int first=v.Count;for(int j=0;j<=count;j++){float t=j/(float)count,angle=t*5*Mathf.PI*2+strand*Mathf.PI*2/3;var center=Vector3.Lerp(start,end,t)+(right*Mathf.Sin(angle)+forward*Mathf.Cos(angle))*radius*.65f*(1-.35f*t);var tangent=(axis*(end-start).magnitude+(right*Mathf.Cos(angle)-forward*Mathf.Sin(angle))*radius*.65f*(1-.35f*t)*5*Mathf.PI*2).normalized;var radial=(right*Mathf.Sin(angle)+forward*Mathf.Cos(angle)).normalized;var across=Vector3.Cross(tangent,radial).normalized;for(int k=0;k<=sides;k++){float a=k*Mathf.PI*2/sides;v.Add(P(center+(across*Mathf.Cos(a)+radial*Mathf.Sin(a))*radius*.57f*(1-.6f*t)));}}Grid(first,count,sides,strand%3);}Ellipsoid(end,new Vector3(radius*.58f,radius*.8f,radius*.58f),0,7,12);}
  public Mesh Mesh(){var mesh=new Mesh{name="Original editable hairstyle prototype"};mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;mesh.SetVertices(v);mesh.subMeshCount=3;for(int i=0;i<3;i++)mesh.SetTriangles(tri[i],i);mesh.RecalculateNormals();mesh.RecalculateBounds();return mesh;}
 }
 static Vector3 Surface(float az,float polar,float push=1.08f)=>new Vector3(Mathf.Sin(az)*Mathf.Sin(polar)*push,Mathf.Cos(polar)*push,Mathf.Cos(az)*Mathf.Sin(polar)*push);
 public static GameObject Create(bool female,int style,Vector3 radii,Transform parent){
  var b=new Builder{size=radii};var random=new System.Random(1643+style*77+(female?90:0));float Rand(float lo,float hi)=>(float)(lo+random.NextDouble()*(hi-lo));
  bool buzz=!female&&style==9,undercut=!female&&style==3,slick=!female&&style==8;bool shortCut=buzz||undercut||slick||(!female&&style<4)||(female&&style==1);
  b.Cap(undercut||buzz);int layers=buzz?100:shortCut?82:94;
  for(int i=0;i<layers;i++){float az=i*2.399963f,front=Mathf.Clamp01((Mathf.Cos(az)-.15f)/.65f),polar=Rand(.15f,Mathf.Lerp(1.25f,1.0f,front));var a=Surface(az,polar);float length=buzz?.12f:shortCut?Rand(.35f,.72f):Rand(.4f,.9f);var d=Surface(az+.24f,Mathf.Min(polar+length,Mathf.Lerp(1.86f,1.12f,front)),1.07f);var mid=(a+d)*.55f+Vector3.up*(buzz?.02f:.15f);if(slick){a=Surface(az,Mathf.Min(polar+.45f,1.4f));d=Surface(az+Mathf.PI*.1f,polar,1.1f);mid=(a+d)*.55f+Vector3.back*.2f;}
   if(!female&&style==2){var side=new Vector3(Mathf.Cos(az),.25f,-Mathf.Sin(az));b.Strand(a,a+side*.5f+Vector3.up*.15f,d-side*.5f+Vector3.up*.2f,d,.12f,i%3);}else b.Strand(a,mid,mid+Vector3.up*(buzz?.02f:.07f),d,buzz?.042f:Rand(.075f,.135f),i%3);
  }
  if(!buzz&&!slick){int count=undercut?15:22;for(int i=0;i<count;i++){float x=-.9f+i*1.8f/(count-1),sweep=(!female&&style==1)?-.65f:-.22f;var a=new Vector3(x*.6f,1.02f,-.05f);var d=new Vector3(Mathf.Clamp(x+sweep,-1.05f,1.05f),Rand(.45f,.73f)-Mathf.Abs(x)*.12f,Mathf.Sqrt(Mathf.Max(.1f,1-x*x))*.98f);b.Strand(a,a+new Vector3(.28f,.12f,.6f),d+new Vector3(.12f,.22f,.05f),d,Rand(.075f,.13f),1+i%2);}}
  bool longLoose=(!female&&style==4)||(female&&(style==0||style==2||style==3||style==9));
  if(longLoose){float length=!female?2.25f:style==2?1.35f:style==3?4.1f:2.5f;bool waves=style==0||style==9||!female;for(int i=0;i<34;i++){float az=Mathf.PI*.5f+Mathf.PI*i/33;var a=Surface(az,.9f);float x=Mathf.Sin(az),z=Mathf.Cos(az);var d=new Vector3(x*(waves?1.24f:1.06f),-length+Rand(-.12f,.12f),z*.95f-.17f);b.Strand(a,new Vector3(x*1.25f,-.3f,z*1.2f),new Vector3(x*(waves?1.45f:1.1f),-length*.7f,z*.95f),d,Rand(.17f,.23f),i%3);}
   for(int side=-1;side<=1;side+=2)for(int j=0;j<3;j++){var a=new Vector3(side*(.7f+j*.08f),.55f,.65f);var d=new Vector3(side*(1.12f+j*.07f),-length*.75f,.35f);b.Strand(a,new Vector3(side*1.35f,.1f,.4f),new Vector3(side*(waves?.94f:1.1f),-length*.5f,.6f),d,.18f,1+j%2);}}
  bool pony=(!female&&style==5)||(female&&(style==4||style==5));if(pony){var tie=new Vector3(0,female&&style==4?.85f:-.15f,-1.02f);b.Ellipsoid(tie,new Vector3(.36f,.22f,.32f),0);for(int i=0;i<18;i++){float a=i*Mathf.PI*2/18;var at=tie+new Vector3(Mathf.Sin(a)*.22f,Mathf.Cos(a)*.1f,0);var end=new Vector3(Mathf.Sin(a)*.48f,-(female?3.6f:2.8f),-1.25f+Mathf.Cos(a)*.35f);b.Strand(at,at+new Vector3(.18f,.15f,-.7f),end+new Vector3(-.3f,1.25f,-.4f),end,.2f,i%3);}}
  if((!female&&style==6)||(female&&style==8)){b.Ellipsoid(new Vector3(0,1.17f,-.3f),new Vector3(.57f,.46f,.5f),1);for(int i=0;i<16;i++){float az=i*Mathf.PI*2/16;var a=new Vector3(Mathf.Sin(az)*.48f,1.05f,Mathf.Cos(az)*.42f-.3f);var d=new Vector3(Mathf.Sin(az+.9f)*.42f,1.42f,Mathf.Cos(az+.9f)*.38f-.3f);b.Strand(a,a+Vector3.up*.45f,d+Vector3.up*.2f,d,.12f,i%3);}}
  if((!female&&style==7)||(female&&style==6))b.Braid(new Vector3(-.92f,-.12f,.12f),new Vector3(-1.1f,-3.25f,.65f),female?.35f:.28f);
  if(female&&style==7)for(int side=-1;side<=1;side+=2)b.Braid(new Vector3(side*.94f,-.1f,.08f),new Vector3(side*1.18f,-3.1f,.68f),.28f);
  if(female&&style==9){for(int i=0;i<12;i++){float a=Mathf.PI*.5f+Mathf.PI*i/11;Vector3 at=Surface(a,.75f,1.17f),end=Surface(a+.22f,.75f,1.17f);b.Strand(at,at+Vector3.up*.13f,end+Vector3.up*.13f,end,.13f,i%3);}}
  var obj=new GameObject((female?Female:Male)[style]+" - original 3D hair prototype");obj.transform.SetParent(parent,false);obj.AddComponent<MeshFilter>().sharedMesh=b.Mesh();var r=obj.AddComponent<MeshRenderer>();var colors=new[]{new Color(.17f,.065f,.032f),new Color(.225f,.088f,.044f),new Color(.28f,.115f,.06f)};var mats=new Material[3];for(int i=0;i<3;i++){mats[i]=Material("Chestnut strand "+i,colors[i]);mats[i].SetFloat("_Glossiness",.2f);}r.sharedMaterials=mats;return obj;
 }
}
}
