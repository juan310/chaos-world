using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    /// <summary>The Warden's crypt as the lair pictures show it, while the hunter is inside: every brazier lit per pixel
    /// (the quality preset's four left most of them as flat orange smudges on the walls), soft shadows from the four
    /// fires nearest the camera (point-light shadows cost six views each, so the rest go without), the wet floor's
    /// reflection probes drawn once the fires are lit, and the camera's crypt grade. Built with the crypt
    /// (WardenLair.Build); everything goes back as it was when the hunter climbs out.</summary>
    public class CryptLook : MonoBehaviour
    {
        public List<Light> Fires = new List<Light>();
        public List<ReflectionProbe> Probes = new List<ReflectionProbe>();
        public const int ShadowedFires = 4, PixelLights = 14;
        public bool Inside { get; private set; }
        public int Shadowed { get; private set; }
        int defaultPixelLights = -1;
        bool probesDrawn;
        float next;

        void Update()
        {
            var s = GameSession.I;
            if (!s || !s.Hunter || s.World == null) return;
            bool inside = s.World.IsInCrypt(s.Hunter.transform.position);
            if (inside != Inside)
            {
                Inside = inside;
                if (defaultPixelLights < 0) defaultPixelLights = QualitySettings.pixelLightCount;
                QualitySettings.pixelLightCount = inside ? Mathf.Max(defaultPixelLights, PixelLights) : defaultPixelLights;
                if (!inside) foreach (var l in Fires) if (l) { l.shadows = LightShadows.None; l.renderMode = LightRenderMode.Auto; }
                next = 0;
            }
            var grade = s.CameraRig && s.CameraRig.Camera ? s.CameraRig.Camera.GetComponent<CameraGrade>() : null;
            if (grade) grade.Crypt = inside;
            if (!inside || Time.unscaledTime < next) return;
            next = Time.unscaledTime + .25f;
            var cam = s.CameraRig.Camera.transform.position;
            var order = Fires.Where(l => l).OrderBy(l => (l.transform.position - cam).sqrMagnitude).ToList();
            Shadowed = 0;
            for (int i = 0; i < order.Count; i++)
            {
                bool shadow = i < ShadowedFires;
                order[i].shadows = shadow ? LightShadows.Soft : LightShadows.None;
                order[i].shadowStrength = .85f; order[i].shadowBias = .04f;
                order[i].renderMode = i < PixelLights ? LightRenderMode.ForcePixel : LightRenderMode.Auto;
                if (shadow) Shadowed++;
            }
            if (!probesDrawn) { probesDrawn = true; foreach (var p in Probes) if (p) p.RenderProbe(); }
        }

        void OnDisable()
        {
            if (defaultPixelLights >= 0) QualitySettings.pixelLightCount = defaultPixelLights;
        }
    }
}
