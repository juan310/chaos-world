// WorldForge light shaft: the glow of light falling through a cave mouth or an opening, on a cone or beam mesh (uv.y
// along the beam). Additive; strongest seen side-on, fading along its length, at its edges and against surfaces; slow
// drifting dust in it.
Shader "WorldForge/Shaft"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 0.92, 0.75, 0.25)
        _Soft ("Soft edge (m)", Float) = 0.6
    }
    SubShader
    {
        Tags { "Queue"="Transparent+5" "RenderType"="Transparent" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Color; float _Soft; UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
            struct appdata { float4 vertex : POSITION; float3 normal : NORMAL; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float edge : TEXCOORD1; float4 sp : TEXCOORD2; float3 wp : TEXCOORD3; };
            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.uv = v.uv;
                float3 wn = UnityObjectToWorldNormal(v.normal); float3 wp = mul(unity_ObjectToWorld, v.vertex).xyz; o.wp = wp;
                o.edge = abs(dot(normalize(_WorldSpaceCameraPos - wp), wn));
                o.sp = ComputeScreenPos(o.pos); COMPUTE_EYEDEPTH(o.sp.z);
                return o;
            }
            float N3(float3 p) { return frac(sin(dot(floor(p), float3(12.9898, 78.233, 37.719))) * 43758.5453); }
            fixed4 frag (v2f i) : SV_Target
            {
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.sp)));
                float fade = saturate((scene - i.sp.z) / _Soft) * saturate((i.sp.z - 0.3) / 1.5);
                float along = saturate(1 - i.uv.y) * saturate(i.uv.y * 6);
                float3 q = i.wp * 2 + float3(0, -_Time.y * 0.15, _Time.y * 0.05);
                float dust = 0.75 + 0.25 * lerp(N3(q), N3(q + 1), 0.5);
                float a = _Color.a * pow(i.edge, 1.5) * along * fade * dust;
                return fixed4(_Color.rgb * a, 0);
            }
            ENDCG
        }
    }
}
