namespace BLUnion.Models;

public sealed class Location
{
    public required uint Id { get; init; }

    /// <summary>Zonenname, z.B. "The Aurum Vale".</summary>
    public required string ZoneName { get; init; }

    /// <summary>Optional: Koordinaten im Format "x, y", sofern in freier Wildbahn sinnvoll.</summary>
    public string? Coordinates { get; init; }

    /// <summary>Optional: Dungeon/Trial-Name, falls nicht in offener Welt.</summary>
    public string? DutyName { get; init; }
}
