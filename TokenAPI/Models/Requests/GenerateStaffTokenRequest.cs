namespace TokenAPI.Models.Requests
{
    public class GenerateStaffTokenRequest
    {
        public string StfCd { get; set; } = string.Empty;
        public string LoginObjCd { get; set; } = string.Empty;
    }
}
