using UnityEngine;

namespace FH.Game
{
    /// <summary>The great horned-skull door between the Warden's hallway and its lair: closed until the hunter pushes it,
    /// then both leaves swing inward (their colliders with them).</summary>
    public class GreatDoor : Interactable
    {
        public Transform LeftHinge, RightHinge;
        public bool IsOpen { get; private set; }
        float openedAt = -1;
        const float OpenSeconds = 1.8f, OpenAngle = 84;

        public override string Prompt(GameSession s) => "Push open the great door";
        public override bool Available(GameSession s) => !IsOpen && base.Available(s);

        public override void Use(GameSession s) => Open();

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true; openedAt = Time.time;
            WorldKit.Sound(transform.position, "hit", .5f);
            GamePad.Rumble(.25f, .35f, .5f);
            if (GameSession.I && GameSession.I.CameraRig) GameSession.I.CameraRig.Shake(.12f);
        }

        void Update()
        {
            if (openedAt < 0 || !LeftHinge || !RightHinge) return;
            float u = Mathf.SmoothStep(0, 1, Mathf.Clamp01((Time.time - openedAt) / OpenSeconds));
            LeftHinge.localRotation = Quaternion.Euler(0, -OpenAngle * u, 0);       // both leaves swing into the lair
            RightHinge.localRotation = Quaternion.Euler(0, OpenAngle * u, 0);
            if (u >= 1) openedAt = -1;
        }
    }
}
