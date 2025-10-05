using System;
using System.Collections.Generic;
using System.Linq;

namespace DartsTracker;

[Serializable]
public class LeagueSeries
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string Description { get; set; } = "";
}

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
    public Guid? LeagueSeriesId { get; set; } = null;
    public GameMode Mode { get; set; } = GameMode.FreeForAll;

    public string GetMatchupDisplay()
    {
        if (Mode == GameMode.Bracket && Players.Count == 2)
        {
            return $"{Players[0].PlayerName} vs {Players[1].PlayerName}";
        }
        return $"{PlayerCount} players";
    }
}

[Serializable]
public class MatchHistoryData
{
    public List<MatchResult> Matches { get; set; } = new();
    public List<LeagueSeries> LeagueSeries { get; set; } = new();

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

    public List<MatchResult> GetAllMatches()
    {
        return Matches;
    }

    public LeagueSeries CreateLeagueSeries(string name, string description = "")
    {
        var series = new LeagueSeries
        {
            Name = name,
            Description = description,
            CreatedAt = DateTime.Now
        };
        LeagueSeries.Add(series);
        return series;
    }

    public bool RenameLeagueSeries(Guid seriesId, string newName)
    {
        var series = LeagueSeries.FirstOrDefault(s => s.Id == seriesId);
        if (series == null) return false;

        series.Name = newName;
        return true;
    }

    public bool DeleteLeagueSeries(Guid seriesId)
    {
        var series = LeagueSeries.FirstOrDefault(s => s.Id == seriesId);
        if (series == null) return false;

        // Unassign all matches from this series
        foreach (var match in Matches.Where(m => m.LeagueSeriesId == seriesId))
        {
            match.LeagueSeriesId = null;
        }

        LeagueSeries.Remove(series);
        return true;
    }

    public void AssignMatchToSeries(MatchResult match, Guid? seriesId)
    {
        match.LeagueSeriesId = seriesId;
    }

    public void UnassignMatch(MatchResult match)
    {
        match.LeagueSeriesId = null;
    }

    public List<MatchResult> GetMatchesForSeries(Guid seriesId)
    {
        return Matches.Where(m => m.LeagueSeriesId == seriesId).ToList();
    }

    public List<MatchResult> GetUnassignedMatches()
    {
        return Matches.Where(m => m.LeagueSeriesId == null).ToList();
    }
}