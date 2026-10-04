using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tinderhearth.Rules.Foundation.Content;

/// <summary>
/// 内容数据的 JSON 读法。全项目共用同一套选项，避免各处各配一套导致同一份数据在两个
/// 地方解析出不同结果。
/// </summary>
public static class ContentJson
{
    /// <summary>全项目共用的那一份反序列化选项。</summary>
    /// <remarks>
    /// 放行尾逗号与注释（<c>AllowTrailingCommas</c> 与 <c>ReadCommentHandling</c>）是给手写的
    /// 数据文件的：角色定义、文本、配置都要由人直接编辑，mod 作者更是如此。为一个多余的逗号
    /// 报错会把「数据外置」变成折磨。
    ///
    /// 枚举按名字读写（<c>JsonStringEnumConverter</c>），不按序号。默认行为是序号，而序号在手写
    /// 的数据文件里既读不出含义（<c>"seasons": [0, 1]</c> 要去查 0 是哪个季节），又会在有人往
    /// 枚举中间插一个值时悄悄改掉全部旧数据的含义。名字改了则是解析失败，而失败是查得出来的。
    /// </remarks>
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>反序列化，拿到 <c>null</c> 视为错误 —— 一个内容文件解析成空是缺陷，不是空数据。</summary>
    public static T Parse<T>(string json, string whatForDiagnostics)
    {
        var parsed = JsonSerializer.Deserialize<T>(json, Options);
        return parsed ?? throw new InvalidDataException($"{whatForDiagnostics} 解析结果为空");
    }
}
