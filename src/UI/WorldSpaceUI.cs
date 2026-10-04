using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 世界空间 UI：读条圆环、精英血条、伤害数字，都画在执行者身上、跟着角色走。
/// </summary>
/// <remarks>
/// 挂载点必须是 <see cref="UILayer.WorldSpace"/>，不能改成 <see cref="UILayer.Hud"/>。那一层开了
/// <c>FollowViewportEnabled</c>（见 <see cref="UIRoot"/>），所以它的子节点用世界坐标、自动跟着
/// 相机变换与缩放走，元素只要把 <see cref="Node2D.Position"/> 设成目标的
/// <see cref="Node2D.GlobalPosition"/> 就对齐了。换成 Hud 层不报错，表现是读条跑到界面角落去。
///
/// 尺寸与偏移全取自 <see cref="WorldUILayout"/>，显示的值取自视图模型（<see cref="CastState"/>
/// 这些）。这里不写玩法数字，读条时长与血量上限归各自的玩法实现。
///
/// 颜色一律不透明（<see cref="PixelTheme.ToColor"/> 的 alpha 恒满），伤害数字到期直接消失、
/// 不做半透明淡出 —— 见设计仓 production/像素绘制原则.md 的「硬边、抗锯齿与点绘」一节。
/// </remarks>
public sealed partial class WorldSpaceUI : Node
{
    private readonly CanvasLayer _layer;
    private readonly CastRing _ring;
    private readonly List<WorldHealthBar> _bars = [];
    private readonly List<DamageNumber> _damage = [];
    private WorldUIOptions _options = WorldUIOptions.Default;

    public WorldSpaceUI(UIRoot ui)
    {
        _layer = ui.LayerOf(UILayer.WorldSpace);        // 世界空间层，不能换成 Hud 层
        _ring = new CastRing();
        _layer.AddChild(_ring);
    }

    /// <summary>读条圆环，整个场景共用这一个。</summary>
    public CastRing Ring => _ring;

    /// <summary>现在挂出来的那些精英血条。</summary>
    public IReadOnlyList<WorldHealthBar> Bars => _bars;

    /// <summary>伤害数字当前开没开。</summary>
    public bool DamageEnabled => _options.ShowDamageNumbers;

    /// <summary>此刻还在世界里飘着的伤害数字个数。</summary>
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

    /// <summary>更新读条状态。被打断时圆环就隐藏，玩家看得出读条断了。</summary>
    public void SetCast(CastState state) => _ring.Set(state);

    /// <summary>给一个敌人挂血条。杂兵挂了也不显示，这条由 <see cref="EliteHealth.ShowsBar"/> 判。</summary>
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
    /// 这些元素是本类加到 <see cref="UILayer.WorldSpace"/> 层上的，不是本节点的子节点，
    /// 所以本节点被释放时引擎不会顺带收掉它们 —— 不显式收的话它们会留在层上。
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

    /// <summary>设显示开关。伤害数字默认关着，默认值见 <see cref="WorldUIOptions"/>。</summary>
    public void SetOptions(WorldUIOptions options) => _options = options;

    /// <summary>在目标头顶冒一个伤害数字。开关关着时什么都不做。</summary>
    public void PopDamage(Node2D target, int amount)
    {
        if (!_options.ShowDamageNumbers)
        {
            return;
        }

        // 伤害数字到期会自己 QueueFree（见 DamageNumber._Process），但不会把自己从这张表里摘掉。
        // 所以每次冒新数字前先清掉已经消失的，否则一场战斗下来这张表会越来越长。
        _damage.RemoveAll(n => !GodotObject.IsInstanceValid(n));

        var num = new DamageNumber(amount, target.GlobalPosition);
        _layer.AddChild(num);
        _damage.Add(num);
    }

    /// <summary>载世界空间 UI 的素材。缺了就抛 —— 用空纹理顶上会让读条看起来根本没画出来。</summary>
    public static Texture2D LoadArt(string path) =>
        ResourceLoader.Exists(path)
            ? GD.Load<Texture2D>(path)
            : throw new FileNotFoundException(
                $"世界空间 UI 缺素材：{path}（在 Godot 里确认这个文件已导入）");
}

/// <summary>读条圆环：底环是占位素材 <c>cast-ring.png</c>，进度弧由代码画在环上，跟着执行者走。</summary>
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
            Texture = WorldSpaceUI.LoadArt("res://assets/placeholder/ui/cast-ring.png"),
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
            Position = _target.GlobalPosition;   // 贴在执行者身上
        }
    }

    public override void _Draw()
    {
        if (!_state.Visible)
        {
            return;
        }

        var start = -Mathf.Pi / 2f;                                 // 从 12 点方向起画
        var sweep = (float)(_state.ClampedProgress * Mathf.Tau);    // 顺时针转满一圈就是读完
        DrawArc(Vector2.Zero, WorldUILayout.ArcRadius, start, start + sweep,
            WorldUILayout.ArcSegments, _arc, WorldUILayout.ArcWidthPx);
    }

    /// <summary>当前读条进度，0 到 1。</summary>
    public double Progress => _state.ClampedProgress;
}

/// <summary>精英与 BOSS 的血条：贴在目标头顶上方，跟着目标走。杂兵不显示。</summary>
public sealed partial class WorldHealthBar : Node2D
{
    private readonly Color _track = PixelTheme.ToColor(HudPalette.Track);
    private readonly Color _fill = PixelTheme.ToColor(HudPalette.Health);
    private Node2D? _target;
    private EliteHealth _health = new(0, 1, EnemyRank.Trash);   // 先当杂兵，于是默认不显示

    public WorldHealthBar()
    {
        Name = "WorldHealthBar";
        Visible = false;
    }

    public void Track(Node2D target) => _target = target;

    public void Set(EliteHealth health)
    {
        _health = health;
        Visible = health.ShowsBar;      // 只有精英与 BOSS 显示
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

        var w = WorldUILayout.EliteBarWidth;
        var h = WorldUILayout.EliteBarHeight;
        var origin = new Vector2(-w / 2f, WorldUILayout.EliteBarOffsetY);   // 以目标为中心，往上挪
        DrawRect(new Rect2(origin, new Vector2(w, h)), _track);
        var fill = (int)Mathf.Round(_health.Ratio * w);
        DrawRect(new Rect2(origin, new Vector2(fill, h)), _fill);
    }

    /// <summary>这根条现在显不显示。</summary>
    public bool ShowsBar => _health.ShowsBar;

    /// <summary>剩余血量占上限的比例，0 到 1。</summary>
    public double Ratio => _health.Ratio;
}

/// <summary>伤害数字：从目标头顶冒出，向上飘 <see cref="WorldUILayout.DamageFloatDistancePx"/> 后消失。</summary>
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
        Position = spawnWorldPos + new Vector2(0, WorldUILayout.DamageStartOffsetY);
    }

    public override void _Process(double delta)
    {
        _elapsed += delta;
        var life = _elapsed / WorldUILayout.DamageLifetimeSeconds;
        if (life >= 1.0)
        {
            QueueFree();        // 到期直接消失，不做半透明淡出
            return;
        }

        Position = _base + new Vector2(0, (float)WorldUILayout.DamageRiseAt(life));
    }

    public override void _Draw()
    {
        var w = WorldUILayout.EliteBarWidth;    // 借血条的宽度当文本框宽，数字在里面居中
        DrawString(ThemeDB.FallbackFont, new Vector2(-w / 2f, 0), _amount.ToString(),
            HorizontalAlignment.Center, w, UIMetrics.FontSize, _color);
    }
}
