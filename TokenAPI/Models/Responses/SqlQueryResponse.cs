using TokenAPI.Common;

namespace TokenAPI.Models
{
    public class SqlQueryResponse
    {
        public bool Success { get; set; } = false;
        public string? ErrorMessage { get; set; } = "";
        public string? Data { get; set; } = null;
        public int StatusCode { get; set; } = ApiConstants.STATUS_SERVER_ERROR;

        public static SqlQueryResponse SuccessResult(string? data)
            => new() { Success = true, Data = data, StatusCode = ApiConstants.STATUS_OK };

        public static SqlQueryResponse FailResult(string error, int statusCode)
            => new() { Success = false, ErrorMessage = error, StatusCode = statusCode };
    }
}
