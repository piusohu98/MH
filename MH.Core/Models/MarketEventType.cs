using System.Text.Json.Serialization;

namespace MH.Core.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MarketEventType
{
    DayNight = 0,
    Holiday = 1,
    SupplyChange = 2,
    OcrAnomaly = 3
}
