using TokenAPI.Models.Requests;
using TokenAPI.Models.Responses;

namespace TokenAPI.Services.Staff
{
    public interface ILoginService
    {
        Task<GetStaffResponses> GetStaff(GetStaffRequest request);
        Task<GenerateTokenResponses> GenerateToken(GenerateStaffTokenRequest request);
        Task<string> RefreshToken(string refreshToken);
    }
}
