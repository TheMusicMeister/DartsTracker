using System;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace DartsTracker.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string newPlayerName = "";

    // Throw editing state
    private bool showThrowEditWindow = false;
    private string editingPlayerName = "";
    private int editingRoundNumber = 0;
    private int editingThrowNumber = 0;
    private int editRoll1 = 1;
    private int editRoll2 = 1;
    private int editRoll3 = 1;

    // League series state
    private bool showNewSeriesPopup = false;
    private string newSeriesName = "";
    private string newSeriesDescription = "";

    // Bracket match state
    private bool showPlayerSelectionPopup = false;
    private readonly Dictionary<string, bool> playerSelectionState = new();

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
        // Title bar with buttons on the right
        ImGui.TextUnformatted("Darts League Tracker");

        // Calculate button positions for right alignment
        var windowWidth = ImGui.GetWindowWidth();
        var buttonWidth = 80f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var rightOffset = buttonWidth * 2 + spacing + 20; // 2 buttons + spacing + padding

        ImGui.SameLine(windowWidth - rightOffset);
        if (ImGui.Button("Settings", new Vector2(buttonWidth, 0)))
        {
            plugin.ToggleConfigUi();
        }

        ImGui.SameLine();
        if (ImGui.Button("History", new Vector2(buttonWidth, 0)))
        {
            plugin.ToggleHistoryUi();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("View saved match results and detailed statistics");
            ImGui.EndTooltip();
        }

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
        
        // Draw throw editing popup
        if (showThrowEditWindow)
        {
            DrawThrowEditWindow();
        }

        // Draw new series popup
        if (showNewSeriesPopup)
        {
            DrawNewSeriesPopup();
        }

        // Draw player selection popup
        if (showPlayerSelectionPopup)
        {
            DrawPlayerSelectionPopup();
        }
    }
    
    private void DrawGameControls()
    {
        if (plugin.CurrentGame?.IsActive == true)
        {
            var game = plugin.CurrentGame;

            // Show game mode and matchup info
            var modeText = game.Mode == GameMode.Bracket ? "Bracket Match" : "Free-For-All";
            ImGui.TextUnformatted($"Mode: {modeText}");

            if (game.Mode == GameMode.Bracket && game.Players.Count == 2)
            {
                var players = game.GetOrderedPlayers();
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), $"| {players[0].Name} vs {players[1].Name}");
            }

            ImGui.TextUnformatted($"Game Active - {game.TotalRounds} rounds");

            // Show turn information
            var currentTurnPlayer = game.GetCurrentTurnPlayer();
            if (currentTurnPlayer != null)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"| Turn: {currentTurnPlayer} (Throw {game.CurrentThrowInTurn + 1}/3)");
            }

            if (plugin.CurrentTrackedPlayer != null)
            {
                ImGui.SameLine();
                ImGui.TextUnformatted($"| Rolling: {plugin.CurrentTrackedPlayer} ({plugin.CurrentDiceRolls.Count}/3)");
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
            
            // Show archive button if game is completed
            if (game.IsGameComplete)
            {
                ImGui.Spacing();
                ImGui.TextUnformatted("League Series:");

                // League series selection combo
                var allSeries = plugin.Configuration.MatchHistory.LeagueSeries;
                var selectedSeriesName = "None (Unassigned)";

                if (plugin.SelectedLeagueSeriesId.HasValue)
                {
                    var selectedSeries = allSeries.FirstOrDefault(s => s.Id == plugin.SelectedLeagueSeriesId.Value);
                    if (selectedSeries != null)
                    {
                        selectedSeriesName = selectedSeries.Name;
                    }
                }

                ImGui.SetNextItemWidth(200);
                if (ImGui.BeginCombo("##LeagueSeriesCombo", selectedSeriesName))
                {
                    // None option
                    if (ImGui.Selectable("None (Unassigned)", !plugin.SelectedLeagueSeriesId.HasValue))
                    {
                        plugin.SelectedLeagueSeriesId = null;
                    }

                    ImGui.Separator();

                    // Existing series
                    foreach (var series in allSeries)
                    {
                        var isSelected = plugin.SelectedLeagueSeriesId.HasValue && plugin.SelectedLeagueSeriesId.Value == series.Id;
                        if (ImGui.Selectable(series.Name, isSelected))
                        {
                            plugin.SelectedLeagueSeriesId = series.Id;
                        }
                    }

                    ImGui.Separator();

                    // Create new series option
                    if (ImGui.Selectable("+ Create New Series"))
                    {
                        showNewSeriesPopup = true;
                        newSeriesName = "";
                        newSeriesDescription = "";
                    }

                    ImGui.EndCombo();
                }

                ImGui.SameLine();
                if (ImGui.Button("Archive Game"))
                {
                    plugin.ArchiveGame();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted("Save this completed game to history");
                    ImGui.EndTooltip();
                }
            }
            
            // Announcement buttons
            ImGui.Spacing();
            ImGui.TextUnformatted("Announcements:");
            
            // Turn Order button
            var canAnnounceTurnOrder = game.PlayerOrder.Count > 0;
            if (!canAnnounceTurnOrder) ImGui.BeginDisabled();
            
            if (ImGui.Button("Turn Order"))
            {
                plugin.AnnounceTurnOrder();
            }
            
            if (!canAnnounceTurnOrder) ImGui.EndDisabled();
            
            if (ImGui.IsItemHovered(canAnnounceTurnOrder ? ImGuiHoveredFlags.None : ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                if (canAnnounceTurnOrder)
                {
                    ImGui.TextUnformatted("Announce the turn order to party chat");
                }
                else
                {
                    ImGui.TextUnformatted("No players in the game yet");
                }
                ImGui.EndTooltip();
            }
            
            // Current Turn button
            ImGui.SameLine();
            var canAnnounceCurrentTurn = game.GetCurrentTurnPlayer() != null;
            if (!canAnnounceCurrentTurn) ImGui.BeginDisabled();
            
            if (ImGui.Button("Current Turn"))
            {
                plugin.AnnounceCurrentTurn();
            }
            
            if (!canAnnounceCurrentTurn) ImGui.EndDisabled();
            
            if (ImGui.IsItemHovered(canAnnounceCurrentTurn ? ImGuiHoveredFlags.None : ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                if (canAnnounceCurrentTurn)
                {
                    ImGui.TextUnformatted("Announce whose turn it is to party chat");
                }
                else
                {
                    ImGui.TextUnformatted("No players in the game yet");
                }
                ImGui.EndTooltip();
            }
            
            // Scores button (available when not in middle of round)
            ImGui.SameLine();
            var canAnnounceScores = plugin.CanAnnounceScores();
            if (!canAnnounceScores) ImGui.BeginDisabled();
            
            if (ImGui.Button("Scores"))
            {
                plugin.AnnounceCurrentScores();
            }
            
            if (!canAnnounceScores) ImGui.EndDisabled();
            
            if (ImGui.IsItemHovered(canAnnounceScores ? ImGuiHoveredFlags.None : ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                if (canAnnounceScores)
                {
                    ImGui.TextUnformatted("Announce current standings to party chat");
                }
                else if (game.Players.Count == 0)
                {
                    ImGui.TextUnformatted("No players in the game yet");
                }
                else
                {
                    ImGui.TextUnformatted("Cannot announce scores while players are in the middle of a round");
                }
                ImGui.EndTooltip();
            }
            
            // Turn progression controls
            ImGui.Spacing();
            ImGui.TextUnformatted("Turn Control:");
            
            // Next Player button
            var canAdvanceTurn = plugin.CanAdvanceTurn();
            if (!canAdvanceTurn) ImGui.BeginDisabled();
            
            if (ImGui.Button("Next Player"))
            {
                plugin.AdvanceToNextPlayer();
            }
            
            if (!canAdvanceTurn) ImGui.EndDisabled();
            
            if (ImGui.IsItemHovered(canAdvanceTurn ? ImGuiHoveredFlags.None : ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                if (canAdvanceTurn)
                {
                    ImGui.TextUnformatted("Advance to the next player's turn");
                }
                else
                {
                    ImGui.TextUnformatted("Current player must complete all 3 throws before advancing");
                }
                ImGui.EndTooltip();
            }
        }
        else
        {
            ImGui.TextUnformatted("No active game");
            
            // Player setup section
            DrawManualPlayerSetup();
        }
    }
    
    private void DrawManualPlayerSetup()
    {
        ImGui.Spacing();
        ImGui.TextUnformatted("Manual Player Setup");
        ImGui.Separator();

        // Game mode selection
        ImGui.TextUnformatted("Game Mode:");
        var isFreeForAll = plugin.SelectedGameMode == GameMode.FreeForAll;
        if (ImGui.RadioButton("Free-For-All (All players compete together)", isFreeForAll))
        {
            plugin.SelectedGameMode = GameMode.FreeForAll;
        }

        var isBracket = plugin.SelectedGameMode == GameMode.Bracket;
        if (ImGui.RadioButton("Bracket Tournament (Select 2 players per match)", isBracket))
        {
            plugin.SelectedGameMode = GameMode.Bracket;
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Add new player input
        ImGui.SetNextItemWidth(200);
        if (ImGui.InputText("Player Name", ref newPlayerName, 100, ImGuiInputTextFlags.EnterReturnsTrue))
        {
            if (!string.IsNullOrWhiteSpace(newPlayerName))
            {
                plugin.AddPlayerToSetup(newPlayerName.Trim());
                newPlayerName = "";
            }
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Add Player"))
        {
            if (!string.IsNullOrWhiteSpace(newPlayerName))
            {
                plugin.AddPlayerToSetup(newPlayerName.Trim());
                newPlayerName = "";
            }
        }
        
        // Add targeted player button
        ImGui.SameLine();
        var targetedPlayerName = plugin.GetTargetedPlayerName();
        var canAddTargeted = plugin.CanAddTargetedPlayer();
        
        if (!canAddTargeted)
        {
            ImGui.BeginDisabled();
        }
        
        var buttonText = string.IsNullOrEmpty(targetedPlayerName) 
            ? "Add Target (No Target)" 
            : $"Add Target ({targetedPlayerName})";
            
        if (ImGui.Button(buttonText))
        {
            plugin.AddTargetedPlayerToSetup();
        }
        
        if (!canAddTargeted)
        {
            ImGui.EndDisabled();
            
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                if (string.IsNullOrEmpty(targetedPlayerName))
                {
                    ImGui.TextUnformatted("No player character targeted");
                }
                else
                {
                    ImGui.TextUnformatted($"{targetedPlayerName} is already in the player list");
                }
                ImGui.EndTooltip();
            }
        }
        
        // Add party members button (new line)
        var hasPartyMembers = plugin.HasPartyMembers();
        
        if (!hasPartyMembers)
        {
            ImGui.BeginDisabled();
        }
        
        var partyButtonText = hasPartyMembers 
            ? "Add Party Members" 
            : "Add Party Members (No Party)";
            
        if (ImGui.Button(partyButtonText))
        {
            var added = plugin.AddAllPartyMembersToSetup();
            // Could add a notification here if desired
        }
        
        if (!hasPartyMembers)
        {
            ImGui.EndDisabled();
            
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                ImGui.TextUnformatted("Not in a party or no party members found");
                ImGui.EndTooltip();
            }
        }
        else if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Add all party members to the player list");
            ImGui.TextUnformatted("Duplicates will be automatically skipped");
            ImGui.EndTooltip();
        }
        
        // Display current players with ordering controls
        var setupPlayers = plugin.GetSetupPlayers();
        if (setupPlayers.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Players:");
            
            for (int i = 0; i < setupPlayers.Count; i++)
            {
                var playerName = setupPlayers[i];
                ImGui.TextUnformatted($"{i + 1}. {playerName}");
                
                ImGui.SameLine();
                if (ImGui.SmallButton($"↑##{playerName}") && i > 0)
                {
                    plugin.MovePlayerUpInSetup(playerName);
                }
                
                ImGui.SameLine();
                if (ImGui.SmallButton($"↓##{playerName}") && i < setupPlayers.Count - 1)
                {
                    plugin.MovePlayerDownInSetup(playerName);
                }
                
                ImGui.SameLine();
                if (ImGui.SmallButton($"Remove##{playerName}"))
                {
                    plugin.RemovePlayerFromSetup(playerName);
                }
            }
            
            ImGui.Spacing();

            // Different button based on game mode
            if (plugin.SelectedGameMode == GameMode.FreeForAll)
            {
                if (ImGui.Button("Start Game with These Players"))
                {
                    plugin.StartNewGameWithPlayers();
                }
            }
            else // Bracket mode
            {
                var canStartBracket = setupPlayers.Count >= 2;
                if (!canStartBracket) ImGui.BeginDisabled();

                if (ImGui.Button("Start Bracket Match"))
                {
                    // Open player selection dialog
                    showPlayerSelectionPopup = true;
                    playerSelectionState.Clear();
                    foreach (var player in setupPlayers)
                    {
                        playerSelectionState[player] = false;
                    }
                }

                if (!canStartBracket) ImGui.EndDisabled();

                if (ImGui.IsItemHovered(canStartBracket ? ImGuiHoveredFlags.None : ImGuiHoveredFlags.AllowWhenDisabled))
                {
                    ImGui.BeginTooltip();
                    if (canStartBracket)
                    {
                        ImGui.TextUnformatted("Select 2 players from the pool for this match");
                    }
                    else
                    {
                        ImGui.TextUnformatted("Add at least 2 players to start a bracket match");
                    }
                    ImGui.EndTooltip();
                }
            }
        }
    }
    
    private void DrawGameTable()
    {
        var game = plugin.CurrentGame!;
        var players = game.GetOrderedPlayers();
        
        if (players.Count == 0)
        {
            ImGui.TextUnformatted("No players added to the game yet.");
            ImGui.TextUnformatted("Add players in the setup section before starting.");
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
                
                // Highlight current turn player
                var isCurrentTurn = game.IsPlayerTurn(player.Name);
                if (isCurrentTurn)
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.7f, 0.3f, 0.3f))); // Light green
                }
                else
                {
                    ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiCol.TableHeaderBg));
                }
                
                ImGui.TableSetColumnIndex(0);
                var playerPrefix = isCurrentTurn ? "▶️ " : "🎯 ";
                ImGui.TextUnformatted($"{playerPrefix}{player.Name}");
                
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
        ImGui.TextUnformatted("Add players manually to begin tracking league play.");
        
        ImGui.Spacing();
        ImGui.TextUnformatted("How it works:");
        ImGui.TextUnformatted("• Configure rounds per game in Settings (default: 5)");
        ImGui.TextUnformatted("• Each round = 3 dart throws per player");
        ImGui.TextUnformatted("• Each throw = 3 dice rolls (/random)");
        ImGui.TextUnformatted("• Add players manually, then start the game");
        ImGui.TextUnformatted("• Use 'Add Party Members' to quickly add everyone");
        ImGui.TextUnformatted("• Players take turns in the order they were added");
        ImGui.TextUnformatted("• A player's turn ends after completing 3 throws");
        ImGui.TextUnformatted("• Only the current turn player can throw darts");
        ImGui.TextUnformatted("• Use announcement buttons to share progress with party");
        
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
                // Check for double-click to edit
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                {
                    OpenThrowEditWindow(playerName, roundNumber, throwNumber, dartThrow);
                }
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
                ImGui.TextUnformatted("Double-click to edit");
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
    
    private void DrawThrowEditWindow()
    {
        if (ImGui.BeginPopupModal($"Edit Throw##EditThrow", ref showThrowEditWindow, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted($"Editing {editingPlayerName} - Round {editingRoundNumber}, Throw {editingThrowNumber}");
            ImGui.Separator();
            
            ImGui.TextUnformatted("Dice Rolls:");
            
            // Roll 1 input
            ImGui.SetNextItemWidth(100);
            if (ImGui.SliderInt("Roll 1", ref editRoll1, 1, 100))
            {
                // Ensure valid range
                editRoll1 = Math.Max(1, Math.Min(100, editRoll1));
            }
            
            // Roll 2 input
            ImGui.SetNextItemWidth(100);
            if (ImGui.SliderInt("Roll 2", ref editRoll2, 1, 100))
            {
                editRoll2 = Math.Max(1, Math.Min(100, editRoll2));
            }
            
            // Roll 3 input
            ImGui.SetNextItemWidth(100);
            if (ImGui.SliderInt("Roll 3", ref editRoll3, 1, 20))
            {
                editRoll3 = Math.Max(1, Math.Min(20, editRoll3));
            }
            
            // Preview the calculated score
            ImGui.Spacing();
            var previewResult = DartsScoring.CalculateThrow(editRoll1, editRoll2, editRoll3);
            ImGui.TextUnformatted($"Preview: {previewResult.Score} points ({previewResult.Description})");
            
            ImGui.Spacing();
            ImGui.Separator();
            
            // Buttons
            if (ImGui.Button("Save Changes"))
            {
                plugin.EditThrow(editingPlayerName, editingRoundNumber, editingThrowNumber, editRoll1, editRoll2, editRoll3);
                showThrowEditWindow = false;
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Delete Throw"))
            {
                plugin.DeleteThrow(editingPlayerName, editingRoundNumber, editingThrowNumber);
                showThrowEditWindow = false;
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                showThrowEditWindow = false;
            }
            
            ImGui.EndPopup();
        }
        
        // Open popup if flag is set but popup isn't open yet
        if (showThrowEditWindow && !ImGui.IsPopupOpen($"Edit Throw##EditThrow"))
        {
            ImGui.OpenPopup($"Edit Throw##EditThrow");
        }
    }
    
    private void OpenThrowEditWindow(string playerName, int roundNumber, int throwNumber, DartsThrow dartThrow)
    {
        editingPlayerName = playerName;
        editingRoundNumber = roundNumber;
        editingThrowNumber = throwNumber;
        editRoll1 = dartThrow.Roll1 ?? 1;
        editRoll2 = dartThrow.Roll2 ?? 1;
        editRoll3 = dartThrow.Roll3 ?? 1;
        showThrowEditWindow = true;
    }

    private void DrawNewSeriesPopup()
    {
        if (ImGui.BeginPopupModal("Create New League Series##NewSeries", ref showNewSeriesPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Create a new league series to group related matches.");
            ImGui.Spacing();

            ImGui.TextUnformatted("Series Name:");
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("##SeriesName", ref newSeriesName, 100);

            ImGui.Spacing();
            ImGui.TextUnformatted("Description (optional):");
            ImGui.SetNextItemWidth(300);
            ImGui.InputTextMultiline("##SeriesDescription", ref newSeriesDescription, 500, new Vector2(300, 60));

            ImGui.Spacing();
            ImGui.Separator();

            var canCreate = !string.IsNullOrWhiteSpace(newSeriesName);
            if (!canCreate) ImGui.BeginDisabled();

            if (ImGui.Button("Create"))
            {
                var newSeries = plugin.CreateLeagueSeries(newSeriesName.Trim(), newSeriesDescription.Trim());
                plugin.SelectedLeagueSeriesId = newSeries.Id;
                showNewSeriesPopup = false;
            }

            if (!canCreate) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                showNewSeriesPopup = false;
            }

            ImGui.EndPopup();
        }

        // Open popup if flag is set but popup isn't open yet
        if (showNewSeriesPopup && !ImGui.IsPopupOpen("Create New League Series##NewSeries"))
        {
            ImGui.OpenPopup("Create New League Series##NewSeries");
        }
    }

    private void DrawPlayerSelectionPopup()
    {
        if (ImGui.BeginPopupModal("Select Players for Match##PlayerSelection", ref showPlayerSelectionPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Select 2 players for this bracket match:");
            ImGui.Spacing();

            var selectedCount = playerSelectionState.Count(kvp => kvp.Value);

            // Display player checkboxes
            foreach (var player in playerSelectionState.Keys.ToList())
            {
                var isSelected = playerSelectionState[player];

                // Disable checkbox if 2 players are already selected and this one isn't selected
                var shouldDisable = selectedCount >= 2 && !isSelected;
                if (shouldDisable) ImGui.BeginDisabled();

                if (ImGui.Checkbox(player, ref isSelected))
                {
                    playerSelectionState[player] = isSelected;
                }

                if (shouldDisable) ImGui.EndDisabled();
            }

            ImGui.Spacing();
            ImGui.TextUnformatted($"Selected: {selectedCount} / 2 players");
            ImGui.Spacing();
            ImGui.Separator();

            // Start button
            var canStart = selectedCount == 2;
            if (!canStart) ImGui.BeginDisabled();

            if (ImGui.Button("Start Match"))
            {
                var selectedPlayers = playerSelectionState
                    .Where(kvp => kvp.Value)
                    .Select(kvp => kvp.Key)
                    .ToList();

                plugin.StartBracketMatchWithPlayers(selectedPlayers);
                showPlayerSelectionPopup = false;
            }

            if (!canStart) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                showPlayerSelectionPopup = false;
            }

            ImGui.EndPopup();
        }

        // Open popup if flag is set but popup isn't open yet
        if (showPlayerSelectionPopup && !ImGui.IsPopupOpen("Select Players for Match##PlayerSelection"))
        {
            ImGui.OpenPopup("Select Players for Match##PlayerSelection");
        }
    }
}
