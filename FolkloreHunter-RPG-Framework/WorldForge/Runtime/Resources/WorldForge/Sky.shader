// WorldForge sky: a gradient from the zenith to the horizon to the ground, a sun or moon disc with a glow, and stars at
// night. Used as the skybox; WFWorld sets its colours for day, dusk or night.
Shader "WorldForge/Sky"
{
    Properties
    {
        _Top ("Zenith", Color) = (0.30, 0.48, 0.78, 1)
        _Horizon ("Horizon", Color) = (0.75, 0.80, 0.86, 1)
        _Ground ("Below the horizon", Color) = (0.25, 0.24, 0.22, 1)
        _SunColor ("Sun / moon", Color) = (1, 0.95, 0.85, 1)
        _SunDir ("Towards the sun / moon", Vector) = (0, 0.5, 0.8, 0)
        _SunSize ("Disc size", Range(0, 0.1)) = 0.02
        _Stars ("Stars", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            float4 _Top, _Horizon, _Ground, _SunColor, _SunDir; float _SunSize, _Stars;
            struct v2f { float4 pos : SV_POSITION; float3 dir : TEXCOORD0; };
            v2f vert (float4 v : POSITION) { v2f o; o.pos = UnityObjectToClipPos(v); o.dir = v.xyz; return o; }
            float Hash(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            fixed4 frag (v2f i) : SV_Target
            {
                float3 d = normalize(i.dir);
                float3 c = d.y > 0 ? lerp(_Horizon.rgb, _Top.rgb, pow(saturate(d.y), 0.55)) : lerp(_Horizon.rgb, _Ground.rgb, saturate(-d.y * 4));
                float s = dot(d, normalize(_SunDir.xyz));
                c += _SunColor.rgb * (smoothstep(1 - _SunSize, 1 - _SunSize * 0.6, s) * 3 + pow(saturate(s), 64) * 0.5 + pow(saturate(s), 6) * 0.12);
                float3 g = floor(d * 380);
                float star = step(0.9975, Hash(g)) * saturate(d.y * 3) * _Stars;
                c += star * (0.6 + 0.4 * sin(_Time.y * 2 + Hash(g + 3) * 20));
                return fixed4(c, 1);
            }
            ENDCG
        }
    }
}
