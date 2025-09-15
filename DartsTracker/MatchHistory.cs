using System;
using System.Collections.Generic;
using System.Linq;

namespace DartsTracker;

[Serializable]
public class MatchPlayerResult
{
    public string PlayerName { get; set; } = "";
    public int TotalScore { get; set; }
    public int Position { get; set; }
    public List<MatchRoundResult> Rounds { get; set; } = new();
}

[Serializable]
public class MatchRoundResult
{
    public int RoundNumber { get; set; }
    public List<MatchThrowResult> Throws { get; set; } = new();
    public int RoundScore => Throws.Sum(t => t.Score);
}

[Serializable]
public class MatchThrowResult
{
    public int Roll1 { get; set; }
    public int Roll2 { get; set; }
    public int Roll3 { get; set; }
    public int Score { get; set; }
    public string Description { get; set; } = "";
}

[Serializable]
public class MatchResult
{
    public DateTime CompletedAt { get; set; }
    public int TotalRounds { get; set; }
    public bool WasManualPlayerMode { get; set; }
    public TimeSpan Duration { get; set; }
    public List<MatchPlayerResult> Players { get; set; } = new();
    public string Winner => Players.FirstOrDefault()?.PlayerName ?? "Unknown";
    public int PlayerCount => Players.Count;
}

[Serializable]
public class MatchHistoryData
{
    public List<MatchResult> Matches { get; set; } = new();
    
    public void AddMatch(MatchResult match)
    {
        Matches.Insert(0, match); // Add to beginning for most recent first
        
        // Keep only the last 100 matches to prevent file bloat
        if (Matches.Count > 100)
        {
            Matches.RemoveRange(100, Matches.Count - 100);
        }
    }
    
    public List<MatchResult> GetRecentMatches(int count = 20)
    {
        return Matches.Take(count).ToList();
    }
}