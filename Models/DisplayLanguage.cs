namespace BLUnion.Models;

/// <summary>
/// Sprache, in der Spell-Namen in der UI angezeigt werden. Deckt bewusst genau die 4
/// offiziellen FFXIV-Clientsprachen ab (siehe <see cref="Spell.GetName"/>) - mehr gibt
/// weder das Action-Sheet noch <c>Dalamud.Game.ClientLanguage</c> her.
/// </summary>
public enum DisplayLanguage
{
    German,
    English,
    French,
    Japanese,
}
