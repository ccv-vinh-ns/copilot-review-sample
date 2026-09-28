namespace TokenAPI.Models.Responses
{
    public class GenerateTokenResponses
    {
        public string AccessToken { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
    }
}
