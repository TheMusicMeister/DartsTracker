using System;
using System.Collections.Generic;
using System.Linq;

namespace DartsTracker;

public class DartsThrow
{
    public int? Roll1 { get; set; }
    public int? Roll2 { get; set; }
    public int? Roll3 { get; set; }
    public int Score { get; set; }
    public string Description { get; set; }
    
    public DartsThrow()
    {
        Score = 0;
        Description = "In Progress...";
    }
    
    public bool IsComplete => Roll1.HasValue && Roll2.HasValue && Roll3.HasValue;
    
    public bool IsActive => !IsComplete;
    
    public void AddRoll(int rollValue)
    {
        
        if (!Roll1.HasValue)
            Roll1 = rollValue;
        else if (!Roll2.HasValue)
            Roll2 = rollValue;
        else if (!Roll3.HasValue)
            Roll3 = rollValue;
    }
    
    public void CalculateScore()
    {
        if (IsComplete)
        {
            var result = DartsScoring.CalculateThrow(Roll1!.Value, Roll2!.Value, Roll3!.Value);
            Score = result.Score;
            Description = result.Description;
        }
    }
}

public class DartsRound
{
    public int RoundNumber { get; set; }
    public DartsThrow Throw1 { get; set; }
    public DartsThrow Throw2 { get; set; }
    public DartsThrow Throw3 { get; set; }
    public int RoundScore => Throw1.Score + Throw2.Score + Throw3.Score;
    
    public DartsRound(int roundNumber)
    {
        RoundNumber = roundNumber;
        Throw1 = new DartsThrow();
        Throw2 = new DartsThrow();
        Throw3 = new DartsThrow();
    }
    
    public bool IsComplete => Throw1.IsComplete && Throw2.IsComplete && Throw3.IsComplete;
    
    public DartsThrow? GetCurrentThrow()
    {
        if (Throw1.IsActive) return Throw1;
        if (Throw2.IsActive) return Throw2;
        if (Throw3.IsActive) return Throw3;
        return null; // Round complete or requires reset
    }
    
}

public class DartsPlayer
{
    public string Name { get; set; }
    public List<DartsRound> Rounds { get; set; }
    public int TotalScore => Rounds.Sum(r => r.RoundScore);
    
    public DartsPlayer(string name, int totalRounds)
    {
        Name = name;
        Rounds = new List<DartsRound>();
        for (int i = 1; i <= totalRounds; i++)
        {
            Rounds.Add(new DartsRound(i));
        }
    }
    
    public bool IsGameComplete => Rounds.All(r => r.IsComplete);
    
    public DartsRound? GetCurrentRound()
    {
        return Rounds.FirstOrDefault(r => !r.IsComplete);
    }
    
}

public class DartsGame
{
    public Dictionary<string, DartsPlayer> Players { get; set; }
    public int TotalRounds { get; set; }
    public DateTime StartTime { get; set; }
    public bool IsActive { get; set; }
    
    public DartsGame(int totalRounds = 5)
    {
        Players = new Dictionary<string, DartsPlayer>();
        TotalRounds = totalRounds;
        StartTime = DateTime.Now;
        IsActive = false;
    }
    
    public void AddPlayer(string playerName)
    {
        if (!Players.ContainsKey(playerName))
        {
            Players[playerName] = new DartsPlayer(playerName, TotalRounds);
        }
    }
    
    public DartsPlayer? GetPlayer(string playerName)
    {
        Players.TryGetValue(playerName, out var player);
        return player;
    }
    
    public bool IsGameComplete => Players.Values.All(p => p.IsGameComplete);
    
    public List<DartsPlayer> GetLeaderboard()
    {
        return Players.Values.OrderByDescending(p => p.TotalScore).ToList();
    }
    
}