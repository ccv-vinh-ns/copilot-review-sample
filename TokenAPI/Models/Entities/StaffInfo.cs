using System.Text.Json.Serialization;

namespace TokenAPI.Models.Entities
{
    public class StaffInfo
    {
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string STFCD { get; set; }  = string.Empty;
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string FAMILY { get; set; } = string.Empty;
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string GIVEN { get; set; } = string.Empty;
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string KANAFAMILY { get; set; } = string.Empty;
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string KANAGIVEN { get; set; } = string.Empty;
        [JsonConverter(typeof(EmptyObjectToDateTimeConverter))]
        public DateTime BIRTH { get; set; }
        [JsonConverter(typeof(EmptyObjectToIntConverter))]
        public int SEX { get; set; }
        [JsonConverter(typeof(EmptyObjectToStringConverter))]
        public string USERID { get; set; } = string.Empty;
        public bool isValidStf { get; set; } = true;
    }
}
