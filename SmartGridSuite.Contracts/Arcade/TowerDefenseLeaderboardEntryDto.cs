namespace SmartGridSuite.Contracts.Arcade;

public sealed class TowerDefenseLeaderboardEntryDto
{
    public int Rank { get; set; }
    public string TechnicianName { get; set; } = "";
    public int Wave { get; set; }
    public int Score { get; set; }
    public DateTime AchievedAtUtc { get; set; }
}
