using System.Text.Json.Nodes;
using Tinderhearth.Rules.Foundation.Save;

namespace Tinderhearth.Rules.Economy;

/// <summary>世界状态的那几个分片：日历、那片地与主角的背包。怎么写进存档、怎么读回来、存档默认值是什么。</summary>
/// <remarks>
/// 都进正式存档：跨过一次睡眠还成立（设计仓 design/存档系统.md 的「进哪一侧：判据一句话」一节）。
/// 每个分片只读自己那一块，几块之间没有先后；各自读完之后才拼成一份世界。
///
/// 地块与背包那两块都按项排，每一项一串，串里是这一项在每一格上的值（地块从上到下、每行从左到右，背包按格号）。
/// 这样一项状态就是分片里的一格：以后加一项，旧存档缺的正是这一格，按存档默认值补得上
/// （设计仓 decisions/ADR-0015-存档的版本与缺字段口径.md）。
/// </remarks>
public static class WorldShards
{
    /// <summary>日历那个分片的标识。</summary>
    public const string CalendarId = "calendar";

    /// <summary>那片地那个分片的标识。</summary>
    public const string PlotsId = "plots";

    /// <summary>主角背包那个分片的标识。</summary>
    public const string BagId = "bag";

    private const string YearKey = "year";
    private const string SeasonKey = "season";
    private const string DayKey = "day";

    private const string OriginKey = "origin";
    private const string WidthKey = "width";
    private const string HeightKey = "height";

    private const string StateKey = "state";
    private const string CropIdKey = "cropId";
    private const string StageKey = "stage";
    private const string DaysInStageKey = "daysInStage";
    private const string WateredTodayKey = "wateredToday";
    private const string WateredDaysThisCropKey = "wateredDaysThisCrop";
    private const string DaysThisCropKey = "daysThisCrop";
    private const string DaysFallowKey = "daysFallow";
    private const string MutatedKey = "mutated";

    private const string ItemIdKey = "itemId";
    private const string QualityIdKey = "qualityId";
    private const string CountKey = "count";
    private const string AcquiredKey = "acquired";

    /// <summary>地块那个分片里，一片地的摆法那几格。</summary>
    public static readonly IReadOnlyList<string> PlotLayoutKeys = [OriginKey, WidthKey, HeightKey];

    /// <summary>地块那个分片里，每一项状态各一格。它与 <see cref="Plot"/> 的公开属性一一对应，有测试盯着。</summary>
    public static readonly IReadOnlyList<string> PlotStateKeys =
    [
        StateKey, CropIdKey, StageKey, DaysInStageKey, WateredTodayKey,
        WateredDaysThisCropKey, DaysThisCropKey, DaysFallowKey, MutatedKey,
    ];

    /// <summary>背包那个分片里，每一格的每一项各一串；分片里只有这几串。它与 <see cref="ItemStack"/> 的字段一一对应，有测试盯着。</summary>
    /// <remarks>
    /// 以后给一堆加一项时留意：旧存档缺的那一串按存档默认值补，而默认值是按现在配置的格数写的。存档里的格数比现在少时，
    /// 补上的那一串项数对不上、整份被拒 —— 那时读法要先把补上的那一串截到存档里的格数。
    /// </remarks>
    public static readonly IReadOnlyList<string> BagSlotKeys = [ItemIdKey, QualityIdKey, CountKey, AcquiredKey];

    /// <summary>把这几个分片注册进去，存档默认值取开新档时那一份。</summary>
    /// <param name="registry">要注册进去的那一份。</param>
    /// <param name="newGamePlots">场景里那片地的起始状态。这里当场写成一份，之后它怎么变都不影响默认值。</param>
    /// <param name="newGameBag">开新档时的背包：空的，第一级的格数。旧版本的存档里没有背包那一片，读进来就是这一份。</param>
    public static void Register(SaveShardRegistry registry, PlotGrid newGamePlots, Bag newGameBag)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(newGamePlots);
        ArgumentNullException.ThrowIfNull(newGameBag);

        var calendarDefault = WriteCalendar(Calendar.StartDate);
        var plotsDefault = WritePlots(newGamePlots);
        var bagDefault = WriteBag(newGameBag);
        registry.Register(new(
            Id: CalendarId, Owner: "日历", Side: SaveSide.FormalSave,
            Default: () => (JsonObject)calendarDefault.DeepClone()));
        registry.Register(new(
            Id: PlotsId, Owner: "地块系统", Side: SaveSide.FormalSave,
            Default: () => (JsonObject)plotsDefault.DeepClone()));
        registry.Register(new(
            Id: BagId, Owner: "背包系统", Side: SaveSide.FormalSave,
            Default: () => (JsonObject)bagDefault.DeepClone()));
    }

    /// <summary>一份世界写成几个分片，按标识给。</summary>
    public static Dictionary<string, JsonObject> Write(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);

        return new()
        {
            [CalendarId] = WriteCalendar(world.Date),
            [PlotsId] = WritePlots(world.Plots),
            [BagId] = WriteBag(world.Bag),
        };
    }

    /// <summary>从几个分片读回一份世界。缺一个分片、或者哪一格读不出来，当场报错说清是哪一格。</summary>
    /// <param name="shards">按标识给的分片，先后不限。</param>
    /// <param name="crops">按标识找作物定义。地里那种作物找不到时，那一格读不出来。</param>
    /// <param name="newGameBag">开新档时的背包：现在配置的格数与在册物品都从它读。</param>
    public static WorldState Read(
        IReadOnlyDictionary<string, JsonObject> shards, IReadOnlyDictionary<string, CropDefinition> crops, Bag newGameBag)
    {
        ArgumentNullException.ThrowIfNull(shards);
        ArgumentNullException.ThrowIfNull(crops);
        ArgumentNullException.ThrowIfNull(newGameBag);

        return new WorldState(
            ReadCalendar(Shard(shards, CalendarId)),
            ReadPlots(Shard(shards, PlotsId), crops),
            ReadBag(Shard(shards, BagId), newGameBag));
    }

    /// <summary>日历那一块：年、季、日。</summary>
    public static JsonObject WriteCalendar(GameDate date) => new()
    {
        [YearKey] = date.Year,
        [SeasonKey] = date.Season.ToString(),
        [DayKey] = date.Day,
    };

    /// <summary>读日历那一块。记的不是日历上的一天（第 0 年、一季第 31 天这一类）时被拒。</summary>
    public static GameDate ReadCalendar(JsonObject shard)
    {
        ArgumentNullException.ThrowIfNull(shard);

        const string where = $"分片「{CalendarId}」";
        var year = ShardFields.Int(ShardFields.Required(shard, YearKey, where), $"{where}的「{YearKey}」");
        var season = ShardFields.EnumName<Season>(ShardFields.Required(shard, SeasonKey, where), $"{where}的「{SeasonKey}」");
        var day = ShardFields.Int(ShardFields.Required(shard, DayKey, where), $"{where}的「{DayKey}」");
        try
        {
            return new GameDate(year, season, day);
        }
        catch (ArgumentOutOfRangeException e)
        {
            throw new InvalidDataException($"{where}记的不是日历上的一天：{e.Message}", e);
        }
    }

    /// <summary>那片地那一块：摆法，加上每一项状态各一串。</summary>
    public static JsonObject WritePlots(PlotGrid grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        var records = grid.Cells.Select(c => c.Plot.ToRecord()).ToList();
        return new JsonObject
        {
            [OriginKey] = new JsonObject { ["x"] = grid.Origin.X, ["y"] = grid.Origin.Y },
            [WidthKey] = grid.Width,
            [HeightKey] = grid.Height,
            [StateKey] = Column(records, r => JsonValue.Create(r.State.ToString())),
            [CropIdKey] = Column(records, r => JsonValue.Create(r.CropId)),
            [StageKey] = Column(records, r => JsonValue.Create(r.Stage)),
            [DaysInStageKey] = Column(records, r => JsonValue.Create(r.DaysInStage)),
            [WateredTodayKey] = Column(records, r => JsonValue.Create(r.WateredToday)),
            [WateredDaysThisCropKey] = Column(records, r => JsonValue.Create(r.WateredDaysThisCrop)),
            [DaysThisCropKey] = Column(records, r => JsonValue.Create(r.DaysThisCrop)),
            [DaysFallowKey] = Column(records, r => JsonValue.Create(r.DaysFallow)),
            [MutatedKey] = Column(records, r => JsonValue.Create(r.Mutated)),
        };
    }

    /// <summary>读那片地那一块。哪一格读不出来、或者记的几项彼此矛盾，全部问题一次列出来。</summary>
    public static PlotGrid ReadPlots(JsonObject shard, IReadOnlyDictionary<string, CropDefinition> crops)
    {
        ArgumentNullException.ThrowIfNull(shard);
        ArgumentNullException.ThrowIfNull(crops);

        const string where = $"分片「{PlotsId}」";
        var origin = ShardFields.Object(ShardFields.Required(shard, OriginKey, where), $"{where}的「{OriginKey}」");
        var originCell = new GridCell(
            ShardFields.Int(ShardFields.Required(origin, "x", $"{where}的「{OriginKey}」"), $"{where}的「{OriginKey}」的 x"),
            ShardFields.Int(ShardFields.Required(origin, "y", $"{where}的「{OriginKey}」"), $"{where}的「{OriginKey}」的 y"));
        var width = ShardFields.Int(ShardFields.Required(shard, WidthKey, where), $"{where}的「{WidthKey}」");
        var height = ShardFields.Int(ShardFields.Required(shard, HeightKey, where), $"{where}的「{HeightKey}」");
        var cells = (long)width * height;
        if (width < 1 || height < 1 || cells > int.MaxValue)
        {
            throw new InvalidDataException($"{where}记的是一片 {width}×{height} 的地，两个方向都要至少 1 格");
        }

        var count = (int)cells;
        JsonArray Column(string key) =>
            ShardFields.Array(ShardFields.Required(shard, key, where), count, $"{where}的「{key}」");

        var states = Column(StateKey);
        var cropIds = Column(CropIdKey);
        var stages = Column(StageKey);
        var daysInStage = Column(DaysInStageKey);
        var wateredToday = Column(WateredTodayKey);
        var wateredDays = Column(WateredDaysThisCropKey);
        var daysThisCrop = Column(DaysThisCropKey);
        var daysFallow = Column(DaysFallowKey);
        var mutated = Column(MutatedKey);

        var plots = new List<Plot>(count);
        var problems = new List<string>();
        for (var i = 0; i < count; i++)
        {
            var at = $"{where}第 {new GridCell(originCell.X + (i % width), originCell.Y + (i / width))} 格";
            try
            {
                var record = new PlotRecord(
                    State: ShardFields.EnumName<PlotState>(states[i], $"{at}的「{StateKey}」"),
                    CropId: ShardFields.StringOrNull(cropIds[i], $"{at}的「{CropIdKey}」"),
                    Stage: ShardFields.Int(stages[i], $"{at}的「{StageKey}」"),
                    DaysInStage: ShardFields.Int(daysInStage[i], $"{at}的「{DaysInStageKey}」"),
                    WateredToday: ShardFields.Bool(wateredToday[i], $"{at}的「{WateredTodayKey}」"),
                    WateredDaysThisCrop: ShardFields.Int(wateredDays[i], $"{at}的「{WateredDaysThisCropKey}」"),
                    DaysThisCrop: ShardFields.Int(daysThisCrop[i], $"{at}的「{DaysThisCropKey}」"),
                    DaysFallow: ShardFields.Int(daysFallow[i], $"{at}的「{DaysFallowKey}」"),
                    Mutated: ShardFields.Bool(mutated[i], $"{at}的「{MutatedKey}」"));
                var crop = record.CropId is string id && crops.TryGetValue(id, out var definition) ? definition : null;
                var contradictions = Plot.Problems(record, crop);
                if (contradictions.Count > 0)
                {
                    problems.Add($"{at}：{string.Join("；", contradictions)}");
                    continue;
                }

                plots.Add(Plot.Restore(record, crop));
            }
            catch (InvalidDataException e)
            {
                problems.Add(e.Message);
            }
        }

        return problems.Count == 0
            ? PlotGrid.FromRows(originCell, width, height, plots)
            : throw new InvalidDataException($"{where}有 {problems.Count} 处读不出来：\n  " + string.Join("\n  ", problems));
    }

    /// <summary>主角背包那一块：每一格的每一项各一串，按格号排。空格那一格的种类与品质写 null，件数与序号写 0。</summary>
    /// <remarks>
    /// 格数就是每一串的项数，之后新进的东西排在哪个序号后面取各堆里最大的那个：两样都由这几串算得出来，所以不另存
    /// （设计仓 design/存档系统.md 的「派生量不单独存」一节）。
    /// </remarks>
    public static JsonObject WriteBag(Bag bag)
    {
        ArgumentNullException.ThrowIfNull(bag);

        var slots = bag.Slots;
        return new JsonObject
        {
            [ItemIdKey] = new JsonArray([.. slots.Select(s => JsonValue.Create(s?.ItemId))]),
            [QualityIdKey] = new JsonArray([.. slots.Select(s => JsonValue.Create(s?.QualityId))]),
            [CountKey] = new JsonArray([.. slots.Select(s => (JsonNode)JsonValue.Create(s?.Count ?? 0))]),
            [AcquiredKey] = new JsonArray([.. slots.Select(s => (JsonNode)JsonValue.Create(s?.AcquiredSequence ?? 0))]),
        };
    }

    /// <summary>
    /// 读主角背包那一块。格数取种类那一串的项数，别的几串不一样长时被拒。存档里的格数比现在配置的多时被拒，说清两边各是几格；
    /// 比现在的少就在后面补空格。物品认不出、品质对不上、件数或序号不对，全部问题一次列出来，点名哪一格。
    /// </summary>
    /// <param name="shard">背包那一块。</param>
    /// <param name="newGameBag">开新档时的背包：现在配置的格数与在册物品都从它读。</param>
    public static Bag ReadBag(JsonObject shard, Bag newGameBag)
    {
        ArgumentNullException.ThrowIfNull(shard);
        ArgumentNullException.ThrowIfNull(newGameBag);

        const string where = $"分片「{BagId}」";
        var itemIds = ShardFields.Array(ShardFields.Required(shard, ItemIdKey, where), $"{where}的「{ItemIdKey}」");
        var savedSlots = itemIds.Count;
        if (savedSlots < 1)
        {
            throw new InvalidDataException($"{where}记的背包一格都没有，至少要有 1 格");
        }

        if (savedSlots > newGameBag.SlotCount)
        {
            throw new InvalidDataException(
                $"{where}记的背包有 {savedSlots} 格，而现在配置的背包只有 {newGameBag.SlotCount} 格 —— 多出来的那几格没有地方放");
        }

        JsonArray Column(string key) =>
            ShardFields.Array(ShardFields.Required(shard, key, where), savedSlots, $"{where}的「{key}」");

        var qualityIds = Column(QualityIdKey);
        var counts = Column(CountKey);
        var acquired = Column(AcquiredKey);

        var slots = new ItemStack?[newGameBag.SlotCount];
        var problems = new List<string>();
        for (var i = 0; i < savedSlots; i++)
        {
            var at = $"{where}第 {i + 1} 格";
            try
            {
                var itemId = ShardFields.StringOrNull(itemIds[i], $"{at}的「{ItemIdKey}」");
                var qualityId = ShardFields.StringOrNull(qualityIds[i], $"{at}的「{QualityIdKey}」");
                var count = ShardFields.Int(counts[i], $"{at}的「{CountKey}」");
                var sequence = ShardFields.Int(acquired[i], $"{at}的「{AcquiredKey}」");
                if (itemId is null)
                {
                    if (qualityId is not null || count != 0 || sequence != 0)
                    {
                        problems.Add($"{at}是空格，品质、件数与序号却不是空的（品质 {qualityId ?? "null"}、{count} 件、序号 {sequence}）");
                    }

                    continue;
                }

                slots[i] = new ItemStack(itemId, qualityId, count, sequence);
            }
            catch (InvalidDataException e)
            {
                problems.Add(e.Message);
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException($"{where}有 {problems.Count} 处读不出来：\n  " + string.Join("\n  ", problems));
        }

        try
        {
            return Bag.Restore(slots, newGameBag.Items);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{where}：{e.Message}", e);
        }
    }

    private static JsonObject Shard(IReadOnlyDictionary<string, JsonObject> shards, string id) =>
        shards.TryGetValue(id, out var shard) && shard is not null
            ? shard
            : throw new InvalidDataException($"存档里缺分片「{id}」");

    private static JsonArray Column(IEnumerable<PlotRecord> records, Func<PlotRecord, JsonNode?> cell) =>
        new([.. records.Select(cell)]);
}
