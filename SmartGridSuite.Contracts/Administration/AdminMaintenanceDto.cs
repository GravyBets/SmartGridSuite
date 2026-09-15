using System.ComponentModel.DataAnnotations;

namespace SmartGridSuite.Contracts.Administration
{
    public sealed class RestartApiRequest
    {
        [Required, MaxLength(256)]
        public string Password { get; set; } = "";
    }

    public sealed class RestartApiResponse
    {
        public bool Accepted { get; set; }
        public DateTime PreviousStartedAtUtc { get; set; }
        public string Message { get; set; } = "";
    }

    public sealed class ParentDatabaseTestResponse
    {
        public bool Succeeded { get; set; }
        public string Message { get; set; } = "";
        public SystemHealthDto Health { get; set; } = new();
    }
}
