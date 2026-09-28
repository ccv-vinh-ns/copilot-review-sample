namespace TokenAPI.Models
{
    public class ApiResponse
    {
        public bool success { get; set; }
        public List<object> data { get; set; } = new List<object>();
        public int statusCode { get; set; }

        // For error cases
        public string error { get; set; } = string.Empty;
    }
}
