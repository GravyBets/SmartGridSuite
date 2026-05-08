using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SmartGridSuite.Api.Data;
using SmartGridSuite.Api.Data.Entities;
using SmartGridSuite.Api.Services;
using SmartGridSuite.Contracts.Snmp;

namespace SmartGridSuite.Api.Controllers
{
    [ApiController]
    [Route("api/snmp-profiles")]
    public sealed class SnmpProfilesController : ControllerBase
    {
        private readonly SmartGridDbContext _db;
        private readonly SnmpPollingService _snmpPolling;

        public SnmpProfilesController(SmartGridDbContext db, SnmpPollingService snmpPolling)
        {
            _db = db;
            _snmpPolling = snmpPolling;
        }

        [HttpGet]
        public async Task<ActionResult<List<SnmpProfileListItemDto>>> GetAll(CancellationToken ct)
        {
            var rows = await _db.SnmpProfiles
                .AsNoTracking()
                .Where(x => !x.IsDeleted)
                .OrderBy(x => x.DeviceFamily)
                .ThenBy(x => x.Name)
                .Select(x => new SnmpProfileListItemDto
                {
                    Id = x.Id,
                    Name = x.Name,
                    DeviceFamily = x.DeviceFamily,
                    IsActive = x.IsActive,
                    OidCount = x.Oids.Count(o => !o.IsDeleted),
                    SnmpVersion = x.SnmpVersion
                })
                .ToListAsync(ct);

            return Ok(rows);
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<SnmpProfileDetailDto>> GetById(ulong id, CancellationToken ct)
        {
            var entity = await _db.SnmpProfiles
                .AsNoTracking()
                .Include(x => x.Oids)
                    .ThenInclude(x => x.DecodeValues)
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

            if (entity is null)
                return NotFound();

            return Ok(MapDetail(entity));
        }

        [HttpPost("save")]
        public async Task<ActionResult<SnmpProfileDetailDto>> Save([FromBody] UpsertSnmpProfileRequest req, CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(req.Name))
                return BadRequest("Profile name is required.");

            if (string.IsNullOrWhiteSpace(req.DeviceFamily))
                return BadRequest("Device family is required.");

            if (req.TimeoutMs <= 0)
                req.TimeoutMs = 1500;

            if (req.Retries < 0)
                req.Retries = 0;

            var normalizedFamily = req.DeviceFamily.Trim().ToUpperInvariant();

            SnmpProfileEntity entity;

            if (req.Id.HasValue && req.Id.Value > 0)
            {
                entity = await _db.SnmpProfiles
                    .Include(x => x.Oids)
                        .ThenInclude(x => x.DecodeValues)
                    .FirstOrDefaultAsync(x => x.Id == req.Id.Value && !x.IsDeleted, ct)
                    ?? throw new InvalidOperationException("SNMP profile not found.");
            }
            else
            {
                entity = new SnmpProfileEntity
                {
                    UpdatedAt = DateTime.Now,
                    Oids = new List<SnmpOidEntity>()
                };

                _db.SnmpProfiles.Add(entity);
            }

            entity.Name = req.Name.Trim();
            entity.DeviceFamily = normalizedFamily;
            entity.IsActive = req.IsActive;

            entity.ReadCommunity = Clean(req.ReadCommunity);
            entity.WriteCommunity = Clean(req.WriteCommunity);
            entity.ContextName = Clean(req.ContextName);

            entity.UsmUser = Clean(req.UsmUser);
            entity.AuthProtocol = Clean(req.AuthProtocol);
            entity.AuthKey = Clean(req.AuthKey);
            entity.PrivacyProtocol = Clean(req.PrivacyProtocol);
            entity.PrivacyKey = Clean(req.PrivacyKey);

            entity.TimeoutMs = req.TimeoutMs;
            entity.Retries = req.Retries;
            entity.UpdatedAt = DateTime.Now;

            entity.SnmpVersion = string.IsNullOrWhiteSpace(req.SnmpVersion)
                ? "v3" : req.SnmpVersion.Trim().ToLowerInvariant();
                        

            var incomingOidIds = req.Oids
                .Where(x => x.Id.HasValue && x.Id.Value > 0)
                .Select(x => x.Id!.Value)
                .ToHashSet();

            foreach (var existingOid in entity.Oids)
            {
                if (!incomingOidIds.Contains(existingOid.Id))
                {
                    existingOid.IsDeleted = true;
                    existingOid.UpdatedAt = DateTime.Now;

                    foreach (var decode in existingOid.DecodeValues)
                    {
                        decode.IsDeleted = true;
                        decode.UpdatedAt = DateTime.Now;
                    }
                }
            }

            foreach (var oidReq in req.Oids)
            {
                if (string.IsNullOrWhiteSpace(oidReq.Label) || string.IsNullOrWhiteSpace(oidReq.Oid))
                    continue;

                SnmpOidEntity oidEntity;

                if (oidReq.Id.HasValue && oidReq.Id.Value > 0)
                {
                    oidEntity = entity.Oids.FirstOrDefault(x => x.Id == oidReq.Id.Value)
                        ?? throw new InvalidOperationException("SNMP OID not found.");
                }
                else
                {
                    oidEntity = new SnmpOidEntity
                    {
                        UpdatedAt = DateTime.Now,
                        DecodeValues = new List<SnmpOidDecodeValueEntity>()
                    };

                    entity.Oids.Add(oidEntity);
                }

                oidEntity.Category = string.IsNullOrWhiteSpace(oidReq.Category)
                    ? "General"
                    : oidReq.Category.Trim();

                oidEntity.Label = oidReq.Label.Trim();
                oidEntity.Oid = oidReq.Oid.Trim();
                oidEntity.ValueType = string.IsNullOrWhiteSpace(oidReq.ValueType)
                    ? "String"
                    : oidReq.ValueType.Trim();

                oidEntity.IsWritable = oidReq.IsWritable;
                oidEntity.ShowInWorkspace = oidReq.ShowInWorkspace;
                oidEntity.SortOrder = oidReq.SortOrder;
                oidEntity.DecodeMode = string.IsNullOrWhiteSpace(oidReq.DecodeMode)
                    ? "Raw"
                    : oidReq.DecodeMode.Trim();
                oidEntity.ShowRawValueAlongsideDecoded = oidReq.ShowRawValueAlongsideDecoded;

                oidEntity.IsDeleted = false;
                oidEntity.UpdatedAt = DateTime.Now;

                var incomingDecodeIds = oidReq.DecodeValues
                    .Where(x => x.Id.HasValue && x.Id.Value > 0)
                    .Select(x => x.Id!.Value)
                    .ToHashSet();

                foreach (var existingDecode in oidEntity.DecodeValues)
                {
                    if (!incomingDecodeIds.Contains(existingDecode.Id))
                    {
                        existingDecode.IsDeleted = true;
                        existingDecode.UpdatedAt = DateTime.Now;
                    }
                }

                foreach (var decodeReq in oidReq.DecodeValues)
                {
                    if (string.IsNullOrWhiteSpace(decodeReq.RawValue) ||
                        string.IsNullOrWhiteSpace(decodeReq.DisplayText))
                        continue;

                    SnmpOidDecodeValueEntity decodeEntity;

                    if (decodeReq.Id.HasValue && decodeReq.Id.Value > 0)
                    {
                        decodeEntity = oidEntity.DecodeValues.FirstOrDefault(x => x.Id == decodeReq.Id.Value)
                            ?? throw new InvalidOperationException("SNMP OID decode value not found.");
                    }
                    else
                    {
                        decodeEntity = new SnmpOidDecodeValueEntity
                        {
                            UpdatedAt = DateTime.Now
                        };

                        oidEntity.DecodeValues.Add(decodeEntity);
                    }

                    decodeEntity.RawValue = decodeReq.RawValue.Trim();
                    decodeEntity.DisplayText = decodeReq.DisplayText.Trim();
                    decodeEntity.SortOrder = decodeReq.SortOrder;
                    decodeEntity.IsDeleted = false;
                    decodeEntity.UpdatedAt = DateTime.Now;
                }
            }

            await _db.SaveChangesAsync(ct);

            var saved = await _db.SnmpProfiles
                .AsNoTracking()
                .Include(x => x.Oids)
                    .ThenInclude(x => x.DecodeValues)
                .FirstAsync(x => x.Id == entity.Id && !x.IsDeleted, ct);

            return Ok(MapDetail(saved));
        }

        [HttpPost("{id}/deactivate")]
        public async Task<ActionResult> Deactivate(ulong id, CancellationToken ct)
        {
            var entity = await _db.SnmpProfiles
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

            if (entity is null)
                return NotFound();

            entity.IsActive = false;
            entity.UpdatedAt = DateTime.Now;

            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                Success = true
            });
        }

        [HttpPost("run-selected")]
        public async Task<ActionResult<SnmpRunResultDto>> RunSelected([FromBody] SnmpRunSelectedRequestDto req, CancellationToken ct)
        {
            var result = await _snmpPolling.RunSelectedAsync(req, ct);
            return Ok(result);
        }

        [HttpPost("set-selected")]
        public async Task<ActionResult<SnmpSetResultDto>> SetSelected([FromBody] SnmpSetSelectedRequestDto req, CancellationToken ct)
        {
            var result = await _snmpPolling.SetSelectedAsync(req, ct);
            return Ok(result);
        }

        [HttpPost("{id}/delete")]
        public async Task<ActionResult> DeleteProfile(ulong id, CancellationToken ct)
        {
            var entity = await _db.SnmpProfiles
                .Include(x => x.Oids)
                    .ThenInclude(x => x.DecodeValues)
                .FirstOrDefaultAsync(x => x.Id == id && !x.IsDeleted, ct);

            if (entity is null)
                return NotFound();

            entity.IsDeleted = true;
            entity.IsActive = false;
            entity.UpdatedAt = DateTime.Now;

            foreach (var oid in entity.Oids)
            {
                oid.IsDeleted = true;
                oid.UpdatedAt = DateTime.Now;

                foreach (var decode in oid.DecodeValues)
                {
                    decode.IsDeleted = true;
                    decode.UpdatedAt = DateTime.Now;
                }
            }

            await _db.SaveChangesAsync(ct);

            return Ok(new
            {
                Success = true
            });
        }


        //Helpers
        private static SnmpProfileDetailDto MapDetail(SnmpProfileEntity entity)
        {
            return new SnmpProfileDetailDto
            {
                Id = entity.Id,
                Name = entity.Name,
                DeviceFamily = entity.DeviceFamily,
                IsActive = entity.IsActive,

                ReadCommunity = entity.ReadCommunity,
                WriteCommunity = entity.WriteCommunity,
                ContextName = entity.ContextName,

                UsmUser = entity.UsmUser,
                AuthProtocol = entity.AuthProtocol,
                AuthKey = entity.AuthKey,
                PrivacyProtocol = entity.PrivacyProtocol,
                PrivacyKey = entity.PrivacyKey,

                TimeoutMs = entity.TimeoutMs,
                Retries = entity.Retries,
                SnmpVersion = entity.SnmpVersion,

                Oids = entity.Oids
                    .Where(x => !x.IsDeleted)
                    .OrderBy(x => x.SortOrder)
                    .ThenBy(x => x.Label)
                    .Select(x => new SnmpOidConfigDto
                    {
                        Id = x.Id,
                        Category = x.Category,
                        Label = x.Label,
                        Oid = x.Oid,
                        ValueType = x.ValueType,
                        IsWritable = x.IsWritable,
                        ShowInWorkspace = x.ShowInWorkspace,
                        SortOrder = x.SortOrder,
                        DecodeMode = x.DecodeMode,
                        ShowRawValueAlongsideDecoded = x.ShowRawValueAlongsideDecoded,
                        DecodeValues = x.DecodeValues
                            .Where(d => !d.IsDeleted)
                            .OrderBy(d => d.SortOrder)
                            .ThenBy(d => d.RawValue)
                            .Select(d => new SnmpOidDecodeValueDto
                            {
                                Id = d.Id,
                                RawValue = d.RawValue,
                                DisplayText = d.DisplayText,
                                SortOrder = d.SortOrder
                            })
                            .ToList()
                    })
                .ToList()
            };
        }

        private static string? Clean(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }        

        
    }
}