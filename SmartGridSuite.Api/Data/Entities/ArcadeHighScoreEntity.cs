namespace SmartGridSuite.Api.Data.Entities;

public sealed class ArcadeHighScoreEntity
{
    public uint TechnicianId { get; set; }
    public int BestScore { get; set; }
    public int BestWave { get; set; }
    public int BestRepairs { get; set; }
    public DateTime AchievedAtUtc { get; set; }

    public TechnicianEntity Technician { get; set; } = null!;
}
