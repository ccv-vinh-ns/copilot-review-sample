namespace TokenAPI.Models.Entities
{
    public class Token
    {
        public string TOKENID { get; set; } = string.Empty;
        public string TYPE { get; set; } = string.Empty;
        public string ACCESSTOKEN { get; set; } = string.Empty;
        public string CUSTID { get; set; } = string.Empty;
        public string REFRESHTOKEN { get; set; } = string.Empty;
        public string STATUS { get; set; } = string.Empty;
        public DateTime IAT { get; set; }
        public DateTime EXP { get; set; }
        public DateTime REFRESHEXP { get; set; }
    }
}
