using System.Text.Json.Nodes;

namespace Tinderhearth.Rules.Foundation.Save;

/// <summary>一个分片的声明：叫什么、谁持有、进哪一侧、缺字段时补什么。</summary>
/// <remarks>
/// 四格缺一格，注册时当场被拒（<see cref="SaveShardRegistry.Register"/>），不留能用的默认值。
///
/// 存档默认值是一个工厂，每次调用给一份新的。它要与「这个分片从来没存过」时的初始值同一处，所以通常就是
/// 拿那个初始值调一次本分片的写法（设计仓 decisions/ADR-0015-存档的版本与缺字段口径.md）。
/// </remarks>
/// <param name="Id">跨版本稳定的名字，存档里按它索引。</param>
/// <param name="Owner">谁负责读写这一块，报错时点名用。</param>
/// <param name="Side">进正式存档还是临时续玩点。</param>
/// <param name="Default">这个分片从来没存过时的样子；旧版本的存档缺字段时按它补。</param>
public sealed record SaveShardDeclaration(string Id, string Owner, SaveSide Side, Func<JsonObject> Default);
