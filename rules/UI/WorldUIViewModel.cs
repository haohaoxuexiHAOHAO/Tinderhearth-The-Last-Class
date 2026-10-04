namespace Tinderhearth.Rules.UI;

/// <summary>
/// 敌人等级。血条只给精英与 BOSS，杂兵没有，见设计仓 canon/gameplay/战斗与关卡.md。
/// </summary>
public enum EnemyRank
{
    /// <summary>杂兵：无血条。</summary>
    Trash,

    /// <summary>精英：有血条。</summary>
    Elite,

    /// <summary>BOSS：有血条。</summary>
    Boss,
}

/// <summary>读条状态。它画在执行者身上，随角色移动。</summary>
/// <remarks>
/// 值由调用方传入，本记录只管钳制与派生。读条时长那类数值不在这里。
/// </remarks>
public sealed record CastState(bool Active, double Progress, bool Interrupted)
{
    /// <summary>读条进度，钳在 0 到 1。</summary>
    public double ClampedProgress => System.Math.Clamp(Progress, 0.0, 1.0);

    /// <summary>
    /// 读条显不显示。受击中断后就不显示 —— 这里只保证画面上停得下来，
    /// 「中断了算不算消耗物品」属于玩法实现，不在这里。
    /// </summary>
    public bool Visible => Active && !Interrupted;

    /// <summary>没有读条时的空状态。</summary>
    public static CastState Idle => new(false, 0.0, false);
}

/// <summary>
/// 一个敌人的血量与等级。决定它出不出血条、条有多长。
/// </summary>
public sealed record EliteHealth
{
    /// <summary>坏值直接抛 —— 负血量是调用方的错，静默钳制会把它藏起来。</summary>
    public EliteHealth(int current, int max, EnemyRank rank)
    {
        if (max < 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(max), max, "血量上限不能为负");
        }

        if (current < 0)
        {
            throw new System.ArgumentOutOfRangeException(nameof(current), current, "当前血量不能为负");
        }

        Current = current;
        Max = max;
        Rank = rank;
    }

    /// <summary>当前血量。</summary>
    public int Current { get; }

    /// <summary>血量上限。</summary>
    public int Max { get; }

    /// <summary>敌人等级。</summary>
    public EnemyRank Rank { get; }

    /// <summary>血量比例，钳在 0 到 1。上限为 0 时记 0，免得除零。</summary>
    public double Ratio => Max <= 0 ? 0.0 : System.Math.Clamp((double)Current / Max, 0.0, 1.0);

    /// <summary>这个敌人出不出血条。</summary>
    public bool ShowsBar => Rank is EnemyRank.Elite or EnemyRank.Boss;
}

/// <summary>
/// 世界空间 UI 的呈现开关。伤害数字默认关闭，玩家可以在设置里打开。
/// </summary>
/// <remarks>
/// 这是表现规则不是玩法数值，所以照 <see cref="CameraFeel"/> 的做法留在规则层：既不进
/// <c>data/config/game.json</c>（那是 mod 能改的内容，而 mod 不该改表现），也不进数值模型。
///
/// 它算玩家级偏好：跨存档位、不进任何存档分片、缺字段补默认值。这一层的机制在设计仓
/// design/存档系统.md 的「玩家级偏好：第三样东西，不是分片」一节。值由调用方注入。
/// </remarks>
public sealed record WorldUIOptions(bool ShowDamageNumbers = false)
{
    /// <summary>默认呈现：伤害数字关。</summary>
    public static WorldUIOptions Default => new();
}
