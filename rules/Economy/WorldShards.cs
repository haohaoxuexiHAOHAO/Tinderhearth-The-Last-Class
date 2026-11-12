using System.Text.Json.Nodes;
using Tinderhearth.Rules.Foundation.Save;

namespace Tinderhearth.Rules.Economy;

/// <summary>世界状态的那几个分片：日历、那片地与主角的背包。怎么写进存档、怎么读回来、存档默认值是什么。</summary>
/// <remarks>
/// 都进正式存档：跨过一次睡眠还成立（设计仓 design/存档系统.md 的「进哪一侧：判据一句话」一节）。
/// 每个分片只读自己那一块，几块之间没有先后；各自读完之后才拼成一份世界。
///
/// 地块与背包那两块都按项排，每一项一串（地块是每一格上的值，从上到下、每行从左到右；背包是随身栏每一格、网格里每一堆）。
/// 这样一项状态就是分片里的一格：以后加一项，旧存档缺的正是这一格，按存档默认值补（网格里那几堆补不上，见
/// <see cref="BagGridStackKeys"/>）；形状改了的那一版，旧档由那个分片声明的升级迁过来（设计仓
/// decisions/ADR-0015-存档的版本与缺字段口径.md）。
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

    private const string CarryItemIdKey = "carryItemId";
    private const string CarryQualityIdKey = "carryQualityId";
    private const string CarryCountKey = "carryCount";
    private const string CarryAcquiredKey = "carryAcquired";
    private const string GridWidthKey = "gridWidth";
    private const string GridHeightKey = "gridHeight";
    private const string GridItemIdKey = "gridItemId";
    private const string GridQualityIdKey = "gridQualityId";
    private const string GridCountKey = "gridCount";
    private const string GridAcquiredKey = "gridAcquired";
    private const string GridXKey = "gridX";
    private const string GridYKey = "gridY";

    // 改成网格之前那一版（存档第 2 版）的背包：一格一项，随身栏是开头那几格。只在升级时读。
    private const int LastFlatBagVersion = 2;
    private const string FlatItemIdKey = "itemId";
    private const string FlatQualityIdKey = "qualityId";
    private const string FlatCountKey = "count";
    private const string FlatAcquiredKey = "acquired";

    /// <summary>地块那个分片里，一片地的摆法那几格。</summary>
    public static readonly IReadOnlyList<string> PlotLayoutKeys = [OriginKey, WidthKey, HeightKey];

    /// <summary>地块那个分片里，每一项状态各一格。它与 <see cref="Plot"/> 的公开属性一一对应，有测试盯着。</summary>
    public static readonly IReadOnlyList<string> PlotStateKeys =
    [
        StateKey, CropIdKey, StageKey, DaysInStageKey, WateredTodayKey,
        WateredDaysThisCropKey, DaysThisCropKey, DaysFallowKey, MutatedKey,
    ];

    /// <summary>背包那个分片里，随身栏那一排每一格的每一项各一串。它与 <see cref="ItemStack"/> 的字段一一对应，有测试盯着。</summary>
    public static readonly IReadOnlyList<string> BagCarryKeys = [CarryItemIdKey, CarryQualityIdKey, CarryCountKey, CarryAcquiredKey];

    /// <summary>背包那个分片里，网格几格宽、几格高那两格。</summary>
    public static readonly IReadOnlyList<string> BagGridSizeKeys = [GridWidthKey, GridHeightKey];

    /// <summary>背包那个分片里，网格里每一堆的每一项各一串：一堆的那几个字段，加它左上角那一格在第几列、第几行。</summary>
    /// <remarks>
    /// 以后给一堆加一项时留意：比它旧的存档缺那一串，按存档默认值补上的是一串空的，项数对不上、整份被拒 ——
    /// 所以那一版要在背包那个分片的升级里给旧档补上那一串（<see cref="Register"/>）。
    /// </remarks>
    public static readonly IReadOnlyList<string> BagGridStackKeys =
        [GridItemIdKey, GridQualityIdKey, GridCountKey, GridAcquiredKey, GridXKey, GridYKey];

    /// <summary>改成网格之前那一版的背包那一块：一格一项，种类、品质、件数、序号各一串，随身栏是开头那几格。</summary>
    private static readonly IReadOnlyList<string> FlatBagKeys = [FlatItemIdKey, FlatQualityIdKey, FlatCountKey, FlatAcquiredKey];

    /// <summary>把这几个分片注册进去，存档默认值取开新档时那一份。</summary>
    /// <param name="registry">要注册进去的那一份。</param>
    /// <param name="newGamePlots">场景里那片地的起始状态。这里当场写成一份，之后它怎么变都不影响默认值。</param>
    /// <param name="newGameBag">
    /// 开新档时的背包：空的，第一级那么大。旧版本的存档里没有背包那一片，读进来就是这一份；改成网格之前那一版的背包那一片，
    /// 升级时照它的网格多大、随身栏几格摆。
    /// </param>
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
            Default: () => (JsonObject)bagDefault.DeepClone(),
            Upgrade: (version, saved) => UpgradeBag(version, saved, newGameBag)));
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

    /// <summary>
    /// 主角背包那一块：随身栏那一排一格一项，网格几格宽、几格高，网格里一堆一项、带着它左上角那一格在第几列第几行，
    /// 按左上角从上到下、从左到右排。随身栏的空格种类与品质写 null、件数与序号写 0；网格里的空格不记。
    /// </summary>
    /// <remarks>
    /// 位置与网格多大都写明，读回来不靠项数推：配置里的网格后来变大了，每一堆照旧在原来那一列那一行。
    /// 之后新进的东西排在哪个序号后面取各堆里最大的那个，不另存（设计仓 design/存档系统.md 的「派生量不单独存」一节）。
    /// </remarks>
    public static JsonObject WriteBag(Bag bag)
    {
        ArgumentNullException.ThrowIfNull(bag);
        return BagJson(bag.Carry, bag.GridWidth, bag.GridHeight, bag.Slots);
    }

    /// <summary>
    /// 读主角背包那一块。存档里的网格比现在配置的宽或高、随身栏比现在的格数多，被拒，说清两边各多大；小的照现在的大小读，
    /// 每一堆还在原来那一列那一行，随身栏后面补空格。
    /// </summary>
    /// <remarks>
    /// 别的问题分两步报，每一步一次列全：先是读不出来的 —— 哪一项类型不对、随身栏的空格却记着东西、网格里一堆没有物品标识、
    /// 左上角不在存档里那张网格里、两堆的左上角在同一格，点名随身栏第几格、网格里第几堆；都读得出来，再是与物品定义和网格
    /// 对不上的 —— 物品认不出、品质对不上、件数或序号不对、随身栏里放着占不止一格的东西、出了网格或压在一起，由
    /// <see cref="Bag"/> 读回那一步点名随身栏第几格、网格里左上角在哪一格的那一堆。
    /// </remarks>
    /// <param name="shard">背包那一块，现在的形状。改成网格之前那一版的，读档时已经照背包那个分片的升级迁过来。</param>
    /// <param name="newGameBag">开新档时的背包：现在配置的网格多大、随身栏几格与在册物品都从它读。</param>
    public static Bag ReadBag(JsonObject shard, Bag newGameBag)
    {
        ArgumentNullException.ThrowIfNull(shard);
        ArgumentNullException.ThrowIfNull(newGameBag);

        const string where = $"分片「{BagId}」";
        var width = ShardFields.Int(ShardFields.Required(shard, GridWidthKey, where), $"{where}的「{GridWidthKey}」");
        var height = ShardFields.Int(ShardFields.Required(shard, GridHeightKey, where), $"{where}的「{GridHeightKey}」");
        if (width < 1 || height < 1)
        {
            throw new InvalidDataException($"{where}记的网格是 {width}×{height}，至少要 1×1");
        }

        if (width > newGameBag.GridWidth || height > newGameBag.GridHeight)
        {
            throw new InvalidDataException(
                $"{where}记的网格是 {width}×{height}，而现在配置的网格是 {newGameBag.GridWidth}×{newGameBag.GridHeight}"
                    + " —— 多出来的那一片没有地方放");
        }

        var problems = new List<string>();
        var savedCarry = ReadCells(shard, BagCarryKeys, where, "随身栏", problems);
        if (savedCarry.Length > newGameBag.CarryCount)
        {
            throw new InvalidDataException(
                $"{where}记的随身栏有 {savedCarry.Length} 格，而现在的随身栏只有 {newGameBag.CarryCount} 格 —— 多出来的那几格没有地方放");
        }

        var anchors = ReadGridStacks(shard, width, height, where, problems);
        if (problems.Count > 0)
        {
            throw new InvalidDataException($"{where}有 {problems.Count} 处读不出来：\n  " + string.Join("\n  ", problems));
        }

        var carry = new ItemStack?[newGameBag.CarryCount];
        savedCarry.CopyTo(carry, 0);
        try
        {
            // 先照存档里那张网格核一遍（出没出网格、压没压在一起），再照现在的大小摆：每一堆还在原来那一列那一行。
            var saved = Bag.Restore(carry, width, height, anchors, newGameBag.Items);
            if (width == newGameBag.GridWidth && height == newGameBag.GridHeight)
            {
                return saved;
            }

            var resized = new ItemStack?[newGameBag.SlotCount];
            for (var cell = 0; cell < anchors.Length; cell++)
            {
                resized[(cell / width * newGameBag.GridWidth) + (cell % width)] = anchors[cell];
            }

            return Bag.Restore(carry, newGameBag.GridWidth, newGameBag.GridHeight, resized, newGameBag.Items);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{where}：{e.Message}", e);
        }
    }

    /// <summary>
    /// 背包那个分片的升级：改成网格之前那一版（一格一项、随身栏是开头那几格）变成现在的形状。开头那几格（随身栏几格那么多）
    /// 进随身栏那一排，其余的按原来的格号从左上往右下摆进现在配置的网格（设计仓 design/背包系统.md「下游同步」那一节）。
    /// </summary>
    /// <remarks>
    /// 原来的格数比随身栏加网格还多就被拒，说清两边各几格：多出来的那几格没有地方放，而任何入口都不自动丢弃。
    /// 那一版的东西全占 1 格，所以按格号摆不会压在一起；物品认不出、件数不对这些留给读的那一步一起报。
    /// 比那一版新的就是现在这个形状：别的分片改形状、版本往上走时，这一片原样交回去。
    /// </remarks>
    private static JsonObject UpgradeBag(int version, JsonObject saved, Bag newGameBag)
    {
        if (version > LastFlatBagVersion)
        {
            return saved;
        }

        const string where = $"分片「{BagId}」（改成网格之前那一版）";
        var problems = new List<string>();
        var cells = ReadCells(saved, FlatBagKeys, where, "", problems);
        var room = newGameBag.CarryCount + newGameBag.SlotCount;
        if (cells.Length > room)
        {
            throw new InvalidDataException(
                $"{where}记的背包有 {cells.Length} 格，而现在的背包只有 {room} 格（随身栏 {newGameBag.CarryCount} 格、"
                    + $"网格 {newGameBag.GridWidth}×{newGameBag.GridHeight}）—— 多出来的那几格没有地方放");
        }

        if (problems.Count > 0)
        {
            throw new InvalidDataException($"{where}有 {problems.Count} 处读不出来：\n  " + string.Join("\n  ", problems));
        }

        var carry = new ItemStack?[newGameBag.CarryCount];
        var anchors = new ItemStack?[newGameBag.SlotCount];
        for (var i = 0; i < cells.Length; i++)
        {
            if (i < carry.Length)
            {
                carry[i] = cells[i];
            }
            else
            {
                anchors[i - carry.Length] = cells[i];
            }
        }

        return BagJson(carry, newGameBag.GridWidth, newGameBag.GridHeight, anchors);
    }

    /// <summary>照现在的形状写一份背包那一块，见 <see cref="WriteBag"/>。</summary>
    /// <param name="carry">随身栏那一排每一格的那一堆。</param>
    /// <param name="width">网格几格宽。</param>
    /// <param name="height">网格几格高。</param>
    /// <param name="anchors">网格每一格记的那一堆，按格号排：一堆记在它左上角那一格。</param>
    private static JsonObject BagJson(IReadOnlyList<ItemStack?> carry, int width, int height, IReadOnlyList<ItemStack?> anchors)
    {
        var stacks = Enumerable.Range(0, anchors.Count)
            .Where(cell => anchors[cell] is not null)
            .Select(cell => (Stack: anchors[cell]!.Value, X: cell % width, Y: cell / width))
            .ToList();
        return new JsonObject
        {
            [CarryItemIdKey] = new JsonArray([.. carry.Select(s => JsonValue.Create(s?.ItemId))]),
            [CarryQualityIdKey] = new JsonArray([.. carry.Select(s => JsonValue.Create(s?.QualityId))]),
            [CarryCountKey] = new JsonArray([.. carry.Select(s => (JsonNode)JsonValue.Create(s?.Count ?? 0))]),
            [CarryAcquiredKey] = new JsonArray([.. carry.Select(s => (JsonNode)JsonValue.Create(s?.AcquiredSequence ?? 0))]),
            [GridWidthKey] = width,
            [GridHeightKey] = height,
            [GridItemIdKey] = new JsonArray([.. stacks.Select(s => JsonValue.Create(s.Stack.ItemId))]),
            [GridQualityIdKey] = new JsonArray([.. stacks.Select(s => JsonValue.Create(s.Stack.QualityId))]),
            [GridCountKey] = new JsonArray([.. stacks.Select(s => (JsonNode)JsonValue.Create(s.Stack.Count))]),
            [GridAcquiredKey] = new JsonArray([.. stacks.Select(s => (JsonNode)JsonValue.Create(s.Stack.AcquiredSequence))]),
            [GridXKey] = new JsonArray([.. stacks.Select(s => (JsonNode)JsonValue.Create(s.X))]),
            [GridYKey] = new JsonArray([.. stacks.Select(s => (JsonNode)JsonValue.Create(s.Y))]),
        };
    }

    /// <summary>
    /// 一格一项的那四串（种类、品质、件数、序号，按 <paramref name="keys"/> 的先后）：每一格是一堆或者空格，四串要一样长。
    /// 空格却记着东西、哪一项读不出来，记进 <paramref name="problems"/>，格号从 1 数。
    /// </summary>
    private static ItemStack?[] ReadCells(
        JsonObject shard, IReadOnlyList<string> keys, string where, string row, List<string> problems)
    {
        var itemIds = ShardFields.Array(ShardFields.Required(shard, keys[0], where), $"{where}的「{keys[0]}」");
        JsonArray Column(string key) =>
            ShardFields.Array(ShardFields.Required(shard, key, where), itemIds.Count, $"{where}的「{key}」");

        var qualityIds = Column(keys[1]);
        var counts = Column(keys[2]);
        var acquired = Column(keys[3]);

        var cells = new ItemStack?[itemIds.Count];
        for (var i = 0; i < cells.Length; i++)
        {
            var at = $"{where}{row}第 {i + 1} 格";
            try
            {
                var itemId = ShardFields.StringOrNull(itemIds[i], $"{at}的「{keys[0]}」");
                var qualityId = ShardFields.StringOrNull(qualityIds[i], $"{at}的「{keys[1]}」");
                var count = ShardFields.Int(counts[i], $"{at}的「{keys[2]}」");
                var sequence = ShardFields.Int(acquired[i], $"{at}的「{keys[3]}」");
                if (itemId is not null)
                {
                    cells[i] = new ItemStack(itemId, qualityId, count, sequence);
                }
                else if (qualityId is not null || count != 0 || sequence != 0)
                {
                    problems.Add($"{at}是空格，品质、件数与序号却不是空的（品质 {qualityId ?? "null"}、{count} 件、序号 {sequence}）");
                }
            }
            catch (InvalidDataException e)
            {
                problems.Add(e.Message);
            }
        }

        return cells;
    }

    /// <summary>
    /// 网格里那几堆，摆成存档里那张网格每一格记的那一堆（一堆记在它左上角那一格）。几串要一样长；一堆没有物品标识、
    /// 左上角不在那张网格里、两堆的左上角在同一格、哪一项读不出来，记进 <paramref name="problems"/>，点名第几堆（从 1 数）。
    /// </summary>
    private static ItemStack?[] ReadGridStacks(JsonObject shard, int width, int height, string where, List<string> problems)
    {
        var itemIds = ShardFields.Array(ShardFields.Required(shard, GridItemIdKey, where), $"{where}的「{GridItemIdKey}」");
        JsonArray Column(string key) =>
            ShardFields.Array(ShardFields.Required(shard, key, where), itemIds.Count, $"{where}的「{key}」");

        var qualityIds = Column(GridQualityIdKey);
        var counts = Column(GridCountKey);
        var acquired = Column(GridAcquiredKey);
        var xs = Column(GridXKey);
        var ys = Column(GridYKey);

        var anchors = new ItemStack?[width * height];
        var owners = new int?[width * height];
        for (var k = 0; k < itemIds.Count; k++)
        {
            var at = $"{where}网格里第 {k + 1} 堆";
            try
            {
                var itemId = ShardFields.StringOrNull(itemIds[k], $"{at}的「{GridItemIdKey}」");
                var qualityId = ShardFields.StringOrNull(qualityIds[k], $"{at}的「{GridQualityIdKey}」");
                var count = ShardFields.Int(counts[k], $"{at}的「{GridCountKey}」");
                var sequence = ShardFields.Int(acquired[k], $"{at}的「{GridAcquiredKey}」");
                var x = ShardFields.Int(xs[k], $"{at}的「{GridXKey}」");
                var y = ShardFields.Int(ys[k], $"{at}的「{GridYKey}」");
                if (itemId is null)
                {
                    problems.Add($"{at}没有物品标识 —— 网格里只记有东西的那几堆");
                    continue;
                }

                if (x < 0 || x >= width || y < 0 || y >= height)
                {
                    problems.Add($"{at}（「{itemId}」）的左上角 ({x}, {y}) 不在存档里 {width}×{height} 的网格里");
                    continue;
                }

                var cell = (y * width) + x;
                if (owners[cell] is { } other)
                {
                    problems.Add($"{at}（「{itemId}」）与第 {other + 1} 堆（「{anchors[cell]!.Value.ItemId}」）的左上角都在 ({x}, {y})");
                    continue;
                }

                owners[cell] = k;
                anchors[cell] = new ItemStack(itemId, qualityId, count, sequence);
            }
            catch (InvalidDataException e)
            {
                problems.Add(e.Message);
            }
        }

        return anchors;
    }

    private static JsonObject Shard(IReadOnlyDictionary<string, JsonObject> shards, string id) =>
        shards.TryGetValue(id, out var shard) && shard is not null
            ? shard
            : throw new InvalidDataException($"存档里缺分片「{id}」");

    private static JsonArray Column(IEnumerable<PlotRecord> records, Func<PlotRecord, JsonNode?> cell) =>
        new([.. records.Select(cell)]);
}
