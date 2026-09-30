using BLUnion.Services;
using Xunit;

namespace BLUnion.Tests;

// Nur die reine Zusammenführung (PartyService.MergePartyMembers) - das Auslesen von IPartyList/
// InfoProxyCrossRealm braucht das laufende Spiel (siehe LiveSyncServiceTests-Klassendoc).
public class PartyServiceTests
{
    private static readonly PartyMemberInfo Local = new("Local Player", "Gilgamesh", 1, true, 80);
    private static readonly PartyMemberInfo Alice = new("Alice Test", "Gilgamesh", 2, true, 80);
    private static readonly PartyMemberInfo Bob = new("Bob Test", "Cerberus", 3, false, 90);

    [Fact]
    public void NormalParty_UsesPartyList()
    {
        var result = PartyService.MergePartyMembers([Local, Alice], [], Local);

        Assert.Equal([Local, Alice], result);
    }

    [Fact]
    public void CrossWorldParty_EmptyPartyList_UsesCrossRealmMembers()
    {
        var result = PartyService.MergePartyMembers([], [Local, Bob], Local);

        Assert.Equal([Local, Bob], result);
    }

    [Fact]
    public void PartyListWins_WhenBothSourcesHaveMembers()
    {
        var result = PartyService.MergePartyMembers([Local, Alice], [Local, Bob], Local);

        Assert.Equal([Local, Alice], result);
    }

    [Fact]
    public void Solo_ReturnsOnlyLocalPlayer()
    {
        var result = PartyService.MergePartyMembers([], [], Local);

        Assert.Equal([Local], result);
    }

    [Fact]
    public void NotLoggedIn_ReturnsEmptyList()
    {
        var result = PartyService.MergePartyMembers([], [], null);

        Assert.Empty(result);
    }

    [Fact]
    public void CrossWorldMembers_WorkWithChatFilter()
    {
        var members = PartyService.MergePartyMembers([], [Local, Bob], Local);

        var decision = ChatAutoImportFilter.EvaluateMessage(
            Dalamud.Game.Text.XivChatType.CrossParty, "Bob Test", "Cerberus", Local.Name, members);

        Assert.Equal(ChatAutoImportDecision.Accept, decision);
    }
}
