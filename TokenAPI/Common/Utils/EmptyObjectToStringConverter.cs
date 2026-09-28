using System.Text.Json;
using System.Text.Json.Serialization;

public class EmptyObjectToStringConverter : JsonConverter<string>
{
    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                // If it's just `{}`, return empty string
                return string.Empty;
            }
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return reader.GetString();
        }

        return string.Empty;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }
}
