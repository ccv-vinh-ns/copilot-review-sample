using System.Text.Json;
using TokenAPI.Models.Responses;

namespace TokenAPI.Common.Utils
{
    public static class SqlObjectResponsesConverter
    {
        public static SqlObjectResponse<T> Parse<T>(string oracleResult)
        {
            if (string.IsNullOrWhiteSpace(oracleResult))
                return new SqlObjectResponse<T>
                {
                    Success = false,
                    Error = "SqlObjectResponseConverter Parse error: Empty Oracle response",
                    Data = new List<SqlQueryData<T>>()
                };
            try
            {
                using var document = JsonDocument.Parse(oracleResult);
                var root = document.RootElement;
                var response = new SqlObjectResponse<T>()
                {
                    Success = root.GetProperty("success").GetBoolean(),
                    Error = root.TryGetProperty("error", out var errProp)
                        ? errProp.GetString()
                        : string.Empty,
                    StatusCode = root.GetProperty("statusCode").GetInt32(),
                    Data = new List<SqlQueryData<T>>()
                };

                if (root.TryGetProperty("data", out var dataProp))
                {
                    foreach (var item in dataProp.EnumerateArray())
                    {
                        var order = item.GetProperty("order").GetInt32();
                        var queryDataArray = item.GetProperty("queryData");
                        var queryDataList = JsonSerializer.Deserialize<List<T>>(queryDataArray.GetRawText()) ?? new List<T>();
                        response.Data.Add(new SqlQueryData<T>
                        {
                            Order = order,
                            QueryData = queryDataList
                        });
                    }
                }
                return response;
            }
            catch (Exception ex)
            {
                return new SqlObjectResponse<T>
                {
                    Success = false,
                    Error = $"SqlObjectResponseConverter Parse error: {ex.Message}",
                    Data = new List<SqlQueryData<T>>()
                };
            }
        }
    }
}
