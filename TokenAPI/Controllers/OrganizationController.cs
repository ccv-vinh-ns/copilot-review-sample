using Microsoft.AspNetCore.Mvc;
using TokenAPI.Models.Requests;
using TokenAPI.Services.Organization;


namespace TokenAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class OrganizationController : ControllerBase
    {
        private readonly IOrganizationService _organizationService;
        public OrganizationController(IOrganizationService organizationService)
        {
            _organizationService = organizationService;
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateToken(GenerateTokenRequest request)
        {
            var result = await _organizationService.GenerateToken(request);
            return Created(string.Empty, result);
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshToken(RefreshTokenRequest request)
        {
            var result = await _organizationService.RefreshToken(request.RefreshToken);
            return Created(string.Empty, new { accessToken = result });
        }
    }
}
