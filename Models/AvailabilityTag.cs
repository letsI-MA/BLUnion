namespace BLUnion.Models;

/// <summary>
/// Verfügbarkeits-Tag für den Gruppenfinder (Phase 2 "Live-Sync", siehe LiveSyncService/
/// UI.MainWindow.DrawGroupFinderTab). Mehrfachauswahl möglich (siehe UI: fünf anklickbare Tags).
///
/// Die Enum-Werte entsprechen inhaltlich 1:1 den vom Worker akzeptierten internen (englischen)
/// String-Werten (siehe worker/src/index.ts ALLOWED_AVAILABILITY_TAGS) - der Worker validiert
/// serverseitig gegen genau diese fünf Strings. Die Übersetzung für die UI passiert
/// AUSSCHLIESSLICH über <see cref="Services.UiStrings"/> (siehe MainWindow), NICHT hier im
/// Modell - das Modell/die Wire-Repräsentation bleibt bewusst einsprachig englisch.
/// </summary>
public enum AvailabilityTag
{
    Morning,
    Afternoon,
    Evening,
    Weekend,
    Flexible,
}

/// <summary>Konvertierung zwischen <see cref="AvailabilityTag"/> und dem vom Worker erwarteten/
/// gelieferten internen String-Wert - zentral hier statt an mehreren Stellen (LiveSyncService
/// beim Push, beim Browse-Parsing) einzeln dupliziert.</summary>
public static class AvailabilityTagExtensions
{
    /// <summary>Der vom Worker erwartete/gespeicherte interne String-Wert (siehe
    /// worker/src/index.ts ALLOWED_AVAILABILITY_TAGS). Bewusst als expliziter switch statt
    /// z.B. <c>tag.ToString().ToLowerInvariant()</c> - eine künftige Umbenennung des Enum-Werts
    /// (z.B. aus Übersetzungs-/Konsistenzgründen im C#-Code) soll NICHT stillschweigend auch das
    /// Wire-Format ändern und dadurch bestehende Server-Profile "entwerten".</summary>
    public static string ToWireValue(this AvailabilityTag tag) => tag switch
    {
        AvailabilityTag.Morning => "morning",
        AvailabilityTag.Afternoon => "afternoon",
        AvailabilityTag.Evening => "evening",
        AvailabilityTag.Weekend => "weekend",
        AvailabilityTag.Flexible => "flexible",
        _ => throw new ArgumentOutOfRangeException(nameof(tag), tag, null),
    };

    /// <summary>Kehrt <see cref="ToWireValue"/> um - für vom Worker/Browse-Endpoint empfangene
    /// Tags fremder Spieler (siehe LiveSyncService.TriggerBrowseAsync). Liefert null statt eine
    /// Exception zu werfen, wenn der Wert unbekannt ist: ein älteres Plugin soll bei einem vom
    /// Server irgendwann ergänzten sechsten Tag nicht abstürzen, sondern den unbekannten Wert
    /// einfach ignorieren (Vorwärtskompatibilität).</summary>
    public static AvailabilityTag? FromWireValue(string wireValue) => wireValue switch
    {
        "morning" => AvailabilityTag.Morning,
        "afternoon" => AvailabilityTag.Afternoon,
        "evening" => AvailabilityTag.Evening,
        "weekend" => AvailabilityTag.Weekend,
        "flexible" => AvailabilityTag.Flexible,
        _ => null,
    };
}
