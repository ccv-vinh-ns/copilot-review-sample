using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using NoSQLConnector;
using NoSQLConnector.Common;
using Oci.Common.Auth;
using Oci.SecretsService;
using Oci.SecretsService.Models;
using Oci.SecretsService.Requests;
using Oci.SecretsService.Responses;
using Oracle.NoSQL.SDK;
using Org.BouncyCastle.Crypto.Prng;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection.Metadata.Ecma335;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using TokenAPI.Common.Enums;
using TokenAPI.Common.Extensions;
using TokenAPI.Models.Entities;

namespace TokenAPI.Common.Utils
{
    public class TokenUtils
    {
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContext;
        private readonly IMemoryCache _cache;
        public SymmetricSecurityKey? _key;
        private readonly SecretsClient _secretsClient;
        //private const string SecretKey = "ThisIsASecretKeyForJwtToken123!@CCJ@CCV";
        private DateTime defaultTime = new DateTime(1990, 01, 01);
        public DateTime issuedTime = new DateTime();
        public DateTime accessExpiredTime = new DateTime();
        public DateTime refreshExpiredTime = new DateTime();
        public TokenUtils(IConfiguration configuration, IHttpContextAccessor httpContext, IMemoryCache cache) 
        { 
            _configuration = configuration;
            _httpContext = httpContext;
            _cache = cache;
           
            // Instance Principals only work inside an OCI Compute instance, OKE pod,
            // or OCI resource that has been granted the right IAM policy
            // var provider = new InstancePrincipalsAuthenticationDetailsProvider();
            //_secretsClient = new SecretsClient(provider);
            // Console.WriteLine($"[Token API] Using Instance Principal to connect to Vault");


            // Instance Principals not work => fallback to ~/.oci/config, DEFAULT profile
            var provider = new ConfigFileAuthenticationDetailsProvider("DEFAULT");
            _secretsClient = new SecretsClient(provider);
            Console.WriteLine($"[Token API] Using Config File Authentication to connect to Vault");
        }
        public NoSqlConfig GetNoSqlConfig()
        {
            var serviceType = _configuration["NoSQLServiceType"];
            var adminRole = _configuration["AdminRole"];
            if (serviceType == "Cloud")
            {
                return new NoSqlConfig()
                {
                    ServiceType = NoSQLConnector.Common.ServiceType.Cloud,
                    Role = adminRole
                };
            }
            else
            {
                return new NoSqlConfig()
                {
                    ServiceType = NoSQLConnector.Common.ServiceType.CloudSim
                };
            }
        }
        public async Task GetJwtSecretKey()
        {
            try
            {
                // Check cache first
                if (_cache.TryGetValue("JwtSecretKey", out _key))
                {
                    Console.WriteLine($"Get Secret from Cache success");
                    return;
                }

                // If cache empty -> get secret from OCI Vault
                var secretOcid = _configuration["SecretOcid"];
                Console.WriteLine($"Get Secret Ocid {secretOcid.ToString().Substring(0,20)}...");
                int maxRetries = 3;
                int delay = 1000; // start with 1s
                
                // Retry mechaism if failed on get secret from OCI Vault
                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        var request = new GetSecretBundleRequest
                        {
                            SecretId = secretOcid
                        };
                        Console.WriteLine($"Starting get Oci Vault...");
                        GetSecretBundleResponse response = await _secretsClient.GetSecretBundle(request);
                        var secretContent = response.SecretBundle.SecretBundleContent as Base64SecretBundleContentDetails;
                        if (secretContent == null)
                        {
                            throw new CustomBadRequestException(new
                            {
                                status = 400,
                                timestamp = DateTime.UtcNow.ToString(),
                                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                                error = new
                                {
                                    code = "BAD_REQUEST_EXCEPTION",
                                    //message = "Secret content is not valid. Check OCI secret type."
                                    message = "無効な「Serect content」です。OCIタイプを確認してください。"
                                }
                            });
                        }

                        string base64Content = secretContent.Content;
                        string secretValue = Encoding.UTF8.GetString(Convert.FromBase64String(base64Content));
                        Console.WriteLine($"Get Secret from OCI Vault: {secretValue}");
                        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretValue));
                        // cache for 7 day (configurable)
                        _cache.Set("JwtSecretKey", _key, TimeSpan.FromDays(7));
                        return;
                    }
                    catch (Exception ex)
                    {
                        if (attempt == maxRetries)
                        {
                            // After last retry -> throw exception
                            Console.WriteLine($"Attempt {attempt} failed. Retrying in {delay / 1000}s...");
                            throw new CustomBadRequestException(new
                            {
                                status = 400,
                                timestamp = DateTime.UtcNow.ToString(),
                                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                                error = new
                                {
                                    code = "BAD_REQUEST_EXCEPTION",
                                    //message = $"OCI Vault is not ready after {maxRetries} retries, detail: {ex.Message}"
                                    message = $"「OCI　Vault」は {maxRetries}回の再試行後も準備ができていません、　詳細：　{ex.Message}"
                                }
                            });
                        }
                        Console.WriteLine($"Attempt {attempt} failed. Retrying in {delay / 1000}s...");
                        Console.WriteLine($"Detail error: {ex.Message}");
                        await Task.Delay(delay);
                        delay *= 2; // exponential backoff: 1s, 2s, 4s
                    }
                }
            }
            catch (Exception ex)
            {
                // If it's already our custom exception, just bubble it up
                if (ex is CustomBadRequestException)
                {
                    throw;
                }

                throw new CustomBadRequestException(new
                {
                    status = 400,
                    timestamp = DateTime.UtcNow.ToString(),
                    path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                    error = new
                    {
                        code = "BAD_REQUEST_EXCEPTION",
                        //message = $"OCI Vault is not ready to use, detail: {ex.Message}"
                        message = $"OCI Vault 未使用可、　詳細：　{ex.Message}"
                    } 
                });
            }
        }
        public async Task<string> GenerateOrgAccessToken(string tokenId,string custId)
        {
            await GetJwtSecretKey();
            var issuedAt = DateTime.UtcNow;
            var expiredAt = issuedAt.AddDays(30);
            var claims = new[]
            {
                new Claim("tokenId", tokenId),
                new Claim("custId", custId),
                new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(issuedAt).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim(JwtRegisteredClaimNames.Exp,
                new DateTimeOffset(expiredAt).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            };
            if (_key == null) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = $"JWT Secret Key is not initialized. Please recheck GetJwtSecretKey."
                    message = $"「JWT Secret Key」が初期化されていません。「GetJwtSecretKey」を再確認してください。"
                }
            });
            var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                claims: claims,
                notBefore: issuedAt,
                expires: expiredAt,
                signingCredentials: creds
            );

            issuedTime = issuedAt;
            accessExpiredTime = expiredAt;
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        public async Task<string> GenerateOrgRefreshToken(string tokenId, string custId)
        {
            await GetJwtSecretKey();
            var issuedAt = DateTime.UtcNow;
            var expiredAt = issuedAt.AddYears(500); // add 500 years = infinite
            var claims = new[]
            {
                new Claim("tokenId", tokenId),
                new Claim("custId", custId),
                new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(issuedAt).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim(JwtRegisteredClaimNames.Exp,
                new DateTimeOffset(expiredAt).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            };
            if (_key == null) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = $"JWT Secret Key is not initialized. Please recheck GetJwtSecretKey."
                    message = $"「JWT Secret Key」が初期化されていません。「GetJwtSecretKey」を再確認してください。"
                }
            });
            var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                claims: claims,
                notBefore: issuedAt,
                expires: expiredAt,
                signingCredentials: creds
            );

            refreshExpiredTime = expiredAt;
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        public async Task<string> GenerateStfAccessToken(string tokenId, string custId, string stfCd, string loginObjCd)
        {
            await GetJwtSecretKey();
            var issuedAt = DateTime.UtcNow;
            var expiredAt = issuedAt.AddHours(8);
            var claims = new[]
            {
                new Claim("tokenId", tokenId),
                new Claim("custId", custId),
                new Claim("stfCd", stfCd),
                new Claim("loginObjCd", loginObjCd),
                new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(issuedAt).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim(JwtRegisteredClaimNames.Exp,
                new DateTimeOffset(expiredAt).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            };
            if (_key == null) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = $"JWT Secret Key is not initialized. Please recheck GetJwtSecretKey."
                    message = $"「JWT Secret Key」が初期化されていません。「GetJwtSecretKey」を再確認してください。"
                }
            });
            var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                claims: claims,
                notBefore: issuedAt,
                expires: expiredAt,
                signingCredentials: creds
            );

            issuedTime = issuedAt;
            accessExpiredTime = expiredAt;
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        public async Task<string> GenerateStfRefreshToken(string tokenId, string custId, string stfCd, string loginObjCd)
        {
            await GetJwtSecretKey();
            var issuedAt = DateTime.UtcNow;
            var expiredAt = issuedAt.AddDays(14);
            var claims = new[]
            {
                new Claim("tokenId", tokenId),
                new Claim("custId", custId),
                new Claim("stfCd", stfCd),
                new Claim("loginObjCd", loginObjCd),
                new Claim(JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(issuedAt).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
                new Claim(JwtRegisteredClaimNames.Exp,
                new DateTimeOffset(expiredAt).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            };
            if (_key == null) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = $"JWT Secret Key is not initialized. Please recheck GetJwtSecretKey."
                    message = $"「JWT Secret Key」が初期化されていません。「GetJwtSecretKey」を再確認してください。"
                }
            });
            var creds = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
            var token = new JwtSecurityToken(
                claims: claims,
                notBefore: issuedAt,
                expires: expiredAt,
                signingCredentials: creds
            );

            refreshExpiredTime = expiredAt;
            return new JwtSecurityTokenHandler().WriteToken(token);
        }
        public bool CheckExpireRefreshToken(DateTime refreshExp)
        {
            if (refreshExp == defaultTime) return true; //  Incorrect Datetime
            if (DateTime.UtcNow > refreshExp) return true; // Expired
            return false; // Available
        }
        public string? GetValueFromToken(string token, string claimType)
        {
            var handler = new JwtSecurityTokenHandler();
            if (!handler.CanReadToken(token)) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = "Invalid JWT format"
                    message = "無効なJWTフォーマットです。"
                }
            });

            var jwtToken = handler.ReadJwtToken(token);
            var claim = jwtToken.Claims.FirstOrDefault(c => c.Type == claimType);
            return claim?.Value;
        }
        public DateTime ConvertUnixToDateTimeUtc(string unixTime)
        {
            if (long.TryParse(unixTime, out long expUnix))
            {
               DateTimeOffset dateTimeOffset = DateTimeOffset.FromUnixTimeSeconds(expUnix);
               return dateTimeOffset.UtcDateTime;
            }
            return defaultTime;
        }
        public async Task<ClaimsPrincipal> ValidateToken(string token, TokenType type)
        {
            await GetJwtSecretKey();
            var tokenHandler = new JwtSecurityTokenHandler();
            bool lifeTime = type == TokenType.RefreshToken ? false : true;
            if (_key == null) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = $"JWT Secret Key is not initialized. Please recheck GetJwtSecretKey."
                    message = $"「JWT Secret Key」が初期化されていません。「GetJwtSecretKey」を再確認してください。"
                }
            });
            try
            {
                var principal = tokenHandler.ValidateToken(token, new TokenValidationParameters
                {
                    ValidateIssuer = false,
                    ValidateAudience = false,

                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = _key,
                    ValidateLifetime = lifeTime,
                    ClockSkew = TimeSpan.Zero
                }, out SecurityToken validatedToken);

                return principal;
            }
            catch (Exception ex)
            {
                throw new CustomBadRequestException(new
                {
                    status = 400,
                    timestamp = DateTime.UtcNow.ToString(),
                    path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                    error = new
                    {
                        code = "BAD_REQUEST_EXCEPTION",
                        //message = $"Validate fail for Token, detail: {ex.Message}"
                        message = $"「Token」の検証に失敗しました、　詳細：　{ex.Message}"
                    }
                });
            }
        }
        public async Task<string> ValidateAccessAndRefreshToken(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken)) throw new CustomBadRequestException(new
            {
                status = 400,
                timestamp = DateTime.UtcNow.ToString(),
                path = _httpContext.HttpContext?.Request.Path.Value?.TrimStart('/').ToLowerInvariant(),
                error = new
                {
                    code = "BAD_REQUEST_EXCEPTION",
                    //message = "refreshToken is not empty"
                    message = "「refreshToken」 は必須項目です。"
                }
            });

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

            var principal = await ValidateToken(accessToken, TokenType.AccessToken);
            var tokenId = principal.FindFirst("tokenId")?.Value;
            var custId = principal.FindFirst("custId")?.Value;
            using INoSQLConnectorManager manager = new NoSQLConnectorManager();
            var noSqlConfig = GetNoSqlConfig();
            var connect = manager.Connect(noSqlConfig);
            if (connect.Success)
            {
                var primaryKey = new MapValue { ["TOKENID"] = $"{tokenId}" };
                var tokenTable = _configuration["TokenTable"];
                var tokenInfo = await manager.GetByPrimaryKeyAsync<Token>(tokenTable, primaryKey);
                if (tokenInfo != null && tokenInfo.Data?.CUSTID == custId && tokenInfo.Data?.REFRESHTOKEN == refreshToken)
                {
                    return accessToken;
                }
            }
            return string.Empty;
        }
        public string GetTypeCd(string baseCd, BaseCdType type)
        {
            if (baseCd.Length < 10) return string.Empty;

            switch (type)
            {
                case BaseCdType.Corpcd:
                    return baseCd.Substring(0, 1);
                case BaseCdType.Officecd:
                    return baseCd.Substring(1, 2);
                case BaseCdType.Blockcd:
                    return baseCd.Substring(3, 2);
                case BaseCdType.Subcd:
                    return baseCd.Substring(5, 2);
                case BaseCdType.Unitcd:
                    return baseCd.Substring(7, 3);
                default:
                    return string.Empty;
            }
        }
    }
}
