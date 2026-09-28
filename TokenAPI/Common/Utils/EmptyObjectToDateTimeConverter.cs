using System.Text.Json;
using System.Text.Json.Serialization;

public class EmptyObjectToDateTimeConverter : JsonConverter<DateTime>
{
    private static readonly DateTime DefaultDate = DateTime.MinValue; 

    public override DateTime Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                return DefaultDate;
            }
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            if (DateTime.TryParse(reader.GetString(), out DateTime dt))
            {
                return dt;
            }
        }

        return DefaultDate;
    }

    public override void Write(Utf8JsonWriter writer, DateTime value, JsonSerializerOptions options)
    {
        // Write in ISO format (yyyy-MM-ddTHH:mm:ss)
        writer.WriteStringValue(value.ToString("yyyy-MM-ddTHH:mm:ss"));
    }
}
