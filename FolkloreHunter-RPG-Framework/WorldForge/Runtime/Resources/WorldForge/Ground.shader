// WorldForge ground: rock, moss, dirt and ruin stone blended per vertex (colour r, g, b; uv2.x ruin), mapped from three
// sides (no stretching on cliffs or cave walls), darker and glossier where wet (colour a), and its ambient light cut by how
// much sky the place sees (uv2.y), so cave interiors are dark without baked lighting. Built-in render pipeline.
Shader "WorldForge/Ground"
{
    Properties
    {
        _RockTex ("Rock (a: height)", 2D) = "gray" {}
        _RockNrm ("Rock normal (rgb)", 2D) = "bump" {}
        _DirtTex ("Dirt (a: height)", 2D) = "gray" {}
        _DirtNrm ("Dirt normal (rgb)", 2D) = "bump" {}
        _MossTex ("Moss (a: height)", 2D) = "gray" {}
        _MossNrm ("Moss normal (rgb)", 2D) = "bump" {}
        _RuinTex ("Ruin stone (a: height)", 2D) = "gray" {}
        _RuinNrm ("Ruin normal (rgb)", 2D) = "bump" {}
        _Tiling ("Metres per tile", Float) = 2.4
        _WetDark ("Wet darkening", Range(0,1)) = 0.45
        _Wet ("Extra wetness (rain)", Range(0,1)) = 0
        _AmbientMin ("Ambient left deep in a cave", Range(0,1)) = 0.1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 300
        CGPROGRAM
        #pragma surface surf Standard fullforwardshadows vertex:vert
        #pragma target 3.5
        sampler2D _RockTex, _RockNrm, _DirtTex, _DirtNrm, _MossTex, _MossNrm, _RuinTex, _RuinNrm;
        float _Tiling, _WetDark, _Wet, _AmbientMin;
        struct Input { float3 worldPos; float4 color : COLOR; float2 wf; float3 worldNormal; INTERNAL_DATA };

        void vert (inout appdata_full v, out Input o)
        {
            UNITY_INITIALIZE_OUTPUT(Input, o);
            o.wf = v.texcoord1.xy;
        }

        // three projections blended by the surface direction; normals by the whiteout blend, in world space
        void Tri(sampler2D tex, sampler2D nrm, float3 p, float3 n, float3 w, out float4 col, out float3 wn)
        {
            float2 ux = p.zy, uy = p.xz, uz = p.xy;
            col = tex2D(tex, ux) * w.x + tex2D(tex, uy) * w.y + tex2D(tex, uz) * w.z;
            float3 tx = tex2D(nrm, ux).xyz * 2 - 1, ty = tex2D(nrm, uy).xyz * 2 - 1, tz = tex2D(nrm, uz).xyz * 2 - 1;
            tx = float3(tx.xy + n.zy, abs(tx.z) * n.x);
            ty = float3(ty.xy + n.xz, abs(ty.z) * n.y);
            tz = float3(tz.xy + n.xy, abs(tz.z) * n.z);
            wn = normalize(tx.zyx * w.x + ty.xzy * w.y + tz.xyz * w.z);
        }

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            float3 n = WorldNormalVector(IN, float3(0, 0, 1));
            float3 w = pow(abs(n), 4); w /= (w.x + w.y + w.z);
            float3 p = IN.worldPos / _Tiling;
            float4 rc, dc, mc, uc; float3 rn, dn, mn, un;
            Tri(_RockTex, _RockNrm, p, n, w, rc, rn);
            Tri(_DirtTex, _DirtNrm, p * 1.37, n, w, dc, dn);
            Tri(_MossTex, _MossNrm, p * 1.13, n, w, mc, mn);
            Tri(_RuinTex, _RuinNrm, p * 0.8, n, w, uc, un);
            float4 m = IN.color;
            float moss = m.g * saturate((n.y - 0.45) * 3);
            float rock = m.r + (m.g - moss);
            float3 k = float3(rock * (rc.a + 0.35), moss * (mc.a + 0.35), m.b * (dc.a + 0.35));
            k = k * k * k; k /= max(1e-4, k.x + k.y + k.z);
            float3 alb = rc.rgb * k.x + mc.rgb * k.y + dc.rgb * k.z;
            float3 wn = normalize(rn * k.x + mn * k.y + dn * k.z);
            float ruin = saturate(IN.wf.x);
            alb = lerp(alb, uc.rgb, ruin); wn = normalize(lerp(wn, un, ruin));
            float wet = saturate(m.a + _Wet);
            alb *= lerp(1, 1 - _WetDark, wet);
            wn = normalize(lerp(wn, n, wet * 0.6));
            o.Albedo = alb;
            o.Smoothness = lerp(0.08 + 0.18 * k.x * rc.a, 0.82, wet * wet);
            o.Metallic = 0;
            o.Occlusion = lerp(_AmbientMin, 1, saturate(IN.wf.y * 1.4));
            float3 T = WorldNormalVector(IN, float3(1, 0, 0)), B = WorldNormalVector(IN, float3(0, 1, 0)), N = WorldNormalVector(IN, float3(0, 0, 1));
            o.Normal = normalize(float3(dot(wn, T), dot(wn, B), dot(wn, N)));
        }
        ENDCG
    }
    FallBack "Diffuse"
}
