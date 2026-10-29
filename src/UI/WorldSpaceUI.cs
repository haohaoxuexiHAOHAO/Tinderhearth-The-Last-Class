using Godot;
using Tinderhearth.Rules.UI;

namespace Tinderhearth.UI;

/// <summary>
/// 世界空间 UI：读条圆环、精英血条、伤害数字，都画在执行者身上、跟着角色走。
/// </summary>
/// <remarks>
/// 挂载点必须是 <see cref="UILayer.WorldSpace"/>，不能改成 <see cref="UILayer.Hud"/>。那一层开了
/// <c>FollowViewportEnabled</c>（见 <see cref="UIRoot"/>），所以它的子节点用世界坐标、自动跟着
/// 相机变换与缩放走，元素只要跟着目标报的那个脚底节点走就对齐了。换成 Hud 层不报错，表现是读条跑到界面角落去。
///
/// 往谁身上挂由目标自己报（<see cref="IWorldUITarget"/>）：血条与伤害数字按它报的头顶摆、血条宽按体型档给，尺寸与偏移
/// 全取自 <see cref="WorldUILayout"/>；读条圆环不跟身高走。显示的值取自视图模型（<see cref="CastState"/> 这些），这里不写玩法数字。
///
/// 精英血条与 HUD 的资源条是同一个 <see cref="GaugeBar"/>，伤害数字到期直接消失、不做半透明淡出 —— 见设计仓
/// production/像素绘制原则.md 的「硬边、抗锯齿与点绘」一节。
/// </remarks>
public sealed partial class WorldSpaceUI : Node
{
    private readonly CanvasLayer _layer;
    private readonly UIArt _art;
    private readonly GaugeMotionSettings _motion;
    private readonly CastRing _ring;
    private readonly List<WorldHealthBar> _bars = [];
    private readonly List<DamageNumber> _damage = [];
    private WorldUIOptions _options = WorldUIOptions.Default;

    /// <summary>装好世界空间那一层的元素。读条圆环与血条的图从素材资源取，血条怎么追、留一截由调用方给。</summary>
    public WorldSpaceUI(UIRoot ui, UIArt art, GaugeMotionSettings motion)
    {
        _layer = ui.LayerOf(UILayer.WorldSpace);        // 世界空间层，不能换成 Hud 层
        _art = art;
        _motion = motion;
        _ring = new CastRing(art);
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

    /// <summary>读条圆环跟谁走（执行者）：贴在它画出来的脚底上，不跟身高走。</summary>
    public void TrackCast(IWorldUITarget target) => _ring.Track(FeetOf(target));

    /// <summary>更新读条状态。被打断时圆环就隐藏，玩家看得出读条断了。</summary>
    public void SetCast(CastState state) => _ring.Set(state);

    /// <summary>
    /// 给一个敌人挂血条。杂兵挂了也不显示，这条由 <see cref="EliteHealth.ShowsBar"/> 判；目标没报头顶高度或体型档当场抛。
    /// </summary>
    public WorldHealthBar AttachBar(IWorldUITarget target, EliteHealth health)
    {
        var bar = new WorldHealthBar(_art, _motion, AnchorOf(target));
        _layer.AddChild(bar);
        bar.Track(FeetOf(target));
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

    /// <summary>在目标头顶冒一个伤害数字。开关关着时什么都不冒，但目标没报头顶高度或体型档照样当场抛。</summary>
    public void PopDamage(IWorldUITarget target, int amount)
    {
        // 先核再看开关：伤害数字默认关着，先看开关的话，没报的目标要等玩家打开那一项才露出来。
        var anchor = AnchorOf(target);
        if (!_options.ShowDamageNumbers)
        {
            return;
        }

        // 伤害数字到期会自己 QueueFree（见 DamageNumber._Process），但不会把自己从这张表里摘掉。
        // 所以每次冒新数字前先清掉已经消失的，否则一场战斗下来这张表会越来越长。
        _damage.RemoveAll(n => !GodotObject.IsInstanceValid(n));

        var num = new DamageNumber(amount, FeetOf(target).GlobalPosition, anchor);
        _layer.AddChild(num);
        _damage.Add(num);
    }

    /// <summary>目标报的头顶高度与体型档，核过的；缺哪一项都抛，并说清是哪个目标。</summary>
    private static WorldUIAnchor AnchorOf(IWorldUITarget target) =>
        WorldUIAnchor.Of(NameOf(target), target.HeadHeightWorldPx, target.BodyTier);

    /// <summary>目标报的那个脚底节点。还没建出来（目标没就绪）就抛，不跟着一个空节点走。</summary>
    private static Node2D FeetOf(IWorldUITarget target) =>
        target.WorldUIFeet ?? throw new InvalidOperationException(
            $"目标「{NameOf(target)}」画出来的脚底那个节点还没有 —— 等它就绪之后再往它身上挂东西");

    private static string NameOf(IWorldUITarget target) => target is Node node ? node.Name : target.GetType().Name;
}

/// <summary>
/// 读条圆环：作者画的底环（<c>world/ring_cast</c>）上叠那一圈（<c>world/ring_cast_fill</c>），那一圈按进度露出，跟着执行者走。
/// </summary>
/// <remarks>
/// 按角度露出交给引擎的进度条（顺时针填充那一种）：它按进度切出那一圈的一扇，从 12 点方向起、顺时针转满一圈就是读完，
/// 不拉伸、不染色。圆环的中心落在执行者身上，所以往左上各挪半张；两个边长都是偶数，挪完仍在整像素上。
/// </remarks>
public sealed partial class CastRing : Node2D
{
    private readonly TextureProgressBar _dial;
    private Node2D? _target;
    private CastState _state = CastState.Idle;

    /// <summary>装一个读条圆环。两张图从素材资源取，缺了当场报错。</summary>
    public CastRing(UIArt art)
    {
        Name = "CastRing";
        Visible = false;
        _dial = new TextureProgressBar
        {
            Name = "Dial",
            TextureUnder = HudPlace.Piece(art.WorldRingCast, "world/ring_cast"),
            TextureProgress = HudPlace.Piece(art.WorldRingCastFill, "world/ring_cast_fill"),
            FillMode = (int)TextureProgressBar.FillModeEnum.Clockwise,
            MinValue = 0,
            MaxValue = 1,
            Step = 0,           // 不按步取整：读条是连续的
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Position = new Vector2(-WorldUILayout.RingWidth / 2, -WorldUILayout.RingHeight / 2),
        };
        AddChild(_dial);
    }

    public void Track(Node2D target) => _target = target;

    public void Set(CastState state)
    {
        _state = state;
        Visible = state.Visible;
        _dial.Value = state.ClampedProgress;
    }

    public override void _Process(double delta)
    {
        if (_target is not null)
        {
            Position = _target.GlobalPosition;   // 贴在执行者身上
        }
    }

    /// <summary>当前读条进度，0 到 1。</summary>
    public double Progress => _state.ClampedProgress;
}

/// <summary>精英与 BOSS 的血条：浮在目标头顶上方，跟着目标画出来的脚底走。杂兵不显示。</summary>
/// <remarks>
/// 条就是 HUD 那一根 <see cref="GaugeBar"/>：同一套槽与生命那张填充，回复时追、扣减时留一截。宽按目标的体型档给、
/// 顶边按它报的头顶摆，横着居中在脚底上（<see cref="WorldUILayout"/>）。
/// </remarks>
public sealed partial class WorldHealthBar : Node2D
{
    private readonly GaugeBar _bar;
    private Node2D? _feet;
    private EliteHealth _health = new(0, 1, EnemyRank.Trash);   // 先当杂兵，于是默认不显示

    /// <summary>装一根血条。槽与填充从素材资源取，缺了当场报错；宽与高度照目标报的那两样。</summary>
    public WorldHealthBar(UIArt art, GaugeMotionSettings motion, WorldUIAnchor anchor)
    {
        Name = "WorldHealthBar";
        Visible = false;
        _bar = new GaugeBar(art, art.HudBarFillHp, "hud/bar_fill_hp", WorldUILayout.EliteBarWidthOf(anchor.Tier), motion)
        {
            Position = new Vector2(WorldUILayout.EliteBarLeftX(anchor.Tier), WorldUILayout.EliteBarTopY(anchor)),
        };
        AddChild(_bar);
    }

    /// <summary>跟着哪个节点走：目标报的那个脚底。</summary>
    public void Track(Node2D feet) => _feet = feet;

    /// <summary>换一份血量。第一次直接画到那么长，之后回复时追、扣减时留一截（<see cref="GaugeBar.Set"/>）。</summary>
    public void Set(EliteHealth health)
    {
        _health = health;
        Visible = health.ShowsBar;      // 只有精英与 BOSS 显示
        _bar.Set(health.Ratio);
    }

    public override void _Process(double delta)
    {
        if (_feet is not null)
        {
            Position = _feet.GlobalPosition;
        }
    }

    /// <summary>这根条现在显不显示。</summary>
    public bool ShowsBar => _health.ShowsBar;

    /// <summary>剩余血量占上限的比例，0 到 1。</summary>
    public double Ratio => _health.Ratio;
}

/// <summary>伤害数字：从目标报的头顶冒出，向上飘 <see cref="WorldUILayout.DamageFloatDistancePx"/> 后消失。</summary>
public sealed partial class DamageNumber : Node2D
{
    private readonly Color _color = PixelTheme.ToColor(HudPalette.Hot);
    private readonly int _amount;
    private readonly Vector2 _base;
    private readonly WorldUIAnchor _anchor;
    private double _elapsed;

    /// <summary>在 <paramref name="spawnFeet"/>（目标画出来的脚底，世界坐标）上方冒一个；冒出来之后不再跟着目标走。</summary>
    public DamageNumber(int amount, Vector2 spawnFeet, WorldUIAnchor anchor)
    {
        Name = "DamageNumber";
        _amount = amount;
        _base = spawnFeet;
        _anchor = anchor;
        Position = spawnFeet + new Vector2(0, WorldUILayout.DamageStartOffsetY(anchor));
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

        Position = _base + new Vector2(0, (float)WorldUILayout.DamageRiseAt(life, _anchor));
    }

    public override void _Draw()
    {
        // 借这个目标那根血条的宽当文本框宽，数字在里面居中。
        DrawString(ThemeDB.FallbackFont, new Vector2(WorldUILayout.EliteBarLeftX(_anchor.Tier), 0), _amount.ToString(),
            HorizontalAlignment.Center, WorldUILayout.EliteBarWidthOf(_anchor.Tier), UIMetrics.FontSize, _color);
    }
}
