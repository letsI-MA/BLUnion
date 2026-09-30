using BLUnion.Models;

namespace BLUnion.Services;

// Anzeige des Online-Status im Group-Finder-Browse - ohne Dalamud-Abhängigkeit (getestet).
public static class PresenceFormatter
{
    // Grob: unter einer Stunde Minuten (mindestens 1), unter einem Tag Stunden, sonst Tage - der Worker
    // liefert lastSeen ohnehin nur auf 15 Minuten genau.
    public static string FormatLastSeen(DateTimeOffset now, DateTimeOffset lastSeen, DisplayLanguage language)
    {
        var elapsed = now - lastSeen;
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (elapsed < TimeSpan.FromHours(1))
            return UiStrings.Format(UiStrings.Key.PresenceLastSeenMinutesFormat, language, Math.Max(1, (int)elapsed.TotalMinutes));

        if (elapsed < TimeSpan.FromDays(1))
            return UiStrings.Format(UiStrings.Key.PresenceLastSeenHoursFormat, language, (int)elapsed.TotalHours);

        return UiStrings.Format(UiStrings.Key.PresenceLastSeenDaysFormat, language, (int)elapsed.TotalDays);
    }

    // Anzeigetext für einen Browse-Eintrag, null = nichts anzeigen (kein Online-Status geteilt bzw.
    // noch nie ein Signal).
    public static string? FormatStatus(bool? online, DateTimeOffset? lastSeen, DateTimeOffset now, DisplayLanguage language) =>
        online == true
            ? UiStrings.Get(UiStrings.Key.PresenceOnline, language)
            : lastSeen is { } seen
                ? FormatLastSeen(now, seen, language)
                : null;

    // Für die Zwischenablage - wird nie gesendet, nur zum Einfügen in den eigenen Chat.
    public static string BuildTellCommand(string characterName, string world) => $"/tell {characterName}@{world} ";
}
