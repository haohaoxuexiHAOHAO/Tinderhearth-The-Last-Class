using Godot;
using Tinderhearth.Rules.Ui;

namespace Tinderhearth.UI;

/// <summary>
/// 世界空间 UI（`UI-9`）：读条圆环、精英血条、伤害数字。**画在执行者身上、随角色走。**
/// </summary>
/// <remarks>
/// **挂在 <see cref="UiLayer.WorldSpace"/> 那一层，不是 <see cref="UiLayer.Hud"/>。** 这是本条的
/// 核心约束：正典要求[读条画在执行者身上而不是界面角落]。世界空间层开了 <c>FollowViewportEnabled</c>
/// （见 <see cref="UiRoot"/>），于是这层的子节点用世界坐标、自动跟相机变换与 2 倍缩放 —— 元素只要
/// 把 <see cref="Node2D.Position"/> 设成目标的 <see cref="Node2D.GlobalPosition"/> 就对齐了，不必手算
/// 世界到屏幕的投影（`check_worldui.py` 静态核会盯住这层的挂载点不被改成 Hud）。
///
/// 尺寸与偏移全取自 <see cref="WorldUiLayout"/>，值取自视图模型（<see cref="CastState"/> 等）——
/// 与 <see cref="LevelHud"/> 同一分层纪律：这里不写玩法数字（读条时长、血量上限归各玩法实现）。
///
/// 冷暖色都用不透明色（<see cref="PixelTheme.ToColor"/>，alpha 恒满）：伤害数字到期直接消失、
/// 不做半透明淡出（像素绘制原则 §9）。
/// </remarks>
public sealed partial class WorldSpaceUi : Node
{
    private readonly CanvasLayer _layer;
    private readonly CastRing _ring;
    private readonly List<WorldHealthBar> _bars = [];
    private readonly List<DamageNumber> _damage = [];
    private WorldUiOptions _options = WorldUiOptions.Default;

    public WorldSpaceUi(UiRoot ui)
    {
        _layer = ui.LayerOf(UiLayer.WorldSpace);        // ← 世界空间层，UI-9 的挂载点
        _ring = new CastRing();
        _layer.AddChild(_ring);
    }

    /// <summary>读条圆环（探针读回用）。</summary>
    public CastRing Ring => _ring;

    /// <summary>已挂出的精英血条（探针读回用）。</summary>
    public IReadOnlyList<WorldHealthBar> Bars => _bars;

    /// <summary>伤害数字当前开没开。</summary>
    public bool DamageEnabled => _options.ShowDamageNumbers;

    /// <summary>仍在世界里飘的伤害数字个数（探针读回用）。</summary>
    public int LiveDamageCount
    {
        get
        {
            var live = 0;
            foreach (var num in _damage)
            {
                if (GodotObject.IsInstanceValid(num))
                {
                    live++;
                }
            }

            return live;
        }
    }

    /// <summary>读条圆环跟谁走（执行者）。</summary>
    public void TrackCast(Node2D target) => _ring.Track(target);

    /// <summary>更新读条状态；受击中断即隐藏（表现能中断）。</summary>
    public void SetCast(CastState state) => _ring.Set(state);

    /// <summary>给一个敌人挂血条；杂兵不会真的显示（<see cref="EliteHealth.ShowsBar"/> 判）。</summary>
    public WorldHealthBar AttachBar(Node2D target, EliteHealth health)
    {
        var bar = new WorldHealthBar();
        _layer.AddChild(bar);
        bar.Track(target);
        bar.Set(health);
        _bars.Add(bar);
        return bar;
    }

    /// <summary>撤掉所有血条。</summary>
    public void ClearBars()
    {
        foreach (var bar in _bars)
        {
            bar.QueueFree();
        }

        _bars.Clear();
    }

    /// <summary>
    /// 离开场景树时收掉自己挂在世界空间层上的元素。
    /// </summary>
    /// <remarks>
    /// 元素是本管理器加到 <see cref="UiLayer.WorldSpace"/> 层的（不是本节点的子节点），
    /// 所以本节点被释放时不会自动带走它们 —— 探针跑完要让层回到干净状态，得显式收。
    /// </remarks>
    public override void _ExitTree()
    {
        if (GodotObject.IsInstanceValid(_ring))
        {
            _ring.QueueFree();
        }

        ClearBars();
        foreach (var num in _damage)
        {
            if (GodotObject.IsInstanceValid(num))
            {
                num.QueueFree();
            }
        }

        _damage.Clear();
    }

    /// <summary>设呈现开关（伤害数字默认关，见 <see cref="WorldUiOptions"/>）。</summary>
    public void SetOptions(WorldUiOptions options) => _options = options;

    /// <summary>在目标头顶冒一个伤害数字。**开关关着时什么都不做**（正典：默认关）。</summary>
    public void PopDamage(Node2D target, int amount)
    {
        if (!_options.ShowDamageNumbers)
        {
            return;
        }

        // 伤害数字到期会自己 QueueFree（见 DamageNumber._Process），但不会把自己从这张表里摘掉。
        // 每次冒新数字前先清掉已消失的，别让表随战斗无限长 —— 真战斗一场会冒成百上千个。
        _damage.RemoveAll(n => !GodotObject.IsInstanceValid(n));

        var num = new DamageNumber(amount, target.GlobalPosition);
        _layer.AddChild(num);
        _damage.Add(num);
    }

    /// <summary>载世界空间 UI 的素材。**缺了就抛** —— 静默用空纹理会让读条看起来没画出来。</summary>
    public static Texture2D LoadArt(string path) =>
        ResourceLoader.Exists(path)
            ? GD.Load<Texture2D>(path)
            : throw new FileNotFoundException(
                $"世界空间 UI 缺素材：{path}（登记表在 tools/asset-registry.json）");
}

/// <summary>读条圆环：底环是占位件 <c>cast-ring.png</c>，进度弧由代码画在环上，跟执行者走。</summary>
public sealed partial class CastRing : Node2D
{
    private readonly Color _arc = PixelTheme.ToColor(HudPalette.Hot);
    private Node2D? _target;
    private CastState _state = CastState.Idle;

    public CastRing()
    {
        Name = "CastRing";
        Visible = false;
        AddChild(new Sprite2D
        {
            Name = "Base",
            Centered = true,
            Texture = WorldSpaceUi.LoadArt("res://assets/placeholder/ui/cast-ring.png"),
        });
    }

    public void Track(Node2D target) => _target = target;

    public void Set(CastState state)
    {
        _state = state;
        Visible = state.Visible;
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_target is not null)
        {
            Position = _target.GlobalPosition;   // 画在执行者身上
        }
    }

    public override void _Draw()
    {
        if (!_state.Visible)
        {
            return;
        }

        var start = -Mathf.Pi / 2f;                                 // 从 12 点方向起
        var sweep = (float)(_state.ClampedProgress * Mathf.Tau);    // 顺时针一整圈 = 满
        DrawArc(Vector2.Zero, WorldUiLayout.ArcRadius, start, start + sweep,
            WorldUiLayout.ArcSegments, _arc, WorldUiLayout.ArcWidthPx);
    }

    /// <summary>探针读回：当前进度。</summary>
    public double Progress => _state.ClampedProgress;
}

/// <summary>精英/BOSS 血条：贴在剪影头顶上方，跟目标走。杂兵不显示。</summary>
public sealed partial class WorldHealthBar : Node2D
{
    private readonly Color _track = PixelTheme.ToColor(HudPalette.Track);
    private readonly Color _fill = PixelTheme.ToColor(HudPalette.Health);
    private Node2D? _target;
    private EliteHealth _health = new(0, 1, EnemyRank.Trash);   // 初始杂兵 → 不显示

    public WorldHealthBar()
    {
        Name = "WorldHealthBar";
        Visible = false;
    }

    public void Track(Node2D target) => _target = target;

    public void Set(EliteHealth health)
    {
        _health = health;
        Visible = health.ShowsBar;      // 只精英与 BOSS（正典）
        QueueRedraw();
    }

    public override void _Process(double delta)
    {
        if (_target is not null)
        {
            Position = _target.GlobalPosition;
        }
    }

    public override void _Draw()
    {
        if (!_health.ShowsBar)
        {
            return;
        }

        var w = WorldUiLayout.EliteBarWidth;
        var h = WorldUiLayout.EliteBarHeight;
        var origin = new Vector2(-w / 2f, WorldUiLayout.EliteBarOffsetY);   // 居中于头顶上方
        DrawRect(new Rect2(origin, new Vector2(w, h)), _track);
        var fill = (int)Mathf.Round(_health.Ratio * w);
        DrawRect(new Rect2(origin, new Vector2(fill, h)), _fill);
    }

    /// <summary>探针读回。</summary>
    public bool ShowsBar => _health.ShowsBar;

    /// <summary>探针读回。</summary>
    public double Ratio => _health.Ratio;
}

/// <summary>伤害数字：从头顶冒出、向上飘 <see cref="WorldUiLayout.DamageFloatDistancePx"/> 后消失。</summary>
public sealed partial class DamageNumber : Node2D
{
    private readonly Color _color = PixelTheme.ToColor(HudPalette.Hot);
    private readonly int _amount;
    private readonly Vector2 _base;
    private double _elapsed;

    public DamageNumber(int amount, Vector2 spawnWorldPos)
    {
        Name = "DamageNumber";
        _amount = amount;
        _base = spawnWorldPos;
        Position = spawnWorldPos + new Vector2(0, WorldUiLayout.DamageStartOffsetY);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        var life = _elapsed / WorldUiLayout.DamageLifetimeSeconds;
        if (life >= 1.0)
        {
            QueueFree();        // 到期消失，不做半透明淡出（无 alpha）
            return;
        }

        Position = _base + new Vector2(0, (float)WorldUiLayout.DamageRiseAt(life));
    }

    public override void _Draw()
    {
        var w = WorldUiLayout.EliteBarWidth;    // 借一个居中用的文本框宽
        DrawString(ThemeDB.FallbackFont, new Vector2(-w / 2f, 0), _amount.ToString(),
            HorizontalAlignment.Center, w, UiMetrics.FontSize, _color);
    }
}
