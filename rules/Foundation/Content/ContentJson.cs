using System.Text.Json;

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
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>反序列化，拿到 <c>null</c> 视为错误 —— 一个内容文件解析成空是缺陷，不是空数据。</summary>
    public static T Parse<T>(string json, string whatForDiagnostics)
    {
        var parsed = JsonSerializer.Deserialize<T>(json, Options);
        return parsed ?? throw new InvalidDataException($"{whatForDiagnostics} 解析结果为空");
    }
}
