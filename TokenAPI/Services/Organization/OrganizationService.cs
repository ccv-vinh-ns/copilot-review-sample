using NoSQLConnector;
using Oracle.NoSQL.SDK;
using TokenAPI.Common.Enums;
using TokenAPI.Common.Extensions;
using TokenAPI.Common.Utils;
using TokenAPI.Models.Entities;
using TokenAPI.Models.Requests;
using TokenAPI.Models.Responses;

namespace TokenAPI.Services.Organization
{
    public class OrganizationService : IOrganizationService
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContext;
        private readonly TokenUtils _tokenUtils;
        public OrganizationService(IConfiguration configuration, IHttpContextAccessor httpContext, TokenUtils tokenUtils) 
        {
            _configuration = configuration;
            _httpContext = httpContext;
            _tokenUtils = tokenUtils;
        }

        public async Task<GenerateTokenResponses> GenerateToken(GenerateTokenRequest request)
        {
            ValidateTokenRequest(request);
            GenerateTokenResponses result = new GenerateTokenResponses();
            using INoSQLConnectorManager manager = new NoSQLConnectorManager();
            var noSqlConfig = _tokenUtils.GetNoSqlConfig();
            var connect = manager.Connect(noSqlConfig);
            if (connect.Success)
            {
                var primaryKey = new MapValue { ["CUSTID"] = $"{request.CustId}" };
                var customerTable = _configuration["CustomerTable"];

                var custInfo = await manager.GetByPrimaryKeyAsync<CustomerInfo>(customerTable, primaryKey);
                if (custInfo.Data?.CUSTPASS != null && custInfo.Data.CUSTPASS == request.CustPass)
                {
                    var newTokenId = Guid.NewGuid().ToString();
                    // Generate Access Token
                    var accessToken = await _tokenUtils.GenerateOrgAccessToken(newTokenId, request.CustId);
                    // Generate Refresh Token
                    var refreshToken = await _tokenUtils.GenerateOrgRefreshToken(newTokenId, request.CustId);

                    if (accessToken != null && refreshToken != null) 
                    {
                        var tokenTable = _configuration["TokenTable"];
                        var tokenItem = new Token 
                        { 
                            TOKENID = newTokenId,
                            TYPE = "ORG",
                            ACCESSTOKEN = accessToken,
                            CUSTID = request.CustId,
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
                        //message = "Your CustId not exist or your CustPass not correct. Please recheck and try again."
                        message = "「CustId」が存在しないかCustPassが無効です。再確認してください。"
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
                    message = "「NoSQL」への接続に失敗しました。"
                }
            }); 
        }
        public async Task<string> RefreshToken(string refreshToken)
        {
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
                var tokenId = principal.FindFirst("tokenId")?.Value ?? "null";
                var custId = principal.FindFirst("custId")?.Value ?? "null";
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
                        var unixTime = _tokenUtils.GetValueFromToken(refreshToken, "exp") ?? "";
                        var refreshExp = _tokenUtils.ConvertUnixToDateTimeUtc(unixTime);
                        if (!_tokenUtils.CheckExpireRefreshToken(refreshExp))
                        {
                            var newTokenId = Guid.NewGuid().ToString();
                            // Generate new AccessToken
                            newAccessToken = await _tokenUtils.GenerateOrgAccessToken(newTokenId, custId);
                            // Save new AccessToken to NoSQL
                            var tokenItem = new Token
                            {
                                TOKENID = newTokenId,
                                TYPE = "ORG",
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
                    message = "「NoSQL」への接続に失敗しました。"
                }
            });
        }
        private void ValidateTokenRequest(GenerateTokenRequest request)
        {
            if (string.IsNullOrEmpty(request.CustId)) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    // message = "CustId is not empty"
                    message = "「CustId」は必須項目です。"
                }
            });
            if (string.IsNullOrEmpty(request.CustPass)) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = "CustPass is not empty"
                    message = "「CustPass」は必須項目です。"
                }
            });
        }
    }
}
