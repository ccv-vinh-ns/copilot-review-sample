using Newtonsoft.Json.Linq;
using NoSQLConnector;
using Oracle.NoSQL.SDK;
using OracleDbConnector.Common.Models;
using OracleDbConnector.CustomerCC;
using System.Text.Json;
using TokenAPI.Common.Enums;
using TokenAPI.Common.Extensions;
using TokenAPI.Common.Utils;
using TokenAPI.Models;
using TokenAPI.Models.Entities;
using TokenAPI.Models.Requests;
using TokenAPI.Models.Responses;
using TokenValidation;

namespace TokenAPI.Services.Staff
{
    public class LoginService : ILoginService
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContext;
        private readonly IValidationAccessToken _validationAccessToken;
        private readonly IOracleConnectionManager _oracle;
        private readonly TokenUtils _tokenUtils;
        private readonly ILogger<INoSQLConnectorManager> _logger;
        public LoginService(IConfiguration configuration, IHttpContextAccessor httpContext, 
            IValidationAccessToken validationAccessToken, IOracleConnectionManager oracle, TokenUtils tokenUtils, ILogger<INoSQLConnectorManager> logger)
        {
            _configuration = configuration;
            _httpContext = httpContext;
            _tokenUtils = tokenUtils;
            _validationAccessToken = validationAccessToken;
            _oracle = oracle;
            _logger = logger;
        }

        public async Task<GenerateTokenResponses> GenerateToken(GenerateStaffTokenRequest request)
        {
            var env = _configuration["Environment"];
            _logger.LogInformation($"Start using environment: {env}");
            ValidateTokenRequest(request);
            GenerateTokenResponses result = new GenerateTokenResponses();
            var (custId, custPass) = await ValidateHeaderToken();
            using INoSQLConnectorManager manager = new NoSQLConnectorManager();
            var noSqlConfig = _tokenUtils.GetNoSqlConfig();
            var connect = manager.Connect(noSqlConfig);
            if (connect.Success)
            {
                var primaryKey = new MapValue { ["CUSTID"] = $"{custId}" };
                var customerTable = _configuration["CustomerTable"];

                var custInfo = await manager.GetByPrimaryKeyAsync<CustomerInfo>(customerTable, primaryKey);
                if (custInfo.Data?.CUSTPASS != null && custInfo.Data.CUSTPASS == custPass)
                {
                    await _oracle.OpenAsync(custId, custPass);
                    if (await CheckStaffLoginCd(request.StfCd, request.LoginObjCd))
                    {
                        var newTokenId = Guid.NewGuid().ToString();
                        // Generate Access Token
                        var accessToken = await _tokenUtils.GenerateStfAccessToken(newTokenId, custId, request.StfCd, request.LoginObjCd);
                        // Generate Refresh Token
                        var refreshToken = await _tokenUtils.GenerateStfRefreshToken(newTokenId, custId, request.StfCd, request.LoginObjCd);

                        if (accessToken != null && refreshToken != null)
                        {
                            var tokenTable = _configuration["TokenTable"];
                            var tokenItem = new Token
                            {
                                TOKENID = newTokenId,
                                TYPE = "STF",
                                ACCESSTOKEN = accessToken,
                                CUSTID = custId,
                                REFRESHTOKEN = refreshToken,
                                STATUS = "ACTIVE",
                                IAT = _tokenUtils.issuedTime,
                                EXP = _tokenUtils.accessExpiredTime,
                                REFRESHEXP = _tokenUtils.refreshExpiredTime
                            };
                            var putResult = await manager.PutAsync(tokenTable, tokenItem);
                            if (putResult.Success)
                            {
                                result.AccessToken = accessToken;
                                result.RefreshToken = refreshToken;
                            }
                            return result;
                        }
                    }
                    throw new CustomUnauthorizedException(new
                    {
                        status = 401,
                        timestamp = DateTime.UtcNow.ToString(),
                        path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                        error = new
                        {
                            code = "UNAUTHORIZED_EXCEPTION",
                            //message = "Your StfCd or LoginObjCd not exist or not correct. Please recheck and try again."
                            message = "「StfCd」 または 「LoginObjCd」 が無効です。再確認してください。"
                        }
                    });
                }
                throw new CustomUnauthorizedException(new
                {
                    status = 401,
                    timestamp = DateTime.UtcNow.ToString(),
                    path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                    error = new
                    {
                        code = "UNAUTHORIZED_EXCEPTION",
                        // message = "Your CustId not exist or your CustPass not correct. Please recheck and try again."
                        message = "「CustId」 が存在しないか「CustPass」が無効です。再確認してください。"
                    }
                });
            }
            throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    // message = "The connection to NoSQL is not successfull."
                    message = "「NoSQL」 への接続に失敗しました。"
                }
            });
        }
        public async Task<GetStaffResponses> GetStaff(GetStaffRequest request)
        {
            var env = _configuration["Environment"];
            _logger.LogInformation($"Start using environment: {env}");
            ValidateGetStaffRequest(request);
            var (custId, custPass) = await ValidateHeaderToken();
            GetStaffResponses result = new GetStaffResponses();

            await _oracle.OpenAsync(custId, custPass);

            var staff = await GetStaffInfo(request.UserId, request.Password);
            if (staff.isValidStf)
            {
                var staffBelong = await GetStaffBelong(staff.STFCD);
                result.StaffId = staff.STFCD;
                result.Family = staff.FAMILY;
                result.Given = staff.GIVEN;
                result.KanaFamily = staff.KANAFAMILY;
                result.KanaGiven = staff.KANAGIVEN;
                result.Birth = staff.BIRTH.ToString("yyyy/MM/dd");
                result.Sex = staff.SEX;
                result.LoginId = staff.USERID;

                foreach (var item in staffBelong)
                {
                    result.Belong.Add(new StfBelong
                    {
                        CorpCd = item.CORPCD,
                        OfficeCd = item.OFFICECD,
                        BlockCd = item.BLOCKCD,
                        SubCd = item.SUBCD,
                        UnitCd = item.UNITCD,
                        ObjNm = await GetObjNm(item)
                    });
                }

                return result;
            }
            throw new CustomUnauthorizedException(new
            {
                status = 401,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "UNAUTHORIZED_EXCEPTION",
                    //message = "Your StaffId not exist or not correct. Please recheck and try again."
                    message = "「StaffId」が無効です。再確認してください。"
                }
            });
        }
        public async Task<string> RefreshToken(string refreshToken)
        {
            var env = _configuration["Environment"];
            _logger.LogInformation($"Start using environment: {env}");
            var accessToken = await _tokenUtils.ValidateAccessAndRefreshToken(refreshToken);
            if (string.IsNullOrEmpty(accessToken)) throw new CustomUnauthorizedException(new
            {
                status = 401,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "UNAUTHORIZED_EXCEPTION",
                    //message = "Your Access Token is not valid. Please recheck and try again."
                    message = "「AcessToken」が無効です。再確認してください。"
                }
            });
            string newAccessToken = string.Empty;
            using INoSQLConnectorManager manager = new NoSQLConnectorManager();
            var noSqlConfig = _tokenUtils.GetNoSqlConfig();
            var connect = manager.Connect(noSqlConfig);
            if (connect.Success)
            {
                var principal = await _tokenUtils.ValidateToken(refreshToken, TokenType.RefreshToken);
                var tokenId = principal.FindFirst("tokenId")?.Value;
                var custId = principal.FindFirst("custId")?.Value ?? "null";
                var stfCd = principal.FindFirst("stfCd")?.Value ?? "null";
                var loginObjCd = principal.FindFirst("loginObjCd")?.Value ?? "null";
                if (!string.IsNullOrEmpty(tokenId))
                {
                    var tokenTable = _configuration["TokenTable"];
                    var queryResult = await manager.QueryAsync<Token>(
                        $"SELECT * FROM {tokenTable} WHERE TOKENID='{tokenId}' AND CUSTID='{custId}' AND STATUS='ACTIVE'"
                        );
                    // Check AccessToken & RefreshToken exist on NoSQL
                    if (queryResult.Data.Count > 0)
                    {
                        // Check refreshToken expired or not
                        var unixTime = _tokenUtils.GetValueFromToken(refreshToken, "exp") ?? "null";
                        var refreshExp = _tokenUtils.ConvertUnixToDateTimeUtc(unixTime);
                        if (!_tokenUtils.CheckExpireRefreshToken(refreshExp))
                        {
                            var newTokenId = Guid.NewGuid().ToString();
                            // Generate new AccessToken
                            newAccessToken = await _tokenUtils.GenerateStfAccessToken(newTokenId, custId, stfCd, loginObjCd);
                            // Save new AccessToken to NoSQL
                            var tokenItem = new Token
                            {
                                TOKENID = newTokenId,
                                TYPE = "STF",
                                ACCESSTOKEN = newAccessToken,
                                CUSTID = custId,
                                REFRESHTOKEN = refreshToken,
                                STATUS = "ACTIVE",
                                IAT = _tokenUtils.issuedTime,
                                EXP = _tokenUtils.accessExpiredTime,
                                REFRESHEXP = refreshExp
                            };
                            var putResult = await manager.PutAsync(tokenTable, tokenItem);

                            if (!string.IsNullOrEmpty(newAccessToken) && putResult.Success)
                            {
                                // UNACTIVE old AccessTokens by RefreshToken provided
                                List<Token> tokens = new List<Token>();
                                tokens = queryResult.Data;
                                foreach (var token in tokens)
                                {
                                    token.STATUS = "UNACTIVE";
                                }
                                var putManyResult = await manager.PutManyAsync(tokenTable, tokens);
                                if (putManyResult.Success) return newAccessToken;
                            }
                            throw new CustomBadRequestException(new
                            {
                                status = 400,
                                timestamp = DateTime.UtcNow.ToString(),
                                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                                error = new
                                {
                                    code = "BAD_REQUEST_EXCEPTION",
                                    //message = "Error on generating new AccessToken. Please recheck and try again."
                                    message = "「AcessToken」の生成に失敗しました。再確認してください。"
                                }
                            });
                        }
                    }
                    throw new CustomUnauthorizedException(new
                    {
                        status = 401,
                        timestamp = DateTime.UtcNow.ToString(),
                        path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                        error = new
                        {
                            code = "UNAUTHORIZED_EXCEPTION",
                            //message = "Your AccessToken/RefreshToken not exist or not correct. Please recheck and try again."
                            message = "「AccessToken/RefreshToken」が無効です。再確認してください。"
                        }
                    });
                }
                throw new CustomUnauthorizedException(new
                {
                    status = 401,
                    timestamp = DateTime.UtcNow.ToString(),
                    path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                    error = new
                    {
                        code = "UNAUTHORIZED_EXCEPTION",
                        //message = "Your Token is not valid. Please recheck and try again."
                        message = "「Token」が無効です。再確認してください。"
                    }
                });
            }
            throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = "The connection to NoSQL is not successfull."
                    message = "「NoSQL」 への接続に失敗しました。"
                }
            });
        }
        private void ValidateGetStaffRequest(GetStaffRequest request)
        {
            if (string.IsNullOrEmpty(request.UserId)) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = "UserId is not empty"
                    message = "「UserId」 は必須項目です。"
                }
            });
            if (string.IsNullOrEmpty(request.Password)) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = "Password is not empty"
                    message = "「Password」は必須項目です。"
                }
            }); 
        }       
        private async Task<(string, string)> ValidateHeaderToken()
        {
            if (!_httpContext.HttpContext.Request.Headers.TryGetValue("Authorization", out var accessToken))
            {
                throw new CustomUnauthorizedException(new
                {
                    status = 401,
                    timestamp = DateTime.UtcNow.ToString(),
                    path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                    error = new
                    {
                        code = "UNAUTHORIZED_EXCEPTION",
                        //message = "Authorization Header required"
                        message = "「Authorization」 ヘッダーが必要です。"
                    }
                });
            }

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new CustomBadRequestException(new
                {
                    status = 401,
                    timestamp = DateTime.UtcNow.ToString(),
                    method = _httpContext.HttpContext?.Request.Method,
                    path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                    error = new
                    {
                        code = "UNAUTHORIZED_EXCEPTION",
                        //message = "AccessToken is not empty"
                        message = "「AccessToken」は必須項目です。"
                    }
                });
            }

            accessToken = accessToken.ToString().Replace("Bearer ", string.Empty).Trim();

            var tokenResult = await _validationAccessToken.ValidationOrganizationToken(accessToken.ToString(), TokenValidation.Models.TokenType.AccessToken);
            if (tokenResult.Success)
            {
                return (tokenResult.CustId, tokenResult.CustPass);
            }
            throw new CustomUnauthorizedException(new
            {
                status = 401,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "UNAUTHORIZED_EXCEPTION",
                    //message = $"Your Access Token is not valid. Message: {tokenResult.Message}"
                    message = $"「AcessToken」が無効です。 Message: {tokenResult.Message}"
                }
            });
        }
        private async Task<StaffInfo> GetStaffInfo(string userId, string password)
        {
            try
            {
                var parameters = new Dictionary<string, object> { { "USERID", userId }, { "PASSWORD", password } };
                string sql = "SELECT STFCD, FAMILY, GIVEN, KANAFAMILY, KANAGIVEN, BIRTH, SEX, USERID FROM STAFF WHERE USERID = :USERID AND PASSWORD = :PASSWORD";
                List<OracleDbConnector.Common.Models.SqlQueryRequest> query = new List<OracleDbConnector.Common.Models.SqlQueryRequest>();
                query.Add(new OracleDbConnector.Common.Models.SqlQueryRequest { Sql = sql, Parameters = parameters});
                var oracleResult = await _oracle.Select(query);
                Console.WriteLine($"GetStaffInfo oracleResult: {oracleResult}");
                var response = SqlObjectResponsesConverter.Parse<StaffInfo>(oracleResult);
                
                Console.WriteLine($"GetStaff response: {oracleResult}");
                if (response.Success)
                {
                    var staff = response.Data?.FirstOrDefault()?.QueryData?.FirstOrDefault();
                    Console.WriteLine($"Success on GetStaffInfo: {staff?.STFCD}");
                    return staff ?? new StaffInfo() { isValidStf = false };
                }
                else
                {
                    Console.WriteLine($"Error on GetStaffInfo: {response.Error}");
                    return new StaffInfo() { isValidStf = false };
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
           
        }
        private async Task<List<StaffBelong>> GetStaffBelong(string staffCd)
        {
            var parameters = new Dictionary<string, object> { { "STFCD", staffCd } };
            string sql = "SELECT CORPCD, OFFICECD, BLOCKCD, SUBCD, UNITCD FROM STFBELONG WHERE STFCD = :STFCD";
            List<OracleDbConnector.Common.Models.SqlQueryRequest> query = new List<OracleDbConnector.Common.Models.SqlQueryRequest>();
            query.Add(new OracleDbConnector.Common.Models.SqlQueryRequest { Sql = sql, Parameters = parameters });
            var oracleResult = await _oracle.Select(query);
            Console.WriteLine($"GetStaffBelong response: {oracleResult}");

            var response = SqlObjectResponsesConverter.Parse<StaffBelong>(oracleResult);
            if (response.Success)
            {
                var staffBelong = response.Data?.FirstOrDefault()?.QueryData?.ToList();
                Console.WriteLine($"Success on GetStaffBelong: {staffBelong?.Count}");
                return staffBelong == null ? new List<StaffBelong>() : staffBelong;
            }
            else 
            {
                Console.WriteLine($"Error on GetStaffBelong: {response.Error}");
                return new List<StaffBelong>();
            }
        }
        private void ValidateTokenRequest(GenerateStaffTokenRequest request)
        {
            if (string.IsNullOrEmpty(request.StfCd)) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = "StfCd is not empty"
                    message = "「StfCd」は必須項目です。"
                }
            });
            if (string.IsNullOrEmpty(request.LoginObjCd)) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = "LoginObjCd is not empty"
                    message = "「LoginObjCd」は必須項目です。"
                }
            });
        }
        private async Task<string> GetObjNm(StaffBelong staffBelong)
        {
            string sql = string.Empty;
            Dictionary<string, object> parameters = new Dictionary<string, object>();

            if (staffBelong.UNITCD != "000")
            {
                parameters = new Dictionary<string, object>{
                        { "UNITCD", staffBelong.UNITCD },
                        { "CORPCD", staffBelong.CORPCD },
                        { "OFFICECD", staffBelong.OFFICECD },
                        { "BLOCKCD", staffBelong.BLOCKCD }
                };
                sql = "SELECT UNITNM FROM UNIT WHERE UNITCD = :UNITCD AND CORPCD = :CORPCD AND OFFICECD = :OFFICECD AND BLOCKCD = :BLOCKCD";
            }
            else if (staffBelong.BLOCKCD.ToString() != "00")
            {
                parameters = new Dictionary<string, object>{
                        { "BLOCKCD", staffBelong.BLOCKCD },
                        { "CORPCD", staffBelong.CORPCD },
                        { "OFFICECD", staffBelong.OFFICECD }
                };
                sql = "SELECT BLOCKNM FROM BLOCK WHERE BLOCKCD = :BLOCKCD AND CORPCD = :CORPCD AND OFFICECD = :OFFICECD";
            }
            else if (staffBelong.OFFICECD.ToString() != "00")
            {
                parameters = new Dictionary<string, object>{
                        { "OFFICECD", staffBelong.OFFICECD }
                        ,{ "CORPCD", staffBelong.CORPCD }
                };
                sql = "SELECT OFFICENM FROM OFFICE WHERE OFFICECD = :OFFICECD AND CORPCD = :CORPCD";
            }
            else if (staffBelong.CORPCD.ToString() != "0")
            {
                parameters = new Dictionary<string, object>{
                        { "CORPCD", staffBelong.CORPCD }
                };
                sql = "SELECT CORPNM FROM CORP WHERE CORPCD = :CORPCD";
            }

            if (string.IsNullOrEmpty(sql))
            {
                throw new CustomBadRequestException(new
                {
                    status = 400,
                    timestamp = DateTime.UtcNow.ToString(),
                    path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                    error = new
                    {
                        code = "BAD_REQUEST_EXCEPTION",
                        //message = "No SQL was generated. Please check staffBelong values."
                        message = "「SQL」が生成されませんでした。「staffBelong」の値を確認してください。"
                    }
                });  
            }
            List<OracleDbConnector.Common.Models.SqlQueryRequest> query = new List<OracleDbConnector.Common.Models.SqlQueryRequest>();
            query.Add(new OracleDbConnector.Common.Models.SqlQueryRequest { Sql = sql, Parameters = parameters });
            var sqlResult = await _oracle.Select(query);
            Console.WriteLine($"GetObjNm response: {sqlResult}");
            var response = SqlObjectResponsesConverter.Parse<object>(sqlResult);
            if (response.Success)
            {
                var firstData = response.Data?.FirstOrDefault();
                if (firstData != null && firstData.QueryData?.Count() > 0)
                {
                    var element = (JsonElement)firstData.QueryData.First();
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        // Return the first property value as string
                        var firstProp = element.EnumerateObject().FirstOrDefault();
                        return firstProp.Value.GetString() ?? string.Empty;
                    }
                }
                return string.Empty;
            }
            else
            {
                Console.WriteLine($"Error on GetObjNm: {response.Error}");
                return string.Empty;
            }
        }
        private async Task<bool> CheckStaffLoginCd(string StaffCd, string LoginObjCd)
        {
            bool check = false;
            string oracleStaffCd = string.Empty;
            string CorpCd = _tokenUtils.GetTypeCd(LoginObjCd, BaseCdType.Corpcd);
            string OfficeCd = _tokenUtils.GetTypeCd(LoginObjCd, BaseCdType.Officecd);
            string BlockCd = _tokenUtils.GetTypeCd(LoginObjCd, BaseCdType.Blockcd);
            string SubCd = _tokenUtils.GetTypeCd(LoginObjCd, BaseCdType.Subcd);
            string UnitCd = _tokenUtils.GetTypeCd(LoginObjCd, BaseCdType.Unitcd);
            var parameters = new Dictionary<string, object> { 
                { "STFCD", StaffCd }, 
                { "CORPCD", CorpCd }, 
                { "OFFICECD", OfficeCd }, 
                { "BLOCKCD", BlockCd }, 
                { "SUBCD", SubCd }, 
                { "UNITCD", UnitCd }, 
            };
            string sql = "SELECT STFCD FROM STFBELONG WHERE STFCD = :STFCD AND CORPCD = :CORPCD AND OFFICECD = :OFFICECD AND BLOCKCD = :BLOCKCD AND SUBCD = :SUBCD AND UNITCD = :UNITCD";
            List<OracleDbConnector.Common.Models.SqlQueryRequest> query = new List<OracleDbConnector.Common.Models.SqlQueryRequest>();
            query.Add(new OracleDbConnector.Common.Models.SqlQueryRequest { Sql = sql, Parameters = parameters });
            var oracleResult = await _oracle.Select(query);
            var response = SqlObjectResponsesConverter.Parse<object>(oracleResult);
            if (response.Success)
            {
                var firstData = response.Data?.FirstOrDefault();
                if (firstData != null && firstData.QueryData?.Count() > 0)
                {
                    var element = (JsonElement)firstData.QueryData.First();
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        // Return the first property value as string
                        var firstProp = element.EnumerateObject().FirstOrDefault();
                        oracleStaffCd = firstProp.Value.GetString() ?? string.Empty;
                    }
                }
               
                if (StaffCd == oracleStaffCd)
                {
                    check = true;
                }
            }
            else
            {
                Console.WriteLine($"Error on CheckStaffLoginCd: {response.Error}");
                check = false;
            }
           
            return check;
        }
    }
}
