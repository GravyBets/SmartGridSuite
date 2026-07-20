using System.Collections.Generic;

namespace SmartGridSuite.Contracts.Administration.Ticket.Status
{
    public sealed class ReorderTicketStatusesRequest
    {
        public List<ulong> OrderedIds { get; set; } = new();
    }
}