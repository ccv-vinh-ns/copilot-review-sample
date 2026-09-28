namespace TokenAPI.Models.Requests
{
    public class GetStaffRequest
    {
        public string UserId { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
