using System.Data;
using System.Text.Json;
using System.Text.Encodings.Web;
using Oracle.ManagedDataAccess.Client;

namespace TokenAPI.Common
{
    public static class OracleUtils
    {
        public static string SerializeResult(object result)
        {
            var jsonOptions = new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            return result switch
            {
                DataTable dt => JsonSerializer.Serialize(
                    dt.AsEnumerable().Select(row => 
                        dt.Columns.Cast<DataColumn>()
                        .ToDictionary(col => col.ColumnName, col => row[col])
                    ), jsonOptions),
                
                IEnumerable<object> enumerable => JsonSerializer.Serialize(enumerable.ToList(), jsonOptions),
                
                _ => JsonSerializer.Serialize(result, jsonOptions)
            };
        }
    }
}
