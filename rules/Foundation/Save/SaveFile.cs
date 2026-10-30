using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace Tinderhearth.Rules.Foundation.Save;

/// <summary>一份存档文件的外形：最外层一个版本号，加各分片各一格。</summary>
/// <remarks>
/// 明文 JSON、缩进排好，用文本编辑器打得开；版本号全存档只有一个，分片不各带版本（设计仓
/// decisions/ADR-0015-存档的版本与缺字段口径.md）。读写文件那一下不在这里：规则层只吃字符串，文件归引擎层。
///
/// 选项与内容数据那一套（ContentJson）刻意分开：内容是手写的，放行注释与尾逗号；存档是程序写的，出现那两样只能是被改坏了。
/// </remarks>
public static class SaveFile
{
    /// <summary>当前的存档版本号。只增不减：任何一个分片的形状变了就加一。</summary>
    public const int CurrentVersion = 1;

    /// <summary>最外层版本号那一格的键。</summary>
    public const string VersionKey = "version";

    /// <summary>最外层各分片那一格的键，里面按分片标识各一格。</summary>
    public const string ShardsKey = "shards";

    // 换行写死成 \n：同一份存档在 Windows 与别的系统上写出来逐字节一样，云同步与比对都靠得住。
    // 不转义中文：存档里会有玩家起的名字，转成 \u 之后文本编辑器里就认不出了。
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>把各分片拼成一份正式存档的文本。</summary>
    /// <param name="registry">已注册的分片。</param>
    /// <param name="shards">每个进正式存档的分片各一份，按标识给。</param>
    /// <remarks>
    /// 进正式存档的分片缺一份、多给一份没注册的、给了一份进续玩点的，当场被拒，问题一次列全：
    /// 写出去的文件里正好是那几个分片，不多不少。分片按注册的先后排。
    /// </remarks>
    public static string WriteFormalSave(SaveShardRegistry registry, IReadOnlyDictionary<string, JsonObject> shards)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(shards);

        var formal = registry.Shards.Where(s => s.Side == SaveSide.FormalSave).ToList();
        var problems = new List<string>();
        problems.AddRange(formal.Where(s => !shards.ContainsKey(s.Id)).Select(s => $"缺分片「{s.Id}」（{s.Owner}）"));
        foreach (var (id, body) in shards)
        {
            if (!registry.TryGet(id, out var declared))
            {
                problems.Add($"分片「{id}」没有注册");
            }
            else if (declared.Side != SaveSide.FormalSave)
            {
                problems.Add($"分片「{id}」进的是临时续玩点，不进正式存档");
            }
            else if (body is null)
            {
                problems.Add($"分片「{id}」是空的");
            }
        }

        if (problems.Count > 0)
        {
            throw new ArgumentException("拼不成一份正式存档：" + string.Join("；", problems), nameof(shards));
        }

        // 拷一份再挂上去：交进来的那一份可能已经挂在别处，一个节点只许有一个父节点。
        var bodies = new JsonObject();
        foreach (var shard in formal)
        {
            bodies[shard.Id] = shards[shard.Id].DeepClone();
        }

        var root = new JsonObject
        {
            [VersionKey] = CurrentVersion,
            [ShardsKey] = bodies,
        };
        return root.ToJsonString(WriteOptions);
    }
}
