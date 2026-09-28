using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using NoSQLConnector;
using OracleDbConnector.DllVersion;
using TokenValidation;

namespace TokenAPI.Controllers
{
    [Route("dll-version")]
    [ApiController]
    public class DllVersionController : ControllerBase
    {
        public DllVersionController(){}

        [HttpGet("")]
        public object DllVersion()
        {
            string oracleVersion = DllVersionChecker.GetDllVersion();
            string tokenVersion = TokenDllVersionChecker.GetDllVersion();
            string noSqlVersion = NoSqlDllVersionChecker.GetDllVersion();
            Console.WriteLine($"OracleDbConnector DLL Version: {oracleVersion}");
            Console.WriteLine($"NoSQLDbConnector DLL Version: {noSqlVersion}");
            Console.WriteLine($"TokenValidation DLL Version: {tokenVersion}");
            return new
            {
                OracleDbConnector = oracleVersion,
                NoSqlDbConnector = noSqlVersion,
                TokenValidation = tokenVersion
            };
        }
    }
}
