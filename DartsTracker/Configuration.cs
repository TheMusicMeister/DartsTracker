using Dalamud.Configuration;
using System;

namespace DartsTracker;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public int RoundsPerGame { get; set; } = 5;
    public int ChatMessageDelayMs { get; set; } = 1000;
    public MatchHistoryData MatchHistory { get; set; } = new();
    
    // Development settings (not saved/persistent)
    [NonSerialized]
    public bool DevMode = false;

    // The below exist just to make saving less cumbersome
    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
