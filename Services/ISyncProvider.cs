using BLUnion.Models;

namespace BLUnion.Services;

/// <summary>
/// Abstraktion für den Austausch von PlayerSpellStatus zwischen verschiedenen
/// Clients. Damit lässt sich die konkrete Methode (manueller Export/Import,
/// Party-Chat-Sync, externer Server) austauschen, ohne den Rest des Plugins
/// (Vergleich, UI) anzufassen.
/// </summary>
public interface ISyncProvider
{
    /// <summary>Alle aktuell bekannten Party-Mitglieder-Stati (inkl. eigenem).</summary>
    IReadOnlyList<PlayerSpellStatus> GetKnownPartyStatus();

    /// <summary>Teilt den eigenen Status mit (Bedeutung je nach Implementierung unterschiedlich).</summary>
    void PublishLocalStatus(PlayerSpellStatus localStatus);

    /// <summary>Entfernt einen bekannten Spieler wieder (z.B. falsch importiert oder veraltet).</summary>
    void RemovePlayer(string characterName);
}
