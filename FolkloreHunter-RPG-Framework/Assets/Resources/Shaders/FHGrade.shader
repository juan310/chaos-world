// Folklore Hunter's own camera grade (built-in pipeline): bloom (threshold, a mip chain of box blurs added back up),
// exposure, an ACES-style filmic curve (blend 0..1), shadow/highlight tints, saturation, contrast and a vignette.
// Driven by CameraGrade.cs; loaded from Resources so the build keeps it.
Shader "Hidden/FH/Grade"
{
    Properties { _MainTex ("Source", 2D) = "white" {} }
    CGINCLUDE
    #include "UnityCG.cginc"
    sampler2D _MainTex; float4 _MainTex_TexelSize;
    sampler2D _Bloom;
    float _Threshold, _Knee, _BloomIntensity, _Exposure, _Tonemap, _Saturation, _Contrast, _Vignette;
    float4 _ShadowTint, _HighTint;

    half3 Box(float2 uv, float d)
    {
        float4 o = _MainTex_TexelSize.xyxy * float4(-d, -d, d, d);
        return (tex2D(_MainTex, uv + o.xy).rgb + tex2D(_MainTex, uv + o.zy).rgb + tex2D(_MainTex, uv + o.xw).rgb + tex2D(_MainTex, uv + o.zw).rgb) * .25;
    }
    half3 Prefilter(half3 c)
    {
        half b = max(c.r, max(c.g, c.b));
        half soft = clamp(b - _Threshold + _Knee, 0, 2 * _Knee);
        soft = soft * soft / (4 * _Knee + 1e-5);
        return c * (max(soft, b - _Threshold) / max(b, 1e-5));
    }
    half3 Filmic(half3 x) { return saturate((x * (2.51 * x + .03)) / (x * (2.43 * x + .59) + .14)); }

    half4 FragPrefilter(v2f_img i) : SV_Target { return half4(Prefilter(Box(i.uv, 1)), 1); }
    half4 FragDown(v2f_img i) : SV_Target { return half4(Box(i.uv, 1), 1); }
    half4 FragUp(v2f_img i) : SV_Target { return half4(Box(i.uv, .5), 1); }
    half4 FragFinal(v2f_img i) : SV_Target
    {
        half3 c = tex2D(_MainTex, i.uv).rgb + tex2D(_Bloom, i.uv).rgb * _BloomIntensity;
        c *= _Exposure;
        c = lerp(saturate(c), Filmic(c), _Tonemap);
        half l = Luminance(c);
        c *= lerp(_ShadowTint.rgb, _HighTint.rgb, saturate(l * 1.6));
        c = lerp(Luminance(c).xxx, c, _Saturation);
        c = saturate((c - .5) * _Contrast + .5);
        float2 d = i.uv - .5;
        c *= saturate(1 - _Vignette * dot(d, d) * 2.4);
        return half4(c, 1);
    }
    ENDCG
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass { CGPROGRAM
               #pragma vertex vert_img
               #pragma fragment FragPrefilter
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert_img
               #pragma fragment FragDown
               ENDCG }
        Pass { Blend One One
               CGPROGRAM
               #pragma vertex vert_img
               #pragma fragment FragUp
               ENDCG }
        Pass { CGPROGRAM
               #pragma vertex vert_img
               #pragma fragment FragFinal
               ENDCG }
    }
}
