using System;
using System.Linq;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Instantiates the rigged KayKit characters exactly as the sample did: scaled from the
    /// FBX_SCALE_ALL export to a target height, feet on the ground, one atlas material.</summary>
    public static class CharacterFactory
    {
        public static Material Atlas(string resource)
        {
            var mat = WorldKit.Mat(resource + " Atlas", Color.white);
            if (!mat.mainTexture) mat.mainTexture = Resources.Load<Texture2D>("Models/" + resource + "_texture");
            return mat;
        }

        public static bool Exists(string resource) => !string.IsNullOrEmpty(resource) && Resources.Load<GameObject>("Models/" + resource) != null;

        public static Transform Create(string resource, Transform parent, float targetHeight, Color? tint = null)
        {
            var prefab = Resources.Load<GameObject>("Models/" + resource);
            if (!prefab) throw new Exception("Missing character model " + resource);
            var o = UnityEngine.Object.Instantiate(prefab, parent);
            o.name = resource + " visual";
            o.transform.localPosition = Vector3.zero;
            o.transform.localRotation = Quaternion.identity;
            var renderers = o.GetComponentsInChildren<Renderer>();
            var body = renderers.Where(x => !x.name.Contains("Sword") && !x.name.Contains("Shield") && !x.name.Contains("Staff") && !x.name.Contains("Wand")).ToArray();
            Bounds b = body[0].bounds;
            foreach (var rr in body.Skip(1)) b.Encapsulate(rr.bounds);
            float k = targetHeight / Mathf.Max(.1f, b.size.y);
            float bottom = b.min.y - parent.position.y;
            o.transform.localScale = Vector3.one * k;
            o.transform.localPosition = Vector3.up * (-bottom * k);
            var mat = Atlas(resource);
            if (tint.HasValue)
            {
                mat = WorldKit.Mat(resource + " Atlas " + ColorUtility.ToHtmlStringRGB(tint.Value), tint.Value);
                if (!mat.mainTexture) mat.mainTexture = Resources.Load<Texture2D>("Models/" + resource + "_texture");
            }
            foreach (var rr in renderers) rr.sharedMaterials = Enumerable.Repeat(mat, rr.sharedMaterials.Length).ToArray();
            foreach (var smr in o.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
            return o.transform;
        }

        public static GameObject Actor(string name, Vector3 position, float height, float radius)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.layer = Layers.Actor;
            var cc = go.AddComponent<CharacterController>();
            cc.height = height * .95f; cc.radius = radius; cc.center = Vector3.up * height * .475f;
            cc.stepOffset = .4f; cc.slopeLimit = 48; cc.skinWidth = .04f;
            return go;
        }
    }
}
