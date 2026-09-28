namespace TokenAPI.Models.Responses
{
    public class GetStaffResponses
    {
        public string StaffId { get; set; } = string.Empty;
        public string Family { get; set; } = string.Empty;
        public string Given { get; set; } = string.Empty;
        public string KanaFamily { get; set; } = string.Empty;
        public string KanaGiven { get; set; } = string.Empty;
        public string Birth { get; set; } = string.Empty;
        public int Sex { get; set; } 
        public string LoginId { get; set; } = string.Empty;
        public List<StfBelong> Belong { get; set;} = new List<StfBelong>();
    }

    public class StfBelong
    {
        public string CorpCd { get; set; } = string.Empty;
        public string OfficeCd { get; set; } = string.Empty;
        public string BlockCd { get; set; } = string.Empty;
        public string SubCd { get; set; } = string.Empty;
        public string UnitCd { get; set; } = string.Empty;
        public string ObjNm { get; set; } = string.Empty;
    }
}
