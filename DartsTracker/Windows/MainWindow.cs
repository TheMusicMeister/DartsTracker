using System;
using System.Numerics;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace DartsTracker.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;

    public MainWindow(Plugin plugin, string goatImagePath)
        : base("Darts League Tracker##DartsTrackerMain", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(600, 400),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextUnformatted("Darts League Tracker");
        ImGui.Separator();
        
        // Game status and controls
        DrawGameControls();
        
        ImGui.Spacing();
        
        // Main game display
        if (plugin.CurrentGame != null && plugin.CurrentGame.IsActive)
        {
            DrawGameTable();
        }
        else
        {
            DrawWaitingState();
        }
    }
    
    private void DrawGameControls()
    {
        if (plugin.CurrentGame?.IsActive == true)
        {
            ImGui.TextUnformatted($"Game Active - {plugin.CurrentGame.TotalRounds} rounds");
            if (plugin.CurrentTrackedPlayer != null)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"| Tracking: {plugin.CurrentTrackedPlayer} ({plugin.CurrentDiceRolls.Count}/3)");
            }
            
            if (ImGui.Button("End Game"))
            {
                plugin.EndGame();
            }
            ImGui.SameLine();
            if (ImGui.Button("Reset Game"))
            {
                plugin.ResetGame();
            }
        }
        else
        {
            ImGui.TextUnformatted("No active game");
            if (ImGui.Button("Start New Game"))
            {
                plugin.StartNewGame();
            }
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Settings"))
        {
            plugin.ToggleConfigUi();
        }
    }
    
    private void DrawGameTable()
    {
        var game = plugin.CurrentGame!;
        var players = game.Players.Values.OrderByDescending(p => p.TotalScore).ToList();
        
        if (players.Count == 0)
        {
            ImGui.TextUnformatted("Waiting for players to throw darts...");
            ImGui.TextUnformatted("Players type 'tosses a dart at the board' in party chat to join");
            return;
        }
        
        // Table with fixed columns: Player/Round | Throw 1 | Throw 2 | Throw 3 | Round Total | Status | Announce
        if (ImGui.BeginTable("GameTable", 7, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
        {
            // Setup columns
            ImGui.TableSetupColumn("Player / Round", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Throw 1", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Throw 2", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Throw 3", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Round Total", ImGuiTableColumnFlags.WidthFixed, 90);
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Announce", ImGuiTableColumnFlags.WidthFixed, 80);
            
            ImGui.TableHeadersRow();
            
            // Display each player with their rounds
            foreach (var player in players)
            {
                // Player header row
                ImGui.TableNextRow();
                ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
                
                ImGui.TableSetColumnIndex(0);
                ImGui.TextUnformatted($"🎯 {player.Name}");
                
                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted("");
                
                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted("");
                
                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted("");
                
                ImGui.TableSetColumnIndex(4);
                ImGui.TextUnformatted($"Total: {player.TotalScore}");
                
                ImGui.TableSetColumnIndex(5);
                var status = player.IsGameComplete ? "✅ COMPLETE" : $"🎲 Round {player.GetCurrentRound()?.RoundNumber ?? game.TotalRounds}";
                ImGui.TextUnformatted(status);
                
                ImGui.TableSetColumnIndex(6);
                ImGui.TextUnformatted("");
                
                // Round rows for this player
                foreach (var round in player.Rounds)
                {
                    ImGui.TableNextRow();
                    
                    ImGui.TableSetColumnIndex(0);
                    ImGui.TextUnformatted($"  Round {round.RoundNumber}");
                    
                    // Determine if this is the active round and throw for this player
                    var isActivePlayer = plugin.CurrentTrackedPlayer == player.Name;
                    var currentRound = player.GetCurrentRound();
                    var isActiveRound = currentRound == round;
                    var activeThrow = isActiveRound ? round.GetCurrentThrow() : null;
                    
                    ImGui.TableSetColumnIndex(1);
                    DrawThrowCell(round.Throw1, player.Name, round.RoundNumber, 1, isActivePlayer, isActiveRound, activeThrow);
                    
                    ImGui.TableSetColumnIndex(2);
                    DrawThrowCell(round.Throw2, player.Name, round.RoundNumber, 2, isActivePlayer, isActiveRound, activeThrow);
                    
                    ImGui.TableSetColumnIndex(3);
                    DrawThrowCell(round.Throw3, player.Name, round.RoundNumber, 3, isActivePlayer, isActiveRound, activeThrow);
                    
                    ImGui.TableSetColumnIndex(4);
                    ImGui.TextUnformatted(round.RoundScore.ToString());
                    
                    ImGui.TableSetColumnIndex(5);
                    if (round.IsComplete)
                    {
                        ImGui.TextUnformatted("Complete");
                    }
                    else if (plugin.CurrentTrackedPlayer == player.Name)
                    {
                        var currentThrow = round.GetCurrentThrow();
                        var throwNum = currentThrow == round.Throw1 ? 1 : currentThrow == round.Throw2 ? 2 : 3;
                        ImGui.TextUnformatted($"Throw {throwNum} in progress");
                    }
                    else
                    {
                        ImGui.TextUnformatted("Waiting for throws");
                    }
                    
                    ImGui.TableSetColumnIndex(6);
                    if (round.IsComplete)
                    {
                        if (ImGui.SmallButton($"Announce##{player.Name}R{round.RoundNumber}"))
                        {
                            plugin.AnnounceRoundResults(player.Name, round);
                        }
                    }
                    else
                    {
                        ImGui.TextUnformatted("");
                    }
                }
                
                // Add spacing between players
                if (player != players.Last())
                {
                    ImGui.TableNextRow();
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.Separator));
                    for (int i = 0; i < 7; i++)
                    {
                        ImGui.TableSetColumnIndex(i);
                        ImGui.TextUnformatted("");
                    }
                }
            }
            
            ImGui.EndTable();
        }
        
        // Show leaderboard
        ImGui.Spacing();
        ImGui.TextUnformatted("Leaderboard:");
        var leaderboard = game.GetLeaderboard();
        for (int i = 0; i < leaderboard.Count; i++)
        {
            var player = leaderboard[i];
            var status = player.IsGameComplete ? "COMPLETE" : $"Round {player.GetCurrentRound()?.RoundNumber ?? game.TotalRounds}";
            ImGui.TextUnformatted($"{i + 1}. {player.Name}: {player.TotalScore} pts ({status})");
        }
    }
    
    private void DrawWaitingState()
    {
        ImGui.TextUnformatted("Start a new game to begin tracking league play.");
        ImGui.Spacing();
        ImGui.TextUnformatted("How it works:");
        ImGui.TextUnformatted("• Configure rounds per game in Settings (default: 5)");
        ImGui.TextUnformatted("• Each round = 3 dart throws per player");
        ImGui.TextUnformatted("• Each throw = 3 dice rolls (/random)");
        ImGui.TextUnformatted("• Players type 'tosses a dart at the board' to throw");
        ImGui.Spacing();
        ImGui.TextUnformatted("Scoring:");
        ImGui.TextUnformatted("• Roll 1: Bullseye check (1-5=Double Bull 50pts, 6-15=Single Bull 25pts)");
        ImGui.TextUnformatted("• Roll 2: Ring (1-30=Single, 31-40=Triple, 41-70=Single, 71-80=Double, 81-100=Miss)");
        ImGui.TextUnformatted("• Roll 3: Wedge number (1-20)");
        ImGui.TextUnformatted("• Final score = Wedge × Ring multiplier (unless bullseye)");
    }
    
    private void DrawThrowCell(DartsThrow dartThrow, string playerName, int roundNumber, int throwNumber, bool isActivePlayer, bool isActiveRound, DartsThrow? activeThrow)
    {
        if (dartThrow.IsComplete)
        {
            // Make the score clickable to show detailed rolls
            if (ImGui.Selectable($"{dartThrow.Score} ({dartThrow.Description})##{playerName}R{roundNumber}T{throwNumber}", false, ImGuiSelectableFlags.None))
            {
                // Click handled by selectable
            }
            
            // Show tooltip with individual rolls on hover
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted($"Throw {throwNumber} Details:");
                ImGui.Separator();
                ImGui.TextUnformatted($"Roll 1: {dartThrow.Roll1}");
                ImGui.TextUnformatted($"Roll 2: {dartThrow.Roll2}");
                ImGui.TextUnformatted($"Roll 3: {dartThrow.Roll3}");
                ImGui.Separator();
                ImGui.TextUnformatted($"Final Score: {dartThrow.Score}");
                ImGui.TextUnformatted($"Description: {dartThrow.Description}");
                ImGui.EndTooltip();
            }
        }
        else if (isActivePlayer && isActiveRound && activeThrow == dartThrow)
        {
            ImGui.TextUnformatted($"Rolling... ({plugin.CurrentDiceRolls.Count}/3)");
        }
        else
        {
            ImGui.TextUnformatted("-");
        }
    }
}
