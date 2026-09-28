using TokenAPI.Models.Requests;
using TokenAPI.Models.Responses;

namespace TokenAPI.Services.Organization
{
    public interface IOrganizationService
    {
        Task<GenerateTokenResponses> GenerateToken(GenerateTokenRequest request);
        Task<string> RefreshToken(string refreshToken);
    }
}
