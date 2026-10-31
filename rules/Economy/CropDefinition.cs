using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Economy;

/// <summary>一种作物的定义，来自数据文件而不是代码。</summary>
/// <remarks>
/// 字段表在设计仓 design/地块系统.md 的「作物定义：一张表，缺字段报错」一节，本类不复述。素材
/// 那几张图（每个阶段各一张，再加一张变异成熟、一张枯死）由作者在 Godot 里配，所以规则层看不见任何
/// 图片路径；「缺哪张报错」由引擎层读出每一格配没配，再交给 <see cref="CropArtCheck"/> 一次核完。
///
/// 收几件与浇水系数不在这里。那两个量的值还没定，要实机试，所以由调用方传一份
/// <see cref="HarvestYieldRule"/> 进来，代码不留能用的默认值。本类只持有阶段天数这一类由内容
/// 决定的数。
///
/// 本类用 <c>required</c> 属性而不是位置参数 record：<c>System.Text.Json</c> 给位置参数 record
/// 反序列化时缺字段不报错、会拿 <c>default</c> 填（有测试实测这条），而
/// <see cref="RegrowFromStage"/> 的 <c>null</c> 是合法值，构造校验分不出缺键与显式写 null。
/// </remarks>
public sealed record CropDefinition
{
    public const string ContentDirectory = "crops";

    /// <summary>一种作物至少要有几个计时阶段。</summary>
    /// <remarks>
    /// 阶段序列是「声明的那几个计时阶段，加上成熟」，第一个必须是种子、最后一个必须是成熟，所以
    /// 最少两个阶段：种子到成熟。一个计时阶段都没有的声明被拒，那样的作物播下去当天就待收。
    /// </remarks>
    public const int MinTimedStages = 1;

    /// <summary>稳定标识。</summary>
    public required string Id
    {
        get;
        init => field = NonEmpty(value, nameof(Id));
    }

    /// <summary>
    /// 每个计时阶段各持续几天，顺序即生长顺序，每项至少 1 天；
    /// 项数就是这一条作物有几个计时阶段，由它自己声明（至少 <see cref="MinTimedStages"/> 项）。
    /// 成熟那一阶段不在其内 —— 它是终态、不带天数。
    /// </summary>
    /// <remarks>
    /// 项数不写死，所以速生的菜与慢熟的药草各按自己的阶段数声明。一条作物最快几天成熟由它自己
    /// 这一项加起来算出来（<see cref="DaysToFirstRipe"/>），全库没有一个「最短生长周期」的数 ——
    /// 那种数只会被抄到别处去然后抄错。
    /// </remarks>
    public required IReadOnlyList<int> StageDays
    {
        get;
        init => field = CheckStageDays(value);
    }

    /// <summary>成熟那一阶段的序号（0 起）。它由这一条作物自己的阶段数算出来，不是全局常量。</summary>
    /// <remarks>阶段序列是「计时阶段，加上成熟」，所以成熟的序号就等于计时阶段的个数。</remarks>
    public int RipeStage => StageDays.Count;

    /// <summary>这一条作物一共有几个阶段。素材张数是它再加一张枯死图。</summary>
    public int StageCount => StageDays.Count + 1;

    /// <summary>
    /// 收获后退回第几阶段（0 起）；<c>null</c> 表示一次性作物，收完地就空了。
    /// </summary>
    /// <remarks>
    /// 不许填成熟那一阶段或更后：退回去之后立刻又是待收，等于一次收获就能无限收下去。
    ///
    /// 这条上界要读 <see cref="RipeStage"/>，而它由 <see cref="StageDays"/> 算出来，跨字段的校验
    /// 在 <c>init</c> 里做不到（属性赋值顺序不保证），所以它落在 <see cref="Parse"/> —— 内容都从
    /// 数据文件走那个入口，直接 <c>new</c> 只出现在测试里。
    ///
    /// 再生间隔没有单独的字段：退回阶段 N 之后照 <see cref="StageDays"/> 里第 N 项往前走，
    /// 而那一项的含义本来就是「从这一阶段到下一阶段要几天」。
    /// </remarks>
    public required int? RegrowFromStage
    {
        get;
        init => field = CheckRegrowNonNegative(value);
    }

    /// <summary>它属于哪几个季节。换季那天新季节不在其中就枯死。</summary>
    /// <remarks>
    /// 单季、跨季与全年是同一条判定的三种填法，所以没有「是不是跨季」那种开关。
    ///
    /// 有一条规则现在还校验不到：跨季的作物不能是粮食。等物品类别里有粮食那一类，「多个季节
    /// 加产出粮食」就该在载入时被拒。
    /// </remarks>
    public required IReadOnlyList<Season> Seasons
    {
        get;
        init => field = CheckSeasons(value);
    }

    /// <summary>播种用掉哪一种物品。存的是标识，与 <see cref="YieldItemId"/> 同一个形状。</summary>
    /// <remarks>
    /// 它指的那一样必须声明无品质：一颗种子长成什么、收上来什么品质由作物、浇水、技能与变异定，种子自己没有做得好坏之分
    /// （设计仓 design/物品系统.md 的「有些物品没有品质这条轴」一节）。指不到在册物品、指到有品质的，<see cref="LoadAll"/> 报。
    /// </remarks>
    public required string SeedItemId
    {
        get;
        init => field = NonEmpty(value, nameof(SeedItemId));
    }

    /// <summary>产出哪一种物品。存的是标识，物品本身的定义在物品系统那一侧。</summary>
    /// <remarks>
    /// 它指的那一样必须声明有品质：变了的那一株收上来是变异品质（设计仓 decisions/ADR-0030-作物成熟时按几率变异.md），
    /// 没有品质这条轴就装不下它。指不到在册物品、指到无品质的，<see cref="LoadAll"/> 报。
    /// </remarks>
    public required string YieldItemId
    {
        get;
        init => field = NonEmpty(value, nameof(YieldItemId));
    }

    /// <summary>这种作物叫什么：文本表里它产出的那一样物品的名字那一条。</summary>
    /// <remarks>
    /// 作物没有自己的名字与描述。地里那一株叫什么、图鉴里怎么写，都读它产出的那一样物品 —— 作物的正式名字归物品定义的
    /// 名称那一格（设计仓 design/数值模型.md 的「不占命名权」那一条），两处各写一份就是同一个名字的两个家，改了一处
    /// 另一处不报错。
    /// </remarks>
    public string NameKeyIn(ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(items);
        return items.Items.TryGetValue(YieldItemId, out var item)
            ? item.NameKey
            : throw new InvalidOperationException(
                $"作物 {Id} 产出的「{YieldItemId}」不在册 —— 作物要经 {nameof(LoadAll)} 读，那一步会先报出这一条");
    }

    /// <summary>这种作物收获之后会退回生长中途、能反复收吗？</summary>
    public bool Regrows => RegrowFromStage is not null;

    /// <summary>从播种到第一次成熟要几天。</summary>
    public int DaysToFirstRipe => StageDays.Sum();

    /// <summary>从退回的那一阶段长回成熟要几天；一次性作物没有这个量。</summary>
    public int? DaysToRegrow =>
        RegrowFromStage is int back ? StageDays.Skip(back).Sum() : null;

    public static CropDefinition Parse(string json, string whatForDiagnostics)
    {
        var crop = ContentJson.Parse<CropDefinition>(json, whatForDiagnostics);
        crop.CheckRegrowBelowRipe();
        return crop;
    }

    /// <summary>读内容目录里的全部作物定义，按标识建表，并核种子与产出那两格都指到在册的物品。</summary>
    /// <remarks>
    /// 启动时与农场里读的是同一处，所以「怎么读、什么算坏」只有这一份。两份文件用了同一个标识在
    /// <see cref="ContentCatalog.ResolveById"/> 里当场报错，不让后读到的那份静默顶掉前一份。
    ///
    /// 物品要先读：参数里要一份 <see cref="ItemCatalog"/>，所以读作物时不可能跳过这一次核对。指不到的、品质那一位不对的，
    /// 一次全报，一行一处，点名是哪种作物的哪一格。一份作物都没有也抛：农场里至少要能种一种。
    /// </remarks>
    public static IReadOnlyDictionary<string, CropDefinition> LoadAll(ContentCatalog catalog, ItemCatalog items)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(items);

        var crops = catalog.ResolveById(ContentDirectory, e => Parse(e.Text, e.RelativePath), c => c.Id);
        if (crops.Count == 0)
        {
            throw new InvalidDataException(
                $"内容目录 {ContentDirectory}/ 下没有作物定义，至少要有一份 .json。"
                    + "字段表在设计仓 design/地块系统.md 的「作物定义：一张表，缺字段报错」一节");
        }

        var problems = crops.Values
            .OrderBy(c => c.Id, StringComparer.Ordinal)
            .SelectMany(c => c.ItemProblems(items))
            .Concat(SharedSeedProblems(crops.Values))
            .ToList();
        return problems.Count == 0
            ? crops
            : throw new InvalidDataException(
                $"作物指的物品对不上，共 {problems.Count} 处：{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    /// <summary>按种子那一样物品找作物：手上拿的是哪一份种子，播下去就是哪一种作物。</summary>
    /// <remarks>一份种子只长一种作物，两种作物用了同一份种子在 <see cref="LoadAll"/> 那一步就被拒，所以这张表里一份种子只对一种。</remarks>
    public static IReadOnlyDictionary<string, CropDefinition> BySeed(IReadOnlyDictionary<string, CropDefinition> crops)
    {
        ArgumentNullException.ThrowIfNull(crops);

        var problems = SharedSeedProblems(crops.Values).ToList();
        return problems.Count == 0
            ? crops.Values.ToDictionary(c => c.SeedItemId, StringComparer.Ordinal)
            : throw new InvalidDataException(string.Join(Environment.NewLine, problems));
    }

    /// <summary>两种以上作物用了同一份种子，各报一句：手上拿着那一份时播下去是哪一种，答不出来。</summary>
    private static IEnumerable<string> SharedSeedProblems(IEnumerable<CropDefinition> crops) =>
        crops
            .GroupBy(c => c.SeedItemId, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"作物「{string.Join("」「", g.Select(c => c.Id).Order(StringComparer.Ordinal))}」用的是同一份种子「{g.Key}」"
                + " —— 一份种子只长一种作物");

    /// <summary>种子与产出那两格：指不到在册物品的，以及品质那一位不对的，各报一句。</summary>
    private IEnumerable<string> ItemProblems(ItemCatalog items)
    {
        if (!items.Items.TryGetValue(SeedItemId, out var seed))
        {
            yield return $"作物「{Id}」的 {nameof(SeedItemId)}「{SeedItemId}」不在册";
        }
        else if (seed.HasQuality)
        {
            yield return $"作物「{Id}」的 {nameof(SeedItemId)}「{SeedItemId}」声明了有品质 —— 种子没有做得好坏之分，要声明无品质";
        }

        if (!items.Items.TryGetValue(YieldItemId, out var produce))
        {
            yield return $"作物「{Id}」的 {nameof(YieldItemId)}「{YieldItemId}」不在册";
        }
        else if (!produce.HasQuality)
        {
            yield return $"作物「{Id}」的 {nameof(YieldItemId)}「{YieldItemId}」声明了无品质 —— 变了的那一株收上来是变异品质，要声明有品质";
        }
    }

    /// <summary>跨字段校验：退回的那一阶段要落在成熟之前。理由见 <see cref="RegrowFromStage"/>。</summary>
    private void CheckRegrowBelowRipe()
    {
        if (RegrowFromStage is int back && back >= RipeStage)
        {
            throw new ArgumentOutOfRangeException(
                nameof(RegrowFromStage),
                $"作物定义 {Id} 的 {nameof(RegrowFromStage)} 是 {back}，而成熟那一阶段是 {RipeStage}"
                    + "：退回成熟或更后等于一次收获就能无限收下去");
        }
    }

    private static string NonEmpty(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"作物定义的 {field} 不得为空", field)
            : value;

    private static IReadOnlyList<int> CheckStageDays(IReadOnlyList<int> value)
    {
        if (value is null || value.Count < MinTimedStages)
        {
            throw new ArgumentException(
                $"作物定义的 {nameof(StageDays)} 至少要有 {MinTimedStages} 项"
                    + $"（成熟那一阶段是终态、不带天数），实际 {value?.Count.ToString() ?? "缺失"}",
                nameof(StageDays));
        }

        var bad = Enumerable.Range(0, value.Count).FirstOrDefault(i => value[i] < 1, -1);
        return bad < 0
            ? value
            : throw new ArgumentOutOfRangeException(
                nameof(StageDays),
                $"作物定义的 {nameof(StageDays)} 第 {bad} 项必须至少 1 天，实际 {value[bad]}");
    }

    private static int? CheckRegrowNonNegative(int? value) => value switch
    {
        null or >= 0 => value,
        _ => throw new ArgumentOutOfRangeException(
            nameof(RegrowFromStage),
            $"作物定义的 {nameof(RegrowFromStage)} 要么是 null（一次性），要么不为负，实际 {value}"),
    };

    private static IReadOnlyList<Season> CheckSeasons(IReadOnlyList<Season> value) =>
        value is { Count: > 0 }
            ? value
            : throw new ArgumentException(
                $"作物定义的 {nameof(Seasons)} 至少要有一个季节，否则它种下去当天就枯死",
                nameof(Seasons));
}
