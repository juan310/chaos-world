using FH.Core;
using UnityEngine;

namespace FH.Game
{
    /// <summary>Third-person orbit camera with lock-on framing and collision so walls never hide the hunter.</summary>
    public class CameraRig : MonoBehaviour
    {
        public Camera Camera;
        public float Yaw = 32, Pitch = 34, Distance = 11;
        public Transform Follow;
        public bool Manual;               // menus and reviews position the camera themselves
        Vector3 target;
        float shake;

        public void Snap()
        {
            if (!Follow) return;
            target = Follow.position + Vector3.up * 1.2f;
            Place(1);
        }

        public void Shake(float amount) => shake = Mathf.Max(shake, amount);

        void LateUpdate()
        {
            if (Manual || !Follow || !Camera) return;
            var look = InputRouter.Look();
            Yaw += look.x;
            Pitch = Mathf.Clamp(Pitch - look.y, 8, 70);
            Distance = Mathf.Clamp(Distance - InputRouter.Zoom() * 1.2f, 5, 20);
            var session = GameSession.I;
            var hunter = session ? session.Hunter : null;
            Vector3 desired = Follow.position + Vector3.up * 1.2f;
            if (hunter && hunter.LockTarget && hunter.LockTarget.Alive)
            {
                var t = hunter.LockTarget.transform.position;
                desired = Vector3.Lerp(desired, t + Vector3.up * 1.2f, .35f);
                var away = Follow.position - t; away.y = 0;
                if (away.sqrMagnitude > .01f)
                {
                    float wantYaw = Mathf.Atan2(away.x, -away.z) * Mathf.Rad2Deg;
                    Yaw = Mathf.LerpAngle(Yaw, wantYaw, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 3.5f));
                }
            }
            target = Vector3.Lerp(target, desired, 1 - Mathf.Exp(-Time.unscaledDeltaTime * 8));
            Place(1 - Mathf.Exp(-Time.unscaledDeltaTime * 10));
        }

        void Place(float blend)
        {
            float y = Yaw * Mathf.Deg2Rad, p = Pitch * Mathf.Deg2Rad;
            Vector3 dir = new Vector3(Mathf.Sin(y) * Mathf.Cos(p), Mathf.Sin(p), -Mathf.Cos(y) * Mathf.Cos(p));
            float d = Distance;
            if (Physics.SphereCast(target, .3f, dir, out var hit, Distance, Layers.WorldMask, QueryTriggerInteraction.Ignore))
                d = Mathf.Max(1.5f, hit.distance - .2f);
            Vector3 pos = target + dir * d;
            if (shake > 0) { pos += Random.insideUnitSphere * shake * .15f; shake = Mathf.Max(0, shake - Time.unscaledDeltaTime * 1.5f); }
            Camera.transform.position = Vector3.Lerp(Camera.transform.position, pos, blend);
            Camera.transform.rotation = Quaternion.LookRotation(target - Camera.transform.position);
        }
    }

}
