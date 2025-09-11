using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using DartsTracker.Windows;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using ECommons;
using ECommons.Automation;

namespace DartsTracker;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;

    private const string CommandName = "/darts";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("DartsTracker");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    // Game state
    private DartsGame? ActiveGame;
    private string? TrackedPlayerName;
    private readonly List<int> DiceRolls = new();
    private const string TriggerPhrase = "tosses a dart at the board";

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ECommonsMain.Init(PluginInterface, this, Module.All);

        // You might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the Darts Tracker interface"
        });

        // Tell the UI system that we want our windows to be drawn throught he window system
        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;

        // This adds a button to the plugin installer entry of this plugin which allows
        // toggling the display status of the configuration ui
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

        // Adds another button doing the same but for the main ui of the plugin
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;

        // Initialize chat monitoring
        ChatGui.ChatMessage += OnChatMessage;
        
        Log.Information($"DartsTracker initialized - watching for trigger phrase");
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        ChatGui.ChatMessage -= OnChatMessage;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of our main ui
        MainWindow.Toggle();
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        // Only process party chat messages
        if (type != XivChatType.Party)
            return;

        var messageText = message.TextValue.ToLowerInvariant();
        var senderName = sender.TextValue;

        // Check for trigger phrase
        if (messageText.Contains(TriggerPhrase))
        {
            // If someone else is currently throwing, ignore other players' trigger phrases
            if (TrackedPlayerName != null && TrackedPlayerName != senderName)
            {
                Log.Information($"Ignoring trigger phrase from {senderName} - {TrackedPlayerName} is currently throwing");
                return;
            }
            // Start game if auto-start is enabled and no game is active
            if (Configuration.AutoStartGame && ActiveGame == null)
            {
                StartNewGame();
            }
            
            // Add player to game if not already added
            if (ActiveGame != null)
            {
                ActiveGame.AddPlayer(senderName);
                
                
                TrackedPlayerName = senderName;
                DiceRolls.Clear();
                Log.Information($"Started tracking darts for player: {senderName}");
            }
            return;
        }

        // Check for dice rolls from tracked player (ignore rolls from other players)
        if (TrackedPlayerName != null && senderName == TrackedPlayerName && ActiveGame != null && DiceRolls.Count < 3)
        {
            var diceMatch = Regex.Match(messageText, @".*Random! \(1-\d+\) (\d+)", RegexOptions.IgnoreCase);
            if (diceMatch.Success)
            {
                var rollValue = int.Parse(diceMatch.Groups[1].Value);
                DiceRolls.Add(rollValue);
                
                var player = ActiveGame.GetPlayer(TrackedPlayerName);
                var currentRound = player?.GetCurrentRound();
                var currentThrow = currentRound?.GetCurrentThrow();
                
                if (currentThrow != null && currentThrow.IsActive)
                {
                    // Add roll using the new method
                    currentThrow.AddRoll(rollValue);
                    
                    if (currentThrow.IsComplete)
                    {
                        currentThrow.CalculateScore();
                        
                        Log.Information($"Completed throw for {TrackedPlayerName}: {string.Join(", ", DiceRolls)} = {currentThrow.Score} points ({currentThrow.Description})");
                        
                        // Check if round is complete (all 3 throws done)
                        var round = player?.GetCurrentRound();
                        if (round != null && round.IsComplete)
                        {
                            Log.Information($"Round {round.RoundNumber} completed for {TrackedPlayerName} with total score: {round.RoundScore}");
                        }
                        
                        // Clear current tracking to allow other players to throw
                        DiceRolls.Clear();
                        TrackedPlayerName = null;
                    }
                }
                
                Log.Information($"Dice roll {DiceRolls.Count}/3: {rollValue}");
            }
        }
        
    }

    // Public properties for UI access
    public string? CurrentTrackedPlayer => TrackedPlayerName;
    public IReadOnlyList<int> CurrentDiceRolls => DiceRolls.AsReadOnly();
    public DartsGame? CurrentGame => ActiveGame;

    // Public methods
    public void StartNewGame()
    {
        ActiveGame = new DartsGame(Configuration.RoundsPerGame);
        ActiveGame.IsActive = true;
        TrackedPlayerName = null;
        DiceRolls.Clear();
        Log.Information($"Started new game with {Configuration.RoundsPerGame} rounds");
    }

    public void EndGame()
    {
        if (ActiveGame != null)
        {
            ActiveGame.IsActive = false;
            Log.Information("Game ended by user");
        }
    }

    public void ResetGame()
    {
        ActiveGame = null;
        TrackedPlayerName = null;
        DiceRolls.Clear();
        Log.Information("Game reset by user");
    }

    public void AnnounceRoundResults(string playerName, DartsRound round)
    {
        if (!round.IsComplete) return;
        
        var throw1 = $"{round.Throw1.Score} ({round.Throw1.Description})";
        var throw2 = $"{round.Throw2.Score} ({round.Throw2.Description})";
        var throw3 = $"{round.Throw3.Score} ({round.Throw3.Description})";
        var roundTotal = round.RoundScore;
        
        var message = $"{playerName} threw a {throw1}, {throw2}, and {throw3} for a total of {roundTotal}!";
        
        // Send message to party chat using ECommons
        Chat.ExecuteCommand($"/p {message}");
        Log.Information($"Announced round results: {message}");
    }
}
