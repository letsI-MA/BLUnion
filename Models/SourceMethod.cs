namespace BLUnion.Models;

/// <summary>
/// Art der Lernquelle eines <see cref="SpellSource"/>-Eintrags. Ursprünglich ein freier
/// String (siehe git-Historie) - auf Enum umgestellt, damit z.B. der "Totems ausblenden"-Filter
/// nicht an mehreren Stellen einzeln Text-Literale vergleichen muss.
/// </summary>
public enum SourceMethod
{
    Duty,
    OpenWorld,
    Levequest,
    DungeonTrash,
    MaskedCarnivale,

    /// <summary>Whalaqee-Totem, generische/ältere Freischaltbedingung (siehe git-Historie) -
    /// mittlerweile größtenteils durch <see cref="TotemSpellCount"/>/<see cref="TotemLevel"/>
    /// abgelöst, aber als Wert erhalten (falls noch irgendwo referenziert).</summary>
    Totem,

    /// <summary>Whalaqee-Totem, freigeschaltet ab einer bestimmten Anzahl gelernter Spells.</summary>
    TotemSpellCount,

    /// <summary>Whalaqee-Totem, freigeschaltet ab einem bestimmten Charakterlevel.</summary>
    TotemLevel,

    /// <summary>Freischaltung durch Abschluss einer bestimmten Anzahl Maskenkarneval-Stufen
    /// (kein Monster, kein Totem - reiner Fortschritts-Meilenstein).</summary>
    MaskedCarnivaleProgress,

    /// <summary>Von Anfang an bekannt (Default-Spell, z.B. Wasserkanone) - kein echtes Monster.</summary>
    StartingSpell,
}

/// <summary>Hilfsmethoden für <see cref="SourceMethod"/>, insbesondere für die
/// "Totems ausblenden"-Filterung (Comparison-/Lernplan-Tab).</summary>
public static class SourceMethodExtensions
{
    /// <summary>True für alle Method-Werte, die eine Spell-Quelle über das Whalaqee-Totem
    /// beschreiben (unabhängig von der genauen Freischaltbedingung) - zentrale Stelle, damit
    /// der "Totems ausblenden"-Filter nicht an mehreren Stellen einzeln die einzelnen
    /// Totem-Werte auflisten muss und künftige neue Totem-Varianten hier nur einmal ergänzt
    /// werden müssen.</summary>
    public static bool IsTotemRelated(this SourceMethod method) =>
        method is SourceMethod.Totem or SourceMethod.TotemSpellCount or SourceMethod.TotemLevel;

    /// <summary>Anzeigetext für Tooltips - bewusst NICHT über <see cref="Services.UiStrings"/>
    /// mehrsprachig (Method-Werte wurden schon vor der Mehrsprachigkeits-Aufgabe einsprachig
    /// angezeigt, siehe bisherige Tooltip-Zeile "Quelle: {0} ({1}) — {2}"; hier nur bei der
    /// Enum-Umstellung 1:1 die bisherigen Anzeigetexte erhalten, keine neue Übersetzungsarbeit).</summary>
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
