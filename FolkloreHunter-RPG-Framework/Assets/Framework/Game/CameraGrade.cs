using UnityEngine;

namespace FH.Game
{
    /// <summary>The game camera's grade (Assets/Resources/Shaders/FHGrade.shader): bloom, exposure, a filmic curve,
    /// tints, saturation, contrast and a vignette. Outdoors it is light (a soft bloom and vignette, colours as they were);
    /// in the Warden's crypt it is the lair pictures' look: bright fire bloom, deep warm shadows, a filmic curve.
    /// GameSession picks the profile each frame.</summary>
    [RequireComponent(typeof(Camera))]
    public class CameraGrade : MonoBehaviour
    {
        public bool Crypt;
        Material mat;
        float blend;                                   // 0 outdoors .. 1 crypt, eased so a doorway does not pop
        readonly RenderTexture[] chain = new RenderTexture[6];
        static readonly int Threshold = Shader.PropertyToID("_Threshold"), Knee = Shader.PropertyToID("_Knee"), Intensity = Shader.PropertyToID("_BloomIntensity"),
            Exposure = Shader.PropertyToID("_Exposure"), Tonemap = Shader.PropertyToID("_Tonemap"), Saturation = Shader.PropertyToID("_Saturation"),
            Contrast = Shader.PropertyToID("_Contrast"), Vignette = Shader.PropertyToID("_Vignette"), ShadowTint = Shader.PropertyToID("_ShadowTint"),
            HighTint = Shader.PropertyToID("_HighTint"), Bloom = Shader.PropertyToID("_Bloom");

        /// <summary>False when the shader is missing (the camera then draws as before).</summary>
        public bool Working => mat;

        void Awake()
        {
            var sh = Resources.Load<Shader>("Shaders/FHGrade");
            if (sh && sh.isSupported) mat = new Material(sh) { hideFlags = HideFlags.HideAndDontSave };
            GetComponent<Camera>().allowHDR = true;    // the fires are brighter than white: that is what blooms
        }

        void OnDestroy() { if (mat) Destroy(mat); }

        void OnRenderImage(RenderTexture src, RenderTexture dst)
        {
            if (!mat) { Graphics.Blit(src, dst); return; }
            blend = Mathf.MoveTowards(blend, Crypt ? 1 : 0, Time.unscaledDeltaTime * 1.5f);
            float k = blend;
            mat.SetFloat(Threshold, Mathf.Lerp(1.1f, .85f, k));
            mat.SetFloat(Knee, .5f);
            mat.SetFloat(Intensity, Mathf.Lerp(.22f, .85f, k));
            mat.SetFloat(Exposure, Mathf.Lerp(1f, 1.45f, k));
            mat.SetFloat(Tonemap, k);
            mat.SetFloat(Saturation, Mathf.Lerp(1.04f, 1.12f, k));
            mat.SetFloat(Contrast, Mathf.Lerp(1.03f, 1.12f, k));
            mat.SetFloat(Vignette, Mathf.Lerp(.18f, .55f, k));
            mat.SetColor(ShadowTint, Color.Lerp(Color.white, new Color(.82f, .86f, 1.05f), k));   // cool, deep shadows
            mat.SetColor(HighTint, Color.Lerp(Color.white, new Color(1.08f, .97f, .86f), k));     // warm firelight

            int w = Mathf.Max(1, src.width / 2), h = Mathf.Max(1, src.height / 2), n = 0;
            var fmt = src.format;
            for (; n < chain.Length && w >= 4 && h >= 4; n++, w /= 2, h /= 2)
                chain[n] = RenderTexture.GetTemporary(w, h, 0, fmt);
            if (n == 0) { Graphics.Blit(src, dst, mat, 3); return; }
            Graphics.Blit(src, chain[0], mat, 0);
            for (int i = 1; i < n; i++) Graphics.Blit(chain[i - 1], chain[i], mat, 1);
            for (int i = n - 2; i >= 0; i--) Graphics.Blit(chain[i + 1], chain[i], mat, 2);   // added onto the larger level
            mat.SetTexture(Bloom, chain[0]);
            Graphics.Blit(src, dst, mat, 3);
            for (int i = 0; i < n; i++) { RenderTexture.ReleaseTemporary(chain[i]); chain[i] = null; }
        }
    }
}
