Shader "Folklore/SpriteCard" {
 // A sprite card standing in the 3D world (SpriteActor): the frame cut out on its alpha, lit by the scene's sun and sky
 // (with a little light of its own, so a flat card does not go black when the sun is behind it), casting a shadow
 // shaped like the drawing, seen from both sides (the card is mirrored to face the way the monster heads).
 Properties {
  _MainTex ("Frame", 2D) = "white" {}
  _Color ("Tint", Color) = (1,1,1,1)
  _Cutoff ("Cut-out", Range(0,1)) = .45
  _Flash ("Hit flash", Range(0,1)) = 0
 }
 SubShader {
  Tags { "Queue"="AlphaTest" "RenderType"="TransparentCutout" "IgnoreProjector"="True" }
  LOD 200
  Cull Off
  CGPROGRAM
  #pragma surface surf Lambert alphatest:_Cutoff addshadow
  sampler2D _MainTex;
  fixed4 _Color;
  half _Flash;
  struct Input { float2 uv_MainTex; };
  void surf (Input IN, inout SurfaceOutput o) {
   fixed4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
   o.Albedo = lerp(c.rgb, fixed3(1, .92, .85), _Flash);
   o.Emission = c.rgb * .22 + _Flash * .5;
   o.Alpha = c.a;
  }
  ENDCG
 }
 Fallback "Legacy Shaders/Transparent/Cutout/VertexLit"
}
