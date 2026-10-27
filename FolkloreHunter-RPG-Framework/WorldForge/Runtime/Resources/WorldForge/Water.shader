// WorldForge water: puddles, pools and rivers. Glossy (it reflects the sky, the reflection probes and every light's
// highlight), two drifting ripple layers, rain or drip rings, soft where it meets the ground (camera depth).
Shader "WorldForge/Water"
{
    Properties
    {
        _Color ("Colour (a: opacity)", Color) = (0.07, 0.09, 0.1, 0.82)
        _Nrm ("Ripple normals (rgb)", 2D) = "bump" {}
        _Ripples ("Ripples", Range(0,1)) = 0.5
        _Flow ("Flow (xz, m/s)", Vector) = (0, 0, 0, 0)
        _Rain ("Rain / drip rings", Range(0,1)) = 0
        _Edge ("Soft edge (m)", Float) = 0.1
    }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "IgnoreProjector"="True" }
        ZWrite Off
        CGPROGRAM
        #pragma surface surf Standard alpha:premul vertex:vert
        #pragma target 3.5
        sampler2D _Nrm; UNITY_DECLARE_DEPTH_TEXTURE(_CameraDepthTexture);
        float4 _Color, _Flow; float _Ripples, _Rain, _Edge;
        struct Input { float3 worldPos; float4 screenPos; float eyeDepth; };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            COMPUTE_EYEDEPTH(o.eyeDepth);
        }

        float2 Hash2(float2 p)
        {
            p = frac(p * float2(5.3983, 5.4427));
            p += dot(p.yx, p.xy + float2(21.5351, 14.3137));
            return frac(float2(p.x * p.y * 95.4337, p.x * p.y * 97.597));
        }

        float2 Rings(float2 uv, float t)
        {
            float2 cell = floor(uv), f = frac(uv), n = 0;
            for (int y = -1; y <= 1; y++)
                for (int x = -1; x <= 1; x++)
                {
                    float2 c = cell + float2(x, y), h = Hash2(c);
                    float ph = frac(t * (0.55 + h.x * 0.5) + h.y), r0 = ph * 0.85;
                    float2 d = f - float2(x, y) - h; float r = length(d);
                    float ring = sin((r - r0) * 45) * saturate(1 - abs(r - r0) * 10) * (1 - ph);
                    n += d / max(r, 1e-3) * ring;
                }
            return n;
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float2 uv = IN.worldPos.xz - _Flow.xz * _Time.y;
            float3 n1 = tex2D(_Nrm, uv * 0.35 + _Time.y * float2(0.021, 0.013)).xyz * 2 - 1;
            float3 n2 = tex2D(_Nrm, uv * 0.73 - _Time.y * float2(0.017, 0.023)).xyz * 2 - 1;
            float3 n = float3((n1.xy + n2.xy) * _Ripples * 0.3, 1);
            n.xy += Rings(IN.worldPos.xz * 2.3, _Time.y) * _Rain * 0.45;
            o.Normal = normalize(n);
            float scene = LinearEyeDepth(SAMPLE_DEPTH_TEXTURE_PROJ(_CameraDepthTexture, UNITY_PROJ_COORD(IN.screenPos)));
            float fade = saturate((scene - IN.eyeDepth) / max(0.01, _Edge));
            o.Albedo = _Color.rgb;
            o.Smoothness = 0.97;
            o.Metallic = 0;
            o.Alpha = _Color.a * fade;
        }
        ENDCG
    }
}
