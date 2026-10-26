using FH.UI;

namespace FH.Game
{
    /// <summary>The Proving Grounds desk: spawn test monsters, toggle training options, leave.</summary>
    public class ProvingLedger : Interactable
    {
        public override string Prompt(GameSession s) => "Proving ledger  (monsters, options, leave)";
        public override bool Available(GameSession s) => s.Proving && gameObject.activeInHierarchy;
        public override void Use(GameSession s) => s.Ui.Push(new ProvingScreen());
    }
}
