namespace SmartGridSuite.Contracts.Arcade;

public sealed class ArcadeLeaderboardEntryDto
{
    public int Rank { get; set; }
    public string TechnicianName { get; set; } = "";
    public int Score { get; set; }
    public int Wave { get; set; }
    public int Repairs { get; set; }
    public DateTime AchievedAtUtc { get; set; }
}
