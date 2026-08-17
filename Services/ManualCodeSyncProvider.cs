using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BLUnion.Models;

namespace BLUnion.Services;

/// <summary>
/// Sync-Option A: Spieler exportieren ihren Status als kurzen Text-Code
/// (z.B. über Discord/Chat geteilt), andere importieren ihn per Copy/Paste.
/// Kein Server, kein Netzwerkzugriff des Plugins nötig.
/// </summary>
public sealed class ManualCodeSyncProvider : ISyncProvider
{
    private readonly Dictionary<string, PlayerSpellStatus> known = new();

    public IReadOnlyList<PlayerSpellStatus> GetKnownPartyStatus() => this.known.Values.ToList();

    public void PublishLocalStatus(PlayerSpellStatus localStatus)
    {
        // Bei Option A bedeutet "Publish" nur: lokal für die eigene Anzeige merken.
        // Das eigentliche Teilen passiert über ExportToCode() + Discord/Chat.
        this.known[localStatus.CharacterName] = localStatus;
    }

    public void RemovePlayer(string characterName) => this.known.Remove(characterName);

    /// <summary>Erzeugt einen kompakten, teilbaren Code aus einem Status.</summary>
    public string ExportToCode(PlayerSpellStatus status)
    {
        var json = JsonSerializer.Serialize(status);
        var bytes = Encoding.UTF8.GetBytes(json);

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            gzip.Write(bytes);

        return "BLU1:" + Convert.ToBase64String(output.ToArray());
    }

    /// <summary>Importiert einen von einem anderen Spieler geteilten Code.</summary>
    public void ImportCode(string code)
    {
        const string prefix = "BLU1:";
        if (!code.StartsWith(prefix, StringComparison.Ordinal))
            throw new FormatException("Unbekanntes Code-Format (erwartetes Präfix fehlt).");

        var compressed = Convert.FromBase64String(code[prefix.Length..]);

        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);

        var json = Encoding.UTF8.GetString(output.ToArray());
        var status = JsonSerializer.Deserialize<PlayerSpellStatus>(json)
            ?? throw new FormatException("Code konnte nicht als Spellstatus gelesen werden.");

        this.known[status.CharacterName] = status with { IsLocalPlayer = false };
    }
}
