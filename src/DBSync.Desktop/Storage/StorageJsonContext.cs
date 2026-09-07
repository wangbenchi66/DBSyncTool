using System.Text.Json.Serialization;
using DBSync.Core.Models;
using DBSync.Desktop.Models;

namespace DBSync.Desktop.Storage;

/// <summary>
/// 存储层 JSON Source Generator 上下文，覆盖所有需要序列化/反序列化的类型
///</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<LocalConnectionStore.ConnectionDto>))]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(SyncProject))]
internal partial class StorageJsonContext : JsonSerializerContext;
