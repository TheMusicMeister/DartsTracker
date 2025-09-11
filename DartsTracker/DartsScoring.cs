using System;

namespace DartsTracker;

public static class DartsScoring
{
    public record DartThrow(int Score, string Description);

    public static DartThrow CalculateThrow(int bullseyeRoll, int ringRoll, int wedgeRoll)
    {
        // First check for bullseye
        if (bullseyeRoll >= 1 && bullseyeRoll <= 5)
        {
            return new DartThrow(50, "Double Bullseye");
        }
        
        if (bullseyeRoll >= 6 && bullseyeRoll <= 15)
        {
            return new DartThrow(25, "Single Bullseye");
        }
        
        // Not a bullseye, check ring and wedge
        var ringResult = GetRingResult(ringRoll);
        var wedgeNumber = GetWedgeNumber(wedgeRoll);
        
        if (ringResult.Multiplier == 0)
        {
            return new DartThrow(0, "Miss");
        }
        
        var score = wedgeNumber * ringResult.Multiplier;
        var description = $"{ringResult.Description} {wedgeNumber}";
        
        return new DartThrow(score, description);
    }
    
    private static (int Multiplier, string Description) GetRingResult(int ringRoll)
    {
        return ringRoll switch
        {
            >= 1 and <= 30 => (1, "Single"),
            >= 31 and <= 40 => (3, "Triple"),
            >= 41 and <= 70 => (1, "Single"),
            >= 71 and <= 80 => (2, "Double"),
            >= 81 and <= 100 => (0, "Miss"),
            _ => (0, "Miss")
        };
    }
    
    private static int GetWedgeNumber(int wedgeRoll)
    {
        return wedgeRoll switch
        {
            >= 1 and <= 20 => wedgeRoll,
            _ => 1 // Default to wedge 1 if out of range
        };
    }
}