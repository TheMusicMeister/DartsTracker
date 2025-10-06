using System;
using System.Numerics;
using System.Linq;
using System.Collections.Generic;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace DartsTracker.Windows;

public class LeagueStanding
{
    public string PlayerName { get; set; } = "";
    public int LeaguePoints { get; set; }
    public int MatchesWon { get; set; }
    public int TotalMatches { get; set; }
}

public class HistoryWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private MatchResult? selectedMatch;
    private LeagueSeries? selectedSeries;
    private Tournament? selectedTournament;
    private MatchResult? selectedTournamentMatch; // Drill-down match within tournament
    private bool showNewSeriesPopup = false;
    private bool showRenameSeriesPopup = false;
    private string newSeriesName = "";
    private string newSeriesDescription = "";
    private Guid? editingSeriesId = null;
    private string editingSeriesName = "";

    public HistoryWindow(Plugin plugin)
        : base("Match History##DartsTrackerHistory", ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 500),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        this.plugin = plugin;
    }

    public void Dispose() { }

    public override void Draw()
    {
        var allMatches = plugin.Configuration.MatchHistory.GetAllMatches();

        if (allMatches.Count == 0)
        {
            ImGui.TextUnformatted("No match history available.");
            ImGui.TextUnformatted("Complete some games to see them here!");
            return;
        }

        // Split the window into two columns
        if (ImGui.BeginTable("HistoryLayout", 2, ImGuiTableFlags.Resizable | ImGuiTableFlags.BordersInnerV))
        {
            ImGui.TableSetupColumn("Match List", ImGuiTableColumnFlags.WidthFixed, 350);
            ImGui.TableSetupColumn("Match Details", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableNextRow();

            // Left column: Match list
            ImGui.TableSetColumnIndex(0);
            DrawMatchListGrouped();

            // Right column: Match details, series summary, or tournament details
            ImGui.TableSetColumnIndex(1);
            if (selectedSeries != null)
            {
                DrawSeriesSummary();
            }
            else if (selectedTournament != null)
            {
                if (selectedTournamentMatch != null)
                {
                    DrawTournamentMatchDetails();
                }
                else
                {
                    DrawTournamentDetails();
                }
            }
            else
            {
                DrawMatchDetails();
            }

            ImGui.EndTable();
        }

        // Draw popups
        if (showNewSeriesPopup)
        {
            DrawNewSeriesPopup();
        }

        if (showRenameSeriesPopup)
        {
            DrawRenameSeriesPopup();
        }
    }
    
    private void DrawMatchListGrouped()
    {
        ImGui.TextUnformatted("Match History:");

        if (ImGui.Button("+ New Series"))
        {
            showNewSeriesPopup = true;
            newSeriesName = "";
            newSeriesDescription = "";
        }

        // Clear button (only visible in DEV mode)
        if (plugin.Configuration.DevMode)
        {
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.8f, 0.2f, 0.2f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(1.0f, 0.3f, 0.3f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.6f, 0.1f, 0.1f, 1.0f));

            if (ImGui.Button("Clear All Match History"))
            {
                ImGui.OpenPopup("ConfirmClearMatches");
            }

            ImGui.PopStyleColor(3);

            // Confirmation popup
            if (ImGui.BeginPopupModal("ConfirmClearMatches", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextUnformatted("Are you sure you want to clear ALL match history?");
                ImGui.TextColored(new Vector4(1.0f, 0.5f, 0.5f, 1.0f), "This action cannot be undone!");
                ImGui.Spacing();

                if (ImGui.Button("Yes, Clear All", new Vector2(120, 0)))
                {
                    plugin.ClearAllMatchHistory();
                    selectedMatch = null;
                    selectedSeries = null;
                    selectedTournament = null;
                    selectedTournamentMatch = null;
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

        if (ImGui.BeginChild("MatchList", new Vector2(0, 0), true))
        {
            var allSeries = plugin.Configuration.MatchHistory.LeagueSeries;

            // Draw each league series
            foreach (var series in allSeries.OrderByDescending(s => s.CreatedAt))
            {
                var seriesMatches = plugin.Configuration.MatchHistory.GetMatchesForSeries(series.Id);

                var headerText = $"{series.Name} ({seriesMatches.Count} matches)";
                if (ImGui.CollapsingHeader(headerText, ImGuiTreeNodeFlags.DefaultOpen))
                {
                    // Context menu for series header
                    if (ImGui.BeginPopupContextItem($"SeriesContext##{series.Id}"))
                    {
                        if (ImGui.MenuItem("View Summary"))
                        {
                            selectedSeries = series;
                            selectedMatch = null;
                        }

                        if (ImGui.MenuItem("Rename"))
                        {
                            editingSeriesId = series.Id;
                            editingSeriesName = series.Name;
                            showRenameSeriesPopup = true;
                        }

                        ImGui.Separator();

                        if (ImGui.MenuItem("Delete Series"))
                        {
                            plugin.DeleteLeagueSeries(series.Id);
                        }

                        ImGui.EndPopup();
                    }

                    ImGui.Indent();

                    if (!string.IsNullOrWhiteSpace(series.Description))
                    {
                        ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), series.Description);
                        ImGui.Spacing();
                    }

                    ImGui.Spacing();

                    // Draw matches in this series
                    foreach (var match in seriesMatches)
                    {
                        DrawMatchItem(match, series.Id);
                    }

                    ImGui.Unindent();
                    ImGui.Spacing();
                }
            }

            // Draw unassigned matches
            var unassignedMatches = plugin.Configuration.MatchHistory.GetUnassignedMatches();
            if (unassignedMatches.Count > 0)
            {
                var unassignedHeader = $"Unassigned Matches ({unassignedMatches.Count})";
                if (ImGui.CollapsingHeader(unassignedHeader, ImGuiTreeNodeFlags.DefaultOpen))
                {
                    // Context menu for unassigned matches header
                    if (ImGui.BeginPopupContextItem("UnassignedMatchesContext"))
                    {
                        ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f), $"Delete all {unassignedMatches.Count} unassigned matches?");
                        ImGui.Separator();

                        if (ImGui.MenuItem("Delete All Unassigned Matches"))
                        {
                            plugin.DeleteAllUnassignedMatches();
                            selectedMatch = null;
                            selectedTournament = null;
                            selectedTournamentMatch = null;
                        }

                        ImGui.EndPopup();
                    }

                    ImGui.Indent();

                    foreach (var match in unassignedMatches)
                    {
                        DrawMatchItem(match, null);
                    }

                    ImGui.Unindent();
                }
            }

            ImGui.EndChild();
        }
    }

    private void DrawMatchItem(MatchResult match, Guid? currentSeriesId)
    {
        // Skip individual tournament matches - they're shown under tournament entries
        if (match.TournamentId.HasValue && !match.IsTournamentChampionship)
        {
            return;
        }

        var isSelected = selectedMatch == match || selectedTournament != null;

        // Display text
        var displayText = match.CompletedAt.ToString("MM/dd/yyyy HH:mm");
        if (match.IsTournamentChampionship)
        {
            var tournament = plugin.Configuration.Tournaments.FirstOrDefault(t => t.Id == match.TournamentId);
            if (tournament != null)
            {
                displayText = $"{tournament.Name} Tournament";
                isSelected = selectedTournament == tournament;
            }
        }

        if (ImGui.Selectable($"{displayText}##{match.Id}", isSelected))
        {
            if (match.IsTournamentChampionship && match.TournamentId.HasValue)
            {
                var tournament = plugin.Configuration.Tournaments.FirstOrDefault(t => t.Id == match.TournamentId);
                selectedTournament = tournament;
                selectedMatch = null;
                selectedSeries = null;
                selectedTournamentMatch = null;
            }
            else
            {
                selectedMatch = match;
                selectedSeries = null;
                selectedTournament = null;
                selectedTournamentMatch = null;
            }
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            if (match.IsTournamentChampionship)
            {
                var tournament = match.TournamentId.HasValue
                    ? plugin.Configuration.Tournaments.FirstOrDefault(t => t.Id == match.TournamentId)
                    : null;

                if (tournament != null)
                {
                    ImGui.TextUnformatted("Tournament Bracket");
                    ImGui.TextUnformatted($"Champion: {match.Winner}");
                    ImGui.TextUnformatted("Click to view bracket details");
                }
                else
                {
                    ImGui.TextUnformatted("Orphaned Tournament Match");
                    ImGui.TextColored(new Vector4(1.0f, 0.7f, 0.3f, 1.0f), "Tournament was deleted");
                    ImGui.TextUnformatted($"Champion: {match.Winner}");
                    ImGui.TextUnformatted("Right-click to delete");
                }
            }
            else
            {
                ImGui.TextUnformatted($"Mode: {(match.Mode == GameMode.Bracket ? "Bracket" : "Free-For-All")}");
                ImGui.TextUnformatted($"Winner: {match.Winner}");
                ImGui.TextUnformatted($"Players: {match.GetMatchupDisplay()}");
                ImGui.TextUnformatted($"Duration: {match.Duration:mm\\:ss}");
                ImGui.TextUnformatted("Right-click for options");
            }
            ImGui.EndTooltip();
        }

        // Check if this championship match is orphaned (tournament was deleted)
        var isOrphanedChampionship = false;
        Tournament? tournamentForBracket = null;
        if (match.IsTournamentChampionship && match.TournamentId.HasValue)
        {
            tournamentForBracket = plugin.Configuration.Tournaments.FirstOrDefault(t => t.Id == match.TournamentId);
            isOrphanedChampionship = (tournamentForBracket == null);
        }

        // Allow context menu for all matches (including all tournament championships)
        if (ImGui.BeginPopupContextItem($"MatchContext##{match.Id}"))
        {
            // For valid tournament championships, show option to open in brackets window
            if (match.IsTournamentChampionship && !isOrphanedChampionship && tournamentForBracket != null)
            {
                if (ImGui.MenuItem("Open in Brackets Window"))
                {
                    plugin.OpenBracketWindowWithTournament(tournamentForBracket);
                }
                ImGui.Separator();
            }

            ImGui.TextUnformatted("Move to Series:");
            ImGui.Separator();

            // Option to unassign
            if (currentSeriesId.HasValue)
            {
                if (ImGui.MenuItem("None (Unassign)"))
                {
                    plugin.UnassignMatch(match);
                }
            }

            // Options for each series
            foreach (var series in plugin.Configuration.MatchHistory.LeagueSeries)
            {
                if (currentSeriesId.HasValue && currentSeriesId.Value == series.Id)
                    continue; // Skip current series

                if (ImGui.MenuItem(series.Name))
                {
                    plugin.AssignMatchToSeries(match, series.Id);
                }
            }

            ImGui.Separator();

            // Delete option
            if (ImGui.MenuItem("Delete Match"))
            {
                plugin.DeleteMatch(match.Id);
                if (selectedMatch == match)
                {
                    selectedMatch = null;
                }
            }

            ImGui.EndPopup();
        }

        if (!match.IsTournamentChampionship)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"- {match.Winner} won");

            // Second line with additional info
            ImGui.Indent();

            var modeIndicator = match.Mode == GameMode.Bracket ? "[1v1]" : "[FFA]";
            ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1.0f), modeIndicator);
            ImGui.SameLine();
            ImGui.TextUnformatted($"{match.GetMatchupDisplay()}, {match.TotalRounds} rounds");

            ImGui.Unindent();
        }
        else
        {
            // Tournament championship entry
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), "🏆");
            ImGui.SameLine();
            ImGui.TextUnformatted($"Champion: {match.Winner}");

            ImGui.Indent();
            var tournament = plugin.Configuration.Tournaments.FirstOrDefault(t => t.Id == match.TournamentId);
            if (tournament != null)
            {
                ImGui.TextColored(new Vector4(0.7f, 0.9f, 0.7f, 1.0f), "[Tournament]");
                ImGui.SameLine();
                ImGui.TextUnformatted($"{tournament.ParticipantPool.Count} participants");
            }
            ImGui.Unindent();
        }

        ImGui.Spacing();
    }
    
    private void DrawMatchDetails()
    {
        if (selectedMatch == null)
        {
            ImGui.TextUnformatted("Select a match from the list to view details.");
            return;
        }

        var match = selectedMatch;

        ImGui.TextUnformatted("Match Details");
        ImGui.Separator();

        // Match info
        ImGui.TextUnformatted($"Date: {match.CompletedAt:yyyy-MM-dd HH:mm:ss}");
        ImGui.TextUnformatted($"Duration: {match.Duration:hh\\:mm\\:ss}");
        ImGui.TextUnformatted($"Rounds: {match.TotalRounds}");
        ImGui.TextUnformatted($"Game Mode: {(match.Mode == GameMode.Bracket ? "Bracket (1v1)" : "Free-For-All")}");
        if (match.Mode == GameMode.Bracket)
        {
            ImGui.TextUnformatted($"Matchup: {match.GetMatchupDisplay()}");
        }

        // Show league series if assigned
        if (match.LeagueSeriesId.HasValue)
        {
            var series = plugin.Configuration.MatchHistory.LeagueSeries.FirstOrDefault(s => s.Id == match.LeagueSeriesId.Value);
            if (series != null)
            {
                ImGui.TextUnformatted($"League Series: {series.Name}");
            }
        }
        else
        {
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "League Series: Unassigned");
        }

        ImGui.Spacing();
        ImGui.TextUnformatted("Final Results:");
        ImGui.Separator();
        
        // Results table
        if (ImGui.BeginTable("Results", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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
        if (ImGui.BeginTabBar("PlayerTabs"))
        {
            foreach (var player in match.Players)
            {
                if (ImGui.BeginTabItem($"{player.PlayerName}##breakdown"))
                {
                    if (ImGui.BeginTable($"PlayerRounds{player.PlayerName}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
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

    private void DrawNewSeriesPopup()
    {
        if (ImGui.BeginPopupModal("Create New League Series##NewSeriesHistory", ref showNewSeriesPopup, ImGuiWindowFlags.AlwaysAutoResize))
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
                plugin.CreateLeagueSeries(newSeriesName.Trim(), newSeriesDescription.Trim());
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
        if (showNewSeriesPopup && !ImGui.IsPopupOpen("Create New League Series##NewSeriesHistory"))
        {
            ImGui.OpenPopup("Create New League Series##NewSeriesHistory");
        }
    }

    private void DrawRenameSeriesPopup()
    {
        if (ImGui.BeginPopupModal("Rename League Series##RenameSeriesHistory", ref showRenameSeriesPopup, ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted("Rename this league series:");
            ImGui.Spacing();

            ImGui.TextUnformatted("New Name:");
            ImGui.SetNextItemWidth(300);
            ImGui.InputText("##NewSeriesName", ref editingSeriesName, 100);

            ImGui.Spacing();
            ImGui.Separator();

            var canRename = !string.IsNullOrWhiteSpace(editingSeriesName) && editingSeriesId.HasValue;
            if (!canRename) ImGui.BeginDisabled();

            if (ImGui.Button("Rename"))
            {
                if (editingSeriesId.HasValue)
                {
                    plugin.RenameLeagueSeries(editingSeriesId.Value, editingSeriesName.Trim());
                }
                showRenameSeriesPopup = false;
            }

            if (!canRename) ImGui.EndDisabled();

            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
            {
                showRenameSeriesPopup = false;
            }

            ImGui.EndPopup();
        }

        // Open popup if flag is set but popup isn't open yet
        if (showRenameSeriesPopup && !ImGui.IsPopupOpen("Rename League Series##RenameSeriesHistory"))
        {
            ImGui.OpenPopup("Rename League Series##RenameSeriesHistory");
        }
    }

    private void DrawSeriesSummary()
    {
        if (selectedSeries == null)
        {
            ImGui.TextUnformatted("No series selected.");
            return;
        }

        var series = selectedSeries;
        var matches = plugin.Configuration.MatchHistory.GetMatchesForSeries(series.Id);

        ImGui.TextUnformatted($"League Series: {series.Name}");
        ImGui.Separator();

        // Series info
        ImGui.TextUnformatted($"Created: {series.CreatedAt:yyyy-MM-dd}");
        ImGui.TextUnformatted($"Total Matches: {matches.Count}");

        if (!string.IsNullOrWhiteSpace(series.Description))
        {
            ImGui.TextUnformatted($"Description: {series.Description}");
        }

        if (matches.Count == 0)
        {
            ImGui.Spacing();
            ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "No matches in this series yet.");
            return;
        }

        // Calculate league standings
        var standings = CalculateLeagueStandings(matches);

        ImGui.Spacing();
        ImGui.TextUnformatted("League Standings:");

        // Announce button
        if (ImGui.Button("Announce League Standings"))
        {
            plugin.AnnounceLeagueStandings(series, standings);
        }

        ImGui.Separator();

        // Standings table
        if (ImGui.BeginTable("LeagueStandings", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Position", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("League Points", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Wins / Matches", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableHeadersRow();

            for (int i = 0; i < standings.Count; i++)
            {
                var standing = standings[i];
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                var positionText = i switch
                {
                    0 => "🥇 1st",
                    1 => "🥈 2nd",
                    2 => "🥉 3rd",
                    _ => $"{i + 1}th"
                };
                ImGui.TextUnformatted(positionText);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(standing.PlayerName);

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(standing.LeaguePoints.ToString());

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted($"{standing.MatchesWon} / {standing.TotalMatches}");
            }

            ImGui.EndTable();
        }

        // Match history within series
        ImGui.Spacing();
        ImGui.TextUnformatted("Match History:");
        ImGui.Separator();

        if (ImGui.BeginTable("SeriesMatches", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Date", ImGuiTableColumnFlags.WidthFixed, 140);
            ImGui.TableSetupColumn("Mode", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Winner", ImGuiTableColumnFlags.WidthFixed, 130);
            ImGui.TableSetupColumn("Matchup", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableHeadersRow();

            foreach (var match in matches.OrderByDescending(m => m.CompletedAt))
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                if (ImGui.Selectable($"{match.CompletedAt:MM/dd/yyyy HH:mm}##{match.CompletedAt.Ticks}", selectedMatch == match))
                {
                    selectedMatch = match;
                    selectedSeries = null; // Switch to match view
                }

                ImGui.TableSetColumnIndex(1);
                var modeText = match.Mode == GameMode.Bracket ? "1v1" : "FFA";
                ImGui.TextUnformatted(modeText);

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(match.Winner);

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted(match.GetMatchupDisplay());
            }

            ImGui.EndTable();
        }
    }

    private List<LeagueStanding> CalculateLeagueStandings(List<MatchResult> matches)
    {
        var standings = new Dictionary<string, LeagueStanding>();

        foreach (var match in matches)
        {
            // Award 1 league point to the winner (1st place)
            var winner = match.Players.FirstOrDefault(p => p.Position == 1);
            if (winner != null)
            {
                if (!standings.ContainsKey(winner.PlayerName))
                {
                    standings[winner.PlayerName] = new LeagueStanding
                    {
                        PlayerName = winner.PlayerName
                    };
                }
                standings[winner.PlayerName].LeaguePoints += 1;
                standings[winner.PlayerName].MatchesWon += 1;
            }

            // Track total matches for all players
            foreach (var player in match.Players)
            {
                if (!standings.ContainsKey(player.PlayerName))
                {
                    standings[player.PlayerName] = new LeagueStanding
                    {
                        PlayerName = player.PlayerName
                    };
                }
                standings[player.PlayerName].TotalMatches += 1;
            }
        }

        // Sort by league points descending, then by matches won
        return standings.Values
            .OrderByDescending(s => s.LeaguePoints)
            .ThenByDescending(s => s.MatchesWon)
            .ToList();
    }

    private void DrawTournamentDetails()
    {
        if (selectedTournament == null)
        {
            ImGui.TextUnformatted("No tournament selected.");
            return;
        }

        var tournament = selectedTournament;

        ImGui.TextUnformatted($"Tournament: {tournament.Name}");
        if (ImGui.Button("← Back to List"))
        {
            selectedTournament = null;
            selectedTournamentMatch = null;
        }

        ImGui.Separator();

        // Tournament info
        ImGui.TextUnformatted($"Created: {tournament.CreatedAt:yyyy-MM-dd}");
        ImGui.TextUnformatted($"Participants: {tournament.ParticipantPool.Count}");
        ImGui.TextUnformatted($"Status: {(tournament.IsComplete ? "Complete" : tournament.GetCurrentRoundName())}");

        if (tournament.IsComplete)
        {
            var champion = tournament.Rounds.Last().Matches.First().Winner;
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.2f, 1.0f), $"🏆 Champion: {champion}");
        }

        if (tournament.LeagueSeriesId.HasValue)
        {
            var series = plugin.Configuration.MatchHistory.LeagueSeries.FirstOrDefault(s => s.Id == tournament.LeagueSeriesId);
            if (series != null)
            {
                ImGui.TextUnformatted($"League Series: {series.Name}");
            }
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Bracket Results:");
        ImGui.Separator();

        // Display bracket rounds
        if (ImGui.BeginChild("TournamentBracket", new Vector2(0, 0), false))
        {
            foreach (var round in tournament.Rounds)
            {
                if (ImGui.CollapsingHeader($"{round.RoundName} ({round.Matches.Count} matches)", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    ImGui.Indent();

                    if (ImGui.BeginTable($"RoundMatches{round.RoundNumber}", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                    {
                        ImGui.TableSetupColumn("Match", ImGuiTableColumnFlags.WidthFixed, 60);
                        ImGui.TableSetupColumn("Player 1", ImGuiTableColumnFlags.WidthFixed, 150);
                        ImGui.TableSetupColumn("Player 2", ImGuiTableColumnFlags.WidthFixed, 150);
                        ImGui.TableSetupColumn("Result", ImGuiTableColumnFlags.WidthStretch);

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
                                // Check if there's detailed match data
                                if (match.MatchResultId.HasValue)
                                {
                                    var matchResult = plugin.Configuration.MatchHistory.Matches.FirstOrDefault(m => m.Id == match.MatchResultId.Value);
                                    if (matchResult != null)
                                    {
                                        if (ImGui.SmallButton($"View Details##{match.Id}"))
                                        {
                                            selectedTournamentMatch = matchResult;
                                        }
                                        ImGui.SameLine();
                                    }
                                }
                                ImGui.TextColored(new Vector4(0.3f, 0.9f, 0.3f, 1.0f), $"Winner: {match.Winner}");
                            }
                            else
                            {
                                ImGui.TextColored(new Vector4(0.7f, 0.7f, 0.7f, 1.0f), "Not played");
                            }
                        }

                        ImGui.EndTable();
                    }

                    ImGui.Unindent();
                    ImGui.Spacing();
                }
            }

            ImGui.EndChild();
        }
    }

    private void DrawTournamentMatchDetails()
    {
        if (selectedTournamentMatch == null || selectedTournament == null)
        {
            ImGui.TextUnformatted("No match selected.");
            return;
        }

        var match = selectedTournamentMatch;

        ImGui.TextUnformatted($"Tournament: {selectedTournament.Name}");
        if (ImGui.Button("← Back to Bracket"))
        {
            selectedTournamentMatch = null;
        }

        ImGui.Separator();

        // Find which round this match was in
        BracketMatch? bracketMatch = null;
        BracketRound? bracketRound = null;
        foreach (var round in selectedTournament.Rounds)
        {
            bracketMatch = round.Matches.FirstOrDefault(m => m.MatchResultId == match.Id);
            if (bracketMatch != null)
            {
                bracketRound = round;
                break;
            }
        }

        if (bracketRound != null && bracketMatch != null)
        {
            ImGui.TextUnformatted($"Round: {bracketRound.RoundName} - Match #{bracketMatch.MatchNumber}");
        }

        ImGui.TextUnformatted($"Date: {match.CompletedAt:yyyy-MM-dd HH:mm:ss}");
        ImGui.TextUnformatted($"Duration: {match.Duration:hh\\:mm\\:ss}");
        ImGui.TextUnformatted($"Winner: {match.Winner}");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.TextUnformatted("Match Results:");
        ImGui.Separator();

        // Display player results in a table
        if (ImGui.BeginTable("TournamentMatchResults", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
        {
            ImGui.TableSetupColumn("Position", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Total Score", ImGuiTableColumnFlags.WidthFixed, 100);
            ImGui.TableSetupColumn("Rounds", ImGuiTableColumnFlags.WidthStretch);

            ImGui.TableHeadersRow();

            foreach (var player in match.Players)
            {
                ImGui.TableNextRow();

                ImGui.TableSetColumnIndex(0);
                var positionText = player.Position switch
                {
                    1 => "🥇 1st",
                    2 => "🥈 2nd",
                    _ => $"{player.Position}th"
                };
                ImGui.TextUnformatted(positionText);

                ImGui.TableSetColumnIndex(1);
                ImGui.TextUnformatted(player.PlayerName);

                ImGui.TableSetColumnIndex(2);
                ImGui.TextUnformatted(player.TotalScore.ToString());

                ImGui.TableSetColumnIndex(3);
                ImGui.TextUnformatted($"{player.Rounds.Count} rounds completed");
            }

            ImGui.EndTable();
        }

        // Display round-by-round details
        if (match.Players.Count > 0 && match.Players[0].Rounds.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Round-by-Round Scores:");
            ImGui.Separator();

            if (ImGui.BeginTabBar("RoundTabs"))
            {
                foreach (var player in match.Players)
                {
                    if (ImGui.BeginTabItem(player.PlayerName))
                    {
                        if (ImGui.BeginTable($"PlayerRounds{player.PlayerName}", 5, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
                        {
                            ImGui.TableSetupColumn("Round", ImGuiTableColumnFlags.WidthFixed, 60);
                            ImGui.TableSetupColumn("Throw 1", ImGuiTableColumnFlags.WidthFixed, 100);
                            ImGui.TableSetupColumn("Throw 2", ImGuiTableColumnFlags.WidthFixed, 100);
                            ImGui.TableSetupColumn("Throw 3", ImGuiTableColumnFlags.WidthFixed, 100);
                            ImGui.TableSetupColumn("Round Total", ImGuiTableColumnFlags.WidthStretch);

                            ImGui.TableHeadersRow();

                            foreach (var round in player.Rounds)
                            {
                                ImGui.TableNextRow();

                                ImGui.TableSetColumnIndex(0);
                                ImGui.TextUnformatted($"#{round.RoundNumber}");

                                for (int i = 0; i < 3 && i < round.Throws.Count; i++)
                                {
                                    ImGui.TableSetColumnIndex(i + 1);
                                    var throwResult = round.Throws[i];
                                    ImGui.TextUnformatted($"{throwResult.Score}");
                                    if (ImGui.IsItemHovered())
                                    {
                                        ImGui.BeginTooltip();
                                        ImGui.TextUnformatted($"{throwResult.Roll1}-{throwResult.Roll2}-{throwResult.Roll3}");
                                        ImGui.TextUnformatted(throwResult.Description);
                                        ImGui.EndTooltip();
                                    }
                                }

                                ImGui.TableSetColumnIndex(4);
                                ImGui.TextColored(new Vector4(0.3f, 0.8f, 1.0f, 1.0f), round.RoundScore.ToString());
                            }

                            ImGui.EndTable();
                        }

                        ImGui.EndTabItem();
                    }
                }

                ImGui.EndTabBar();
            }
        }
    }
}