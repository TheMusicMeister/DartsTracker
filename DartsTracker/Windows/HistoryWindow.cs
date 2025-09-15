using System;
using System.Numerics;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace DartsTracker.Windows;

public class HistoryWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private MatchResult? selectedMatch;

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
        var matches = plugin.Configuration.MatchHistory.GetRecentMatches();
        
        if (matches.Count == 0)
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
            DrawMatchList(matches);
            
            // Right column: Match details
            ImGui.TableSetColumnIndex(1);
            DrawMatchDetails();
            
            ImGui.EndTable();
        }
    }
    
    private void DrawMatchList(System.Collections.Generic.List<MatchResult> matches)
    {
        ImGui.TextUnformatted("Recent Matches:");
        ImGui.Separator();
        
        if (ImGui.BeginChild("MatchList", new Vector2(0, 0), true))
        {
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var isSelected = selectedMatch == match;
                
                if (ImGui.Selectable($"{match.CompletedAt:MM/dd/yyyy HH:mm}##{i}", isSelected))
                {
                    selectedMatch = match;
                }
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.TextUnformatted($"Winner: {match.Winner}");
                    ImGui.TextUnformatted($"Players: {match.PlayerCount}");
                    ImGui.TextUnformatted($"Duration: {match.Duration:mm\\:ss}");
                    ImGui.EndTooltip();
                }
                
                ImGui.SameLine();
                ImGui.TextUnformatted($"- {match.Winner} won");
                
                // Second line with additional info
                ImGui.Indent();
                ImGui.TextUnformatted($"{match.PlayerCount} players, {match.TotalRounds} rounds");
                if (match.WasManualPlayerMode)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(new Vector4(0.7f, 0.7f, 1.0f, 1.0f), "(Manual)");
                }
                ImGui.Unindent();
                
                ImGui.Spacing();
            }
            
            ImGui.EndChild();
        }
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
        ImGui.TextUnformatted($"Mode: {(match.WasManualPlayerMode ? "Manual Player Order" : "Automatic")}");
        
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
}