namespace BLUnion.Models;

public sealed record OwnProfileSnapshot
{
    public required string DataCenter { get; init; }

    public required bool VisibleInGroupFinder { get; init; }

    public required IReadOnlyList<AvailabilityTag> AvailabilityTags { get; init; }

    public required string Note { get; init; }

    public required int WantedPlayerCount { get; init; }

    /// <summary>Klickbarer Discord-Link, wenn das Profil aktuell gelistet ist (siehe Worker
    /// buildDiscordChannelLink) - null, wenn der Server dafür keinen bereitstellt (z.B. nicht
    /// gelistet oder Data Center nicht auflösbar).</summary>
    public string? DiscordChannelUrl { get; init; }

    /// <summary>Nur für die Anzeige ("Discord: #&lt;name&gt;") - null, wenn der Server dafür keinen
    /// Namen konfiguriert hat, auch wenn DiscordChannelUrl gesetzt ist.</summary>
    public string? DiscordChannelName { get; init; }
}
