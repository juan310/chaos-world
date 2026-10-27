using System;
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
        public static string MonsterModel(MonsterDef d)
            => BodyDouble.Exists(d.body) && Exists(d.fallbackModel) ? d.fallbackModel        // the KayKit skeleton, under the Codex body
             : Exists(d.model) || string.IsNullOrEmpty(d.fallbackModel) ? d.model : d.fallbackModel;

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
            Bounds b = body[0].bounds;
            foreach (var rr in body.Skip(1)) b.Encapsulate(rr.bounds);
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
            var weaponMat = resource.StartsWith("Hunter_") ? Atlas("Hunter") : mat;
            foreach (var rr in renderers)
            {
                // A piece with a texture of its own (a weapon modelled from the concept art: Models/<piece>_texture) wears it.
                var own = !creatureMat && Resources.Load<Texture2D>("Models/" + rr.name + "_texture") ? Atlas(rr.name) : null;
                rr.sharedMaterials = Enumerable.Repeat(own ? own : IsWeapon(rr.name) ? weaponMat : mat, rr.sharedMaterials.Length).ToArray();
            }
            foreach (var smr in o.GetComponentsInChildren<SkinnedMeshRenderer>()) smr.updateWhenOffscreen = true;
            return o.transform;
        }

        static bool IsWeapon(string n) => n.Contains("Sword") || n.Contains("Shield") || n.Contains("Staff") || n.Contains("Wand") || n.StartsWith("Hunt_");

        static Transform CreateCreature(string resource,GameObject prefab,Transform parent,float height) {
            // A non-animated wrapper owns scale/ground alignment; FBX root animation cannot overwrite it.
            var wrapper=new GameObject(resource+" visual").transform;wrapper.SetParent(parent,false);
            var model=UnityEngine.Object.Instantiate(prefab,wrapper);
            model.transform.localPosition=Vector3.zero;model.transform.localRotation=Quaternion.identity;
            var material=Resources.Load<Material>("Monsters/"+resource);
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

        public static Bounds ActualBounds(GameObject o) {
            // Standard skinning in world space, including all 8 influences. Avoid BakeMesh scale ambiguity.
            bool first=true;var result=new Bounds();
            foreach(var skin in o.GetComponentsInChildren<SkinnedMeshRenderer>()) {
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
