using System.Text.Json;
using System.Text.Json.Serialization;
using TinyLink.Contracts;

namespace RedirectService;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web)]
[JsonSerializable(typeof(LinkResponse))]
[JsonSerializable(typeof(RecordClickRequest))]
[JsonSerializable(typeof(RecordClicksBatchRequest))]
internal partial class AppJsonContext : JsonSerializerContext;
