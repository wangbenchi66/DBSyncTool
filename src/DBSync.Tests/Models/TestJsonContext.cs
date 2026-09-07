using System.Text.Json.Serialization;
using DBSync.Core.Models;

namespace DBSync.Tests.Models;

/// <summary>
/// 测试用 JSON Source Generator 上下文
///</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SyncProject))]
internal partial class TestJsonContext : JsonSerializerContext;
