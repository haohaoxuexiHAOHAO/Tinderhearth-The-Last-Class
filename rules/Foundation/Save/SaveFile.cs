using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;

namespace Tinderhearth.Rules.Foundation.Save;

/// <summary>一份存档文件的外形与读写：最外层一个版本号，加各分片各一格。</summary>
/// <remarks>
/// 明文 JSON、缩进排好，用文本编辑器打得开；版本号全存档只有一个，分片不各带版本（设计仓
/// decisions/ADR-0015-存档的版本与缺字段口径.md）。读写文件那一下不在这里：规则层只吃字符串，文件归引擎层。
///
/// 选项与内容数据那一套（ContentJson）刻意分开：内容是手写的，放行注释与尾逗号；存档是程序写的，出现那两样只能是被改坏了。
/// </remarks>
public static class SaveFile
{
    /// <summary>当前的存档版本号。只增不减：任何一个分片的形状变了、或者多了一个分片，就加一。</summary>
    /// <remarks>旧版本的存档缺的那几片与那几格，按各分片的存档默认值补（<see cref="ReadFormalSave(SaveShardRegistry, string)"/>）。</remarks>
    public const int CurrentVersion = 2;

    /// <summary>最低兼容存档版本：低于它的存档被拒并指向更新公告。只增不减，是规则常量，mod 改不了。</summary>
    /// <remarks>
    /// 它与 <see cref="CurrentVersion"/> 写在一起，读版本与判兼容是同一步（设计仓
    /// decisions/ADR-0017-版本更新对旧存档的两种后果.md）。能迁就必须迁：只有迁移写不出来时才许升它。
    /// </remarks>
    public const int MinimumCompatibleVersion = 1;

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

    private static readonly JsonDocumentOptions ReadOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
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

    /// <summary>读一份正式存档：先判兼容，再按版本补或拒缺字段，丢掉多出来的字段，拒认不出的分片。</summary>
    /// <returns>每个进正式存档的分片各一份，按标识给；缺的字段已按存档默认值补上，多出来的已丢掉。</returns>
    /// <exception cref="SaveRejectedException">打不开的时候，带着是哪一种。这时没有任何一个分片交出去。</exception>
    public static IReadOnlyDictionary<string, JsonObject> ReadFormalSave(SaveShardRegistry registry, string text) =>
        ReadFormalSave(registry, text, CurrentVersion, MinimumCompatibleVersion);

    /// <summary>同上，版本界限由调用方给。</summary>
    /// <remarks>
    /// 游戏里一律用上面那个；这一个给测试演练「当前是第 N 版、最低兼容是第 M 版」那几种组合，不必等那两个常量真的走到那一步。
    ///
    /// 补与丢都只在分片的第一层：分片里的一格是什么形状由它的持有者定，持有者读的时候自己判。
    /// </remarks>
    public static IReadOnlyDictionary<string, JsonObject> ReadFormalSave(
        SaveShardRegistry registry, string text, int currentVersion, int minimumCompatibleVersion)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(text);

        var root = ParseRoot(text);
        var version = root[VersionKey] is JsonValue versionValue && versionValue.TryGetValue<int>(out var number)
            ? number
            : throw new SaveRejectedException(SaveRejection.Unreadable, $"存档最外层缺「{VersionKey}」，或者它不是一个整数");

        if (version < minimumCompatibleVersion)
        {
            throw new SaveRejectedException(SaveRejection.BelowMinimumVersion,
                $"这份存档是第 {version} 版，这一版的游戏最低只认第 {minimumCompatibleVersion} 版：这一版要求重开档");
        }

        if (version > currentVersion)
        {
            throw new SaveRejectedException(SaveRejection.NewerVersion,
                $"这份存档是第 {version} 版，比这一版的游戏（第 {currentVersion} 版）新：它是更新的游戏写的");
        }

        var saved = root[ShardsKey] as JsonObject
            ?? throw new SaveRejectedException(SaveRejection.Unreadable, $"存档最外层缺「{ShardsKey}」，或者它不是一个对象");

        var formal = registry.Shards.Where(s => s.Side == SaveSide.FormalSave).ToList();
        var unknown = saved.Select(p => p.Key).Where(id => formal.All(s => s.Id != id)).ToList();
        if (unknown.Count > 0)
        {
            throw new SaveRejectedException(SaveRejection.UnknownShard,
                $"存档里有认不出的分片：{string.Join("、", unknown.Select(id => $"「{id}」"))}。它不许被丢掉，所以这份存档先不读");
        }

        var older = version < currentVersion;
        var missing = new List<string>();
        var result = new Dictionary<string, JsonObject>();
        foreach (var shard in formal)
        {
            var defaults = shard.Default();
            if (!saved.TryGetPropertyValue(shard.Id, out var node) || node is null)
            {
                if (older)
                {
                    result[shard.Id] = defaults;
                }
                else
                {
                    missing.Add($"分片「{shard.Id}」整个");
                }

                continue;
            }

            var body = node as JsonObject
                ?? throw new SaveRejectedException(SaveRejection.Unreadable, $"分片「{shard.Id}」不是一个对象");
            var normalized = new JsonObject();
            foreach (var (key, fallback) in defaults)
            {
                if (body.TryGetPropertyValue(key, out var value))
                {
                    normalized[key] = value?.DeepClone();
                }
                else if (older)
                {
                    normalized[key] = fallback?.DeepClone();
                }
                else
                {
                    missing.Add($"分片「{shard.Id}」的「{key}」");
                }
            }

            result[shard.Id] = normalized;
        }

        if (missing.Count > 0)
        {
            throw new SaveRejectedException(SaveRejection.MissingField,
                $"这份存档与这一版的游戏同为第 {version} 版，却缺{string.Join("、", missing)}：同一版不会缺字段，是文件被写坏了");
        }

        return result;
    }

    /// <summary>一份存档里每个分片各占多少字节，按存档里的先后。</summary>
    /// <remarks>
    /// 体积要按分片分摊才说得出是哪一块在长（设计仓 design/存档系统.md 的「存档位摘要与体积实测」一节）。
    /// 每一块按写存档的那套选项单独量一次，所以几块加起来比整份略少：差的是最外层那几个键，和嵌在里面每一行多出来的缩进。
    /// </remarks>
    public static IReadOnlyList<(string Id, int Bytes)> ShardBytes(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var shards = ParseRoot(text)[ShardsKey] as JsonObject
            ?? throw new SaveRejectedException(SaveRejection.Unreadable, $"存档最外层缺「{ShardsKey}」，或者它不是一个对象");
        return [.. shards.Select(p => (p.Key, System.Text.Encoding.UTF8.GetByteCount(p.Value?.ToJsonString(WriteOptions) ?? "null")))];
    }

    private static JsonObject ParseRoot(string text)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(text, documentOptions: ReadOptions);
        }
        catch (JsonException e)
        {
            throw new SaveRejectedException(SaveRejection.Unreadable, $"存档不是一份读得通的 JSON：{e.Message}");
        }

        return root as JsonObject ?? throw new SaveRejectedException(SaveRejection.Unreadable, "存档最外层不是一个对象");
    }
}
