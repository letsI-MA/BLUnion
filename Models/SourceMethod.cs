using BLUnion.Services;

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

    public static string GetDisplayName(this SourceMethod method, DisplayLanguage language) => method switch
    {
        SourceMethod.Duty => UiStrings.Get(UiStrings.Key.SourceMethodDuty, language),
        SourceMethod.OpenWorld => UiStrings.Get(UiStrings.Key.SourceMethodOpenWorld, language),
        SourceMethod.Levequest => UiStrings.Get(UiStrings.Key.SourceMethodLevequest, language),
        SourceMethod.DungeonTrash => UiStrings.Get(UiStrings.Key.SourceMethodDungeonTrash, language),
        SourceMethod.MaskedCarnivale => UiStrings.Get(UiStrings.Key.SourceMethodMaskedCarnivale, language),
        SourceMethod.Totem => UiStrings.Get(UiStrings.Key.SourceMethodTotem, language),
        SourceMethod.TotemSpellCount => UiStrings.Get(UiStrings.Key.SourceMethodTotemSpellCount, language),
        SourceMethod.TotemLevel => UiStrings.Get(UiStrings.Key.SourceMethodTotemLevel, language),
        SourceMethod.MaskedCarnivaleProgress => UiStrings.Get(UiStrings.Key.SourceMethodMaskedCarnivaleProgress, language),
        SourceMethod.StartingSpell => UiStrings.Get(UiStrings.Key.SourceMethodStartingSpell, language),
        _ => method.ToString(),
    };
}
