#nullable enable
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using System.Threading;

namespace SmartGridSuite.Api.Services
{
    public sealed class TruckBoardInitializationService
    {
        private readonly SmartGridDbContext _db;

        private const string TechnicianRoleCode = "TECHNICIAN";

        /*
         * Truck Board and Daily Assignments may both load at application start.
         * This prevents two requests in this API process from attempting to
         * initialize the same new board date at the same time.
         */
        private static readonly SemaphoreSlim InitializationLock = new(1, 1);

        public TruckBoardInitializationService(SmartGridDbContext db)
        {
            _db = db;
        }

        public async Task EnsureBoardInitializedAsync(
            DateTime workDate,
            CancellationToken ct = default)
        {
            workDate = workDate.Date;

            var alreadyInitialized = await _db.TruckBoardDays
                .AsNoTracking()
                .AnyAsync(x => x.WorkDate == workDate, ct);

            if (alreadyInitialized)
                return;

            await InitializationLock.WaitAsync(ct);

            try
            {
                alreadyInitialized = await _db.TruckBoardDays
                    .AsNoTracking()
                    .AnyAsync(x => x.WorkDate == workDate, ct);

                if (alreadyInitialized)
                    return;

                await using var tx = await _db.Database.BeginTransactionAsync(ct);

                try
                {
                    var now = DateTime.Now;

                    /*
                     * Safety path for data created before the marker table was added.
                     * If roster rows already exist for this date, preserve them and
                     * simply mark this board day as initialized.
                     */
                    var alreadyHasRosterRows = await _db.TruckRosters
                        .AsNoTracking()
                        .AnyAsync(x => x.WorkDate == workDate, ct);

                    if (alreadyHasRosterRows)
                    {
                        _db.TruckBoardDays.Add(new TruckBoardDayEntity
                        {
                            WorkDate = workDate,
                            InitializationSource = "ExistingRoster",
                            CarriedFromWorkDate = null,
                            InitializedAt = now,
                            UpdatedAt = now
                        });

                        await _db.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);

                        return;
                    }

                    /*
                     * Use the most recent initialized board date, even when that
                     * board was explicitly saved empty. That preserves an intentional
                     * empty board instead of resurrecting an older roster.
                     */
                    var priorBoardDate = await _db.TruckBoardDays
                        .AsNoTracking()
                        .Where(x => x.WorkDate < workDate)
                        .OrderByDescending(x => x.WorkDate)
                        .Select(x => (DateTime?)x.WorkDate)
                        .FirstOrDefaultAsync(ct);

                    /*
                     * Fallback only for old/unseeded data.
                     */
                    if (!priorBoardDate.HasValue)
                    {
                        priorBoardDate = await _db.TruckRosters
                            .AsNoTracking()
                            .Where(x => x.WorkDate < workDate)
                            .OrderByDescending(x => x.WorkDate)
                            .Select(x => (DateTime?)x.WorkDate)
                            .FirstOrDefaultAsync(ct);
                    }

                    _db.TruckBoardDays.Add(new TruckBoardDayEntity
                    {
                        WorkDate = workDate,
                        InitializationSource = priorBoardDate.HasValue
                            ? "CarryForward"
                            : "NewEmpty",
                        CarriedFromWorkDate = priorBoardDate,
                        InitializedAt = now,
                        UpdatedAt = now
                    });

                    if (!priorBoardDate.HasValue)
                    {
                        await _db.SaveChangesAsync(ct);
                        await tx.CommitAsync(ct);

                        return;
                    }

                    /*
                     * Carry forward only active Technician-role users assigned
                     * to active trucks.
                     */
                    var priorRows = await (
                        from roster in _db.TruckRosters.AsNoTracking()
                        join technician in ActiveFieldTechniciansQuery()
                            on roster.TechnicianId equals technician.Id
                        join truck in _db.Trucks
                                .AsNoTracking()
                                .Where(x => x.IsActive)
                            on roster.TruckId equals truck.Id
                        where roster.WorkDate == priorBoardDate.Value
                        select new CarriedRosterRow
                        {
                            TechnicianId = roster.TechnicianId,
                            TruckId = roster.TruckId
                        })
                        .Distinct()
                        .ToListAsync(ct);

                    foreach (var row in priorRows)
                    {
                        _db.TruckRosters.Add(new TruckRosterEntity
                        {
                            WorkDate = workDate,
                            TechnicianId = row.TechnicianId,
                            TruckId = row.TruckId
                        });
                    }

                    await _db.SaveChangesAsync(ct);

                    await CreateCarriedForwardCrewsAsync(
                        workDate,
                        priorBoardDate.Value,
                        priorRows,
                        now,
                        ct);

                    await _db.SaveChangesAsync(ct);
                    await tx.CommitAsync(ct);
                }
                catch
                {
                    await tx.RollbackAsync(ct);
                    throw;
                }
            }
            finally
            {
                InitializationLock.Release();
            }
        }

        public async Task MarkExplicitSaveAsync(
            DateTime workDate,
            CancellationToken ct = default)
        {
            workDate = workDate.Date;

            var now = DateTime.Now;

            var marker = await _db.TruckBoardDays
                .FirstOrDefaultAsync(x => x.WorkDate == workDate, ct);

            if (marker == null)
            {
                _db.TruckBoardDays.Add(new TruckBoardDayEntity
                {
                    WorkDate = workDate,
                    InitializationSource = "ExplicitCommit",
                    CarriedFromWorkDate = null,
                    InitializedAt = now,
                    UpdatedAt = now
                });

                return;
            }

            marker.InitializationSource = "ExplicitCommit";
            marker.CarriedFromWorkDate = null;
            marker.UpdatedAt = now;
        }

        private async Task CreateCarriedForwardCrewsAsync(
            DateTime workDate,
            DateTime priorBoardDate,
            IReadOnlyList<CarriedRosterRow> priorRows,
            DateTime now,
            CancellationToken ct)
        {
            if (priorRows.Count == 0)
                return;

            var carriedTruckIds = priorRows
                .Select(x => x.TruckId)
                .Distinct()
                .ToList();

            var carriedTechnicianIds = priorRows
                .Select(x => x.TechnicianId)
                .Distinct()
                .ToList();

            var trucks = await _db.Trucks
                .AsNoTracking()
                .Where(x => x.IsActive && carriedTruckIds.Contains(x.Id))
                .ToListAsync(ct);

            var technicians = await ActiveFieldTechniciansQuery()
                .Where(x => carriedTechnicianIds.Contains(x.Id))
                .ToListAsync(ct);

            var technicianById = technicians
                .ToDictionary(x => x.Id);

            var priorCrewsWithLeads = await _db.Crews
                .AsNoTracking()
                .Where(x =>
                    x.WorkDate == priorBoardDate &&
                    x.TruckNumber != null &&
                    x.LeadTechnicianId != null)
                .ToListAsync(ct);

            var pendingCrews = new List<CarriedCrewMembers>();

            foreach (var truck in trucks)
            {
                var memberTechs = priorRows
                    .Where(x => x.TruckId == truck.Id)
                    .Select(x => technicianById.TryGetValue(x.TechnicianId, out var tech)
                        ? tech
                        : null)
                    .Where(x => x != null)
                    .Select(x => x!)
                    .DistinctBy(x => x.Id)
                    .OrderByDescending(x => x.HomeTruckId == truck.Id)
                    .ThenByDescending(GetTitleRank)
                    .ThenBy(x => x.LastName)
                    .ThenBy(x => x.FirstName)
                    .ToList();

                /*
                 * Single-tech trucks are still valid truck-board assignments,
                 * but they do not need a multi-member CrewEntity record.
                 */
                if (memberTechs.Count <= 1)
                    continue;

                var truckNumber = (truck.TruckNumber ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(truckNumber))
                    continue;

                var priorCrew = priorCrewsWithLeads.FirstOrDefault(x =>
                    string.Equals(
                        (x.TruckNumber ?? string.Empty).Trim(),
                        truckNumber,
                        StringComparison.OrdinalIgnoreCase));

                var priorLeadTechnicianId = priorCrew?.LeadTechnicianId;

                var leadTech = PickLeadTechnician(
                    memberTechs,
                    truck.Id,
                    priorLeadTechnicianId);

                if (leadTech == null)
                    continue;

                var crew = new CrewEntity
                {
                    WorkDate = workDate,
                    TruckNumber = truckNumber,
                    LeadTechnicianId = leadTech.Id,
                    UpdatedAt = now
                };

                _db.Crews.Add(crew);

                pendingCrews.Add(new CarriedCrewMembers
                {
                    Crew = crew,
                    TechnicianIds = memberTechs
                        .Select(x => x.Id)
                        .Distinct()
                        .ToList()
                });
            }

            if (pendingCrews.Count == 0)
                return;

            /*
             * Save crews first so their generated IDs are available for
             * technician_roster records.
             */
            await _db.SaveChangesAsync(ct);

            foreach (var pendingCrew in pendingCrews)
            {
                foreach (var technicianId in pendingCrew.TechnicianIds)
                {
                    _db.TechnicianRosters.Add(new TechnicianRosterEntity
                    {
                        WorkDate = workDate,
                        TechnicianId = technicianId,
                        CrewId = pendingCrew.Crew.Id
                    });
                }
            }
        }

        private IQueryable<TechnicianEntity> ActiveFieldTechniciansQuery()
        {
            return _db.Technicians
                .AsNoTracking()
                .Where(t =>
                    t.IsActive &&
                    t.TechnicianRoles.Any(tr => tr.Role.Code == TechnicianRoleCode));
        }

        private static TechnicianEntity? PickLeadTechnician(
            IReadOnlyList<TechnicianEntity> technicians,
            uint truckId,
            uint? priorLeadTechnicianId)
        {
            if (technicians.Count == 0)
                return null;

            if (priorLeadTechnicianId.HasValue)
            {
                var previousLead = technicians.FirstOrDefault(x =>
                    x.Id == priorLeadTechnicianId.Value);

                if (previousLead != null)
                    return previousLead;
            }

            var homeTruckLead = technicians.FirstOrDefault(x =>
                x.HomeTruckId == truckId);

            if (homeTruckLead != null)
                return homeTruckLead;

            return technicians
                .OrderByDescending(GetTitleRank)
                .ThenBy(x => x.LastName)
                .ThenBy(x => x.FirstName)
                .FirstOrDefault();
        }

        private static int GetTitleRank(TechnicianEntity technician)
        {
            var title = (technician.Title ?? string.Empty).Trim();

            if (title.Equals("Supervisor", StringComparison.OrdinalIgnoreCase))
                return 400;

            if (title.Equals("Head Journeyman", StringComparison.OrdinalIgnoreCase))
                return 300;

            if (title.Equals("Journeyman", StringComparison.OrdinalIgnoreCase))
                return 200;

            if (title.Equals("Apprentice", StringComparison.OrdinalIgnoreCase))
                return 100;

            return 0;
        }

        private sealed class CarriedRosterRow
        {
            public uint TechnicianId { get; init; }
            public uint TruckId { get; init; }
        }

        private sealed class CarriedCrewMembers
        {
            public CrewEntity Crew { get; init; } = new();

            public List<uint> TechnicianIds { get; init; } = new();
        }
    }
}