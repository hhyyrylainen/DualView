using System.Text.Json;
using System.Text.Json.Serialization;

namespace DualView.Server.Models;

/// <summary>
///   A message exchanged with the DualView browser plugin.
/// </summary>
public sealed class BrowserPluginMessage
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("requestId")]
    public string? RequestId { get; set; }

    [JsonPropertyName("message")]
    public string? Message { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement> Data { get; set; } = new();
}
