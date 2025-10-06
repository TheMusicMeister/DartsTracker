using System;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace DartsTracker.Windows;

public class BracketWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private Tournament? selectedTournament;
    private MatchResult? selectedMatchDetails = null;
    private bool showCreateTournamentPopup = false;
    private string newTournamentName = "";
    private bool linkToLeagueSeries = false;
    private Guid? selectedLeagueSeriesId = null;
    private bool createNewSeries = false;
    private string newSeriesName = "";
    private string newSeriesDescription = "";

    public BracketWindow(Plugin plugin)
        : base("Tournament Brackets##DartsTrackerBrackets", ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(900, 600),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public void SelectTournament(Tournament tournament)
    {
        selectedTournament = tournament;
        selectedMatchDetails = null;
    }

    public override void Draw()
    {
        // Split into two columns
        if (ImGui.BeginTable("BracketLayout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Tournament List", ImGuiTableColumnFlags.WidthFixed, 280);
            ImGui.TableSetupColumn("Bracket View", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();

            // Left column: Tournament list
            ImGui.TableSetColumnIndex(0);
            DrawTournamentList();

            // Right column: Bracket visualization
            ImGui.TableSetColumnIndex(1);
            DrawBracketView();

            ImGui.EndTable();
        }

        // Draw popups
        if (showCreateTournamentPopup)
        {
            DrawCreateTournamentPopup();
        }
    }

    private void DrawTournamentList()
    {
        ImGui.TextUnformatted("Tournaments");

        if (ImGui.Button("+ Create Tournament"))
        {
            var setupPlayers = plugin.GetSetupPlayers();
            if (setupPlayers.Count >= 2)
            {
                showCreateTournamentPopup = true;
                newTournamentName = "";
                linkToLeagueSeries = false;
                selectedLeagueSeriesId = null;
                createNewSeries = false;
                newSeriesName = "";
                newSeriesDescription = "";
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            var setupPlayers = plugin.GetSetupPlayers();
            if (setupPlayers.Count >= 2)
            {
                ImGui.TextUnformatted($"Create tournament with {setupPlayers.Count} players");
            }
            else
            {
                ImGui.TextUnformatted("Add at least 2 players to the setup list first");
            }
            ImGui.EndTooltip();
        }

        // Clear tournament button (only visible in DEV mode)
        if (plugin.Configuration.DevMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1.0f, 0.3f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.1f, 0.1f, 1.0f));

            if (ImGui.Button("Clear All Tournaments"))
            {
                ImGui.OpenPopup("ConfirmClearTournaments");
            }

            ImGui.PopStyleColor(3);

            // Confirmation popup
            if (ImGui.BeginPopupModal("ConfirmClearTournaments", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("Are you sure you want to clear ALL tournament history?");
                ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f), "This action cannot be undone!");
                ImGui.Spacing();

                if (ImGui.Button("Yes, Clear All", new Vector2(120, 0)))
                {
                    plugin.ClearAllTournamentHistory();
                    selectedTournament = null;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
        }

        ImGui.Separator();

        if (ImGui.BeginChild("TournamentList", new Vector2(0, 0), true))
        {
            var tournaments = plugin.Configuration.Tournaments;

            if (tournaments.Count == 0)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No tournaments created yet.");
                ImGui.TextWrapped("Add players to the setup list in the main window, then create a tournament here.");
            }
            else
            {
                foreach (var tournament in tournaments.OrderByDescending(t => t.CreatedAt))
                {
                    var isSelected = selectedTournament == tournament;
                    var statusIcon = tournament.IsComplete ? "✓" : "▶";
                    var displayName = $"{statusIcon} {tournament.Name}";

                    if (ImGui.Selectable(displayName, isSelected))
                    {
                        selectedTournament = tournament;
                        plugin.SetActiveTournament(tournament);
                    }

                    if (ImGui.IsItemHovered())
                    {
                        ImGui.BeginTooltip();
                        ImGui.TextUnformatted($"Players: {tournament.ParticipantPool.Count}");
                        ImGui.TextUnformatted($"Created: {tournament.CreatedAt:MM/dd/yyyy}");
                        ImGui.TextUnformatted($"Status: {(tournament.IsComplete ? "Complete" : tournament.GetCurrentRoundName())}");
                        ImGui.EndTooltip();
                    }

                    // Context menu for tournament
                    if (ImGui.BeginPopupContextItem($"TournamentContext##{tournament.Id}"))
                    {
                        if (ImGui.MenuItem("Delete Tournament"))
                        {
                            plugin.DeleteTournament(tournament.Id);
                            if (selectedTournament == tournament)
                            {
                                selectedTournament = null;
                            }
                        }
                        ImGui.EndPopup();
                    }

                    ImGui.Indent();
                    ImGui.TextColored(new Vector4(0.6f, 0.6f, 0.6f, 1.0f),
                        tournament.IsComplete ? "Tournament Complete" : tournament.GetCurrentRoundName());
                    ImGui.Unindent();
                    ImGui.Spacing();
                }
            }

            ImGui.EndChild();
        }
    }

    private void DrawBracketView()
    {
        if (selectedTournament == null)
        {
            ImGui.TextUnformatted("Select a tournament to view bracket");
            return;
        }

        // If viewing match details, show those instead
        if (selectedMatchDetails != null)
        {
            DrawMatchDetails();
            return;
        }

        var tournament = selectedTournament;

        ImGui.TextUnformatted($"Tournament: {tournament.Name}");
        ImGui.Separator();

        // Tournament info
        ImGui.TextUnformatted($"Status: {(tournament.IsComplete ? "Complete" : tournament.GetCurrentRoundName())}");
        ImGui.TextUnformatted($"Participants: {tournament.ParticipantPool.Count}");

        if (tournament.LeagueSeriesId.HasValue)
        {
            var series = plugin.Configuration.MatchHistory.LeagueSeries.FirstOrDefault(s => s.Id == tournament.LeagueSeriesId.Value);
            if (series != null)
            {
                ImGui.TextUnformatted($"League Series: {series.Name}");
            }
        }

        ImGui.Spacing();

        // Next match quick action
        if (!tournament.IsComplete)
        {
            var nextMatch = tournament.GetNextPendingMatch();
            if (nextMatch != null)
            {
                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), $"Next Match: {nextMatch.GetDisplayText()}");

                if (plugin.CurrentGame == null || !plugin.CurrentGame.IsActive)
                {
                    if (ImGui.Button("Start This Match"))
                    {
                        plugin.StartTournamentMatch(nextMatch);
                    }
                }
            }
        }

        ImGui.Spacing();
        ImGui.Separator();

        // Bracket visualization
        if (ImGui.BeginChild("BracketView", new Vector2(0, 0), false))
        {
            DrawBracketRounds(tournament);
            ImGui.EndChild();
        }
    }

    private void DrawBracketRounds(Tournament tournament)
    {
        foreach (var round in tournament.Rounds)
        {
            if (ImGui.CollapsingHeader($"{round.RoundName} ({round.Matches.Count} matches)", ImGuiTreeNodeFlags.DefaultOpen))
            {
                ImGui.Indent();

                if (ImGui.BeginTable($"Round{round.RoundNumber}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                {
                    ImGui.TableSetupColumn("Match", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableSetupColumn("Player 1", ImGuiTableColumnFlags.WidthFixed, 150);
                    ImGui.TableSetupColumn("Player 2", ImGuiTableColumnFlags.WidthFixed, 150);
                    ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch);

                    ImGui.TableHeadersRow();

                    foreach (var match in round.Matches)
                    {
                        ImGui.TableNextRow();

                        ImGui.TableSetColumnIndex(0);
                        ImGui.TextUnformatted($"#{match.MatchNumber}");

                        ImGui.TableSetColumnIndex(1);
                        var p1Color = match.Winner == match.Player1 ? new Vector4(0.3f, 0.9f, 0.3f, 1.0f) : new Vector4(1, 1, 1, 1);
                        ImGui.TextColored(p1Color, match.Player1 ?? "TBD");

                        ImGui.TableSetColumnIndex(2);
                        var p2Color = match.Winner == match.Player2 ? new Vector4(0.3f, 0.9f, 0.3f, 1.0f) : new Vector4(1, 1, 1, 1);
                        ImGui.TextColored(p2Color, match.Player2 ?? "TBD");

                        ImGui.TableSetColumnIndex(3);
                        if (match.IsBye)
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Bye");
                        }
                        else if (match.IsComplete)
                        {
                            ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.3f, 1.0f), $"✓ {match.Winner}");
                        }
                        else if (match.Player1 != null && match.Player2 != null)
                        {
                            ImGui.TextUnformatted("Pending");
                        }
                        else
                        {
                            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Waiting...");
                        }

                        ImGui.TableSetColumnIndex(4);
                        DrawMatchActions(match);
                    }

                    ImGui.EndTable();
                }

                ImGui.Unindent();
                ImGui.Spacing();
            }
        }
    }

    private void DrawMatchActions(BracketMatch match)
    {
        var canStart = match.Player1 != null && match.Player2 != null && !match.IsComplete && !match.IsBye;
        var canReset = match.IsComplete && !match.IsBye;
        var canSetWinner = (match.Player1 != null || match.Player2 != null) && !match.IsComplete && !match.IsBye;

        // Start match button
        if (canStart && (plugin.CurrentGame == null || !plugin.CurrentGame.IsActive))
        {
            if (ImGui.SmallButton($"Start##{match.Id}"))
            {
                plugin.StartTournamentMatch(match);
            }
            ImGui.SameLine();
        }

        // Manual winner setting
        if (canSetWinner)
        {
            if (ImGui.SmallButton($"Set Winner##{match.Id}"))
            {
                ImGui.OpenPopup($"SetWinner##{match.Id}");
            }

            if (ImGui.BeginPopup($"SetWinner##{match.Id}"))
            {
                ImGui.TextUnformatted("Select Winner:");
                if (match.Player1 != null && ImGui.MenuItem(match.Player1))
                {
                    plugin.CompleteTournamentMatch(match.Id, match.Player1);
                }
                if (match.Player2 != null && ImGui.MenuItem(match.Player2))
                {
                    plugin.CompleteTournamentMatch(match.Id, match.Player2);
                }
                ImGui.EndPopup();
            }
            ImGui.SameLine();
        }

        // Reset match button
        if (canReset)
        {
            if (ImGui.SmallButton($"Reset##{match.Id}"))
            {
                plugin.ResetTournamentMatch(match.Id);
            }
        }

        // View details button (if match has saved results)
        var matchResult = match.MatchResultId.HasValue
            ? plugin.Configuration.MatchHistory.Matches.FirstOrDefault(m => m.Id == match.MatchResultId.Value)
            : null;

        if (matchResult != null)
        {
            // Match result exists - show working button
            if (canReset || canSetWinner || canStart)
            {
                ImGui.SameLine();
            }

            if (ImGui.SmallButton($"View Details##{match.Id}"))
            {
                selectedMatchDetails = matchResult;
            }
        }
        else if (match.MatchResultId.HasValue)
        {
            // Match result was deleted - show disabled button with explanation
            if (canReset || canSetWinner || canStart)
            {
                ImGui.SameLine();
            }

            ImGui.BeginDisabled();
            ImGui.SmallButton($"Details Missing##{match.Id}");
            ImGui.EndDisabled();

            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.BeginTooltip();
                ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f), "Match details not found");
                ImGui.TextUnformatted("The detailed results for this match were deleted from history.");
                ImGui.TextUnformatted("This likely happened when unassigned matches were cleared.");
                ImGui.EndTooltip();
            }
        }
    }

    private void DrawMatchDetails()
    {
        if (selectedMatchDetails == null) return;

        var match = selectedMatchDetails;

        // Back button
        if (ImGui.Button("← Back to Bracket"))
        {
            selectedMatchDetails = null;
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Match Details");
        ImGui.Separator();

        // Match info
        ImGui.TextUnformatted($"Date: {match.CompletedAt:yyyy-MM-dd HH:mm:ss}");
        ImGui.TextUnformatted($"Duration: {match.Duration:hh\\:mm\\:ss}");
        ImGui.TextUnformatted($"Rounds: {match.TotalRounds}");
        ImGui.TextUnformatted($"Matchup: {match.GetMatchupDisplay()}");

        ImGui.Spacing();
        ImGui.TextUnformatted("Final Results:");
        ImGui.Separator();

        // Results table
        if (ImGui.BeginTable("BracketMatchResults", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Position", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Total Score", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Rounds Completed", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableHeadersRow();

            foreach (var player in match.Players)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                var positionText = player.Position == 1 ? "🥇 1st" :
                                  player.Position == 2 ? "🥈 2nd" :
                                  player.Position == 3 ? "🥉 3rd" :
                                  $"{player.Position}th";
                ImGui.TextUnformatted(positionText);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(player.PlayerName);

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(player.TotalScore.ToString());

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted($"{player.Rounds.Count}/{match.TotalRounds}");
            }

            ImGui.EndTable();
        }

        // Detailed round breakdown
        ImGui.Spacing();

        // Announce scores button
        if (ImGui.Button("Announce These Scores"))
        {
            plugin.AnnounceHistoricalScores(match);
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted("Announce these final scores to party chat");
            ImGui.EndTooltip();
        }

        ImGui.Spacing();
        if (ImGui.CollapsingHeader("Round-by-Round Breakdown"))
        {
            DrawRoundBreakdown(match);
        }
    }

    private void DrawRoundBreakdown(MatchResult match)
    {
        if (ImGui.BeginTabBar("BracketPlayerTabs"))
        {
            foreach (var player in match.Players)
            {
                if (ImGui.BeginTabItem($"{player.PlayerName}##bracketbreakdown"))
                {
                    if (ImGui.BeginTable($"BracketPlayerRounds{player.PlayerName}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                    {
                        ImGui.TableSetupColumn("Round", ImGuiTableColumnFlags.WidthFixed, 50);
                        ImGui.TableSetupColumn("Throw 1", ImGuiTableColumnFlags.WidthFixed, 120);
                        ImGui.TableSetupColumn("Throw 2", ImGuiTableColumnFlags.WidthFixed, 120);
                        ImGui.TableSetupColumn("Throw 3", ImGuiTableColumnFlags.WidthFixed, 120);
                        ImGui.TableSetupColumn("Round Total", ImGuiTableColumnFlags.WidthFixed, 80);

                        ImGui.TableHeadersRow();

                        foreach (var round in player.Rounds.OrderBy(r => r.RoundNumber))
                        {
                            ImGui.TableNextRow();

                            ImGui.TableSetColumnIndex(0);
                            ImGui.TextUnformatted($"R{round.RoundNumber}");

                            for (int throwIndex = 0; throwIndex < 3; throwIndex++)
                            {
                                ImGui.TableSetColumnIndex(throwIndex + 1);
                                if (throwIndex < round.Throws.Count)
                                {
                                    var throwResult = round.Throws[throwIndex];
                                    ImGui.TextUnformatted($"{throwResult.Score}");

                                    if (ImGui.IsItemHovered())
                                    {
                                        ImGui.BeginTooltip();
                                        ImGui.TextUnformatted($"Rolls: {throwResult.Roll1}, {throwResult.Roll2}, {throwResult.Roll3}");
                                        ImGui.TextUnformatted($"Result: {throwResult.Description}");
                                        ImGui.EndTooltip();
                                    }
                                }
                                else
                                {
                                    ImGui.TextUnformatted("-");
                                }
                            }

                            ImGui.TableSetColumnIndex(4);
                            ImGui.TextUnformatted(round.RoundScore.ToString());
                        }

                        ImGui.EndTable();
                    }

                    ImGui.EndTabItem();
                }
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawCreateTournamentPopup()
    {
        if (ImGui.BeginPopupModal("Create Tournament##NewTournament", ref showCreateTournamentPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            var setupPlayers = plugin.GetSetupPlayers();

            ImGui.TextUnformatted($"Create a tournament bracket with {setupPlayers.Count} players");
            ImGui.Spacing();

            ImGui.TextUnformatted("Tournament Name:");
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("##TournamentName", ref newTournamentName, 100);

            ImGui.Spacing();

            // League series linkage
            ImGui.Checkbox("Link to League Series", ref linkToLeagueSeries);

            if (linkToLeagueSeries)
            {
                ImGui.Indent();
                var allSeries = plugin.Configuration.MatchHistory.LeagueSeries;
                var selectedSeriesName = createNewSeries ? "Create New Series" : "Select Series...";

                if (!createNewSeries && selectedLeagueSeriesId.HasValue)
                {
                    var series = allSeries.FirstOrDefault(s => s.Id == selectedLeagueSeriesId.Value);
                    if (series != null)
                    {
                        selectedSeriesName = series.Name;
                    }
                }

                ImGui.SetNextItemWidth(280);
                if (ImGui.BeginCombo("##LeagueSeriesSelect", selectedSeriesName))
                {
                    foreach (var series in allSeries)
                    {
                        var isSelected = !createNewSeries && selectedLeagueSeriesId.HasValue && selectedLeagueSeriesId.Value == series.Id;
                        if (ImGui.Selectable(series.Name, isSelected))
                        {
                            selectedLeagueSeriesId = series.Id;
                            createNewSeries = false;
                        }
                    }

                    ImGui.Separator();

                    // Create new series option
                    if (ImGui.Selectable("+ Create New Series", createNewSeries))
                    {
                        createNewSeries = true;
                        selectedLeagueSeriesId = null;
                        newSeriesName = "";
                        newSeriesDescription = "";
                    }

                    ImGui.EndCombo();
                }

                // Show series creation fields if creating new
                if (createNewSeries)
                {
                    ImGui.Spacing();
                    ImGui.TextUnformatted("New Series Name:");
                    ImGui.SetNextItemWidth(280);
                    ImGui.InputText("##NewSeriesName", ref newSeriesName, 100);

                    ImGui.TextUnformatted("Description (optional):");
                    ImGui.SetNextItemWidth(280);
                    ImGui.InputTextMultiline("##NewSeriesDescription", ref newSeriesDescription, 500, new Vector2(280, 60));
                }

                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();

            var canCreate = !string.IsNullOrWhiteSpace(newTournamentName) && setupPlayers.Count >= 2;

            // Additional validation if creating new series
            if (linkToLeagueSeries && createNewSeries)
            {
                canCreate = canCreate && !string.IsNullOrWhiteSpace(newSeriesName);
            }

            if (!canCreate) ImGui.BeginDisabled();

            if (ImGui.Button("Create Tournament"))
            {
                Guid? seriesId = null;

                if (linkToLeagueSeries)
                {
                    if (createNewSeries)
                    {
                        // Create the new series first
                        var newSeries = plugin.CreateLeagueSeries(newSeriesName.Trim(), newSeriesDescription.Trim());
                        seriesId = newSeries.Id;
                    }
                    else
                    {
                        seriesId = selectedLeagueSeriesId;
                    }
                }

                var tournament = plugin.CreateTournament(newTournamentName.Trim(), setupPlayers.ToList(), seriesId);
                selectedTournament = tournament;
                plugin.SetActiveTournament(tournament);
                showCreateTournamentPopup = false;
            }

            if (!canCreate) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                showCreateTournamentPopup = false;
            }

            ImGui.EndPopup();
        }

        if (showCreateTournamentPopup && !ImGui.IsPopupOpen("Create Tournament##NewTournament"))
        {
            ImGui.OpenPopup("Create Tournament##NewTournament");
        }
    }
}
