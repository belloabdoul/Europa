using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Core.Entities;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Api;

[JsonSerializable(typeof(SearchParameters))]
[JsonSerializable(typeof(BadRequest))]
[JsonSerializable(typeof(IDictionary<string, string[]>))]
[JsonSerializable(typeof(Ok))]
[JsonSerializable(typeof(DuplicatesResponse))]
[JsonSerializable(typeof(Notification))]
[JsonSerializable(typeof(ImagesGroup))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(Similarity))]
[JsonSerializable(typeof(Half[]))]
public partial class AppJsonSerializerContext : JsonSerializerContext
{
}