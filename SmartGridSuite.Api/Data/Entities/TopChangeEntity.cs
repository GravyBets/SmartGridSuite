using System.ComponentModel.DataAnnotations.Schema;
using SmartGridSuite.Contracts.Tickets;

namespace SmartGridSuite.Api.Data.Entities;

[Table("ticket_top_changes")]
public sealed class TopChangeEntity : TopChangeDto
{
    // Unique while active, NULL after the completing write-up. Allows history.
    public long? ActiveTicketId { get; set; }
}
