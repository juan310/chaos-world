using System;
using System.Collections.Generic;
using System.Linq;
using FH.Core;
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

        /// <summary>The model a monster is shown with: its own, or the older one it replaced when the new one is not in the build.</summary>
        public static string MonsterModel(MonsterDef d) => Exists(d.model) || string.IsNullOrEmpty(d.fallbackModel) ? d.model : d.fallbackModel;

        public static Transform Create(string resource, Transform parent, float targetHeight, Color? tint = null)
        {
            var prefab = Resources.Load<GameObject>("Models/" + resource);
            if (!prefab) throw new Exception("Missing character model " + resource);
            if(Resources.Load<Material>("Monsters/"+resource))return CreateCreature(resource,prefab,parent,targetHeight);
            var o = UnityEngine.Object.Instantiate(prefab, parent);
            o.name = resource + " visual";
            o.transform.localPosition = Vector3.zero;
            o.transform.localRotation = Quaternion.identity;
            var renderers = o.GetComponentsInChildren<Renderer>();
            var body = renderers.Where(x => !IsWeapon(x.name)).ToArray();
            // (a slim body is sized by its skinned mesh alone: a bone-held piece such as the Warden's helmet sits where the file's
            // default pose puts its bone, which need not be the rest pose)
            var skinned = body.OfType<SkinnedMeshRenderer>().ToArray<Renderer>();
            Bounds b = Slim(resource) ? RestBounds(skinned.Length > 0 ? skinned : body) : body[0].bounds;
            if (!Slim(resource)) foreach (var rr in body.Skip(1)) b.Encapsulate(rr.bounds);
            float k = targetHeight / Mathf.Max(.1f, b.size.y);
            float bottom = b.min.y - parent.position.y;
            o.transform.localScale = Vector3.one * k;
            o.transform.localPosition = Vector3.up * (-bottom * k);
            var creatureMat = Resources.Load<Material>("Monsters/" + resource);
            var mat = creatureMat ? creatureMat : Atlas(resource);
            if (tint.HasValue && !creatureMat)
            {
                mat = WorldKit.Mat(resource + " Atlas " + ColorUtility.ToHtmlStringRGB(tint.Value), tint.Value);
                if (!mat.mainTexture) mat.mainTexture = Resources.Load<Texture2D>("Models/" + resource + "_texture");
            }
            // A new hunter body (Hunter_<look>) has its own painted texture; its weapons keep the KayKit atlas.
            var weaponMat = resource.StartsWith("Hunter_") || resource.StartsWith("Skel_") ? Atlas("Hunter") : mat;   // the hunt pieces' atlas
            foreach (var rr in renderers)
            {
                // A piece with a texture of its own (a weapon modelled from the concept art: Models/<piece>_texture) wears it.
                var own = !creatureMat && Resources.Load<Texture2D>("Models/" + rr.name + "_texture") ? Atlas(rr.name) : null;
                rr.sharedMaterials = Enumerable.Repeat(own ? own : IsWeapon(rr.name) ? weaponMat : mat, rr.sharedMaterials.Length).ToArray();
            }
            foreach (var smr in o.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
            // fingers closed round what each hand holds (a slim body built with finger bones and its grips file)
            if (Slim(resource)) HandGrip.Attach(o, resource);
            return o.transform;
        }

        /// <summary>The slim characters (Tools/slim): their clips roll, dive and lie down, and Unity's import bounds cover every
        /// clip, so their size is measured on the mesh in its bind pose (the rest pose they are made in).</summary>
        /// <summary>A body with modelled hair (its build wrote Models/&lt;model&gt;_info.json with "ownHair"): no hairstyle is added.</summary>
        public static bool OwnHair(string resource)
        {
            var t = Resources.Load<TextAsset>("Models/" + resource + "_info");
            return t && System.Text.RegularExpressions.Regex.IsMatch(t.text, "\"ownHair\"\\s*:\\s*true");
        }

        static bool Slim(string resource) => resource.EndsWith("_Slim") || resource.StartsWith("Skel_") || resource.StartsWith("Villager_");

        /// <summary>The meshes' bounds in their bind pose, in world space (independent of the clip playing).</summary>
        public static Bounds RestBounds(Renderer[] body)
        {
            bool any = false; var b = new Bounds();
            foreach (var r in body)
            {
                var m = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>() ? r.GetComponent<MeshFilter>().sharedMesh : null;
                if (!m) continue;
                foreach (var v in m.vertices) { var p = r.transform.TransformPoint(v); if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p); }
            }
            return any ? b : body[0].bounds;
        }

        /// <summary>The bounds of the meshes as they are posed this frame (skinned meshes baked); Unity's own skinned
        /// bounds are looser than the body.</summary>
        public static Bounds PosedBounds(IEnumerable<Renderer> renderers)
        {
            bool any = false; var b = new Bounds();
            foreach (var r in renderers)
            {
                if (!r.enabled) continue;
                Bounds rb;
                if (r is SkinnedMeshRenderer smr) rb = ActualBounds(smr);
                else if (r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh)
                {
                    var v = r.GetComponent<MeshFilter>().sharedMesh.vertices; rb = new Bounds(r.transform.TransformPoint(v[0]), Vector3.zero);
                    foreach (var p in v) rb.Encapsulate(r.transform.TransformPoint(p));
                }
                else continue;
                if (!any) { b = rb; any = true; } else b.Encapsulate(rb);
            }
            return b;
        }

        static bool IsWeapon(string n) => n.Contains("Sword") || n.Contains("Shield") || n.Contains("Staff") || n.Contains("Wand") || n.StartsWith("Hunt_");

        static Transform CreateCreature(string resource,GameObject prefab,Transform parent,float height) {
            // A non-animated wrapper owns scale/ground alignment; FBX root animation cannot overwrite it.
            var wrapper=new GameObject(resource+" visual").transform;wrapper.SetParent(parent,false);
            var model=UnityEngine.Object.Instantiate(prefab,wrapper);
            model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;
            var material=Resources.Load<Material>("Monsters/"+resource);
            if(resource=="Briarhound")material=BriarCoat(model)??material;
            foreach(var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>()){
                skin.sharedMaterials=Enumerable.Repeat(material,skin.sharedMaterials.Length).ToArray();skin.updateWhenOffscreen=true;
            }
            var driver=new AnimationDriver(model.transform,resource);
            foreach(AnimationState state in driver.Anim){state.enabled=false;state.weight=0;}
            var idle=driver.State("Idle");if(idle!=null){idle.enabled=true;idle.weight=1;idle.time=0;driver.Anim.Sample();}
            var bounds=ActualBounds(wrapper.gameObject);
            wrapper.localScale*=height/Mathf.Max(.01f,bounds.size.y);
            bounds=ActualBounds(wrapper.gameObject);
            wrapper.position+=Vector3.up*(parent.position.y-bounds.min.y);
            if(idle!=null){idle.enabled=false;idle.weight=0;}
            return wrapper;
        }

        /// <summary>True once the Briarhound wears the repaired coat (Codex's coat repair V2, Docs/Briarhound-Coat.md).</summary>
        public static bool BriarCoatOn { get; private set; }

        /// <summary>Codex's Briarhound coat repair: the same mesh with its rest positions, normals and coat mask in spare
        /// vertex channels (Monsters/Briarhound_FurMeshV2), drawn by the fur shader that lays a matte fur tile over the
        /// smeared coat (charcoal, cream chest, tan legs) and keeps the face, saddle and gear from the original atlas.</summary>
        static Material BriarCoat(GameObject model)
        {
            var repaired = Resources.Load<Mesh>("Monsters/Briarhound_FurMeshV2");
            var fur = Resources.Load<Material>("Monsters/Briarhound_FurV2");
            BriarCoatOn = false;
            var shader = Shader.Find("Folklore/BriarhoundFurRepair");          // Framework/Resources/Shaders (Codex's, unchanged)
            if (!repaired || !fur || !shader || !shader.isSupported) return null;
            foreach (var skin in model.GetComponentsInChildren<SkinnedMeshRenderer>())
                if (skin.sharedMesh && skin.sharedMesh.vertexCount == repaired.vertexCount) { skin.sharedMesh = repaired; BriarCoatOn = true; }
            if (!BriarCoatOn) return null;
            var coat = new Material(fur) { name = "Briarhound coat", shader = shader, mainTexture = Resources.Load<Texture2D>("Monsters/Briarhound_basecolor") };
            if (!coat.GetTexture("_FurTex")) coat.SetTexture("_FurTex", Resources.Load<Texture2D>("Monsters/Briarhound_fur_detail_v2"));
            return coat;
        }

        public static Bounds ActualBounds(GameObject o) => ActualBounds(o.GetComponentsInChildren<SkinnedMeshRenderer>());

        public static Bounds ActualBounds(params SkinnedMeshRenderer[] skins) {
            // Standard skinning in world space, including all 8 influences. Avoid BakeMesh scale ambiguity.
            bool first=true;var result=new Bounds();
            foreach(var skin in skins) {
                var mesh=skin.sharedMesh;var vertices=mesh.vertices;var bind=mesh.bindposes;var bones=skin.bones;
                var matrices=bones.Select((b,i)=>b.localToWorldMatrix*bind[i]).ToArray();
                var counts=mesh.GetBonesPerVertex();var weights=mesh.GetAllBoneWeights();int cursor=0;
                for(int i=0;i<vertices.Length;i++){
                    Vector3 p=Vector3.zero;
                    for(int j=0;j<counts[i];j++){var w=weights[cursor++];p+=matrices[w.boneIndex].MultiplyPoint3x4(vertices[i])*w.weight;}
                    if(counts[i]==0)p=skin.transform.TransformPoint(vertices[i]);
                    if(first){result=new Bounds(p,Vector3.zero);first=false;}else result.Encapsulate(p);
                }
                counts.Dispose();weights.Dispose();
            }
            return result;
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
