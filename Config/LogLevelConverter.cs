using System.Text.Json;
using System.Text.Json.Serialization;
using WebParser.Logger;

namespace WebParser.Config
{
    public sealed class LogLevelConverter : JsonConverter<LogLevel>
    {
        public override LogLevel Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                {
                    string? value = reader.GetString();
                    if (value != null && Enum.TryParse(value, ignoreCase: true, out LogLevel level))
                    {
                        return level;
                    }

                    return LogLevel.Info;
                }
                case JsonTokenType.Number:
                    return Enum.IsDefined(typeof(LogLevel), reader.GetInt32())
                        ? (LogLevel)reader.GetInt32()
                        : LogLevel.Info;
                default:
                    return LogLevel.Info;
            }
        }

        public override void Write(Utf8JsonWriter writer, LogLevel value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}