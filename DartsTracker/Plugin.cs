using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using DartsTracker.Windows;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using ECommons.DalamudServices;
using ECommons;
using ECommons.Automation;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Enums;
using ECommons.Automation.NeoTaskManager;

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
    [PluginService] internal static ITargetManager TargetManager { get; private set; } = null!;
    [PluginService] internal static IPartyList PartyList { get; private set; } = null!;

    private const string CommandName = "/darts";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("DartsTracker");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private HistoryWindow HistoryWindow { get; init; }

    // Game state
    private DartsGame? ActiveGame;
    private string? TrackedPlayerName;
    private readonly List<int> DiceRolls = new();
    private readonly List<string> SetupPlayers = new();
    private const string TriggerPhrase = "tosses a dart at the board";
    
    
    // Development settings (non-persistent)
    public enum ChatChannel
    {
        Party,
        Say,
        Yell,
        Shout,
        FreeCompany,
        Linkshell1,
        Echo
    }
    
    public ChatChannel SelectedChatChannel { get; set; } = ChatChannel.Party;
    
    // TaskManager for chat message handling
    private TaskManager ChatTaskManager = new();

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        ECommonsMain.Init(PluginInterface, this);

        // You might normally want to embed resources and load them from the manifest stream
        var goatImagePath = Path.Combine(PluginInterface.AssemblyLocation.Directory?.FullName!, "goat.png");

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, goatImagePath);
        HistoryWindow = new HistoryWindow(this);
        ChatTaskManager = new TaskManager();

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(HistoryWindow);

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
        
    }

    public void Dispose()
    {
        // Unregister all actions to not leak anything during disposal of plugin
        ChatGui.ChatMessage -= OnChatMessage;
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
        
        // Dispose TaskManager
        ChatTaskManager?.Dispose();
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        HistoryWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        // In response to the slash command, toggle the display status of our main ui
        MainWindow.Toggle();
    }
    
    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
    public void ToggleHistoryUi() => HistoryWindow.Toggle();
    
    private string GetChatCommand(string message)
    {
        var prefix = SelectedChatChannel switch
        {
            ChatChannel.Party => "/p",
            ChatChannel.Say => "/s",
            ChatChannel.Yell => "/y",
            ChatChannel.Shout => "/sh",
            ChatChannel.FreeCompany => "/fc",
            ChatChannel.Linkshell1 => "/l1",
            ChatChannel.Echo => "/e",
            _ => "/p"
        };
        
        // Handle messages that already have the /p prefix from old code
        if (message.StartsWith("/p "))
        {
            return message.Replace("/p ", $"{prefix} ");
        }
        
        return $"{prefix} {message}";
    }
    
    private void SendChatMessage(string message)
    {
        var chatCommand = GetChatCommand(message);
        Chat.ExecuteCommand(chatCommand);
    }
    
    private void EnqueueChatMessage(string message)
    {
        ChatTaskManager.Enqueue(() => SendChatMessage(message));
        ChatTaskManager.EnqueueDelay(Configuration.ChatMessageDelayMs);
    }
    
    private string GetPlayerNameFromMessage(SeString sender, SeString message)
    {
        // Try to get player name from message payload first
        foreach (var payload in message.Payloads)
        {
            if (payload is PlayerPayload playerPayload)
            {
                var rawName = playerPayload.PlayerName;
                var cleanName = CleanPlayerName(rawName);
                return cleanName;
            }
        }
        
        // Try to get from sender payload as fallback
        foreach (var payload in sender.Payloads)
        {
            if (payload is PlayerPayload playerPayload)
            {
                var rawName = playerPayload.PlayerName;
                var cleanName = CleanPlayerName(rawName);
                return cleanName;
            }
        }
        
        // Try alternative: check if sender text has clean name without special characters
        var senderText = sender.TextValue;
        var cleanSenderText = CleanPlayerName(senderText);
        
        // Final fallback to sender text value
        return cleanSenderText;
    }
    
    private string CleanPlayerName(string playerName)
    {
        if (string.IsNullOrEmpty(playerName))
            return "";
            
        // Remove common prefixes that might contain emojis or special characters
        var cleaned = playerName;
        
        // Remove any leading non-letter characters (emojis, symbols, etc.)
        while (cleaned.Length > 0 && !char.IsLetter(cleaned[0]))
        {
            cleaned = cleaned.Substring(1);
        }
        
        // Remove any trailing non-letter/non-space characters
        while (cleaned.Length > 0 && !char.IsLetterOrDigit(cleaned[cleaned.Length - 1]) && cleaned[cleaned.Length - 1] != ' ')
        {
            cleaned = cleaned.Substring(0, cleaned.Length - 1);
        }
        
        // Trim any remaining whitespace
        cleaned = cleaned.Trim();
        
        return cleaned;
    }

    private void OnChatMessage(XivChatType type, int timestamp, ref SeString sender, ref SeString message, ref bool isHandled)
    {
        // Only process party chat messages
        if (type != XivChatType.Party)
            return;

        var messageText = message.TextValue.ToLowerInvariant();
        
        // Get reliable player name from message payload
        var senderName = GetPlayerNameFromMessage(sender, message);
        

        // Check for trigger phrase
        if (messageText.Contains(TriggerPhrase))
        {
            // If someone else is currently throwing, ignore other players' trigger phrases
            if (TrackedPlayerName != null && TrackedPlayerName != senderName)
            {
                Log.Information($"Ignoring trigger phrase from {senderName} - {TrackedPlayerName} is currently throwing");
                return;
            }
            
            
            // Check if detection is paused
            if (ActiveGame != null && ActiveGame.IsDetectionPaused)
            {
                Log.Information($"Ignoring trigger phrase from {senderName} - detection is paused until next turn is announced");
                return;
            }
            
            // Only allow players in the game to throw
            if (ActiveGame != null && !ActiveGame.Players.ContainsKey(senderName))
            {
                Log.Information($"Ignoring trigger phrase from {senderName} - not in the player list. Players: {string.Join(", ", ActiveGame.Players.Keys)}");
                return;
            }
            
            // Enforce turn order
            if (ActiveGame != null)
            {
                var currentTurnPlayer = ActiveGame.GetCurrentTurnPlayer();
                
                if (!ActiveGame.IsPlayerTurn(senderName))
                {
                    Log.Information($"Ignoring trigger phrase from {senderName} - it's {currentTurnPlayer}'s turn (Turn index: {ActiveGame.CurrentPlayerIndex}, Throw: {ActiveGame.CurrentThrowInTurn + 1}/3)");
                    return;
                }
            }
            
            // Set up tracking for the current player
            if (ActiveGame != null)
            {
                TrackedPlayerName = senderName;
                DiceRolls.Clear();
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
                        
                        // Update current throw counter but don't advance to next player automatically
                        if (ActiveGame != null)
                        {
                            ActiveGame.CurrentThrowInTurn++;
                            
                            // Check if all 3 throws are complete for this turn
                            var currentPlayerRound = player?.GetCurrentRound();
                            var allThrowsComplete = currentPlayerRound != null && 
                                                   currentPlayerRound.Throw1.IsComplete && 
                                                   currentPlayerRound.Throw2.IsComplete && 
                                                   currentPlayerRound.Throw3.IsComplete;
                            
                            if (allThrowsComplete)
                            {
                                // Player completed all 3 throws - pause detection and wait for manual advance
                                ActiveGame.IsDetectionPaused = true;
                            }
                        }
                        
                        // Check if the entire game is complete
                        if (ActiveGame != null && ActiveGame.IsGameComplete)
                        {
                            // Game completed! Ready for archival.
                            // Don't automatically save or close - wait for user to archive
                        }
                        
                        // Clear current tracking to allow other players to throw
                        DiceRolls.Clear();
                        TrackedPlayerName = null;
                    }
                }
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
    }
    
    public void StartNewGameWithPlayers()
    {
        if (SetupPlayers.Count == 0) return;
        
        ActiveGame = new DartsGame(Configuration.RoundsPerGame);
        foreach (var playerName in SetupPlayers)
        {
            ActiveGame.AddPlayer(playerName); // SetupPlayers already contains cleaned names
        }
        ActiveGame.IsActive = true;
        ActiveGame.ResetTurn();
        TrackedPlayerName = null;
        DiceRolls.Clear();
        SetupPlayers.Clear();
    }
    
    // Manual player setup methods
    public void AddPlayerToSetup(string playerName)
    {
        var cleanName = CleanPlayerName(playerName);
        if (!SetupPlayers.Contains(cleanName))
        {
            SetupPlayers.Add(cleanName);
        }
    }
    
    public void RemovePlayerFromSetup(string playerName)
    {
        var cleanName = CleanPlayerName(playerName);
        SetupPlayers.Remove(cleanName);
    }
    
    public void MovePlayerUpInSetup(string playerName)
    {
        var cleanName = CleanPlayerName(playerName);
        var index = SetupPlayers.IndexOf(cleanName);
        if (index > 0)
        {
            SetupPlayers.RemoveAt(index);
            SetupPlayers.Insert(index - 1, cleanName);
        }
    }
    
    public void MovePlayerDownInSetup(string playerName)
    {
        var cleanName = CleanPlayerName(playerName);
        var index = SetupPlayers.IndexOf(cleanName);
        if (index >= 0 && index < SetupPlayers.Count - 1)
        {
            SetupPlayers.RemoveAt(index);
            SetupPlayers.Insert(index + 1, cleanName);
        }
    }
    
    public IReadOnlyList<string> GetSetupPlayers() => SetupPlayers.AsReadOnly();
    
    public List<string> GetPartyMemberNames()
    {
        var partyMembers = new List<string>();
        
        // Add the current player
        if (ClientState.LocalPlayer != null)
        {
            var cleanName = CleanPlayerName(ClientState.LocalPlayer.Name.ToString());
            partyMembers.Add(cleanName);
        }
        
        // Add other party members
        foreach (var member in PartyList)
        {
            var memberName = CleanPlayerName(member.Name.ToString());
            var localPlayerName = CleanPlayerName(ClientState.LocalPlayer?.Name.ToString() ?? "");
            if (memberName != localPlayerName)
            {
                partyMembers.Add(memberName);
            }
        }
        
        return partyMembers;
    }
    
    public int AddAllPartyMembersToSetup()
    {
        var partyMembers = GetPartyMemberNames();
        var addedMembers = new List<string>();
        
        foreach (var memberName in partyMembers)
        {
            if (!SetupPlayers.Contains(memberName))
            {
                SetupPlayers.Add(memberName);
                addedMembers.Add(memberName);
            }
        }
        
        
        return addedMembers.Count;
    }
    
    public bool HasPartyMembers()
    {
        return PartyList.Count > 0 || ClientState.LocalPlayer != null;
    }
    
    public string? GetTargetedPlayerName()
    {
        var target = TargetManager.Target;
        if (target != null && target.ObjectKind == ObjectKind.Player)
        {
            return CleanPlayerName(target.Name.ToString());
        }
        return null;
    }
    
    public bool CanAddTargetedPlayer()
    {
        var targetedName = GetTargetedPlayerName();
        return !string.IsNullOrEmpty(targetedName) && !SetupPlayers.Contains(targetedName);
    }
    
    public void AddTargetedPlayerToSetup()
    {
        var targetedName = GetTargetedPlayerName();
        if (!string.IsNullOrEmpty(targetedName) && !SetupPlayers.Contains(targetedName))
        {
            AddPlayerToSetup(targetedName);
        }
    }

    public void EndGame()
    {
        if (ActiveGame != null)
        {
            // Save match results if game was completed
            if (ShouldSaveMatch(ActiveGame))
            {
                SaveMatchResults(ActiveGame);
            }
            
            ActiveGame.IsActive = false;
        }
    }
    
    private bool ShouldSaveMatch(DartsGame game)
    {
        // Only save if at least one player has completed at least one round
        return game.Players.Values.Any(p => p.Rounds.Any(r => r.IsComplete));
    }
    
    private void SaveMatchResults(DartsGame game)
    {
        try
        {
            var matchResult = new MatchResult
            {
                CompletedAt = DateTime.Now,
                TotalRounds = game.TotalRounds,
                WasManualPlayerMode = true,
                Duration = DateTime.Now - game.StartTime
            };
            
            // Convert players to match results and sort by score
            var playerResults = game.Players.Values
                .Select(player => ConvertToMatchPlayerResult(player))
                .OrderByDescending(p => p.TotalScore)
                .ToList();
            
            // Assign positions
            for (int i = 0; i < playerResults.Count; i++)
            {
                playerResults[i].Position = i + 1;
            }
            
            matchResult.Players = playerResults;
            
            Configuration.MatchHistory.AddMatch(matchResult);
            Configuration.Save();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to save match results: {ex.Message}");
        }
    }
    
    private MatchPlayerResult ConvertToMatchPlayerResult(DartsPlayer player)
    {
        var result = new MatchPlayerResult
        {
            PlayerName = player.Name,
            TotalScore = player.TotalScore
        };
        
        foreach (var round in player.Rounds.Where(r => r.IsComplete))
        {
            var roundResult = new MatchRoundResult
            {
                RoundNumber = round.RoundNumber
            };
            
            var throws = new[] { round.Throw1, round.Throw2, round.Throw3 }
                .Where(t => t.IsComplete)
                .Select(t => new MatchThrowResult
                {
                    Roll1 = t.Roll1!.Value,
                    Roll2 = t.Roll2!.Value,
                    Roll3 = t.Roll3!.Value,
                    Score = t.Score,
                    Description = t.Description
                }).ToList();
            
            roundResult.Throws = throws;
            result.Rounds.Add(roundResult);
        }
        
        return result;
    }

    public void ResetGame()
    {
        ActiveGame = null;
        TrackedPlayerName = null;
        DiceRolls.Clear();
        SetupPlayers.Clear();
    }
    
    public void ArchiveGame()
    {
        if (ActiveGame != null)
        {
            // Save match results if game was completed
            if (ShouldSaveMatch(ActiveGame))
            {
                SaveMatchResults(ActiveGame);
            }
            
            ActiveGame = null;
            TrackedPlayerName = null;
            DiceRolls.Clear();
            SetupPlayers.Clear();
        }
    }

    public void AnnounceRoundResults(string playerName, DartsRound round)
    {
        if (!round.IsComplete) return;
        
        var throw1 = $"{round.Throw1.Score} ({round.Throw1.Description})";
        var throw2 = $"{round.Throw2.Score} ({round.Throw2.Description})";
        var throw3 = $"{round.Throw3.Score} ({round.Throw3.Description})";
        var roundTotal = round.RoundScore;
        
        var message = $"{playerName} threw a {throw1}, {throw2}, and {throw3} for a total of {roundTotal}!";
        
        // Send message to chat using TaskManager
        EnqueueChatMessage($"/p {message}");
    }
    
    public void AnnounceTurnOrder()
    {
        if (ActiveGame == null) return;
        
        var players = ActiveGame.GetOrderedPlayers();
        if (players.Count == 0) return;
        
        // Send first message immediately
        EnqueueChatMessage("/p ===== Turn Order =====");
        
        for (int i = 0; i < players.Count; i++)
        {
            var playerMessage = $"{i + 1}. {players[i].Name}";
            EnqueueChatMessage($"/p {playerMessage}");
        }
    }
    
    public void AnnounceCurrentTurn()
    {
        if (ActiveGame == null) return;
        
        var currentPlayer = ActiveGame.GetCurrentTurnPlayer();
        if (currentPlayer == null) return;
        
        var player = ActiveGame.GetPlayer(currentPlayer);
        var currentRound = player?.GetCurrentRound();
        if (currentRound == null) return;
        
        var roundNumber = currentRound.RoundNumber;
        var throwNumber = ActiveGame.CurrentThrowInTurn + 1;
        
        var message = ActiveGame.CurrentThrowInTurn > 0 
            ? $"== It's {currentPlayer}'s turn! Round {roundNumber} (Throw {throwNumber}/3)"
            : $"== It's {currentPlayer}'s turn! Round {roundNumber}";
        
        // Resume detection when turn is announced
        ActiveGame.ResumeDetection();
        
        EnqueueChatMessage($"/p {message}");
    }
    
    public void AnnounceCurrentScores()
    {
        if (ActiveGame == null) return;
        
        var leaderboard = ActiveGame.GetLeaderboard();
        if (leaderboard.Count == 0) return;
        
        AnnounceCurrentScoresMultiLine(leaderboard);
    }
    
    public void AnnounceHistoricalScores(MatchResult match)
    {
        var leaderboard = match.Players.OrderByDescending(p => p.TotalScore).ToList();
        if (leaderboard.Count == 0) return;
        
        AnnounceHistoricalScoresMultiLine(leaderboard);
    }
    
    private void AnnounceCurrentScoresMultiLine(List<DartsPlayer> players)
    {
        // Get the number of completed rounds
        var completedRounds = GetCompletedRoundsCount();
        
        // Send title with completed rounds count immediately
        var title = completedRounds > 0 ? $"Current Standings After {completedRounds} Round{(completedRounds == 1 ? "" : "s")}" : "Current Standings";
        EnqueueChatMessage($"/p ===== {title} =====");
        
        // Send each player with their position with delays
        for (int i = 0; i < players.Count; i++)
        {
            var position = i switch
            {
                0 => "1st",
                1 => "2nd", 
                2 => "3rd",
                _ => $"{i + 1}th"
            };
            
            var playerMessage = $"{position} {players[i].Name}: {players[i].TotalScore}pts";
            EnqueueChatMessage($"/p {playerMessage}");
        }
    }
    
    private void AnnounceHistoricalScoresMultiLine(List<MatchPlayerResult> players)
    {
        // Send title immediately
        EnqueueChatMessage("/p ===== Historical Standings =====");
        
        // Send each player with their position with delays
        for (int i = 0; i < players.Count; i++)
        {
            var position = i switch
            {
                0 => "1st",
                1 => "2nd", 
                2 => "3rd",
                _ => $"{i + 1}th"
            };
            
            var playerMessage = $"{position} {players[i].PlayerName}: {players[i].TotalScore}pts";
            EnqueueChatMessage($"/p {playerMessage}");
        }
    }
    
    public bool CanAnnounceScores()
    {
        if (ActiveGame == null || !ActiveGame.IsActive) return false;
        
        // Can't announce scores if we're in the middle of a round
        // (i.e., not all players have completed the current round)
        return !IsInMiddleOfRound();
    }
    
    private bool IsInMiddleOfRound()
    {
        if (ActiveGame == null) return false;
        
        // Get all players and check if they're all at the same round completion level
        var players = ActiveGame.Players.Values.ToList();
        if (players.Count == 0) return false;
        
        // Find the minimum number of completed rounds among all players
        var minCompletedRounds = players.Min(p => p.Rounds.Count(r => r.IsComplete));
        var maxCompletedRounds = players.Max(p => p.Rounds.Count(r => r.IsComplete));
        
        // If there's a difference, we're in the middle of a round
        return minCompletedRounds != maxCompletedRounds;
    }
    
    private int GetCompletedRoundsCount()
    {
        if (ActiveGame == null) return 0;
        
        var players = ActiveGame.Players.Values.ToList();
        if (players.Count == 0) return 0;
        
        // Return the minimum number of completed rounds among all players
        // This represents the number of rounds that ALL players have completed
        return players.Min(p => p.Rounds.Count(r => r.IsComplete));
    }
    
    public void EditThrow(string playerName, int roundNumber, int throwNumber, int roll1, int roll2, int roll3)
    {
        if (ActiveGame == null) return;
        
        var player = ActiveGame.GetPlayer(playerName);
        if (player == null) return;
        
        var round = player.Rounds.FirstOrDefault(r => r.RoundNumber == roundNumber);
        if (round == null) return;
        
        DartsThrow? targetThrow = throwNumber switch
        {
            1 => round.Throw1,
            2 => round.Throw2,
            3 => round.Throw3,
            _ => null
        };
        
        if (targetThrow == null) return;
        
        // Update the throw with new values
        targetThrow.Roll1 = roll1;
        targetThrow.Roll2 = roll2;
        targetThrow.Roll3 = roll3;
        targetThrow.CalculateScore();
        
    }
    
    public void DeleteThrow(string playerName, int roundNumber, int throwNumber)
    {
        if (ActiveGame == null) return;
        
        var player = ActiveGame.GetPlayer(playerName);
        if (player == null) return;
        
        var round = player.Rounds.FirstOrDefault(r => r.RoundNumber == roundNumber);
        if (round == null) return;
        
        DartsThrow? targetThrow = throwNumber switch
        {
            1 => round.Throw1,
            2 => round.Throw2,
            3 => round.Throw3,
            _ => null
        };
        
        if (targetThrow == null) return;
        
        // Reset the throw to incomplete state
        targetThrow.Roll1 = null;
        targetThrow.Roll2 = null;
        targetThrow.Roll3 = null;
        targetThrow.Score = 0;
        targetThrow.Description = "In Progress...";
        
        // If this player's turn was completed but we deleted a throw, make it their turn again
        if (ActiveGame.IsPlayerTurn(playerName) == false)
        {
            // Find this player's index and set them as current turn
            var playerIndex = ActiveGame.PlayerOrder.IndexOf(playerName);
            if (playerIndex >= 0)
            {
                ActiveGame.CurrentPlayerIndex = playerIndex;
                // Determine which throw they should be on based on completed throws in this round
                var completedThrows = 0;
                if (round.Throw1.IsComplete) completedThrows++;
                if (round.Throw2.IsComplete) completedThrows++;
                if (round.Throw3.IsComplete) completedThrows++;
                ActiveGame.CurrentThrowInTurn = completedThrows;
                ActiveGame.ResumeDetection(); // Allow them to throw again
            }
        }
        
    }
    
    public void AdvanceToNextPlayer()
    {
        if (ActiveGame == null) return;
        
        var currentPlayer = ActiveGame.GetCurrentTurnPlayer();
        
        // Move to next player
        ActiveGame.CurrentPlayerIndex = (ActiveGame.CurrentPlayerIndex + 1) % ActiveGame.PlayerOrder.Count;
        ActiveGame.CurrentThrowInTurn = 0;
        ActiveGame.ResumeDetection();
        
        var nextPlayer = ActiveGame.GetCurrentTurnPlayer();
    }
    
    public bool CanAdvanceTurn()
    {
        if (ActiveGame == null) return false;
        
        var currentPlayer = ActiveGame.GetCurrentTurnPlayer();
        if (currentPlayer == null) return false;
        
        var player = ActiveGame.GetPlayer(currentPlayer);
        var currentRound = player?.GetCurrentRound();
        
        // Can advance if current player has completed all 3 throws in their current turn
        return currentRound != null && 
               currentRound.Throw1.IsComplete && 
               currentRound.Throw2.IsComplete && 
               currentRound.Throw3.IsComplete;
    }
}
