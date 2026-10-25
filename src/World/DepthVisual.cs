using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>
/// 一个占着纵深的角色。纵深值只有一份，实现者从自己那唯一一处转发出来，不另存一份。
/// </summary>
/// <remarks>
/// 绘制排序与命中判定必须读同一份纵深值 —— 排错了，玩家看到的前后关系会与打得着打不着相反。
/// 这个接口就是那句「同一份」在类型上的落点：排序与命中容差都经它取值。
/// 为什么战斗关卡有这么一条纵深轴，见设计仓 canon/gameplay/战斗与关卡.md 的
/// 「战斗关卡的空间模型：带纵深的横版」一节。
/// </remarks>
public interface IDepthActor
{
    /// <summary>纵深位置，世界像素。坐标口径见 <see cref="DepthBand"/>（0 最靠后）。</summary>
    double DepthWorldPx { get; }

    /// <summary>排序要用的那两个量。实现者转发自己的 <see cref="DepthVisual.Subject"/>，不自己拼。</summary>
    /// <remarks>
    /// 放进接口，而不是让排序层去角色的子节点里找 <see cref="DepthVisual"/>：找的那一步每帧都要
    /// 遍历一遍子节点，而且「找不到就改用角色自己的 Y」这条兜底会悄悄给出错的顺序 —— 跳跃高度
    /// 会混进本该只有地面高度的那个量里。
    /// </remarks>
    DepthSubject DepthSubject { get; }
}

/// <summary>
/// 角色的纵深可视根：按纵深把画面上下挪一段、把影子摆在地上、报排序要用的量。
/// </summary>
/// <remarks>
/// 角色的可视子节点都挂在这里，不挂在角色本体上。于是「纵深偏移」只有一处来源（本节点的
/// <c>Position</c>），新增一种角色时漏不掉 —— 漏了的表现是那个角色在纵深上走动时画面不动，
/// 而它不报错。角色本体只留物理：它的 <c>Position</c> 仍然只管横向与跳跃高度。
///
/// 地面靠射线每帧问一次，不靠「记住上次站在多高」。影子要落在角色正下方的地面上，而地形有起伏
/// 和坑；记忆式的做法在跳过坑时会把影子留在坑沿的高度 —— 那时影子指的位置是错的，而画面上只是
/// 「影子有点怪」。射线问的是真实地形，坑底就是坑底。
/// </remarks>
public partial class DepthVisual : Node2D
{
    private RayCast2D _ground = null!;
    private Node2D _host = null!;

    /// <summary>纵深从哪个角色读。建节点时给，本节点不缓存纵深值。</summary>
    public required IDepthActor Actor { get; init; }

    /// <summary>脚下那套影子图，按离地高度从大到小排：第一张贴地，往后一张比一张小。</summary>
    /// <remarks>
    /// 建本节点时给，侧视角色从 <see cref="LoadShadowSteps"/> 取。空的就不画：不离地的物件把影子画进
    /// 自己的图里，用不着单画的这一张；缺图时也是空的，载入那一步已经点名报过缺哪张。
    /// </remarks>
    public required IReadOnlyList<Texture2D> ShadowSteps { get; init; }

    /// <summary>找地面的射线往下探多远，世界像素。够穿过一层地形就行。</summary>
    public int GroundProbeWorldPx { get; init; } = 256;

    /// <summary>这一帧影子落在的地面全局 Y。射线什么都没打到时是 <c>null</c>，那时不画影子。</summary>
    public double? GroundYWorldPx { get; private set; }

    /// <summary>这一帧角色离地多高，世界像素。没有地面时是 0。</summary>
    public double HeightAboveGroundWorldPx { get; private set; }

    /// <summary>排序要用的两个量。没有地面时把脚底当成与角色同高。</summary>
    public DepthSubject Subject => new(Actor.DepthWorldPx, GroundYWorldPx ?? _host.GlobalPosition.Y);

    /// <summary>
    /// 载入一套侧视影子图，按序号排好：第一张贴地，往后一张比一张小。一张都没有时返回空的。
    /// </summary>
    /// <param name="set">哪一套，也就是文件名去掉序号的那一截，例如体型表里「人」那一档是 <c>human</c>。</param>
    /// <remarks>
    /// 一套是 <c>&lt;套名&gt;1.png</c>、<c>&lt;套名&gt;2.png</c>……序号从 1 连着往下排，同一档的角色共用一套。
    /// 交付约定在设计仓 production/像素绘制原则.md 的「侧视里会离地的，影子单画一张」一节。
    ///
    /// 按路径找、不在检查器里拖：用它的角色现在还是代码建的，检查器够不着（训练房等着改成作者搭的
    /// 节点树，设计仓台账 ENG-21）。
    ///
    /// 缺图只打警告、不抛：缺了在画面上一眼看得出来（脚下没有影子），抛的话图画出来之前整个训练房都
    /// 打不开 —— 缺精灵表时 <see cref="PlayerActor"/> 也是这个口径。序号断了一格同样点名，不点的话
    /// 断口后面那几张静默不用，画面上只是「跳起来影子没变」。
    /// </remarks>
    public static IReadOnlyList<Texture2D> LoadShadowSteps(string set)
    {
        var steps = new List<Texture2D>();
        while (ResourceLoader.Exists(ShadowPath(set, steps.Count + 1)))
        {
            var path = ShadowPath(set, steps.Count + 1);
            steps.Add(GD.Load<Texture2D>(path)
                ?? throw new InvalidOperationException($"[影子] {path} 在，但载入不出一张图 —— 在 Godot 里看它的导入设置"));
        }

        if (steps.Count == 0)
        {
            GD.PushWarning($"[影子] 缺侧视影子图 {ShadowPath(set, 1)}，用这一套的角色脚下不画影子");
        }
        else if (ResourceLoader.Exists(ShadowPath(set, steps.Count + 2)))
        {
            GD.PushWarning($"[影子] 侧视影子 {set} 那一套缺 {ShadowPath(set, steps.Count + 1)}，"
                + "它后面那几张没有载入 —— 序号要从 1 连着往下排");
        }

        return steps;
    }

    /// <summary>一套影子图里第 <paramref name="step"/> 张（从 1 起）的资源路径。</summary>
    private static string ShadowPath(string set, int step) =>
        $"res://assets/self-drawn/shadow_side/{set}{step}.png";

    public override void _Ready()
    {
        _host = GetParent<Node2D>();
        _ground = new RayCast2D
        {
            TargetPosition = new Vector2(0, GroundProbeWorldPx),
            // 层 1 是地形层，但主角也在这一层（木桩靠它实现「被击退时撞上主角会停住」）。所以这条
            // 射线只排除宿主自己，另一个角色正好压在宿主正下方时仍会被当成地面。当前场景里碰不到
            // 那种摆法；真要让角色叠在一起站，得先给地形一个专属的碰撞层。
            CollisionMask = 1,
            // 脚底正好贴在地面表面上，射线起点因此落在碰撞体边界上；不许从内部命中就会漏掉那一帧。
            HitFromInside = true,
            Enabled = true,
        };
        // 必须排除宿主自己：不排除的话射线往下第一个打到的就是角色自己的碰撞体，于是「地面」永远
        // 等于角色当前高度、影子永远贴在脚底不动，跳起来看不出高度，而它不报错。挂到宿主之后
        // ExcludeParent 本来就排除了它，这里再显式排一次，好让射线换个挂载点也仍然对。
        if (_host is CollisionObject2D body)
        {
            _ground.AddException(body);
        }
        // 射线挂宿主、不挂本节点：本节点带着纵深的绘制偏移，射线跟着它走就从偏过的位置起算，量出来
        // 的「地面」也带上那次偏移，画影子时又加一次 —— 影子会偏出去整整一个偏移量（实测木桩站在带
        // 最前沿时，影子落到脚底下方 24 像素）。地面是物理量，所以射线得站在宿主的物理位置上问。
        _host.AddChild(_ground);
        Sync();
    }

    /// <summary>按当前纵深与地面刷新绘制偏移和影子。</summary>
    /// <remarks>
    /// 由角色在物理推进之后显式调。顿帧冻结的那几帧不调，于是「冻结期间画面一动不动」连纵深偏移
    /// 与影子一起成立。
    /// </remarks>
    public void Sync()
    {
        Position = new Vector2(0, (float)DepthRendering.DrawOffsetWorldPx(Actor.DepthWorldPx));
        // 位置这一帧刚变过，而射线里存的还是上一帧的结果，所以要强制它重算一次。
        _ground.ForceRaycastUpdate();
        GroundYWorldPx = _ground.IsColliding() ? _ground.GetCollisionPoint().Y : null;
        HeightAboveGroundWorldPx = GroundYWorldPx is double ground
            ? Math.Max(0.0, ground - _host.GlobalPosition.Y)
            : 0.0;
        QueueRedraw();
    }

    /// <summary>影子：从那套图里按离地高度挑一张，摆在角色正下方的地面上。</summary>
    /// <remarks>
    /// 局部 y 取「地面 Y 减角色 Y」：两边各自都带着同一份纵深偏移，相减正好抵掉，所以这里不必
    /// 再管纵深 —— 贴地时得 0（影子中线与脚底重合），跳起来时得正数（影子留在地面上）。
    ///
    /// 图的中心对着那一点，摆在整数的局部坐标上。精灵的左上角也落在整数的局部坐标上（见
    /// <see cref="PlayerActor"/> 里精灵的偏移），所以两者是同一套像素格、跟着角色一起走 —— 哪天精灵挪到
    /// 半像素上，这一条就不成立了。不另去对齐世界的整像素：角色本身停在小数位置上，影子单独对齐世界的话
    /// 会和脚差出不到一像素，走动时在脚下来回跳。代价是跳起来时影子离真实地面最多差半像素。
    ///
    /// 不按朝向翻转：影子对称落在脚底中线上（光源在正上方正中），朝左朝右本来就是同一张。
    /// 本节点的 <c>_Draw</c> 先于子节点画，所以影子压在身体下面，不必另管先后。
    /// </remarks>
    public override void _Draw()
    {
        if (GroundYWorldPx is not double ground || ShadowSteps.Count == 0)
        {
            return;
        }

        var shadow = ShadowSteps[DepthRendering.ShadowStepAt(HeightAboveGroundWorldPx, ShadowSteps.Count)];
        var groundY = (int)Math.Round(ground - _host.GlobalPosition.Y);
        DrawTexture(shadow, new Vector2(-(shadow.GetWidth() / 2), groundY - (shadow.GetHeight() / 2)));
    }
}
