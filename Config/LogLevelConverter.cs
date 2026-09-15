using System.Text.Json;
using System.Text.Json.Serialization;
using WebParser.Logger;

namespace WebParser.Config
{
    /// <summary>
    /// Конвертер <see cref="LogLevel"/> для System.Text.Json: терпим к строкам, числам и опечаткам.
    /// </summary>
    public sealed class LogLevelConverter : JsonConverter<LogLevel>
    {
        /// <summary>
        /// Читает имя уровня из строки или числа; при неизвестном значении возвращает Info.
        /// </summary>
        /// <param name="reader">Читатель JSON.</param>
        /// <param name="typeToConvert">Целевой тип (LogLevel).</param>
        /// <param name="options">Параметры сериализации.</param>
        /// <returns>Значение уровня логгирования.</returns>
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

        /// <summary>
        /// Записывает уровень в JSON как строку.
        /// </summary>
        /// <param name="writer">Писатель JSON.</param>
        /// <param name="value">Значение уровня логгирования.</param>
        /// <param name="options">Параметры сериализации.</param>
        public override void Write(Utf8JsonWriter writer, LogLevel value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}