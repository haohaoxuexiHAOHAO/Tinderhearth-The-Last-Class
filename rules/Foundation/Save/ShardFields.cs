using System.Text.Json.Nodes;

namespace Tinderhearth.Rules.Foundation.Save;

/// <summary>读一个分片时取一格的那几样写法：取不到、类型不对，一律报出是哪一格。</summary>
/// <remarks>
/// 每个分片的持有者都要读自己那一块，报错的口径应该一样：说清是哪个分片的哪一格、实际写的是什么。
/// 抛的一律是 <see cref="InvalidDataException"/>：存档读不出来是数据的问题，不是调用方传错了参数。
/// </remarks>
public static class ShardFields
{
    /// <summary>这一格必须在。</summary>
    /// <param name="shard">分片那一块。</param>
    /// <param name="key">哪一格。</param>
    /// <param name="where">报错时说是谁，例如「分片 plots」。</param>
    public static JsonNode Required(JsonObject shard, string key, string where) =>
        shard.TryGetPropertyValue(key, out var node) && node is not null
            ? node
            : throw new InvalidDataException($"{where}缺「{key}」这一格");

    /// <summary>一个整数。</summary>
    public static int Int(JsonNode? node, string where) =>
        node is JsonValue value && value.TryGetValue<int>(out var number)
            ? number
            : throw new InvalidDataException($"{where}要是一个整数，实际是 {Show(node)}");

    /// <summary>真或假。</summary>
    public static bool Bool(JsonNode? node, string where) =>
        node is JsonValue value && value.TryGetValue<bool>(out var flag)
            ? flag
            : throw new InvalidDataException($"{where}要是 true 或 false，实际是 {Show(node)}");

    /// <summary>一段字，或者 <c>null</c>。</summary>
    public static string? StringOrNull(JsonNode? node, string where) => node switch
    {
        null => null,
        JsonValue value when value.TryGetValue<string>(out var text) => text,
        _ => throw new InvalidDataException($"{where}要是一段字或者 null，实际是 {Show(node)}"),
    };

    /// <summary>一个按名字写的枚举值，名字要一字不差、而且是那个枚举里真有的。</summary>
    public static TEnum EnumName<TEnum>(JsonNode? node, string where)
        where TEnum : struct, Enum =>
        node is JsonValue value && value.TryGetValue<string>(out var name)
            && Enum.TryParse<TEnum>(name, ignoreCase: false, out var parsed) && Enum.IsDefined(parsed)
            && parsed.ToString() == name
            ? parsed
            : throw new InvalidDataException($"{where}要是 {typeof(TEnum).Name} 里的一个名字，实际是 {Show(node)}");

    /// <summary>一个对象。</summary>
    public static JsonObject Object(JsonNode? node, string where) =>
        node as JsonObject ?? throw new InvalidDataException($"{where}要是一个对象，实际是 {Show(node)}");

    /// <summary>一串，项数要正好是给的那个数。</summary>
    public static JsonArray Array(JsonNode? node, int count, string where)
    {
        var array = node as JsonArray ?? throw new InvalidDataException($"{where}要是一串，实际是 {Show(node)}");
        return array.Count == count
            ? array
            : throw new InvalidDataException($"{where}要正好 {count} 项，实际 {array.Count} 项");
    }

    private static string Show(JsonNode? node) => node?.ToJsonString() ?? "null";
}
