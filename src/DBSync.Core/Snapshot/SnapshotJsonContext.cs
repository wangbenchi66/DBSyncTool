using System.Text.Json.Serialization;
using DBSync.Core.Data;
using DBSync.Core.Models;

namespace DBSync.Core.Snapshot;

/// <summary>
/// 快照紧凑 JSON Source Generator 上下文（单行，无缩进）
///</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(SnapshotManifest))]
[JsonSerializable(typeof(TableModel))]
[JsonSerializable(typeof(RowHash))]
internal partial class SnapshotJsonContext : JsonSerializerContext;

/// <summary>
/// 快照缩进 JSON Source Generator 上下文（格式化输出）
///</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(typeof(SnapshotManifest))]
[JsonSerializable(typeof(TableModel))]
[JsonSerializable(typeof(RowHash))]
internal partial class SnapshotJsonIndentedContext : JsonSerializerContext;
