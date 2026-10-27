// WorldForge props and test characters: a texture times a colour, standard lighting, optional glow (emission).
Shader "WorldForge/Prop"
{
    Properties
    {
        _Color ("Colour", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _Glow ("Glow", Color) = (0, 0, 0, 0)
        _Smoothness ("Smoothness", Range(0, 1)) = 0.2
        _Metallic ("Metallic", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows
        #pragma target 3.0
        sampler2D _MainTex; float4 _Color, _Glow; float _Smoothness, _Metallic;
        struct Input { float2 uv_MainTex; };
        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float4 c = tex2D(_MainTex, IN.uv_MainTex) * _Color;
            o.Albedo = c.rgb; o.Emission = _Glow.rgb; o.Smoothness = _Smoothness; o.Metallic = _Metallic;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
