namespace BLUnion.Services;

public enum AetheryteKind
{
    // Echter Aetheryt (Teleport-Ziel im Teleport-Fenster).
    Aetheryte,

    // Aethernet-Shard bzw. Stadttor (Aetheryte.IsAetheryte = false) - kein Teleport-Ziel.
    AethernetShard,

    // Aetheryt in einem Wohngebiet (TerritoryIntendedUse Housing) - bewusst nie gewählt.
    Housing,
}

// Ein Aetheryt aus den Spieldaten. MapX/MapY = Kartenkoordinaten aus dem MapMarker-Sheet (null = keine
// Position bekannt). Das Level-Sheet ist bei echten Aetheryten leer (per Lumina geprüft, Stand
// 2026.09.15) und taugt deshalb nicht als Positionsquelle.
public sealed record AetheryteCandidate(uint Id, uint TerritoryTypeId, AetheryteKind Kind, float? MapX, float? MapY);

// Wahl des Teleport-Ziels für einen Ort - bewusst ohne Dalamud-Abhängigkeit (getestet in BLUnion.Tests).
public static class AetheryteSelector
{
    // Haupt-Aetheryt einer Zone: der echte Aetheryt mit der niedrigsten ID (z.B. Central Shroud ->
    // Bentbranch Meadows, Nördliches Thanalan -> Camp Bluefog).
    public static AetheryteCandidate? SelectMain(IEnumerable<AetheryteCandidate> candidates, uint territoryTypeId) =>
        candidates
            .Where(candidate => candidate.Kind == AetheryteKind.Aetheryte && candidate.TerritoryTypeId == territoryTypeId)
            .MinBy(candidate => candidate.Id);

    // Der echte Aetheryt der Zone, der (x, y) am nächsten liegt. Ohne bekannte Positionen: Haupt-Aetheryt.
    // Bei gleichem Abstand gewinnt die niedrigere ID.
    public static AetheryteCandidate? SelectNearest(IEnumerable<AetheryteCandidate> candidates, uint territoryTypeId, float x, float y)
    {
        var inZone = candidates
            .Where(candidate => candidate.Kind == AetheryteKind.Aetheryte && candidate.TerritoryTypeId == territoryTypeId)
            .ToList();

        return inZone
            .Where(candidate => candidate is { MapX: not null, MapY: not null })
            .OrderBy(candidate => Square(candidate.MapX!.Value - x) + Square(candidate.MapY!.Value - y))
            .ThenBy(candidate => candidate.Id)
            .FirstOrDefault()
            ?? inZone.MinBy(candidate => candidate.Id);
    }

    // Zonen ohne eigenen echten Aetheryten -> angrenzendes Gebiet, dessen Haupt-Aetheryt genommen wird
    // (Aetheryte-ID und Name kommen damit aus den Spieldaten). Per Lumina geprüft für alle Open-World-Zonen
    // in Data/locations.json (Stand 2026.09.15): nur The Dravanian Hinterlands hat keinen eigenen.
    public static readonly IReadOnlyDictionary<uint, uint> FallbackTerritories = new Dictionary<uint, uint>
    {
        // The Dravanian Hinterlands (399) -> Idyllshire (478, genau ein echter Aetheryt).
        [399] = 478,
    };

    // mapLink gesetzt (Monster mit Koordinaten): nächstgelegener Aetheryt, sonst der Haupt-Aetheryt der Zone.
    // Ohne eigenen Aetheryten: Haupt-Aetheryt des Ausweich-Gebiets (die Koordinaten des Monsters gehören zu
    // einer anderen Karte und taugen dort nicht zum Vergleich).
    public static AetheryteCandidate? Select(IEnumerable<AetheryteCandidate> candidates, uint territoryTypeId, MapLinkTarget? mapLink)
    {
        var list = candidates as IReadOnlyCollection<AetheryteCandidate> ?? candidates.ToList();
        var inZone = mapLink is { } target
            ? SelectNearest(list, territoryTypeId, target.X, target.Y)
            : SelectMain(list, territoryTypeId);

        return inZone ?? (FallbackTerritories.TryGetValue(territoryTypeId, out var fallbackTerritoryId)
            ? SelectMain(list, fallbackTerritoryId)
            : null);
    }

    private static float Square(float value) => value * value;
}
