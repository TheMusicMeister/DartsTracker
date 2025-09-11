﻿using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace DartsTracker.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    // We give this window a constant ID using ###.
    // This allows for labels to be dynamic, like "{FPS Counter}fps###XYZ counter window",
    // and the window ID will always be "###XYZ counter window" for ImGui
    public ConfigWindow(Plugin plugin) : base("Darts Tracker Configuration###DartsTrackerConfig")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(280, 180);
        SizeCondition = ImGuiCond.Always;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        // Flags must be added or removed before Draw() is being called, or they won't apply
        if (configuration.IsConfigWindowMovable)
        {
            Flags &= ~ImGuiWindowFlags.NoMove;
        }
        else
        {
            Flags |= ImGuiWindowFlags.NoMove;
        }
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
        
        var autoStart = configuration.AutoStartGame;
        if (ImGui.Checkbox("Auto-start game on first player", ref autoStart))
        {
            configuration.AutoStartGame = autoStart;
            configuration.Save();
        }
        
        
        ImGui.Spacing();
        ImGui.TextUnformatted("Window Settings:");
        ImGui.Separator();
        
        var movable = configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable Config Window", ref movable))
        {
            configuration.IsConfigWindowMovable = movable;
            configuration.Save();
        }
    }
}
