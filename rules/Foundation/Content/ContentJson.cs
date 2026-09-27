using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tinderhearth.Rules.Foundation.Content;

/// <summary>
/// 内容数据的 JSON 读法。全项目共用同一套选项，避免各处各配一套导致同一份数据在两个
/// 地方解析出不同结果。
/// </summary>
public static class ContentJson
{
    /// <remarks>
    /// <c>AllowTrailingCommas</c> 与注释放行是给**手写数据文件**的：角色定义、文本、配置
    /// 都要由人直接编辑，mod 作者更是如此。为一个逗号报错会把「数据外置」变成折磨。
    /// </remarks>
    /// <remarks>
    /// 枚举**按名字读写**（<c>JsonStringEnumConverter</c>），不按序号。默认行为是序号，而序号在
    /// 手写的数据文件里既读不出含义（`"seasons": [0, 1]` 要去查 0 是哪个季节），又会在有人往
    /// 枚举中间插一个值时**静默改掉全部旧数据的含义** —— 那种错不报错。名字改了则是解析失败，
    /// 而失败是查得出来的。
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
