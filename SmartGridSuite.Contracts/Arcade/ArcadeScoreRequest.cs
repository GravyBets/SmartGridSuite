namespace SmartGridSuite.Contracts.Arcade;

public sealed class ArcadeScoreRequest
{
    // The client resolves this ID from CurrentUserService, not free-text.
    public string EmployeeId { get; set; } = "";
    public int Score { get; set; }
    public int Wave { get; set; }
    public int Repairs { get; set; }
}
