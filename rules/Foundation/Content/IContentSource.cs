namespace Tinderhearth.Rules.Foundation.Content;

/// <summary>
/// 内容文件的来源。规则层只声明「我要读这个相对路径」，不做文件 I/O。
/// </summary>
/// <remarks>
/// I/O 挡在规则层外面的原因是读文件要走 Godot 的 <c>FileAccess</c>（<c>res://</c> 在导出后打进
/// <c>.pck</c>，普通 .NET 的 <c>File</c> 读不到），而规则层不引用 Godot。所以这里只留接口，
/// 实现在 Godot 那一侧的 <c>src/Platform/GodotContentSource.cs</c>。
///
/// 附带好处：测试可以塞一个纯内存实现，不碰磁盘也不需要引擎，这一层的测试正是这么做的。
/// </remarks>
public interface IContentSource
{
    /// <summary>来源的名字，只用于诊断和「这条内容来自哪个 mod」的提示。</summary>
    string Name { get; }

    /// <summary>列出某个相对目录下的内容文件相对路径；目录不存在时返回空序列，不抛异常。</summary>
    IEnumerable<string> List(string relativeDirectory);

    /// <summary>读取文本内容。路径不存在时抛异常 —— 列出来了却读不到是真错误，不该静默。</summary>
    string ReadAllText(string relativePath);
}
