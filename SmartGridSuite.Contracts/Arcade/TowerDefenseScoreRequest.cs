namespace SmartGridSuite.Contracts.Arcade;

public sealed class TowerDefenseScoreRequest
{
    public string EmployeeId { get; set; } = "";
    public int Wave { get; set; }
    public int Score { get; set; }
}
