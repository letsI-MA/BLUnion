using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BLUnion.Models;

namespace BLUnion.Services;

/// <summary>
/// Sync-Option A: Spieler exportieren ihren Status als kurzen Text-Code (z.B. über
/// Discord/Chat geteilt), andere importieren ihn per Copy/Paste. Kein Server, kein
/// Netzwerkzugriff des Plugins nötig.
///
/// AKTUELLES Exportformat "BLU:" (siehe <see cref="ExportToCode"/>) - festes Bitmasken-Layout,
/// byte-genau abgestimmt mit der Web-Companion-Implementierung (letsi-ma.github.io/BLUnion):
///   Byte 0:      Länge des Namens in UTF-8-Bytes (max. 255)
///   Byte 1..N:   Name als UTF-8
///   danach 16 Bytes: Bitmaske (128 Bits, aktuell 124 genutzt) über
///                <see cref="SpellDataService.OrderedSpellIds"/> (aufsteigend nach Spell-Id,
///                Bit-Index 0 = kleinste Id); Bit-Position: bitmask[idx >> 3] |= 1 << (idx % 8),
///                gesetzt = Spell gelernt.
///   Kodierung:   Base64 URL-safe OHNE Padding ('-'/'_' statt '+'/'/' , kein '=' am Ende).
/// Kein gzip mehr - eine Bitmaske komprimiert kaum, der gzip-Overhead würde den Code eher
/// verlängern als verkürzen.
///
/// ALTES Format "BLU1:" (gzip-komprimiertes JSON von <see cref="PlayerSpellStatus"/>) wird beim
/// Import weiterhin automatisch erkannt und gelesen (Codes/Web-Companion-Versionen von vor
/// diesem Format-Wechsel), aber nicht mehr exportiert.
/// </summary>
public sealed class ManualCodeSyncProvider : ISyncProvider
{
    /// <summary>Präfix des aktuellen Codeformats (siehe Klassendoc) - bewusst public: dient
    /// MainWindow.OnChatMessage (Feature "Gruppenanführer" - automatisches Einlesen von im Chat
    /// gefundenen Sync-Codes) als EINZIGE Quelle für den zu suchenden Teilstring, statt das
    /// Literal "BLU:" ein zweites Mal an anderer Stelle zu duplizieren.</summary>
    public const string CurrentPrefix = "BLU:";

    private const string LegacyPrefix = "BLU1:";

    /// <summary>Feste Größe der Bitmaske im "BLU:"-Format (128 Bits, siehe Klassendoc).</summary>
    private const int BitmaskBytes = 16;

    private readonly Dictionary<string, PlayerSpellStatus> known = new();
    private readonly SpellDataService spellDataService;

    public ManualCodeSyncProvider(SpellDataService spellDataService)
    {
        this.spellDataService = spellDataService;
    }

    public IReadOnlyList<PlayerSpellStatus> GetKnownPartyStatus() => this.known.Values.ToList();

    public void PublishLocalStatus(PlayerSpellStatus localStatus)
    {
        // Bei Option A bedeutet "Publish" nur: lokal für die eigene Anzeige merken.
        // Das eigentliche Teilen passiert über ExportToCode() + Discord/Chat.
        this.known[localStatus.CharacterName] = localStatus;
    }

    public void RemovePlayer(string characterName) => this.known.Remove(characterName);

    /// <summary>Erzeugt einen kompakten, teilbaren Code aus einem Status - immer im aktuellen
    /// "BLU:"-Bitmaskenformat (siehe Klassendoc).</summary>
    public string ExportToCode(PlayerSpellStatus status)
    {
        var nameBytes = Encoding.UTF8.GetBytes(status.CharacterName);
        if (nameBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException(
                $"Name \"{status.CharacterName}\" ist mit {nameBytes.Length} UTF-8-Bytes zu lang " +
                $"für das Sync-Codeformat (max. {byte.MaxValue}).");
        }

        var orderedIds = this.spellDataService.OrderedSpellIds;
        EnsureBitmaskCapacity(orderedIds.Count);

        var bitmask = new byte[BitmaskBytes];
        for (var idx = 0; idx < orderedIds.Count; idx++)
        {
            if (status.LearnedSpellIds.Contains(orderedIds[idx]))
                bitmask[idx >> 3] |= (byte)(1 << (idx % 8));
        }

        var payload = new byte[1 + nameBytes.Length + BitmaskBytes];
        payload[0] = (byte)nameBytes.Length;
        nameBytes.CopyTo(payload, 1);
        bitmask.CopyTo(payload, 1 + nameBytes.Length);

        return CurrentPrefix + ToBase64Url(payload);
    }

    /// <summary>Importiert einen von einem anderen Spieler geteilten Code - erkennt anhand des
    /// Präfixes automatisch, ob es sich um das aktuelle "BLU:"-Bitmaskenformat oder das alte
    /// "BLU1:"-Format (gzip+JSON) handelt.</summary>
    public void ImportCode(string code)
    {
        PlayerSpellStatus status;

        if (code.StartsWith(CurrentPrefix, StringComparison.Ordinal))
            status = this.DecodeCurrentFormat(code[CurrentPrefix.Length..]);
        else if (code.StartsWith(LegacyPrefix, StringComparison.Ordinal))
            status = DecodeLegacyFormat(code[LegacyPrefix.Length..]);
        else
            throw new FormatException("Unbekanntes Code-Format (erwartetes Präfix fehlt).");

        this.known[status.CharacterName] = status with { IsLocalPlayer = false };
    }

    private PlayerSpellStatus DecodeCurrentFormat(string payloadBase64Url)
    {
        byte[] payload;
        try
        {
            payload = FromBase64Url(payloadBase64Url);
        }
        catch (FormatException ex)
        {
            throw new FormatException("Sync-Code ist kein gültiges Base64.", ex);
        }

        if (payload.Length < 1 + BitmaskBytes)
            throw new FormatException($"Sync-Code ist zu kurz ({payload.Length} Bytes).");

        var nameLength = payload[0];
        var expectedLength = 1 + nameLength + BitmaskBytes;
        if (payload.Length != expectedLength)
        {
            throw new FormatException(
                $"Sync-Code hat unerwartete Länge ({payload.Length} statt {expectedLength} Bytes).");
        }

        var name = Encoding.UTF8.GetString(payload, 1, nameLength);
        var bitmaskOffset = 1 + nameLength;

        var orderedIds = this.spellDataService.OrderedSpellIds;
        EnsureBitmaskCapacity(orderedIds.Count);

        var learnedIds = new HashSet<uint>();
        for (var idx = 0; idx < orderedIds.Count; idx++)
        {
            var b = payload[bitmaskOffset + (idx >> 3)];
            if ((b & (1 << (idx % 8))) != 0)
                learnedIds.Add(orderedIds[idx]);
        }

        return new PlayerSpellStatus
        {
            CharacterName = name,
            LearnedSpellIds = learnedIds,
        };
    }

    private static PlayerSpellStatus DecodeLegacyFormat(string payloadBase64)
    {
        var compressed = Convert.FromBase64String(payloadBase64);

        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);

        var json = Encoding.UTF8.GetString(output.ToArray());
        return JsonSerializer.Deserialize<PlayerSpellStatus>(json)
            ?? throw new FormatException("Code konnte nicht als Spellstatus gelesen werden.");
    }

    /// <summary>Wirft statt eines stillen Bit-/Indexfehlers eine klare Exception, falls die
    /// bekannten Spell-Daten jemals über die Kapazität des festen 16-Byte-Bitmaskenformats
    /// (128 Bits) hinauswachsen sollten.</summary>
    private static void EnsureBitmaskCapacity(int knownSpellCount)
    {
        if (knownSpellCount > BitmaskBytes * 8)
        {
            throw new InvalidOperationException(
                $"Zu viele bekannte Spells ({knownSpellCount}) für das aktuelle Bitmasken-" +
                $"Codeformat (Kapazität: {BitmaskBytes * 8} Bits). Bitmaskengröße erhöhen - " +
                "und die Web-Companion-Implementierung entsprechend mitziehen.");
        }
    }

    private static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string base64Url)
    {
        var base64 = base64Url.Replace('-', '+').Replace('_', '/');
        base64 += (base64.Length % 4) switch
        {
            2 => "==",
            3 => "=",
            _ => string.Empty,
        };
        return Convert.FromBase64String(base64);
    }
}
