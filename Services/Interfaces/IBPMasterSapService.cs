using JSAPNEW.Models;

namespace JSAPNEW.Services.Interfaces
{
    public interface IBPMasterSapService
    {
        Task<BpSapPostResult> PostBusinessPartnerAsync(BpSapPostRequest request, CancellationToken cancellationToken = default);
    }
}
