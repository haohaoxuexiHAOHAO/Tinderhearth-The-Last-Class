namespace Tinderhearth.Rules.Foundation.Content;

/// <summary>
/// 内容文件的来源。规则层只声明「我要读这个相对路径」，**不做文件 I/O**。
/// </summary>
/// <remarks>
/// 为什么把 I/O 挡在规则层外面：读文件要走 Godot 的 <c>FileAccess</c>（`res://` 在导出后
/// 打进 `.pck`，普通 .NET 的 <c>File</c> 读不到），而规则层不引用 Godot。于是这里留接口，
/// 实现放在 Godot 侧（<c>src/Platform/GodotContentSource.cs</c>）。
///
/// 附带好处：测试可以塞一个纯内存实现，不碰磁盘也不需要引擎 —— 本条的测试正是这么做的。
/// </remarks>
public interface IContentSource
{
    /// <summary>来源的名字，只用于诊断与「这条内容来自哪个 mod」的提示（`ENG-7`）。</summary>
    string Name { get; }

    /// <summary>列出某个相对目录下的内容文件相对路径；目录不存在时返回空序列，不抛异常。</summary>
    IEnumerable<string> List(string relativeDirectory);

    /// <summary>读取文本内容。路径不存在时抛异常 —— 列出来了却读不到属于真错误，不该静默。</summary>
    string ReadAllText(string relativePath);
}
