namespace SmartGridSuite.Contracts.Arcade;

public sealed class ArcadeScoreRequest
{
    // The client resolves this ID from CurrentUserService, not free-text.
    public string EmployeeId { get; set; } = "";
    // NET CASH retained after buying any truck upgrades.
    public int Score { get; set; }
    public int Wave { get; set; }
    public int Repairs { get; set; }
}
