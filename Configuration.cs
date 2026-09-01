using Dalamud.Configuration;
using Dalamud.Plugin;

namespace BLUnion;

public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool LiveSyncEnabled { get; set; }

    public Dictionary<string, string> LiveSyncEditTokens { get; set; } = new();

    public Dictionary<string, string> GroupFinderOwnGroupIds { get; set; } = new();

    public Dictionary<string, string> GroupFinderGroupEditTokens { get; set; } = new();

    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterfaceToUse)
    {
        this.pluginInterface = pluginInterfaceToUse;
    }

    public void Save()
    {
        this.pluginInterface?.SavePluginConfig(this);
    }
}
