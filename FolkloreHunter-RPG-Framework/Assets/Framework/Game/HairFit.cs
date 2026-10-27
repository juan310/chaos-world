using System.Linq;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Puts one of the ten hairstyles (HunterHair, the Codex prototype's hair builder) on a slim hunter's head:
    /// sized from the head's own vertices (those weighted to the head bone near the top of the body), anchored to the
    /// head bone so it follows every clip. The body must still be in its bind pose.</summary>
    public static class HairFit
    {
        public static GameObject Wear(Transform visual, Transform root, bool female, int style)
        {
            var head = WorldKit.Find(visual, "head");
            var skin = visual.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(s => s.bones.Contains(head));
            if (!head || !skin) return null;
            float scale = Mathf.Max(.5f, skin.bounds.size.y / 1.8f);       // Codex sized hair for 1.8 m hunters
            var hb = HeadBounds(skin, head, scale);
            var center = hb.center; center.y = hb.max.y - .112f * scale;
            var size = new Vector3(Mathf.Clamp(hb.extents.x, .07f * scale, .13f * scale) + .004f, .125f * scale, Mathf.Clamp(hb.extents.z, .07f * scale, .12f * scale) + .005f);
            var anchor = new GameObject("Hair anchor").transform;
            anchor.SetParent(head, false); anchor.position = center; anchor.rotation = root.rotation;
            anchor.localScale = Vector3.one / head.lossyScale.x;
            return HunterHair.Create(female, ((style % 10) + 10) % 10, size, anchor);
        }

        static Bounds HeadBounds(SkinnedMeshRenderer skin, Transform headBone, float scale)
        {
            var mesh = skin.sharedMesh;
            var vertices = mesh.vertices; var weights = mesh.boneWeights;
            var matrices = skin.bones.Select((bone, i) => bone.localToWorldMatrix * mesh.bindposes[i]).ToArray();
            int head = System.Array.IndexOf(skin.bones, headBone);
            float top = skin.bounds.max.y;
            bool any = false; var result = new Bounds();
            for (int i = 0; i < vertices.Length && weights.Length == vertices.Length; i++)
            {
                var w = weights[i];
                float value = (w.boneIndex0 == head ? w.weight0 : 0) + (w.boneIndex1 == head ? w.weight1 : 0) + (w.boneIndex2 == head ? w.weight2 : 0) + (w.boneIndex3 == head ? w.weight3 : 0);
                if (value < .45f) continue;
                var p = matrices[w.boneIndex0].MultiplyPoint3x4(vertices[i]) * w.weight0;
                if (w.weight1 > 0) p += matrices[w.boneIndex1].MultiplyPoint3x4(vertices[i]) * w.weight1;
                if (w.weight2 > 0) p += matrices[w.boneIndex2].MultiplyPoint3x4(vertices[i]) * w.weight2;
                if (w.weight3 > 0) p += matrices[w.boneIndex3].MultiplyPoint3x4(vertices[i]) * w.weight3;
                if (p.y < top - .22f * scale) continue;
                if (!any) { result = new Bounds(p, Vector3.zero); any = true; } else result.Encapsulate(p);
            }
            if (!any) result = new Bounds(headBone.position + Vector3.up * .1f * scale, new Vector3(.19f, .22f, .18f) * scale);
            return result;
        }
    }
}
