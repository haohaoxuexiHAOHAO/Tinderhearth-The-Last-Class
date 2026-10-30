using System.Text.Json.Nodes;
using Tinderhearth.Rules.Foundation.Save;

namespace Tinderhearth.Rules.Economy;

/// <summary>世界状态的那两个分片：日历与那片地。怎么写进存档、怎么读回来、存档默认值是什么。</summary>
/// <remarks>
/// 两个都进正式存档：跨过一次睡眠还成立（设计仓 design/存档系统.md 的「进哪一侧：判据一句话」一节）。
/// 每个分片只读自己那一块，两块之间没有先后；各自读完之后才拼成一份世界。
///
/// 地块那一块按项排，每一项一串，串里是这一项在每一格上的值，从上到下、每行从左到右。这样地块的一项状态就是分片里的一格：
/// 以后给地块加一项，旧存档缺的正是这一格，按存档默认值补得上（设计仓 decisions/ADR-0015-存档的版本与缺字段口径.md）。
/// </remarks>
public static class WorldShards
{
    /// <summary>日历那个分片的标识。</summary>
    public const string CalendarId = "calendar";

    /// <summary>那片地那个分片的标识。</summary>
    public const string PlotsId = "plots";

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

    /// <summary>地块那个分片里，一片地的摆法那几格。</summary>
    public static readonly IReadOnlyList<string> PlotLayoutKeys = [OriginKey, WidthKey, HeightKey];

    /// <summary>地块那个分片里，每一项状态各一格。它与 <see cref="Plot"/> 的公开属性一一对应，有测试盯着。</summary>
    public static readonly IReadOnlyList<string> PlotStateKeys =
    [
        StateKey, CropIdKey, StageKey, DaysInStageKey, WateredTodayKey,
        WateredDaysThisCropKey, DaysThisCropKey, DaysFallowKey, MutatedKey,
    ];

    /// <summary>把这两个分片注册进去，存档默认值取开新档时那一份。</summary>
    /// <param name="registry">要注册进去的那一份。</param>
    /// <param name="newGamePlots">场景里那片地的起始状态。这里当场写成一份，之后它怎么变都不影响默认值。</param>
    public static void Register(SaveShardRegistry registry, PlotGrid newGamePlots)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(newGamePlots);

        var calendarDefault = WriteCalendar(Calendar.StartDate);
        var plotsDefault = WritePlots(newGamePlots);
        registry.Register(new(
            Id: CalendarId, Owner: "日历", Side: SaveSide.FormalSave,
            Default: () => (JsonObject)calendarDefault.DeepClone()));
        registry.Register(new(
            Id: PlotsId, Owner: "地块系统", Side: SaveSide.FormalSave,
            Default: () => (JsonObject)plotsDefault.DeepClone()));
    }

    /// <summary>一份世界写成两个分片，按标识给。</summary>
    public static Dictionary<string, JsonObject> Write(WorldState world)
    {
        ArgumentNullException.ThrowIfNull(world);

        return new()
        {
            [CalendarId] = WriteCalendar(world.Date),
            [PlotsId] = WritePlots(world.Plots),
        };
    }

    /// <summary>从两个分片读回一份世界。缺一个分片、或者哪一格读不出来，当场报错说清是哪一格。</summary>
    /// <param name="shards">按标识给的分片，先后不限。</param>
    /// <param name="crops">按标识找作物定义。地里那种作物找不到时，那一格读不出来。</param>
    public static WorldState Read(IReadOnlyDictionary<string, JsonObject> shards, IReadOnlyDictionary<string, CropDefinition> crops)
    {
        ArgumentNullException.ThrowIfNull(shards);
        ArgumentNullException.ThrowIfNull(crops);

        return new WorldState(ReadCalendar(Shard(shards, CalendarId)), ReadPlots(Shard(shards, PlotsId), crops));
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

    private static JsonObject Shard(IReadOnlyDictionary<string, JsonObject> shards, string id) =>
        shards.TryGetValue(id, out var shard) && shard is not null
            ? shard
            : throw new InvalidDataException($"存档里缺分片「{id}」");

    private static JsonArray Column(IEnumerable<PlotRecord> records, Func<PlotRecord, JsonNode?> cell) =>
        new([.. records.Select(cell)]);
}
