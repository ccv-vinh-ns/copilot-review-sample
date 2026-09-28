using System.Text.Json;
using System.Text.Json.Serialization;
using TokenAPI.Common;
using TokenAPI.Common.Utils;

namespace TokenAPI.Models.Responses
{
    public class SqlObjectResponse<T>
    {
        public bool Success { get; set; } = false;
        public int StatusCode { get; set; } = ApiConstants.STATUS_SERVER_ERROR;
        public string? Error { get; set; } = string.Empty;
        public List<SqlQueryData<T>>? Data { get; set; } = new List<SqlQueryData<T>>();
    }

    public class SqlQueryData<T>
    {
        public int Order { get; set; } = 0;
        public List<T>? QueryData { get; set; }
    }
}
