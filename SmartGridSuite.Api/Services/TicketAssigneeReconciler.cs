#nullable enable
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Tickets;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGridSuite.Api.Services
{
    /// <summary>
    /// Rebuilds the visible ticket assignees from two independent sources:
    /// explicit assignments made in Tickets and today's active published routes.
    /// Removed/completed routes and immutable publication snapshots are not owners.
    /// </summary>
    public static class TicketAssigneeReconciler
    {
        public static async Task ReconcileAsync(
            SmartGridDbContext db,
            IEnumerable<long> ticketIds,
            DateTime workDate,
            CancellationToken ct)
        {
            var ids = ticketIds.Where(id => id > 0).Distinct().ToList();
            if (ids.Count == 0) return;

            var tickets = await db.Tickets
                .Where(ticket => ids.Contains(ticket.Id))
                .ToListAsync(ct);

            var closedNames = await db.TicketStatuses
                .AsNoTracking()
                .Where(status => status.IsActive && status.IsClosed)
                .Select(status => status.Name)
                .ToListAsync(ct);
            var closed = closedNames.ToHashSet(StringComparer.OrdinalIgnoreCase);

            var assignments = await db.DailyTicketAssignments
                .AsNoTracking()
                .Where(assignment =>
                    ids.Contains(assignment.TicketId) &&
                    assignment.AssignmentDate == workDate.Date &&
                    assignment.AssignmentStatus == "Active" &&
                    assignment.IsPublished)
                .OrderBy(assignment => assignment.SortOrder)
                .ThenBy(assignment => assignment.Id)
                .ToListAsync(ct);

            var truckIds = assignments
                .Where(a => a.TruckId.HasValue)
                .Select(a => a.TruckId!.Value)
                .Distinct()
                .ToList();
            var technicianIds = assignments
                .Where(a => a.TechnicianId.HasValue)
                .Select(a => a.TechnicianId!.Value)
                .Distinct()
                .ToList();

            var technicians = await db.Technicians
                .AsNoTracking()
                .Where(tech => technicianIds.Contains(tech.Id))
                .ToListAsync(ct);
            var techniciansById = technicians.ToDictionary(tech => tech.Id);

            var trucks = await db.Trucks
                .AsNoTracking()
                .Where(truck => truckIds.Contains(truck.Id))
                .ToListAsync(ct);
            var trucksById = trucks.ToDictionary(truck => truck.Id);

            var rosterRows = await (
                from roster in db.TruckRosters.AsNoTracking()
                join tech in db.Technicians.AsNoTracking()
                    on roster.TechnicianId equals tech.Id
                where roster.WorkDate == workDate.Date &&
                      truckIds.Contains(roster.TruckId) &&
                      tech.IsActive
                select new
                {
                    roster.TruckId,
                    tech.FirstName,
                    tech.LastName,
                    tech.EmployeeId
                }).ToListAsync(ct);

            var namesByTruckId = rosterRows
                .GroupBy(row => row.TruckId)
                .ToDictionary(
                    group => group.Key,
                    group => group
                        .Select(row => FormatName(
                            row.FirstName, row.LastName, row.EmployeeId))
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(name => name)
                        .ToList());

            var assignmentsByTicket = assignments
                .GroupBy(a => a.TicketId)
                .ToDictionary(group => group.Key, group => group.ToList());

            foreach (var ticket in tickets)
            {
                // A closed ticket's historical association is left untouched.
                if (closed.Contains(ticket.Status ?? string.Empty))
                    continue;

                // NULL means the legacy ticket has no verified manual source.
                // Never import its cumulative AssignedTech text as manual names.
                var display = TicketAssigneeText.Merge(
                    ticket.ManualAssignedTech, null);

                if (assignmentsByTicket.TryGetValue(ticket.Id, out var routes))
                {
                    foreach (var route in routes)
                    {
                        var names = BuildRouteNames(
                            route, namesByTruckId, techniciansById,
                            trucksById);
                        display = TicketAssigneeText.Merge(display, names);
                    }
                }

                if (display.Length > TicketTextLimits.AssignedTech)
                    throw new InvalidOperationException(
                        $"Assigned To exceeds {TicketTextLimits.AssignedTech} characters for ticket {ticket.Id}.");

                if (!string.Equals(
                    ticket.AssignedTech, display, StringComparison.Ordinal))
                {
                    ticket.AssignedTech = display;
                    ticket.LastActivityAt = DateTime.Now;
                }
            }
        }

        private static string BuildRouteNames(
            DailyTicketAssignmentEntity assignment,
            IReadOnlyDictionary<uint, List<string>> namesByTruckId,
            IReadOnlyDictionary<uint, TechnicianEntity> techniciansById,
            IReadOnlyDictionary<uint, TruckEntity> trucksById)
        {
            if (assignment.TruckId is uint truckId)
            {
                if (namesByTruckId.TryGetValue(truckId, out var names) &&
                    names.Count > 0)
                    return string.Join(" & ", names);

                if (assignment.TechnicianId is uint leadId &&
                    techniciansById.TryGetValue(leadId, out var lead))
                    return FormatName(
                        lead.FirstName, lead.LastName, lead.EmployeeId);

                if (trucksById.TryGetValue(truckId, out var truck) &&
                    !string.IsNullOrWhiteSpace(truck.TruckNumber))
                    return $"Truck {truck.TruckNumber.Trim()}";
            }

            if (assignment.TechnicianId is uint technicianId &&
                techniciansById.TryGetValue(technicianId, out var technician))
                return FormatName(
                    technician.FirstName, technician.LastName,
                    technician.EmployeeId);

            return assignment.TechnicianId is uint fallbackId
                ? $"Technician {fallbackId}"
                : "(Unassigned)";
        }

        private static string FormatName(
            string? firstName, string? lastName, string? employeeId)
        {
            var name = $"{firstName ?? string.Empty} {lastName ?? string.Empty}".Trim();
            return name.Length > 0
                ? name
                : (employeeId ?? "Unknown").Trim();
        }
    }
}
