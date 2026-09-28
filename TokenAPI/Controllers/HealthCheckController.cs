using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace TokenAPI.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class HealthCheckController : ControllerBase
    {
        public HealthCheckController(){}
        [HttpGet("")]
        public string HealthCheck()
        {
            return "OK";
        }
    }
}
