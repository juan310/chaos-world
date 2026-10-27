Shader "Folklore/BriarhoundFurRepair" {
 Properties {
  _MainTex ("Original atlas: face and equipment", 2D) = "white" {}
  _FurTex ("Fur detail",2D)="gray"{}
  _FurScale ("Fur repeats per metre",Float)=2.2
  _FurMid ("Fur source linear midtone",Float)=.18
  _FurDark ("Charcoal coat",Color)=(.18,.19,.20,1)
  _FurCream ("Cream chest",Color)=(.57,.48,.35,1)
  _FurTan ("Warm lower legs",Color)=(.38,.29,.20,1)
  _RepairStrength ("Repair blend",Range(0,1))=1
 }
 SubShader {
  Tags {"RenderType"="Opaque"} LOD 300
  CGPROGRAM
  #pragma surface surf Standard fullforwardshadows vertex:vert
  #pragma target 4.0
  sampler2D _MainTex,_FurTex;
  float _FurScale,_FurMid,_RepairStrength;
  fixed4 _FurDark,_FurCream,_FurTan;
  struct Input {float2 uv_MainTex;float3 coatPosition;float3 coatNormal;float4 coatMask;};
  void vert(inout appdata_full v,out Input o){UNITY_INITIALIZE_OUTPUT(Input,o);o.coatPosition=v.texcoord2.xyz;o.coatNormal=v.texcoord3.xyz;o.coatMask=v.color;}
  void surf(Input IN,inout SurfaceOutputStandard o){
   fixed3 original=tex2D(_MainTex,IN.uv_MainTex).rgb;
   float3 p=IN.coatPosition*_FurScale;
   float3 w=pow(abs(normalize(IN.coatNormal)),4);w/=max(dot(w,float3(1,1,1)),.0001);
   float f=tex2D(_FurTex,p.zy).r*w.x+tex2D(_FurTex,p.xz).r*w.y+tex2D(_FurTex,p.xy).r*w.z;
   float grain=clamp(f/max(.01,_FurMid),.65,1.38);
   fixed3 tint=lerp(_FurDark.rgb,_FurCream.rgb,saturate(IN.coatMask.g));tint=lerp(tint,_FurTan.rgb,saturate(IN.coatMask.b));
   float blend=saturate(IN.coatMask.r*_RepairStrength);
   o.Albedo=lerp(original,tint*grain,blend);o.Metallic=0;o.Smoothness=lerp(.18,.035,blend);o.Alpha=1;
  }
  ENDCG
 }
 Fallback "Standard"
}
