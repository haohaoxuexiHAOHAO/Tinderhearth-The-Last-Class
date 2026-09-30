using Godot;

namespace Tinderhearth.World.Terrain;

/// <summary>
/// 双网格：你在数据层刷地形，它把显示层画出来（`ENG-25`）。
/// </summary>
/// <remarks>
/// **为什么要这一段代码**：Godot 原生不支持双网格。做法与理由的权威是
/// [场景绘制约定 · 双网格：一套 16 张管一种交接]，本类只记落成代码之后的三个形状。
///
/// **一、显示层偏移半格，由代码设、不由你填。** 那不是摆位而是算出来的（格大小的一半），
/// 填错了整套图会错开半格而不报错。所以它不是 `ADR-0009` 说的那种「参数」。
///
/// **二、你刷的是地形编号，不是具体哪一张图。** 数据层的瓦片只回答「这一格是草还是土」，
/// 所以刷的时候完全不用管边缘 —— 这正是双网格换来的东西。
///
/// **三、四角涉及三种以上地形时报错，不猜。** 配对法只给「一对」画了素材，三种地形碰在一个角上
/// 时没有任何一张图是对的。**这种情况靠地图纪律避免**（让某两种地形之间总隔一圈别的），
/// 而不是靠代码挑一张凑上去 —— 凑了就是静默画错。
///
/// **四、它是 `[Tool]` 脚本，因为在编辑器里就得看得出效果。** 这是 `ENG-25` 的硬要求：双网格的
/// 显示层是算出来的，编辑器里看不见就等于盲刷地形。所以它订阅数据层的 <c>Changed</c> 信号，
/// 你刷一笔它跟着重画一次。
///
/// **编辑器里配不全不抛异常，只提示。** 运行时才抛 —— 编辑器里你正配到一半，抛异常会让面板一直
/// 弹错误而挡住你干活；而进了游戏还缺配置就是真的缺，那时必须当场停。
/// </remarks>
[Tool]
public partial class DualGridPainter : Node
{
    /// <summary>你刷的那一层。**它自己不显示**，只存「每格是哪种地形」。</summary>
    /// <remarks>
    /// 把它的 <c>Enabled</c> 关掉或让它用一套纯色瓦片都行 —— 玩家看到的是显示层。
    /// 代码只读它每格的地形编号，不读用的是哪一张图。
    /// </remarks>
    [Export]
    public TileMapLayer? DataLayer { get; set; }

    /// <summary>玩家看到的那一层。**代码往它上面画，你不要手刷。**</summary>
    [Export]
    public TileMapLayer? DisplayLayer { get; set; }

    /// <summary>数据层用的是第几个地形集。</summary>
    [Export]
    public int DataTerrainSet { get; set; }

    /// <summary>有哪几对地形各有一套 16 张。</summary>
    [Export]
    public Godot.Collections.Array<DualGridPair> Pairs { get; set; } = [];

    /// <summary>
    /// 四角编码到图集格的映射表。**索引是编码，值是图集里的格坐标。**
    /// </summary>
    /// <remarks>
    /// 编码按「左上、右上、左下、右下」四位拼成 0–15（左上是最高位），0 表示那个角是
    /// <see cref="DualGridPair.TerrainA"/>。
    ///
    /// **这张表是从素材里量出来的，不是排出来的**：`assets/self-drawn/tiles/grass-dirt.png` 与
    /// `grass-water.png` 两套的 16 张逐格取四角主色推出组合，两套顺序一致且 16 种恰好各一次。
    ///
    /// ⚠️ **素材里那 16 张的位置定下来之后不许再调换**（[场景绘制约定 · 两条交付纪律]）——
    /// 调换要跟着改这张表，而它**不报错**，表现只是边缘拼错，你得对着图找是哪一张错了位。
    /// </remarks>
    private static readonly Vector2I[] CodeToAtlasOffset =
    [
        new(0, 3), // 0000 四角全是 A
        new(1, 3), // 0001
        new(0, 0), // 0010
        new(3, 0), // 0011
        new(0, 2), // 0100
        new(1, 0), // 0101
        new(2, 3), // 0110
        new(1, 1), // 0111
        new(3, 3), // 1000
        new(0, 1), // 1001
        new(3, 2), // 1010
        new(2, 0), // 1011
        new(1, 2), // 1100
        new(2, 2), // 1101
        new(3, 1), // 1110
        new(2, 1), // 1111 四角全是 B
    ];

    /// <summary>手动重画一次。改完 <see cref="Pairs"/> 或图集之后点它。</summary>
    /// <remarks>
    /// 刷格子有 <c>Changed</c> 信号自动接住，但改导出属性没有 —— 那时点这个按钮比重新打开场景快。
    /// </remarks>
    [ExportToolButton("重画显示层")]
    public Callable RepaintButton => Callable.From(Repaint);

    private TileMapLayer? _data;
    private TileMapLayer? _display;
    private bool _repaintQueued;
    private bool _reportedUncoveredCorner;

    public override void _Ready()
    {
        if (!TryBind(out var reason))
        {
            Report(reason);
            return;
        }

        _data!.Changed += OnDataLayerChanged;
        AlignDisplayLayer();
        Repaint();
    }

    public override void _ExitTree()
    {
        if (_data is not null)
        {
            _data.Changed -= OnDataLayerChanged;
        }
    }

    /// <summary>
    /// 数据层变了。**不当场重画，延到帧末画一次。**
    /// </summary>
    /// <remarks>
    /// 官方对这个信号写明批量修改时它会发得非常频繁，要求连接的函数别做复杂处理、考虑延到帧末。
    /// 而本类的重画是整层的，正是「复杂处理」——所以靠一个标记把一帧之内的多次变化合成一次。
    /// </remarks>
    private void OnDataLayerChanged()
    {
        if (_repaintQueued)
        {
            return;
        }

        _repaintQueued = true;
        Callable.From(FlushRepaint).CallDeferred();
    }

    private void FlushRepaint()
    {
        _repaintQueued = false;
        if (_data is not null && _display is not null)
        {
            Repaint();
        }
    }

    /// <summary>接线并校验；说不通就把原因写在 <paramref name="reason"/> 里。</summary>
    private bool TryBind(out string reason)
    {
        _data = null;
        _display = null;

        if (DataLayer is null)
        {
            reason = $"{nameof(DataLayer)} 没接上 —— 它要的是你刷地形的那一层";
            return false;
        }

        if (DisplayLayer is null)
        {
            reason = $"{nameof(DisplayLayer)} 没接上 —— 它要的是玩家看到的那一层，代码往它上面画";
            return false;
        }

        if (DataLayer.TileSet is null || DisplayLayer.TileSet is null)
        {
            reason = "两层都要先有 TileSet";
            return false;
        }

        if (ReferenceEquals(DataLayer, DisplayLayer))
        {
            reason = "两格接的是同一层 —— 双网格要两层，一层存数据、一层显示";
            return false;
        }

        if (Pairs.Count == 0)
        {
            reason = $"{nameof(Pairs)} 是空的 —— 至少要一对地形，每对配一套 16 张";
            return false;
        }

        for (var i = 0; i < Pairs.Count; i++)
        {
            if (Pairs[i] is null)
            {
                reason = $"{nameof(Pairs)} 第 {i} 项是空的";
                return false;
            }

            try
            {
                Pairs[i].RequireConfigured(i);
            }
            catch (InvalidOperationException ex)
            {
                reason = ex.Message;
                return false;
            }
        }

        _data = DataLayer;
        _display = DisplayLayer;
        reason = "";
        return true;
    }

    /// <summary>
    /// 配不全时怎么说。**编辑器里只提示，运行时抛。**
    /// </summary>
    /// <remarks>
    /// 编辑器里你可能正配到一半，抛异常会让输出面板一直弹错误、挡住你干活；而进了游戏还缺配置
    /// 就是真的缺，那时必须当场停（`ADR-0009`：缺配置不许悄悄兜底）。
    /// </remarks>
    private void Report(string reason)
    {
        var message = $"{nameof(DualGridPainter)}（节点 {Name}）：{reason}";
        if (Engine.IsEditorHint())
        {
            GD.PushWarning(message + "（配齐之后点检查器里那个「重画显示层」）");
            return;
        }

        throw new InvalidOperationException(message);
    }

    /// <summary>
    /// 把显示层往左上挪半格。
    /// </summary>
    /// <remarks>
    /// 半格偏移必须落在整数像素上，而基础单位是偶数（16），所以这里除得尽。格大小是奇数时
    /// 会产生半像素，那与最近邻过滤、整数缩放、「只许全透明或全不透明」三条硬规则直接打架 ——
    /// 所以除不尽时报错，而不是四舍五入过去。
    /// </remarks>
    private void AlignDisplayLayer()
    {
        Vector2I tile = _data!.TileSet.TileSize;
        if (tile.X % 2 != 0 || tile.Y % 2 != 0)
        {
            Report($"格大小 {tile} 有奇数边 —— 双网格要往左上挪半格，奇数会挪出半像素");
            return;
        }

        _display!.Position = new Vector2(-tile.X / 2f, -tile.Y / 2f);
    }

    /// <summary>按数据层现在的内容重画整个显示层。</summary>
    /// <remarks>
    /// **整层重画，不做增量。** 改一格数据会影响它周围四格显示，而「哪几格要跟着重画」算错了
    /// 不报错、只表现为某处边缘没跟着变。基地这个量级（[内容规模基准]里那个格数）整层重画不值得优化。
    /// </remarks>
    public void Repaint()
    {
        // 按钮可以在还没接线时被点到，所以这里自己接一次而不是假定 _Ready 跑过了。
        if (_data is null || _display is null)
        {
            if (!TryBind(out var reason))
            {
                Report(reason);
                return;
            }

            AlignDisplayLayer();
        }

        _reportedUncoveredCorner = false;
        _display!.Clear();

        Rect2I used = _data!.GetUsedRect();
        if (used.Size == Vector2I.Zero)
        {
            return;
        }

        // 显示层比数据层多一圈：最外面那一圈显示格的四个角里有一半压在数据层外面。
        for (var y = used.Position.Y; y <= used.End.Y + 1; y++)
        {
            for (var x = used.Position.X; x <= used.End.X + 1; x++)
            {
                PaintDisplayCell(new Vector2I(x, y));
            }
        }
    }

    private void PaintDisplayCell(Vector2I cell)
    {
        // 显示层格 (x,y) 往左上挪了半格，所以它四个角正好落在数据层这四格的中心上。
        var corners = new[]
        {
            TerrainAt(cell + new Vector2I(-1, -1)),
            TerrainAt(cell + new Vector2I(0, -1)),
            TerrainAt(cell + new Vector2I(-1, 0)),
            TerrainAt(cell),
        };

        DualGridPair? pair = PairFor(corners, cell);
        if (pair is null)
        {
            return;
        }

        var code = 0;
        foreach (var terrain in corners)
        {
            code = (code << 1) | pair.BitOf(terrain);
        }

        _display!.SetCell(cell, pair.DisplaySourceId, pair.AtlasOrigin + CodeToAtlasOffset[code]);
    }

    /// <summary>某个数据格是哪种地形；空格与没标地形的都算 -1。</summary>
    private int TerrainAt(Vector2I cell) => _data!.GetCellTileData(cell) is TileData data
        && data.TerrainSet == DataTerrainSet
            ? data.Terrain
            : -1;

    /// <summary>
    /// 这四个角该用哪一对的素材；四角全空时返回 <c>null</c>（那一格不画）。
    /// </summary>
    /// <remarks>
    /// 四角只有一种地形时任何认得它的那一对都行，取第一对 —— 那一张是「四角全同」的图，
    /// 两套里画的是同一片地面。
    /// </remarks>
    private DualGridPair? PairFor(int[] corners, Vector2I cell)
    {
        var distinct = new List<int>();
        foreach (var terrain in corners)
        {
            if (terrain >= 0 && !distinct.Contains(terrain))
            {
                distinct.Add(terrain);
            }
        }

        if (distinct.Count == 0)
        {
            return null;
        }

        foreach (DualGridPair pair in Pairs)
        {
            var covered = true;
            foreach (var terrain in distinct)
            {
                if (!pair.Covers(terrain))
                {
                    covered = false;
                    break;
                }
            }

            if (covered)
            {
                return pair;
            }
        }

        // 一整片刷错时每格都报会把输出面板刷满，所以一次重画只报第一处 —— 改完再刷一次就报下一处。
        if (!_reportedUncoveredCorner)
        {
            _reportedUncoveredCorner = true;
            Report($"显示格 {cell} 的四个角涉及地形 [{string.Join(", ", distinct)}]，"
                + "没有哪一对的素材同时覆盖它们 —— 配对法只给成对的交接画了图。"
                + "要么给这一对补一套 16 张，要么改地图让这两种地形之间隔一圈别的"
                + "（[场景绘制约定] 那条用地图约束换素材的纪律）");
        }

        return null;
    }
}
