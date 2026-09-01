namespace BLUnion.Models;

public enum SourceMethod
{
    Duty,
    OpenWorld,
    Levequest,
    DungeonTrash,
    MaskedCarnivale,

    Totem,

    TotemSpellCount,

    TotemLevel,

    MaskedCarnivaleProgress,

    StartingSpell,
}

public static class SourceMethodExtensions
{
    public static bool IsTotemRelated(this SourceMethod method) =>
        method is SourceMethod.Totem or SourceMethod.TotemSpellCount or SourceMethod.TotemLevel;

    public static string GetDisplayName(this SourceMethod method) => method switch
    {
        SourceMethod.Duty => "Duty",
        SourceMethod.OpenWorld => "Open World",
        SourceMethod.Levequest => "Levequest",
        SourceMethod.DungeonTrash => "Dungeon-Trash",
        SourceMethod.MaskedCarnivale => "Masked Carnivale",
        SourceMethod.Totem => "Totem",
        SourceMethod.TotemSpellCount => "Totem (nach Spell-Anzahl)",
        SourceMethod.TotemLevel => "Totem (nach Level)",
        SourceMethod.MaskedCarnivaleProgress => "Masked Carnivale (Fortschritt)",
        SourceMethod.StartingSpell => "Von Anfang an bekannt",
        _ => method.ToString(),
    };
}
