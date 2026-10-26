namespace FH.Game
{
    /// <summary>Interact with the Briarhound to ride it. With the weapon drawn, X sheathes first (as in Monster Hunter);
    /// only a sheathed hunter gets the ride prompt, so sheathing next to the hound never mounts it by accident.</summary>
    public class HoundMount : Interactable
    {
        public HoundCompanion Hound;
        public override string Prompt(GameSession s) => "Ride the " + HoundCompanion.DisplayName;
        public override bool Available(GameSession s) => Hound && Hound.CanMount && gameObject.activeInHierarchy && !(s.Hunter.Hunt && !s.Hunter.Hunt.Sheathed);
        public override void Use(GameSession s) => Hound.Mount();
    }
}
