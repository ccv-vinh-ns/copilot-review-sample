using System.Text.Json.Serialization;

namespace TokenAPI.Models.Entities
{
    public class StaffBelong
    {
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string CORPCD { get; set; } = string.Empty;
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string OFFICECD { get; set; } = string.Empty;
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string BLOCKCD { get; set; } = string.Empty;
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string SUBCD { get; set; } = string.Empty;
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string UNITCD { get; set; } = string.Empty;
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string NAME {  get; set; } = string.Empty;
    }
}
