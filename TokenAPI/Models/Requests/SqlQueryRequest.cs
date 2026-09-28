using System.Collections.Generic;

namespace TokenAPI.Models
{
    public class SqlQueryRequest
    {
        public string Sql { get; set; } = string.Empty;
        public Dictionary<string, object>? Parameters { get; set; }
    }
}
