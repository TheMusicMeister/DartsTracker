using System;
using System.Collections.Generic;
using System.Linq;

namespace DartsTracker;

[Serializable]
public class BracketMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int MatchNumber { get; set; }
    public string? Player1 { get; set; }
    public string? Player2 { get; set; }
    public string? Winner { get; set; }
    public bool IsComplete { get; set; }
    public bool IsBye { get; set; }
    public DateTime? CompletedAt { get; set; }
    public Guid? MatchResultId { get; set; } = null; // Links to saved MatchResult for detailed scores

    public string GetDisplayText()
    {
        if (IsBye)
        {
            return $"{Player1 ?? "TBD"} (Bye)";
        }

        var p1 = Player1 ?? "TBD";
        var p2 = Player2 ?? "TBD";

        if (IsComplete && Winner != null)
        {
            return $"{Winner} def. {(Winner == Player1 ? Player2 : Player1)}";
        }

        return $"{p1} vs {p2}";
    }
}

[Serializable]
public class BracketRound
{
    public int RoundNumber { get; set; }
    public string RoundName { get; set; } = "";
    public List<BracketMatch> Matches { get; set; } = new();

    public bool IsComplete => Matches.All(m => m.IsComplete);
}

[Serializable]
public class Tournament
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public List<string> ParticipantPool { get; set; } = new();
    public List<BracketRound> Rounds { get; set; } = new();
    public bool IsComplete { get; set; }
    public Guid? LeagueSeriesId { get; set; }

    public string GetCurrentRoundName()
    {
        var currentRound = Rounds.FirstOrDefault(r => !r.IsComplete);
        return currentRound?.RoundName ?? "Tournament Complete";
    }

    public BracketMatch? GetNextPendingMatch()
    {
        foreach (var round in Rounds)
        {
            var pendingMatch = round.Matches.FirstOrDefault(m => !m.IsComplete && m.Player1 != null && m.Player2 != null);
            if (pendingMatch != null)
            {
                return pendingMatch;
            }
        }
        return null;
    }
}

public static class BracketGenerator
{
    public static Tournament GenerateTournament(string name, List<string> players, Guid? leagueSeriesId = null)
    {
        if (players.Count < 2)
        {
            throw new ArgumentException("Tournament requires at least 2 players");
        }

        var tournament = new Tournament
        {
            Name = name,
            ParticipantPool = new List<string>(players),
            LeagueSeriesId = leagueSeriesId
        };

        // Determine bracket size (next power of 2)
        var bracketSize = GetNextPowerOfTwo(players.Count);
        var byeCount = bracketSize - players.Count;

        // Calculate number of rounds
        var roundCount = (int)Math.Log2(bracketSize);

        // Generate rounds from final to first
        for (int i = roundCount; i >= 1; i--)
        {
            var round = new BracketRound
            {
                RoundNumber = i,
                RoundName = GetRoundName(i, roundCount)
            };
            tournament.Rounds.Insert(0, round);
        }

        // Fill first round with players using proper bracket seeding
        var firstRound = tournament.Rounds[0];
        var matchCount = bracketSize / 2;

        // Shuffle players to randomize seed assignment (optional - can be removed for manual seeding)
        var shuffledPlayers = players.OrderBy(x => Guid.NewGuid()).ToList();

        // Get proper bracket ordering (e.g., [1,8,4,5,2,7,3,6] for 8 players)
        var bracketOrder = GenerateBracketOrder(bracketSize);

        // Create matches using bracket order
        for (int i = 0; i < matchCount; i++)
        {
            var match = new BracketMatch
            {
                MatchNumber = i + 1
            };

            // Get seed numbers for this match from bracket order
            var seed1 = bracketOrder[i * 2] - 1;  // Convert to 0-based index
            var seed2 = bracketOrder[i * 2 + 1] - 1;

            // Assign players based on seeds (null if seed > player count)
            match.Player1 = seed1 < shuffledPlayers.Count ? shuffledPlayers[seed1] : null;
            match.Player2 = seed2 < shuffledPlayers.Count ? shuffledPlayers[seed2] : null;

            // Mark as bye if missing players
            if (match.Player1 == null && match.Player2 == null)
            {
                // Both players missing - empty bye
                match.IsBye = true;
                match.IsComplete = true;
                match.Winner = null;
            }
            else if (match.Player2 == null)
            {
                // Player 2 missing - Player 1 gets bye (top seed advantage)
                match.IsBye = true;
                match.Winner = match.Player1;
                match.IsComplete = true;
            }
            else if (match.Player1 == null)
            {
                // Player 1 missing - Player 2 gets bye
                match.IsBye = true;
                match.Winner = match.Player2;
                match.IsComplete = true;
            }

            firstRound.Matches.Add(match);
        }

        // Create placeholder matches for subsequent rounds
        for (int roundIdx = 1; roundIdx < tournament.Rounds.Count; roundIdx++)
        {
            var round = tournament.Rounds[roundIdx];
            var prevRound = tournament.Rounds[roundIdx - 1];
            var matchesInRound = prevRound.Matches.Count / 2;

            for (int i = 0; i < matchesInRound; i++)
            {
                round.Matches.Add(new BracketMatch
                {
                    MatchNumber = i + 1
                });
            }
        }

        // Auto-advance byes
        AdvanceWinners(tournament);

        return tournament;
    }

    public static void AdvanceWinners(Tournament tournament)
    {
        for (int roundIdx = 0; roundIdx < tournament.Rounds.Count - 1; roundIdx++)
        {
            var currentRound = tournament.Rounds[roundIdx];
            var nextRound = tournament.Rounds[roundIdx + 1];

            for (int matchIdx = 0; matchIdx < currentRound.Matches.Count; matchIdx += 2)
            {
                var match1 = currentRound.Matches[matchIdx];
                var match2 = matchIdx + 1 < currentRound.Matches.Count ? currentRound.Matches[matchIdx + 1] : null;

                var nextMatchIdx = matchIdx / 2;
                if (nextMatchIdx < nextRound.Matches.Count)
                {
                    var nextMatch = nextRound.Matches[nextMatchIdx];

                    // Advance winner from match 1
                    if (match1.IsComplete && match1.Winner != null)
                    {
                        nextMatch.Player1 = match1.Winner;
                    }

                    // Advance winner from match 2
                    if (match2 != null && match2.IsComplete && match2.Winner != null)
                    {
                        nextMatch.Player2 = match2.Winner;
                    }

                    // Only determine bye status when BOTH source matches are complete
                    if (match1.IsComplete && (match2 == null || match2.IsComplete))
                    {
                        // Check if next match should be marked as a bye
                        if (nextMatch.Player1 == null && nextMatch.Player2 == null)
                        {
                            // Both players are null - empty bye
                            nextMatch.IsBye = true;
                            nextMatch.IsComplete = true;
                            nextMatch.Winner = null;
                        }
                        else if (nextMatch.Player2 == null && nextMatch.Player1 != null)
                        {
                            // Only Player2 is null - Player1 gets a bye
                            nextMatch.IsBye = true;
                            nextMatch.Winner = nextMatch.Player1;
                            nextMatch.IsComplete = true;
                        }
                        else if (nextMatch.Player1 == null && nextMatch.Player2 != null)
                        {
                            // Only Player1 is null - Player2 gets a bye
                            nextMatch.IsBye = true;
                            nextMatch.Winner = nextMatch.Player2;
                            nextMatch.IsComplete = true;
                        }
                        // else: both players exist, normal match - no bye status
                    }
                    // else: source matches not all complete yet, just advance winners without setting bye status
                }
            }
        }

        // Check if tournament is complete
        var finalRound = tournament.Rounds.Last();
        tournament.IsComplete = finalRound.IsComplete;
    }

    public static void SetMatchWinner(Tournament tournament, Guid matchId, string winner)
    {
        foreach (var round in tournament.Rounds)
        {
            var match = round.Matches.FirstOrDefault(m => m.Id == matchId);
            if (match != null)
            {
                // Validate winner is one of the participants (allowing null players)
                var validWinner = (match.Player1 != null && match.Player1 == winner) ||
                                  (match.Player2 != null && match.Player2 == winner);

                if (!validWinner)
                {
                    throw new ArgumentException($"Winner must be one of the match participants");
                }

                match.Winner = winner;
                match.IsComplete = true;
                match.CompletedAt = DateTime.Now;

                // Advance winners to next round
                AdvanceWinners(tournament);
                return;
            }
        }
    }

    public static void SwapPlayers(Tournament tournament, Guid matchId, bool swapInMatch)
    {
        foreach (var round in tournament.Rounds)
        {
            var match = round.Matches.FirstOrDefault(m => m.Id == matchId);
            if (match != null && !match.IsComplete)
            {
                if (swapInMatch && match.Player1 != null && match.Player2 != null)
                {
                    (match.Player1, match.Player2) = (match.Player2, match.Player1);
                }
                return;
            }
        }
    }

    public static void ResetMatch(Tournament tournament, Guid matchId)
    {
        foreach (var round in tournament.Rounds)
        {
            var match = round.Matches.FirstOrDefault(m => m.Id == matchId);
            if (match != null && !match.IsBye)
            {
                match.Winner = null;
                match.IsComplete = false;
                match.CompletedAt = null;
                match.MatchResultId = null;

                // Need to reset subsequent rounds
                var roundIdx = tournament.Rounds.IndexOf(round);
                for (int i = roundIdx + 1; i < tournament.Rounds.Count; i++)
                {
                    foreach (var m in tournament.Rounds[i].Matches)
                    {
                        m.Player1 = null;
                        m.Player2 = null;
                        m.Winner = null;
                        m.IsComplete = false;
                        m.CompletedAt = null;
                        m.MatchResultId = null;
                        m.IsBye = false; // Clear bye status so it can be recalculated
                    }
                }

                AdvanceWinners(tournament);
                return;
            }
        }
    }

    private static int GetNextPowerOfTwo(int n)
    {
        int power = 1;
        while (power < n)
        {
            power *= 2;
        }
        return power;
    }

    private static List<int> GenerateBracketOrder(int bracketSize)
    {
        // Generate standard tournament bracket ordering
        // Ensures top seeds face bottom seeds (1v8, 2v7, 3v6, 4v5 for 8 players)
        // Top 2 seeds can only meet in finals

        if (bracketSize < 2)
        {
            return new List<int> { 1 };
        }

        var rounds = (int)Math.Log2(bracketSize) - 1;
        var order = new List<int> { 1, 2 };

        for (int i = 0; i < rounds; i++)
        {
            var newOrder = new List<int>();
            var maxSeed = order.Count * 2 + 1;

            foreach (var seed in order)
            {
                newOrder.Add(seed);
                newOrder.Add(maxSeed - seed);
            }

            order = newOrder;
        }

        return order;
    }

    private static string GetRoundName(int roundNumber, int totalRounds)
    {
        var matchesInFinal = (int)Math.Pow(2, totalRounds - roundNumber);

        return matchesInFinal switch
        {
            1 => "Finals",
            2 => "Semifinals",
            4 => "Quarterfinals",
            8 => "Round of 16",
            16 => "Round of 32",
            _ => $"Round {roundNumber}"
        };
    }
}
