using System.Text.Json.Nodes;

namespace Tinderhearth.Rules.Foundation.Save;

/// <summary>一个分片的声明：叫什么、谁持有、进哪一侧、缺字段时补什么，形状改过的话旧版本那一份怎么升上来。</summary>
/// <remarks>
/// 标识、持有者、哪一侧、存档默认值缺一样，注册时当场被拒（<see cref="SaveShardRegistry.Register"/>），不留能用的默认值。存档默认值是一个工厂，
/// 每次调用给一份新的；它要与「这个分片从来没存过」时的初始值同一处，所以通常就是拿那个初始值调一次本分片的写法。
///
/// 升级只有形状改过的分片才给：比当前版本旧的存档里有这个分片时，先拿它把那一份变成现在的形状，再照存档默认值补缺的那几格。
/// 不给就是形状从来没改过。版本号全存档一个、一次迁移只处理那一版改了的分片，理由在设计仓
/// decisions/ADR-0015-存档的版本与缺字段口径.md。
/// </remarks>
/// <param name="Id">跨版本稳定的名字，存档里按它索引。</param>
/// <param name="Owner">谁负责读写这一块，报错时点名用。</param>
/// <param name="Side">进正式存档还是临时续玩点。</param>
/// <param name="Default">这个分片从来没存过时的样子；旧版本的存档缺字段时按它补。</param>
/// <param name="Upgrade">
/// 形状改过时给：拿存档的版本号与存档里那一份，交回现在形状的一份。它要认得每一个比当前旧、又不低于最低兼容版本的形状。
/// 形状没改过是 <c>null</c>。
/// </param>
public sealed record SaveShardDeclaration(
    string Id, string Owner, SaveSide Side, Func<JsonObject> Default, Func<int, JsonObject, JsonObject>? Upgrade = null);
