using Dalamud.Game.Text;

namespace BLUnion.Services;

public enum ChatAutoImportDecision
{
    Accept,
    IgnoredChannel,
    UnknownSender,
    SenderIsLocalPlayer,
    SenderNotInParty,
    CodeNameMismatch,
    CodeIsLocalPlayer,
}

// Filterlogik für den Chat-Auto-Import (siehe MainWindow.OnChatMessage) - bewusst ohne
// Dalamud-Runtime-Typen außer dem XivChatType-Enum, damit sie in BLUnion.Tests direkt testbar ist.
// Zweistufig: EvaluateMessage läuft vor dem Decodieren (billig, für jede Chatzeile),
// EvaluateCodeName erst mit dem aus dem Code gelesenen Namen.
public static class ChatAutoImportFilter
{
    public static bool IsPartyChannel(XivChatType chatType) =>
        chatType is XivChatType.Party or XivChatType.CrossParty;

    // senderName/senderWorld stammen aus dem PlayerPayload des Absenders (nicht aus Sender.TextValue,
    // das je nach Kanal Welt-Icons/Party-Nummern enthalten kann). senderWorld darf null/leer sein -
    // dann wird nur der Name gegen die Party geprüft.
    public static ChatAutoImportDecision EvaluateMessage(
        XivChatType chatType,
        string? senderName,
        string? senderWorld,
        string? localPlayerName,
        IEnumerable<PartyMemberInfo> partyMembers)
    {
        if (!IsPartyChannel(chatType))
            return ChatAutoImportDecision.IgnoredChannel;

        if (string.IsNullOrEmpty(senderName))
            return ChatAutoImportDecision.UnknownSender;

        if (localPlayerName is not null && string.Equals(senderName, localPlayerName, StringComparison.Ordinal))
            return ChatAutoImportDecision.SenderIsLocalPlayer;

        var isInParty = partyMembers.Any(member =>
            string.Equals(member.Name, senderName, StringComparison.Ordinal)
            && (string.IsNullOrEmpty(senderWorld)
                || string.IsNullOrEmpty(member.World)
                || string.Equals(member.World, senderWorld, StringComparison.OrdinalIgnoreCase)));

        return isInParty ? ChatAutoImportDecision.Accept : ChatAutoImportDecision.SenderNotInParty;
    }

    // Der Code darf nur den Eintrag des Absenders selbst setzen - sonst könnte ein Party-Mitglied den
    // Eintrag eines anderen Mitglieds (oder den eigenen des lokalen Spielers) überschreiben.
    public static ChatAutoImportDecision EvaluateCodeName(string codeCharacterName, string senderName, string? localPlayerName)
    {
        if (localPlayerName is not null && string.Equals(codeCharacterName, localPlayerName, StringComparison.Ordinal))
            return ChatAutoImportDecision.CodeIsLocalPlayer;

        return string.Equals(codeCharacterName, senderName, StringComparison.Ordinal)
            ? ChatAutoImportDecision.Accept
            : ChatAutoImportDecision.CodeNameMismatch;
    }
}
