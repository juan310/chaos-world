using UnityEngine;

namespace FH.Game
{
    /// <summary>Swings the cottage's hinged door mesh open as the hunter approaches (hinge axis respects the import rotation).</summary>
    public class CottageDoor : MonoBehaviour
    {
        public Transform Door;
        Quaternion closed;
        bool ready;

        void Start() { if (Door) { closed = Door.localRotation; ready = true; } }

        void Update()
        {
            if (!ready || !GameSession.I || !GameSession.I.Hunter) return;
            float d = Vector3.Distance(GameSession.I.Hunter.transform.position, Door.position);
            float angle = d < 2.6f && GameSession.I.Mode == SessionMode.Playing ? 100 : 0;
            var target = Quaternion.AngleAxis(angle, Door.parent.InverseTransformDirection(Vector3.up)) * closed;
            Door.localRotation = Quaternion.Slerp(Door.localRotation, target, Time.deltaTime * 4);
        }
    }
}
