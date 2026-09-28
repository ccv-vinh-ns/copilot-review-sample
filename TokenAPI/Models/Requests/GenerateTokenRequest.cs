namespace TokenAPI.Models.Requests
{
    public class GenerateTokenRequest
    {
        public string CustId { get; set; } = string.Empty;
        public string CustPass { get; set; } = string.Empty;
    }
}
