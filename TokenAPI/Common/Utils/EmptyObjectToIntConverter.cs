using System.Text.Json;
using System.Text.Json.Serialization;

public class EmptyObjectToIntConverter : JsonConverter<int>
{
    public override int Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using (JsonDocument doc = JsonDocument.ParseValue(ref reader))
            {
                return 0; // default for int
            }
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out int value))
        {
            return value;
        }

        if (reader.TokenType == JsonTokenType.String && int.TryParse(reader.GetString(), out int strValue))
        {
            return strValue;
        }

        return 0;
    }

    public override void Write(Utf8JsonWriter writer, int value, JsonSerializerOptions options)
    {
        writer.WriteNumberValue(value);
    }
}
