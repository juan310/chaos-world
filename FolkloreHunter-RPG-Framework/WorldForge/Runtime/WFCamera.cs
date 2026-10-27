using UnityEngine;

namespace FH.WorldForge
{
    /// <summary>A third-person camera like the game's: behind and above the hunter, the mouse turning it, a sphere cast
    /// pulling it in front of any wall between it and the hunter (tight tunnels). It reports, every frame, how far walls
    /// pushed it in, whether the line to the hunter's head is still blocked, and whether the camera itself is inside rock.
    /// Auto (the tester) swings it behind the hunter's heading.</summary>
    [RequireComponent(typeof(Camera))]
    public class WFCamera : MonoBehaviour
    {
        public Transform Target;
        public float Distance = 4.2f, Height = 1.55f, Yaw, Pitch = 14;
        public bool Auto;
        public float PulledIn; public bool LineBlocked, InsideRock;
        const int Mask = Physics.DefaultRaycastLayers;

        void Start()
        {
            var cam = GetComponent<Camera>(); cam.nearClipPlane = .1f; cam.farClipPlane = 600; cam.fieldOfView = 60;
            cam.depthTextureMode |= DepthTextureMode.Depth;
            if (Target) Yaw = Target.eulerAngles.y;
        }

        void LateUpdate()
        {
            if (!Target) return;
            if (Auto) { Yaw = Mathf.LerpAngle(Yaw, Target.eulerAngles.y, 1 - Mathf.Exp(-3 * Time.deltaTime)); Pitch = Mathf.Lerp(Pitch, 14, Time.deltaTime); }
            else
            {
                Yaw += Input.GetAxis("Mouse X") * 3f; Pitch = Mathf.Clamp(Pitch - Input.GetAxis("Mouse Y") * 2f, -25, 65);
                Distance = Mathf.Clamp(Distance - Input.GetAxis("Mouse ScrollWheel") * 3, 1.5f, 9);
            }
            var pivot = Target.position + Vector3.up * Height;
            var dir = Quaternion.Euler(Pitch, Yaw, 0) * Vector3.back;
            float d = Distance;
            if (Physics.SphereCast(pivot, .22f, dir, out var h, Distance, Mask, QueryTriggerInteraction.Ignore)) d = Mathf.Max(.35f, h.distance - .05f);
            PulledIn = Distance - d;
            transform.position = pivot + dir * d;
            transform.rotation = Quaternion.LookRotation(pivot - transform.position);
            LineBlocked = Physics.Linecast(transform.position, Target.position + Vector3.up * 1.6f, Mask, QueryTriggerInteraction.Ignore);
            InsideRock = Physics.CheckSphere(transform.position, .1f, Mask, QueryTriggerInteraction.Ignore);
        }
    }
}
