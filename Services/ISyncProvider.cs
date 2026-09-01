using BLUnion.Models;

namespace BLUnion.Services;

public interface ISyncProvider
{
    IReadOnlyList<PlayerSpellStatus> GetKnownPartyStatus();

    void PublishLocalStatus(PlayerSpellStatus localStatus);

    void RemovePlayer(string characterName);
}
