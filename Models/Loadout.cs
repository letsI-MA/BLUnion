namespace BLUnion.Models;

/// <summary>
/// Content-Typ, für den ein <see cref="Loadout"/> kuratiert wurde (siehe Data/loadouts.json).
/// Bewusst NUR diese zwei Werte fürs Erste - Dungeons/Savage sind eine spätere Erweiterung, siehe
/// Aufgabenstellung zum Loadouts-Tab.
/// </summary>
public enum LoadoutContentType
{
    MaskedCarnivale,
    Fates,
}

/// <summary>
/// Eine kuratierte Spell-Empfehlung für einen bestimmten <see cref="LoadoutContentType"/> (z.B.
/// "Masked-Carnivale-Standardset"). Wird aus Data/loadouts.json geladen - diese Datei ist bewusst
/// NICHT automatisiert befüllt (siehe dortiger Kommentar), sondern manuell vom Projektinhaber
/// kuratiert, anders als spells.json/sources.json/monsters.json/locations.json.
/// </summary>
public sealed class Loadout
{
    /// <summary>Freier, eindeutiger Bezeichner (kein Bezug zu einer Lumina-Sheet-Id, anders als
    /// z.B. <see cref="Spell.Id"/>) - dient nur als stabile ImGui-Widget-Id (siehe
    /// UI/MainWindow.cs DrawLoadoutsTab).</summary>
    public required string Id { get; init; }

    public required LoadoutContentType ContentType { get; init; }

    /// <summary>Name in allen 4 offiziellen FFXIV-Clientsprachen, analog <see cref="Spell.NameDe"/>
    /// & Co.</summary>
    public required string NameDe { get; init; }

    public required string NameEn { get; init; }

    public required string NameFr { get; init; }

    public required string NameJa { get; init; }

    /// <summary>Die empfohlenen Spells dieses Loadouts, als Action-Sheet-Ids (siehe
    /// <see cref="Spell.Id"/>) - Anzeigereihenfolge in DrawLoadoutsTab folgt der Reihenfolge
    /// dieser Liste (nicht z.B. SpellbookOrder), damit der Kurator die Reihenfolge selbst
    /// bestimmen kann (z.B. Rotation statt Spellbook-Nummer).</summary>
    public required List<uint> SpellIds { get; init; }

    /// <summary>Kurze Quellenangabe (z.B. "Blue Academy (mage.blue)") - null, falls keine
    /// Quelle hinterlegt ist.</summary>
    public string? SourceNote { get; init; }

    /// <summary>Optionale URL zur vollständigen Quelle, per "Quelle öffnen"-Button im Browser
    /// aufrufbar (siehe DrawLoadoutsTab) - null, falls keine URL hinterlegt ist (SourceNote kann
    /// trotzdem gesetzt sein, z.B. bei einer nicht verlinkbaren Quelle).</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Liefert <see cref="NameDe"/>/<see cref="NameEn"/>/<see cref="NameFr"/>/
    /// <see cref="NameJa"/> passend zur gewählten <see cref="DisplayLanguage"/>, analog
    /// <see cref="Spell.GetName"/>.</summary>
    public string GetName(DisplayLanguage language) => language switch
    {
        DisplayLanguage.German => this.NameDe,
        DisplayLanguage.English => this.NameEn,
        DisplayLanguage.French => this.NameFr,
        DisplayLanguage.Japanese => this.NameJa,
        _ => this.NameEn,
    };
}
