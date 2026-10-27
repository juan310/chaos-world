using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>The test character: a CharacterController with the Folklore Hunter player's limits (height 1.8 m,
    /// radius .35, step .4 m, slope 48 degrees) and speeds (walk 1.6, jog 3.6, sprint 5.2 m/s), wearing the slim hunter
    /// model with its own idle, walk, jog and sprint clips (legacy animation, played at the speed that keeps its feet
    /// planted). Driven by the keyboard (WASD, Shift to sprint, Ctrl to walk) or, with Auto, by Stick/Run from the tester.</summary>
    [RequireComponent(typeof(CharacterController))]
    public class WFWalker : MonoBehaviour
    {
        public const float WalkSpeed = 1.6f, JogSpeed = 3.6f, SprintSpeed = 5.2f;
        public Vector2 Stick; public bool Run, Slow, Auto;
        public Vector3 AutoDir;                 // Auto: the direction to go (world, flat)
        public Transform Visual, BallL, BallR, FootL, FootR, CameraT;
        public Animation Anim;
        public Vector3 Velocity; public bool Grounded;
        public string Clip = "";
        CharacterController cc; float vy;
        readonly Dictionary<string, (string clip, float natural)> gaits = new Dictionary<string, (string, float)>();

        public static WFWalker Spawn(Vector3 at, float yaw)
        {
            var go = new GameObject("Test hunter"); go.layer = 2;
            go.transform.SetPositionAndRotation(at + Vector3.up * .1f, Quaternion.Euler(0, yaw, 0));
            var cc = go.AddComponent<CharacterController>(); cc.height = 1.8f; cc.radius = .35f; cc.center = Vector3.up * .9f;
            cc.stepOffset = .4f; cc.slopeLimit = 48; cc.skinWidth = .04f; cc.minMoveDistance = 0;
            var w = go.AddComponent<WFWalker>();
            var prefab = Resources.Load<GameObject>("WorldForgeTest/Hunter");
            if (prefab)
            {
                var v = Instantiate(prefab, go.transform); v.name = "Visual"; w.Visual = v.transform;
                foreach (var t in v.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 2;
                var tex = Resources.Load<Texture2D>("WorldForgeTest/Hunter_texture");
                var mat = new Material(Shader.Find("WorldForge/Prop")) { mainTexture = tex }; mat.SetFloat("_Smoothness", .25f);
                foreach (var r in v.GetComponentsInChildren<Renderer>(true))
                {
                    bool body = r is SkinnedMeshRenderer && r.name.EndsWith("_Body");
                    r.enabled = body; r.sharedMaterials = Enumerable.Repeat(mat, r.sharedMaterials.Length).ToArray();
                    if (body && r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
                }
                // sized to 1.8 m from its bind pose
                var body0 = v.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(r => r.name.EndsWith("_Body"));
                if (body0 && body0.sharedMesh) { float h = body0.sharedMesh.bounds.size.y * body0.transform.lossyScale.y; if (h > .3f) v.transform.localScale *= 1.8f / h; }
                w.Anim = v.GetComponentInChildren<Animation>();
                Transform F(string n) => v.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == n);
                w.FootL = F("foot.l"); w.FootR = F("foot.r"); w.BallL = F("toes.l") ?? w.FootL; w.BallR = F("toes.r") ?? w.FootR;
            }
            else
            {
                var cap = GameObject.CreatePrimitive(PrimitiveType.Capsule); WFEffects.Kill(cap.GetComponent<Collider>());
                cap.transform.SetParent(go.transform, false); cap.transform.localPosition = Vector3.up * .9f; cap.layer = 2; w.Visual = cap.transform;
                Debug.LogWarning("WorldForge: no Resources/WorldForgeTest/Hunter model; a capsule stands in");
            }
            return w;
        }

        void Start()
        {
            cc = GetComponent<CharacterController>();
            if (!Anim) return;
            foreach (AnimationState st in Anim) st.wrapMode = WrapMode.Loop;
            foreach (var (key, names) in new[] { ("idle", new[] { "Idle" }), ("walk", new[] { "Gait_Walk", "Walking_A" }), ("jog", new[] { "Gait_Jog", "Running_A" }), ("sprint", new[] { "Gait_Sprint", "Hunt_Sprint", "Running_A" }) })
            {
                string clip = null;
                foreach (var n in names) { clip = Anim.Cast<AnimationState>().Select(s => s.name).FirstOrDefault(s => s == n || s.EndsWith("|" + n)); if (clip != null) break; }
                if (clip != null) gaits[key] = (clip, key == "idle" ? 0 : Natural(clip));
            }
            Debug.Log("WorldForge walker: " + string.Join(", ", gaits.Select(g => g.Key + " " + g.Value.clip + " " + g.Value.natural.ToString("0.00") + " m/s")));
            Play("idle", 1);
        }

        /// <summary>The ground speed a gait clip was made for: how fast a planted ball of the foot sweeps back.</summary>
        float Natural(string clip)
        {
            var st = Anim[clip]; if (!st || !BallL || !BallR) return 0;
            const int N = 48; var y = new float[2, N + 1]; var z = new float[2, N + 1];
            foreach (AnimationState s in Anim) { s.enabled = false; s.weight = 0; }
            st.enabled = true; st.weight = 1;
            for (int i = 0; i <= N; i++)
            {
                st.time = st.length * i / N; Anim.Sample();
                for (int f = 0; f < 2; f++) { var p = transform.InverseTransformPoint((f == 0 ? BallL : BallR).position); y[f, i] = p.y; z[f, i] = p.z; }
            }
            st.enabled = false;
            float sum = 0; int n = 0;
            for (int f = 0; f < 2; f++)
            {
                float lo = 1e9f; for (int i = 0; i <= N; i++) lo = Mathf.Min(lo, y[f, i]);
                for (int i = 0; i < N; i++) if (y[f, i] < lo + .03f && y[f, i + 1] < lo + .03f && z[f, i + 1] < z[f, i]) { sum += z[f, i] - z[f, i + 1]; n++; }
            }
            return n >= 4 ? sum / (n * st.length / N) : 0;
        }

        void Play(string gait, float speed)
        {
            if (!Anim || !gaits.TryGetValue(gait, out var g)) return;
            if (Clip != g.clip) { Anim.CrossFade(g.clip, .15f); Clip = g.clip; }
            var st = Anim[g.clip]; if (st) st.speed = g.natural > .1f ? Mathf.Clamp(speed / g.natural, .4f, 2.5f) : 1;
        }

        void Update()
        {
            if (!cc) return;
            if (!Auto)
            {
                Stick = new Vector2((Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ? 1 : 0) - (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow) ? 1 : 0),
                                    (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ? 1 : 0) - (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow) ? 1 : 0));
                Stick += new Vector2(Input.GetAxis("Horizontal"), Input.GetAxis("Vertical"));
                Stick = Vector2.ClampMagnitude(Stick, 1);
                Run = Input.GetKey(KeyCode.LeftShift); Slow = Input.GetKey(KeyCode.LeftControl);
            }
            float yaw = CameraT ? CameraT.eulerAngles.y : transform.eulerAngles.y;
            var dir = Auto ? Vector3.ClampMagnitude(new Vector3(AutoDir.x, 0, AutoDir.z), 1) : Quaternion.Euler(0, yaw, 0) * new Vector3(Stick.x, 0, Stick.y);
            float speed = dir.magnitude < .1f ? 0 : Slow ? WalkSpeed : Run ? SprintSpeed : JogSpeed;
            if (dir.sqrMagnitude > .01f) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(dir), 720 * Time.deltaTime);
            Grounded = cc.isGrounded;
            vy = Grounded ? -2f : vy - 20f * Time.deltaTime;
            var move = transform.forward * speed + Vector3.up * vy;
            var before = transform.position;
            cc.Move(move * Time.deltaTime);
            Velocity = Time.deltaTime > 0 ? (transform.position - before) / Time.deltaTime : Vector3.zero;
            float flat = new Vector2(Velocity.x, Velocity.z).magnitude;
            if (speed < .1f) Play("idle", 1); else Play(Slow ? "walk" : Run ? "sprint" : "jog", Mathf.Max(flat, .3f));
        }
    }
}
