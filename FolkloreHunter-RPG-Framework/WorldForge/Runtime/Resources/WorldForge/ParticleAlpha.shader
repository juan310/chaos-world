// WorldForge particles, alpha-blended (smoke, mist, rain): vertex colour times the texture, faded where they meet surfaces (soft particles,
// camera depth) so flames and mist never show hard lines against rock.
Shader "WorldForge/ParticleAlpha"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Soft ("Soft edge (m)", Float) = 0.3
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "UnityCG.cginc"
            sampler2D _MainTex; float4 _MainTex_ST; UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture); float _Soft;
            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float4 sp : TEXCOORD1; UNITY_FOG_COORDS(2) };
            v2f vert (appdata v)
            {
                v2f o; o.pos = UnityObjectToClipPos(v.vertex); o.color = v.color; o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.sp = ComputeScreenPos(o.pos); COMPUTE_EYEDEPTH(o.sp.z);
                UNITY_TRANSFER_FOG(o, o.pos);
                return o;
            }
            fixed4 frag (v2f i) : SV_Target
            {
                float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(i.sp)));
                float fade = saturate((scene - i.sp.z) / max(0.01, _Soft));
                fixed4 c = tex2D(_MainTex, i.uv) * i.color;
                c.a *= fade; UNITY_APPLY_FOG(i.fogCoord, c); return c;
            }
            ENDCG
        }
    }
}
