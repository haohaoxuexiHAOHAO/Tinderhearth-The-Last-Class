using System.Diagnostics.CodeAnalysis;

namespace Tinderhearth.Rules.Foundation.Save;

/// <summary>已注册的分片。一份存档里有哪几块，只由它回答。</summary>
/// <remarks>
/// 注册是显式的，没有自动发现：自动发现会把「忘了注册」变成「运行时扫不到」，而那不报错。
/// 「一共存了什么」由 <see cref="Shards"/> 这个读口回答，不另存一张字段清单（设计仓 design/存档系统.md）。
/// </remarks>
public sealed class SaveShardRegistry
{
    private readonly List<SaveShardDeclaration> _shards = [];

    /// <summary>全部已注册的分片，按注册的先后。</summary>
    public IReadOnlyList<SaveShardDeclaration> Shards => _shards;

    /// <summary>注册一个分片。声明缺一格、或这个标识已经注册过，当场被拒。</summary>
    /// <remarks>存档默认值在这里先取一次：工厂给不出一份时，与没填同样处理。</remarks>
    public void Register(SaveShardDeclaration shard)
    {
        ArgumentNullException.ThrowIfNull(shard);

        var missing = new (bool Missing, string What)[]
        {
            (string.IsNullOrWhiteSpace(shard.Id), "标识"),
            (string.IsNullOrWhiteSpace(shard.Owner), "持有它的系统"),
            (shard.Side is not (SaveSide.FormalSave or SaveSide.ResumePoint), "进哪一侧（正式存档或临时续玩点）"),
            (shard.Default is null || shard.Default() is null, "存档默认值"),
        }.Where(c => c.Missing).Select(c => c.What).ToList();

        if (missing.Count > 0)
        {
            throw new ArgumentException(
                $"分片「{shard.Id}」的声明缺{string.Join("、", missing)}，注册被拒。"
                    + "四格都要填，各是什么在设计仓 design/存档系统.md 的「一个分片要声明什么」一节",
                nameof(shard));
        }

        if (_shards.Any(s => s.Id == shard.Id))
        {
            throw new ArgumentException($"分片「{shard.Id}」已经注册过了，同一个标识只许注册一次", nameof(shard));
        }

        _shards.Add(shard);
    }

    /// <summary>这个标识注册过没有，注册过的话是哪一份声明。</summary>
    public bool TryGet(string id, [MaybeNullWhen(false)] out SaveShardDeclaration shard)
    {
        shard = _shards.FirstOrDefault(s => s.Id == id);
        return shard is not null;
    }
}
