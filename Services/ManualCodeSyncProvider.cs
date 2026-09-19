using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BLUnion.Models;

namespace BLUnion.Services;

public sealed class ManualCodeSyncProvider
{
    public const string CurrentPrefix = "BLU:";

    private const string LegacyPrefix = "BLU1:";

    internal const int BitmaskBytes = 16;

    private readonly Dictionary<string, PlayerSpellStatus> known = new();
    private readonly SpellDataService spellDataService;

    public ManualCodeSyncProvider(SpellDataService spellDataService)
    {
        this.spellDataService = spellDataService;
    }

    public IReadOnlyList<PlayerSpellStatus> GetKnownPartyStatus() => this.known.Values.ToList();

    public void PublishLocalStatus(PlayerSpellStatus localStatus)
    {
        this.known[localStatus.CharacterName] = localStatus;
    }

    public void RemovePlayer(string characterName) => this.known.Remove(characterName);

    public string ExportToCode(PlayerSpellStatus status)
    {
        var nameBytes = Encoding.UTF8.GetBytes(status.CharacterName);
        if (nameBytes.Length > byte.MaxValue)
        {
            throw new InvalidOperationException(
                $"Name \"{status.CharacterName}\" ist mit {nameBytes.Length} UTF-8-Bytes zu lang " +
                $"für das Sync-Codeformat (max. {byte.MaxValue}).");
        }

        var bitmask = EncodeBitmask(this.spellDataService, status.LearnedSpellIds);

        var payload = new byte[1 + nameBytes.Length + BitmaskBytes];
        payload[0] = (byte)nameBytes.Length;
        nameBytes.CopyTo(payload, 1);
        bitmask.CopyTo(payload, 1 + nameBytes.Length);

        return CurrentPrefix + ToBase64Url(payload);
    }

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
        var bitmask = payload[bitmaskOffset..(bitmaskOffset + BitmaskBytes)];

        return new PlayerSpellStatus
        {
            CharacterName = name,
            LearnedSpellIds = DecodeBitmask(this.spellDataService, bitmask),
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

    internal static byte[] EncodeBitmask(SpellDataService spellDataService, IReadOnlySet<uint> learnedSpellIds)
    {
        var orderedIds = spellDataService.OrderedSpellIds;
        EnsureBitmaskCapacity(orderedIds.Count);

        var bitmask = new byte[BitmaskBytes];
        for (var idx = 0; idx < orderedIds.Count; idx++)
        {
            if (learnedSpellIds.Contains(orderedIds[idx]))
                bitmask[idx >> 3] |= (byte)(1 << (idx % 8));
        }

        return bitmask;
    }

    internal static HashSet<uint> DecodeBitmask(SpellDataService spellDataService, byte[] bitmask)
    {
        if (bitmask.Length < BitmaskBytes)
        {
            throw new FormatException(
                $"Bitmaske ist zu kurz ({bitmask.Length} statt mindestens {BitmaskBytes} Bytes).");
        }

        var orderedIds = spellDataService.OrderedSpellIds;
        EnsureBitmaskCapacity(orderedIds.Count);

        var learnedIds = new HashSet<uint>();
        for (var idx = 0; idx < orderedIds.Count; idx++)
        {
            var b = bitmask[idx >> 3];
            if ((b & (1 << (idx % 8))) != 0)
                learnedIds.Add(orderedIds[idx]);
        }

        return learnedIds;
    }

    internal static string ToBase64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    internal static byte[] FromBase64Url(string base64Url)
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
