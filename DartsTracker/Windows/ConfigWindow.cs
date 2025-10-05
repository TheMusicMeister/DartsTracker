using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DartsTracker.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;
    private readonly Plugin plugin;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("Darts Tracker Configuration###DartsTrackerConfig")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(400, 280);
        SizeCondition = ImGuiCond.Always;

        configuration = plugin.Configuration;
        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
    }

    public override void Draw()
    {
        ImGui.TextUnformatted("Game Settings:");
        ImGui.Separator();
        
        var rounds = configuration.RoundsPerGame;
        if (ImGui.SliderInt("Rounds per Game", ref rounds, 1, 10))
        {
            configuration.RoundsPerGame = rounds;
            configuration.Save();
        }
        
        var delayMs = configuration.ChatMessageDelayMs;
        if (ImGui.SliderInt("Chat Message Delay (ms)", ref delayMs, 500, 3000))
        {
            configuration.ChatMessageDelayMs = delayMs;
            configuration.Save();
        }
        
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Delay between multi-line chat announcements");
            ImGui.TextUnformatted("Lower values = faster messages, higher values = easier to read");
            ImGui.EndTooltip();
        }
        
        ImGui.Spacing();
        ImGui.TextUnformatted("Development Settings:");
        ImGui.Separator();
        
        // DEV Mode toggle
        var devMode = configuration.DevMode;
        if (ImGui.Checkbox("DEV Mode (use at your own risk)", ref devMode))
        {
            configuration.DevMode = devMode;
            // Note: DevMode is not saved (marked with [NonSerialized])
        }
        
        // Chat channel selection (only visible in DEV Mode)
        if (configuration.DevMode)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Chat Channel:");
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), "Warning: Changes not saved between sessions!");
            
            var channelNames = new[] { "Party", "Say", "Yell", "Shout", "Free Company", "Linkshell 1", "Echo" };
            var currentChannelIndex = (int)plugin.SelectedChatChannel;
            
            if (ImGui.Combo("Message Channel", ref currentChannelIndex, channelNames, channelNames.Length))
            {
                plugin.SelectedChatChannel = (Plugin.ChatChannel)currentChannelIndex;
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Select which chat channel announcements will be sent to.");
                ImGui.TextUnformatted("Always defaults to Party chat when plugin restarts.");
                ImGui.EndTooltip();
            }
        }
    }
}
