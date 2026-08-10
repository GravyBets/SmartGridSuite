using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Contracts.Settings;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/admin/general-settings")]
    public sealed class GeneralSettingsController : ControllerBase
    {
        private readonly SmartGridDbContext _db;

        public GeneralSettingsController(SmartGridDbContext db)
        {
            _db = db;
        }

        [HttpGet("communication-device-types")]
        public async Task<ActionResult<List<CommunicationDeviceTypeDto>>> GetCommunicationDeviceTypes(
            [FromQuery] bool activeOnly = false,
            CancellationToken ct = default)
        {
            var query = _db.CommunicationDeviceTypes.AsNoTracking();

            if (activeOnly)
                query = query.Where(x => x.IsActive);

            var items = await query
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName)
                .Select(x => new CommunicationDeviceTypeDto
                {
                    Id = x.Id,
                    DisplayName = x.DisplayName,
                    IsActive = x.IsActive,
                    SortOrder = x.SortOrder
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        [HttpPost("communication-device-types")]
        public async Task<ActionResult<CommunicationDeviceTypeDto>> CreateCommunicationDeviceType(
            [FromBody] SaveCommunicationDeviceTypeRequest request,
            CancellationToken ct)
        {
            var displayName = (request.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(displayName))
                return BadRequest("Display name is required.");

            var alreadyExists = await _db.CommunicationDeviceTypes
                .AnyAsync(x => x.DisplayName == displayName, ct);

            if (alreadyExists)
                return Conflict("A communication device type with that name already exists.");

            var entity = new CommunicationDeviceTypeEntity
            {
                DisplayName = displayName,
                IsActive = request.IsActive,
                SortOrder = request.SortOrder,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };

            _db.CommunicationDeviceTypes.Add(entity);
            await _db.SaveChangesAsync(ct);

            return Ok(ToDto(entity));
        }

        [HttpPut("communication-device-types/{id:int}")]
        public async Task<ActionResult<CommunicationDeviceTypeDto>> UpdateCommunicationDeviceType(
            int id,
            [FromBody] SaveCommunicationDeviceTypeRequest request,
            CancellationToken ct)
        {
            if (id <= 0)
                return BadRequest("Invalid communication device type id.");

            var entityId = (uint)id;

            var entity = await _db.CommunicationDeviceTypes
                .FirstOrDefaultAsync(x => x.Id == entityId, ct);

            if (entity is null)
                return NotFound();

            var displayName = (request.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(displayName))
                return BadRequest("Display name is required.");

            var alreadyExists = await _db.CommunicationDeviceTypes
                .AnyAsync(x => x.Id != entityId && x.DisplayName == displayName, ct);

            if (alreadyExists)
                return Conflict("A communication device type with that name already exists.");

            entity.DisplayName = displayName;
            entity.IsActive = request.IsActive;
            entity.SortOrder = request.SortOrder;
            entity.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync(ct);

            return Ok(ToDto(entity));
        }

        [HttpDelete("communication-device-types/{id:long}")]
        public async Task<IActionResult> DeleteCommunicationDeviceType(long id, CancellationToken ct)
        {
            if (id <= 0)
                return BadRequest("Communication device type id is required.");

            var entity = await _db.CommunicationDeviceTypes
                .FirstOrDefaultAsync(x => x.Id == (uint)id, ct);

            if (entity is null)
                return NotFound($"Communication device type {id} was not found.");

            _db.CommunicationDeviceTypes.Remove(entity);

            await _db.SaveChangesAsync(ct);

            return NoContent();
        }

        [HttpGet("write-up-flags")]
        public async Task<ActionResult<List<WriteUpFlagDto>>> GetWriteUpFlags(
            [FromQuery] bool activeOnly = false,
            [FromQuery] bool technicianVisibleOnly = false,
            CancellationToken ct = default)
        {
            var query =
                _db.WriteUpFlags.AsNoTracking();

            if (activeOnly)
                query = query.Where(x => x.IsActive);

            if (technicianVisibleOnly)
            {
                query = query.Where(
                    x => x.IsTechnicianVisible);
            }

            var items = await query
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName)
                .Select(x => new WriteUpFlagDto
                {
                    Id = x.Id,
                    DisplayName = x.DisplayName,
                    IsActive = x.IsActive,
                    SortOrder = x.SortOrder,
                    IsTechnicianVisible =
                        x.IsTechnicianVisible,
                    IsSystem = x.IsSystem,
                    SystemKey = x.SystemKey ?? ""
                })
                .ToListAsync(ct);

            return Ok(items);
        }

        [HttpPost("write-up-flags")]
        public async Task<ActionResult<WriteUpFlagDto>> CreateWriteUpFlag(
            [FromBody] SaveWriteUpFlagRequest request,
            CancellationToken ct)
        {
            var displayName =
                (request.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(displayName))
                return BadRequest("Display name is required.");

            if (displayName.Length > 100)
            {
                return BadRequest(
                    "Display name cannot exceed 100 characters.");
            }

            var alreadyExists =
                await _db.WriteUpFlags.AnyAsync(
                    x => x.DisplayName == displayName,
                    ct);

            if (alreadyExists)
            {
                return Conflict(
                    "A write-up flag with that name already exists.");
            }

            var entity =
                new WriteUpFlagEntity
                {
                    DisplayName = displayName,
                    IsActive = request.IsActive,
                    SortOrder = request.SortOrder,
                    IsTechnicianVisible =
                        request.IsTechnicianVisible,

                    /*
                     * Only server-managed seed/setup code may create
                     * protected system flags.
                     */
                    IsSystem = false,
                    SystemKey = null,

                    CreatedAt = DateTime.Now,
                    UpdatedAt = DateTime.Now
                };

            _db.WriteUpFlags.Add(entity);

            await _db.SaveChangesAsync(ct);

            return Ok(ToDto(entity));
        }

        [HttpPut("write-up-flags/{id:int}")]
        public async Task<ActionResult<WriteUpFlagDto>> UpdateWriteUpFlag(
            int id,
            [FromBody] SaveWriteUpFlagRequest request,
            CancellationToken ct)
        {
            if (id <= 0)
                return BadRequest("Invalid write-up flag id.");

            var entityId =
                (uint)id;

            var entity =
                await _db.WriteUpFlags.FirstOrDefaultAsync(
                    x => x.Id == entityId,
                    ct);

            if (entity is null)
                return NotFound();

            var displayName =
                (request.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(displayName))
                return BadRequest("Display name is required.");

            if (displayName.Length > 100)
            {
                return BadRequest(
                    "Display name cannot exceed 100 characters.");
            }

            /*
             * Protected flags may be enabled, disabled, reordered,
             * hidden from technicians, or configured to create a task.
             * Their names and system identities cannot be changed.
             */
            if (entity.IsSystem &&
                !string.Equals(
                    displayName,
                    entity.DisplayName,
                    StringComparison.OrdinalIgnoreCase))
            {
                return Conflict(
                    "System write-up flags cannot be renamed.");
            }

            var alreadyExists =
                await _db.WriteUpFlags.AnyAsync(
                    x =>
                        x.Id != entityId &&
                        x.DisplayName == displayName,
                    ct);

            if (alreadyExists)
            {
                return Conflict(
                    "A write-up flag with that name already exists.");
            }

            if (!entity.IsSystem)
                entity.DisplayName = displayName;

            entity.IsActive =
                request.IsActive;

            entity.SortOrder =
                request.SortOrder;

            entity.IsTechnicianVisible =
                request.IsTechnicianVisible;

            entity.UpdatedAt =
                DateTime.Now;

            await _db.SaveChangesAsync(ct);

            return Ok(ToDto(entity));
        }

        [HttpDelete("write-up-flags/{id:long}")]
        public async Task<IActionResult> DeleteWriteUpFlag(
            long id,
            CancellationToken ct)
        {
            if (id <= 0)
                return BadRequest("Write-up flag id is required.");

            var entity =
                await _db.WriteUpFlags.FirstOrDefaultAsync(
                    x => x.Id == (uint)id,
                    ct);

            if (entity is null)
            {
                return NotFound(
                    $"Write-up flag {id} was not found.");
            }

            if (entity.IsSystem ||
                !string.IsNullOrWhiteSpace(entity.SystemKey))
            {
                return Conflict(
                    "System write-up flags cannot be deleted. " +
                    "Deactivate the flag instead.");
            }

            _db.WriteUpFlags.Remove(entity);

            await _db.SaveChangesAsync(ct);

            return NoContent();
        }

        private static CommunicationDeviceTypeDto ToDto(CommunicationDeviceTypeEntity entity)
        {
            return new CommunicationDeviceTypeDto
            {
                Id = entity.Id,
                DisplayName = entity.DisplayName,
                IsActive = entity.IsActive,
                SortOrder = entity.SortOrder
            };
        }

        // -------------------------
        // Refer To Options
        // -------------------------

        [HttpGet("refer-to-options")]
        public async Task<ActionResult<List<ReferToOptionDto>>> GetReferToOptions(
            [FromQuery] bool activeOnly = false)
        {
            var query = _db.ReferToOptions
                .AsNoTracking()
                .AsQueryable();

            if (activeOnly)
            {
                query = query.Where(x => x.IsActive);
            }

            var options = await query
                .OrderBy(x => x.SortOrder)
                .ThenBy(x => x.DisplayName)
                .ToListAsync();

            return Ok(options
                .Select(ToReferToOptionDto)
                .ToList());
        }

        [HttpPost("refer-to-options")]
        public async Task<ActionResult<ReferToOptionDto>> CreateReferToOption(
            [FromBody] SaveReferToOptionRequest request)
        {
            var displayName =
                (request.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(displayName))
            {
                return BadRequest(
                    "Refer To destination name is required.");
            }

            if (displayName.Length > 100)
            {
                return BadRequest(
                    "Refer To destination name is limited to 100 characters.");
            }

            var duplicateExists =
                await _db.ReferToOptions.AnyAsync(
                    x => x.DisplayName == displayName);

            if (duplicateExists)
            {
                return Conflict(
                    $"A Refer To destination named \"{displayName}\" already exists.");
            }

            var now = DateTime.UtcNow;

            var entity = new ReferToOptionEntity
            {
                DisplayName = displayName,
                IsActive = request.IsActive,
                SortOrder = request.SortOrder,
                CreatedAt = now,
                UpdatedAt = now
            };

            _db.ReferToOptions.Add(entity);
            await _db.SaveChangesAsync();

            return Ok(ToReferToOptionDto(entity));
        }

        [HttpPut("refer-to-options/{id:int}")]
        public async Task<ActionResult<ReferToOptionDto>> UpdateReferToOption(
            uint id,
            [FromBody] SaveReferToOptionRequest request)
        {
            var entity =
                await _db.ReferToOptions
                    .FirstOrDefaultAsync(x => x.Id == id);

            if (entity == null)
            {
                return NotFound(
                    $"Refer To destination ID {id} was not found.");
            }

            var displayName =
                (request.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(displayName))
            {
                return BadRequest(
                    "Refer To destination name is required.");
            }

            if (displayName.Length > 100)
            {
                return BadRequest(
                    "Refer To destination name is limited to 100 characters.");
            }

            var duplicateExists =
                await _db.ReferToOptions.AnyAsync(
                    x =>
                        x.Id != id &&
                        x.DisplayName == displayName);

            if (duplicateExists)
            {
                return Conflict(
                    $"A Refer To destination named \"{displayName}\" already exists.");
            }

            entity.DisplayName = displayName;
            entity.IsActive = request.IsActive;
            entity.SortOrder = request.SortOrder;
            entity.UpdatedAt = DateTime.UtcNow;

            await _db.SaveChangesAsync();

            return Ok(ToReferToOptionDto(entity));
        }

        [HttpDelete("refer-to-options/{id:int}")]
        public async Task<IActionResult> DeleteReferToOption(
            uint id)
        {
            var entity =
                await _db.ReferToOptions
                    .FirstOrDefaultAsync(x => x.Id == id);

            if (entity == null)
            {
                return NotFound(
                    $"Refer To destination ID {id} was not found.");
            }

            _db.ReferToOptions.Remove(entity);
            await _db.SaveChangesAsync();

            return NoContent();
        }

        // -------------------------
        // Dispatch Closeout Checklist Definitions
        // -------------------------

        [HttpGet("dispatch-closeout-checklist-definitions")]
        public async Task<ActionResult<List<DispatchCloseoutChecklistDefinitionDto>>>
            GetDispatchCloseoutChecklistDefinitions(
                [FromQuery] bool activeOnly = false)
        {
            var query =
                _db.DispatchCloseoutChecklistDefinitions
                    .AsNoTracking()
                    .Include(x => x.WriteUpFlag)
                    .Include(x => x.ReferToOption)
                    .AsQueryable();

            if (activeOnly)
            {
                query = query.Where(x => x.IsActive);
            }

            var definitions =
                await query
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.DisplayName)
                    .ToListAsync();

            return Ok(
                definitions
                    .Select(ToDispatchCloseoutChecklistDefinitionDto)
                    .ToList());
        }

        [HttpPost("dispatch-closeout-checklist-definitions")]
        public async Task<ActionResult<DispatchCloseoutChecklistDefinitionDto>>
            CreateDispatchCloseoutChecklistDefinition(
                [FromBody]
        SaveDispatchCloseoutChecklistDefinitionRequest request)
        {
            var validationResult =
                await ValidateDispatchCloseoutChecklistDefinitionAsync(
                    request);

            if (validationResult.ErrorResult is not null)
            {
                return validationResult.ErrorResult;
            }

            var duplicateExists =
                await _db.DispatchCloseoutChecklistDefinitions
                    .AnyAsync(
                        x =>
                            x.DisplayName ==
                            validationResult.DisplayName);

            if (duplicateExists)
            {
                return Conflict(
                    $"A Dispatch closeout checklist item named " +
                    $"\"{validationResult.DisplayName}\" already exists.");
            }

            var now = DateTime.UtcNow;

            var entity =
                new DispatchCloseoutChecklistDefinitionEntity
                {
                    DisplayName =
                        validationResult.DisplayName,

                    IsActive =
                        request.IsActive,

                    SortOrder =
                        request.SortOrder,

                    IsRequired =
                        request.IsRequired,

                    ConditionType =
                        validationResult.ConditionType,

                    WriteUpFlagId =
                        validationResult.WriteUpFlagId,

                    ReferToOptionId =
                        validationResult.ReferToOptionId,

                    CreatedAt =
                        now,

                    UpdatedAt =
                        now
                };

            _db.DispatchCloseoutChecklistDefinitions.Add(entity);

            await _db.SaveChangesAsync();

            if (entity.WriteUpFlagId.HasValue)
            {
                await _db.Entry(entity)
                    .Reference(x => x.WriteUpFlag)
                    .LoadAsync();
            }

            if (entity.ReferToOptionId.HasValue)
            {
                await _db.Entry(entity)
                    .Reference(x => x.ReferToOption)
                    .LoadAsync();
            }

            return Ok(ToDispatchCloseoutChecklistDefinitionDto(entity));
        }

        [HttpPut("dispatch-closeout-checklist-definitions/{id:int}")]
        public async Task<ActionResult<DispatchCloseoutChecklistDefinitionDto>>UpdateDispatchCloseoutChecklistDefinition(
                uint id, [FromBody]SaveDispatchCloseoutChecklistDefinitionRequest request)
        {
            var entity =
                await _db.DispatchCloseoutChecklistDefinitions
                    .Include(x => x.WriteUpFlag)
                    .Include(x => x.ReferToOption)
                    .FirstOrDefaultAsync(x => x.Id == id);

            if (entity is null)
            {
                return NotFound(
                    $"Dispatch closeout checklist definition ID {id} " +
                    "was not found.");
            }

            var validationResult =
                await ValidateDispatchCloseoutChecklistDefinitionAsync(
                    request);

            if (validationResult.ErrorResult is not null)
            {
                return validationResult.ErrorResult;
            }

            var duplicateExists =
                await _db.DispatchCloseoutChecklistDefinitions
                    .AnyAsync(
                        x =>
                            x.Id != id &&
                            x.DisplayName ==
                            validationResult.DisplayName);

            if (duplicateExists)
            {
                return Conflict(
                    $"A Dispatch closeout checklist item named " +
                    $"\"{validationResult.DisplayName}\" already exists.");
            }

            entity.DisplayName =
                validationResult.DisplayName;

            entity.IsActive =
                request.IsActive;

            entity.SortOrder =
                request.SortOrder;

            entity.IsRequired =
                request.IsRequired;

            entity.ConditionType =
                validationResult.ConditionType;

            entity.WriteUpFlagId =
                validationResult.WriteUpFlagId;

            entity.ReferToOptionId =
                validationResult.ReferToOptionId;

            entity.UpdatedAt =
                DateTime.UtcNow;

            await _db.SaveChangesAsync();

            if (entity.WriteUpFlagId.HasValue)
            {
                await _db.Entry(entity)
                    .Reference(x => x.WriteUpFlag)
                    .LoadAsync();
            }
            else
            {
                entity.WriteUpFlag = null;
            }

            if (entity.ReferToOptionId.HasValue)
            {
                await _db.Entry(entity)
                    .Reference(x => x.ReferToOption)
                    .LoadAsync();
            }
            else
            {
                entity.ReferToOption = null;
            }

            return Ok(ToDispatchCloseoutChecklistDefinitionDto(entity));
        }

        [HttpDelete("dispatch-closeout-checklist-definitions/{id:int}")]
        public async Task<IActionResult>
            DeleteDispatchCloseoutChecklistDefinition(
                uint id)
        {
            var entity =
                await _db.DispatchCloseoutChecklistDefinitions
                    .FirstOrDefaultAsync(x => x.Id == id);

            if (entity is null)
            {
                return NotFound(
                    $"Dispatch closeout checklist definition ID {id} " +
                    "was not found.");
            }

            _db.DispatchCloseoutChecklistDefinitions.Remove(entity);

            await _db.SaveChangesAsync();

            return NoContent();
        }

        private async Task<(
            string DisplayName,
            string ConditionType,
            uint? WriteUpFlagId,
            uint? ReferToOptionId,
            ActionResult? ErrorResult)>
            ValidateDispatchCloseoutChecklistDefinitionAsync(
                SaveDispatchCloseoutChecklistDefinitionRequest request)
        {
            var displayName =
                (request.DisplayName ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(displayName))
            {
                return (
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    BadRequest(
                        "Checklist item name is required."));
            }

            if (displayName.Length > 150)
            {
                return (
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    BadRequest(
                        "Checklist item name is limited to 150 characters."));
            }

            if (!DispatchCloseoutConditionTypes.IsValid(
                    request.ConditionType))
            {
                return (
                    string.Empty,
                    string.Empty,
                    null,
                    null,
                    BadRequest(
                        "Condition type must be Always, " +
                        "WriteUpFlag, or ReferToSelection."));
            }

            var conditionType =
                DispatchCloseoutConditionTypes.Normalize(
                    request.ConditionType);

            uint? writeUpFlagId = null;
            uint? referToOptionId = null;

            if (conditionType ==
                DispatchCloseoutConditionTypes.WriteUpFlag)
            {
                if (!request.WriteUpFlagId.HasValue)
                {
                    return (
                        string.Empty,
                        string.Empty,
                        null,
                        null,
                        BadRequest(
                            "A write-up flag must be selected " +
                            "for a WriteUpFlag condition."));
                }

                var flagExists =
                    await _db.WriteUpFlags.AnyAsync(
                        x => x.Id ==
                             request.WriteUpFlagId.Value);

                if (!flagExists)
                {
                    return (
                        string.Empty,
                        string.Empty,
                        null,
                        null,
                        BadRequest(
                            $"Write-up flag ID " +
                            $"{request.WriteUpFlagId.Value} " +
                            "was not found."));
                }

                writeUpFlagId =
                    request.WriteUpFlagId.Value;
            }
            else if (conditionType ==
                     DispatchCloseoutConditionTypes
                         .ReferToSelection)
            {
                if (!request.ReferToOptionId.HasValue)
                {
                    return (
                        string.Empty,
                        string.Empty,
                        null,
                        null,
                        BadRequest(
                            "A Refer To option must be selected " +
                            "for a ReferToSelection condition."));
                }

                var optionExists =
                    await _db.ReferToOptions.AnyAsync(
                        x => x.Id ==
                             request.ReferToOptionId.Value);

                if (!optionExists)
                {
                    return (
                        string.Empty,
                        string.Empty,
                        null,
                        null,
                        BadRequest(
                            $"Refer To option ID " +
                            $"{request.ReferToOptionId.Value} " +
                            "was not found."));
                }

                referToOptionId =
                    request.ReferToOptionId.Value;
            }

            return (
                displayName,
                conditionType,
                writeUpFlagId,
                referToOptionId,
                null);
        }
        private static DispatchCloseoutChecklistDefinitionDto
            ToDispatchCloseoutChecklistDefinitionDto(
                DispatchCloseoutChecklistDefinitionEntity entity)
        {
            return new DispatchCloseoutChecklistDefinitionDto
            {
                Id =
                    entity.Id,

                DisplayName =
                    entity.DisplayName,

                IsActive =
                    entity.IsActive,

                SortOrder =
                    entity.SortOrder,

                IsRequired =
                    entity.IsRequired,

                ConditionType =
                    entity.ConditionType,

                WriteUpFlagId =
                    entity.WriteUpFlagId,

                WriteUpFlagName =
                    entity.WriteUpFlag?.DisplayName,

                ReferToOptionId =
                    entity.ReferToOptionId,

                ReferToOptionName =
                    entity.ReferToOption?.DisplayName
            };
        }
        private static ReferToOptionDto ToReferToOptionDto(
            ReferToOptionEntity entity)
        {
            return new ReferToOptionDto
            {
                Id = entity.Id,
                DisplayName = entity.DisplayName,
                IsActive = entity.IsActive,
                SortOrder = entity.SortOrder
            };
        }

        private static WriteUpFlagDto ToDto(WriteUpFlagEntity entity)
        {
            return new WriteUpFlagDto
            {
                Id = entity.Id,
                DisplayName = entity.DisplayName,
                IsActive = entity.IsActive,
                SortOrder = entity.SortOrder,
                IsTechnicianVisible =
                    entity.IsTechnicianVisible,
                IsSystem = entity.IsSystem,
                SystemKey = entity.SystemKey ?? ""
            };
        }
    }
}