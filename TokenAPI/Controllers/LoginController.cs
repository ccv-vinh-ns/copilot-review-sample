using Microsoft.AspNetCore.Mvc;
using TokenAPI.Models.Requests;
using TokenAPI.Services.Staff;

namespace TokenAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class LoginController : ControllerBase
    {
        private readonly ILoginService _loginService;
        public LoginController(ILoginService loginService) 
        { 
            _loginService = loginService;
        }

        [HttpPost("getStaff")]
        public async Task<IActionResult> GetStaffInfo(GetStaffRequest request)
        {
            var result = await _loginService.GetStaff(request);
            return Ok(result);
        }

        [HttpPost("generate")]
        public async Task<IActionResult> GenerateToken(GenerateStaffTokenRequest request)
        {
            var result = await _loginService.GenerateToken(request);
            return Created(string.Empty,result);
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshToken(RefreshTokenRequest request)
        {
            var result = await _loginService.RefreshToken(request.RefreshToken);
            return Created(string.Empty, new { accessToken = result });
        }
    }
}
