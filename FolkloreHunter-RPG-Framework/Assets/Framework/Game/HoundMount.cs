namespace FH.Game
{
    /// <summary>Interact with the Cu-sith to ride it (the weapon is sheathed on the way up).</summary>
    public class HoundMount : Interactable
    {
        public HoundCompanion Hound;
        public override string Prompt(GameSession s) => "Ride the Cu-sith";
        public override bool Available(GameSession s) => Hound && Hound.CanMount && gameObject.activeInHierarchy;
        public override void Use(GameSession s) => Hound.Mount();
    }
}
