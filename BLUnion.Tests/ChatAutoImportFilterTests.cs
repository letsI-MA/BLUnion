using BLUnion.Models;
using BLUnion.Services;
using BLUnion.Tests.Fakes;
using Dalamud.Game.Text;
using Xunit;

namespace BLUnion.Tests;

public class ChatAutoImportFilterTests
{
    private const string LocalPlayer = "Local Player";

    private static readonly List<PartyMemberInfo> Party =
    [
        new(LocalPlayer, "Gilgamesh", 1, true, 80),
        new("Alice Test", "Gilgamesh", 2, true, 80),
        new("Bob Test", "Cerberus", 3, true, 80),
    ];

    [Theory]
    [InlineData(XivChatType.Party)]
    [InlineData(XivChatType.CrossParty)]
    public void EvaluateMessage_PartyChannelsFromPartyMember_Accepts(XivChatType chatType)
    {
        var decision = ChatAutoImportFilter.EvaluateMessage(chatType, "Alice Test", "Gilgamesh", LocalPlayer, Party);

        Assert.Equal(ChatAutoImportDecision.Accept, decision);
    }

    [Theory]
    [InlineData(XivChatType.Say)]
    [InlineData(XivChatType.Shout)]
    [InlineData(XivChatType.Yell)]
    [InlineData(XivChatType.TellIncoming)]
    [InlineData(XivChatType.Alliance)]
    [InlineData(XivChatType.FreeCompany)]
    [InlineData(XivChatType.Ls1)]
    [InlineData(XivChatType.CrossLinkShell1)]
    [InlineData(XivChatType.NoviceNetwork)]
    [InlineData(XivChatType.Echo)]
    public void EvaluateMessage_OtherChannels_AreIgnored(XivChatType chatType)
    {
        var decision = ChatAutoImportFilter.EvaluateMessage(chatType, "Alice Test", "Gilgamesh", LocalPlayer, Party);

        Assert.Equal(ChatAutoImportDecision.IgnoredChannel, decision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void EvaluateMessage_WithoutPlayerPayloadName_IsUnknownSender(string? senderName)
    {
        var decision = ChatAutoImportFilter.EvaluateMessage(XivChatType.Party, senderName, null, LocalPlayer, Party);

        Assert.Equal(ChatAutoImportDecision.UnknownSender, decision);
    }

    [Fact]
    public void EvaluateMessage_FromLocalPlayer_IsIgnored()
    {
        var decision = ChatAutoImportFilter.EvaluateMessage(XivChatType.Party, LocalPlayer, "Gilgamesh", LocalPlayer, Party);

        Assert.Equal(ChatAutoImportDecision.SenderIsLocalPlayer, decision);
    }

    [Fact]
    public void EvaluateMessage_SenderNotInParty_IsIgnored()
    {
        var decision = ChatAutoImportFilter.EvaluateMessage(XivChatType.Party, "Mallory Test", "Gilgamesh", LocalPlayer, Party);

        Assert.Equal(ChatAutoImportDecision.SenderNotInParty, decision);
    }

    [Fact]
    public void EvaluateMessage_SameNameButDifferentWorld_IsNotInParty()
    {
        var decision = ChatAutoImportFilter.EvaluateMessage(XivChatType.CrossParty, "Bob Test", "Gilgamesh", LocalPlayer, Party);

        Assert.Equal(ChatAutoImportDecision.SenderNotInParty, decision);
    }

    [Fact]
    public void EvaluateMessage_WithoutSenderWorld_MatchesByNameOnly()
    {
        var decision = ChatAutoImportFilter.EvaluateMessage(XivChatType.Party, "Bob Test", null, LocalPlayer, Party);

        Assert.Equal(ChatAutoImportDecision.Accept, decision);
    }

    [Fact]
    public void EvaluateMessage_EmptyParty_IsNotInParty()
    {
        var decision = ChatAutoImportFilter.EvaluateMessage(XivChatType.Party, "Alice Test", "Gilgamesh", LocalPlayer, []);

        Assert.Equal(ChatAutoImportDecision.SenderNotInParty, decision);
    }

    [Fact]
    public void EvaluateCodeName_MatchingSender_Accepts()
    {
        Assert.Equal(
            ChatAutoImportDecision.Accept,
            ChatAutoImportFilter.EvaluateCodeName("Alice Test", "Alice Test", LocalPlayer));
    }

    [Fact]
    public void EvaluateCodeName_OtherMembersName_IsRejected()
    {
        Assert.Equal(
            ChatAutoImportDecision.CodeNameMismatch,
            ChatAutoImportFilter.EvaluateCodeName("Bob Test", "Alice Test", LocalPlayer));
    }

    [Fact]
    public void EvaluateCodeName_LocalPlayersName_IsRejected()
    {
        Assert.Equal(
            ChatAutoImportDecision.CodeIsLocalPlayer,
            ChatAutoImportFilter.EvaluateCodeName(LocalPlayer, "Alice Test", LocalPlayer));
    }

    [Fact]
    public void EvaluateCodeName_IsCaseSensitive()
    {
        Assert.Equal(
            ChatAutoImportDecision.CodeNameMismatch,
            ChatAutoImportFilter.EvaluateCodeName("alice test", "Alice Test", LocalPlayer));
    }

    [Fact]
    public void TryImportCode_RejectedName_DoesNotStoreEntryButReportsName()
    {
        var provider = CreateProvider();
        var code = provider.ExportToCode(new PlayerSpellStatus { CharacterName = "Bob Test", LearnedSpellIds = [1] });

        var imported = provider.TryImportCode(
            code,
            name => ChatAutoImportFilter.EvaluateCodeName(name, "Alice Test", LocalPlayer) == ChatAutoImportDecision.Accept,
            out var codeCharacterName);

        Assert.False(imported);
        Assert.Equal("Bob Test", codeCharacterName);
        Assert.Empty(provider.GetKnownPartyStatus());
    }

    [Fact]
    public void TryImportCode_RejectedName_DoesNotOverwriteExistingEntry()
    {
        var provider = CreateProvider();
        provider.ImportCode(provider.ExportToCode(new PlayerSpellStatus { CharacterName = "Bob Test", LearnedSpellIds = [1, 2] }));

        var forged = provider.ExportToCode(new PlayerSpellStatus { CharacterName = "Bob Test", LearnedSpellIds = [] });
        provider.TryImportCode(forged, _ => false, out _);

        var entry = Assert.Single(provider.GetKnownPartyStatus());
        Assert.Equal(new HashSet<uint> { 1, 2 }, entry.LearnedSpellIds);
    }

    [Fact]
    public void TryImportCode_AcceptedName_StoresEntry()
    {
        var provider = CreateProvider();
        var code = provider.ExportToCode(new PlayerSpellStatus { CharacterName = "Alice Test", LearnedSpellIds = [3] });

        var imported = provider.TryImportCode(
            code,
            name => ChatAutoImportFilter.EvaluateCodeName(name, "Alice Test", LocalPlayer) == ChatAutoImportDecision.Accept,
            out _);

        Assert.True(imported);
        Assert.Equal("Alice Test", Assert.Single(provider.GetKnownPartyStatus()).CharacterName);
    }

    private static ManualCodeSyncProvider CreateProvider()
    {
        var dataService = new SpellDataService(new TestPluginLog());
        dataService.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "SpellData"));
        return new ManualCodeSyncProvider(dataService);
    }
}
