using SmartGridSuite.Contracts.SiteNotes;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SmartGridSuite.Client.Services
{
    public sealed class SiteNotesApi
    {
        private readonly ApiClient _api;

        public SiteNotesApi(ApiClient api)
        {
            _api = api;
        }

        public async Task<List<SiteNoteDto>> GetBySiteAsync(string siteId, CancellationToken ct = default)
        {
            siteId = (siteId ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(siteId))
                return new();

            return await _api.GetAsync<List<SiteNoteDto>>(
                       $"api/site-notes/{Uri.EscapeDataString(siteId)}",
                       ct)
                   ?? new();
        }

        public async Task<SiteNoteDto?> CreateAsync(CreateSiteNoteRequest request, CancellationToken ct = default)
        {
            return await _api.PostAsync<CreateSiteNoteRequest, SiteNoteDto>(
                "api/site-notes",
                request,
                ct);
        }

        public async Task<SiteNoteDto?> UpdateAsync(UpdateSiteNoteRequest request, CancellationToken ct = default)
        {
            return await _api.PutAsync<UpdateSiteNoteRequest, SiteNoteDto>(
                $"api/site-notes/{request.Id}",
                request,
                ct);
        }

        public async Task DeleteAsync(ulong id, string deletedBy, CancellationToken ct = default)
        {
            await _api.PostAsync<DeleteSiteNoteRequest, object?>(
                $"api/site-notes/{id}/delete",
                new DeleteSiteNoteRequest
                {
                    DeletedBy = deletedBy ?? "Unknown"
                },
                ct);
        }
    }
}