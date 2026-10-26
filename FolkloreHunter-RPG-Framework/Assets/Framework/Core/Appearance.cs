using System;

namespace FH.Core
{
    /// <summary>The hunter's look, chosen when a new game starts: the male or female hunter from the main character
    /// sheet with one of ten hairstyles each, or the classic KayKit knight. Names follow the hairstyle sheets.</summary>
    public static class Appearance
    {
        public const string Male = "male", Female = "female", Classic = "classic";
        public static readonly string[] MaleHair =
            { "Tousled Crop", "Side-Swept", "Short Curls", "Undercut", "Shoulder-Length", "Low Ponytail", "Topknot", "Warrior Braid", "Slicked Back", "Buzz Cut" };
        public static readonly string[] FemaleHair =
            { "Shoulder Waves", "Pixie Cut", "Straight Bob", "Long Straight", "High Ponytail", "Low Ponytail", "Side Braid", "Twin Braids", "Messy Bun", "Half-Up Braid" };
        /// <summary>Default hunter names offered (the player can type their own).</summary>
        public static readonly string[] MaleNames = { "Aldric", "Bram", "Corin", "Darian", "Edric", "Fenn", "Garrick", "Hale", "Ivo", "Joren" };
        public static readonly string[] FemaleNames = { "Aveline", "Brielle", "Cora", "Daria", "Elowen", "Fiora", "Gwen", "Isolde", "Maren", "Wren" };

        public static string[] Hairstyles(string body) => body == Female ? FemaleHair : MaleHair;

        public static int ClampHair(int hair) => Math.Max(1, Math.Min(10, hair));

        /// <summary>Model resources to try in order: the chosen hairstyle, the body's base model, the classic hunter.
        /// Saves from before 0.9 (no body recorded) keep the classic hunter.</summary>
        public static string[] Models(string body, int hair)
        {
            if (body != Male && body != Female) return new[] { "Hunter" };
            string b = body == Male ? "Male" : "Female";
            return new[] { "Hunter_" + b + "_" + ClampHair(hair).ToString("00"), "Hunter_" + b, "Hunter" };
        }

        public static string Describe(string body, int hair)
            => body == Male || body == Female ? (body == Male ? "Male hunter, " : "Female hunter, ") + Hairstyles(body)[ClampHair(hair) - 1] : "Classic hunter";

        /// <summary>A name cleaned for the save and the HUD: printable, trimmed, at most 18 characters; empty becomes "Hunter".</summary>
        public static string CleanName(string name)
        {
            var sb = new System.Text.StringBuilder();
            foreach (char c in (name ?? "").Trim())
                if (!char.IsControl(c) && c != '<' && c != '>' && sb.Length < 18) sb.Append(c);
            var s = sb.ToString().Trim();
            return s.Length == 0 ? "Hunter" : s;
        }
    }
}
