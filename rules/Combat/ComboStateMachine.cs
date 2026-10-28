namespace Tinderhearth.Rules.Combat;

/// <summary>一段攻击的相位。</summary>
public enum AttackPhase
{
    /// <summary>前摇，判定框还没开。</summary>
    Startup,

    /// <summary>命中，判定框开着的这几帧。</summary>
    Active,

    /// <summary>后摇，末尾有一段取消窗。</summary>
    Recovery,
}

/// <summary>轻攻击的连段状态机。逐帧推进，纯逻辑、不碰引擎。</summary>
/// <remarks>
/// 一段攻击走三相：前摇、命中（判定框只在这几帧开）、后摇。后摇末尾有一个取消窗，窗内再按轻攻击
/// 就取消剩余后摇、续下一段；窗外或者按到连段末段就回到待机。
///
/// 普攻只有轻攻击一种（设计仓 canon/gameplay/战斗与关卡.md 的「按键与连击」），所以这里没有「连段
/// 类型」这一说；一招打得重不重是它自己的一格（<see cref="Weight"/>），不是另一种连段。也不做取消到
/// 别的动作。空中起手的连段一落地就打断 —— 落地取消是动作游戏常规手感，也让「跳起来平 A 到落地」
/// 有个明确的收束。
///
/// 「同一次挥击只结算一次」不在这里，那是引擎层每次挥击各自维护已命中集合的事。本机只负责说清楚
/// 这一帧判定框开不开（<see cref="IsHitActive"/>）与取消窗此刻开不开（<see cref="IsComboWindowOpen"/>）。
/// </remarks>
public sealed class ComboStateMachine
{
    private bool _attacking;
    private int _step;
    private int _frameInPhase;
    private bool _startedAirborne;
    private bool _hitLanded;
    private int _buffer;

    /// <summary>当前相位，只在出招时有意义。</summary>
    public AttackPhase Phase { get; private set; } = AttackPhase.Startup;

    /// <summary>当前在连段的第几段，0 起。待机时是 0。</summary>
    public int Step => _step;

    /// <summary>正在出招吗，含前摇、命中与后摇。出招期间地面移动被定住，见 <see cref="MotorState"/>。</summary>
    public bool IsAttacking => _attacking;

    /// <summary>这一帧判定框开不开 —— 只有命中相开。</summary>
    public bool IsHitActive => IsAttacking && Phase == AttackPhase.Active;

    /// <summary>后摇末尾的取消窗此刻开着吗（而且还有下一段可续）。</summary>
    public bool IsComboWindowOpen =>
        IsAttacking
        && Phase == AttackPhase.Recovery
        && HasNextStep
        && _frameInPhase > CombatFeel.LightRecoveryFrames - CombatFeel.LightComboWindowFrames;

    /// <summary>这一段打得轻还是重，命中反应按它取（见 <see cref="HitResolution.Resolve"/>）。</summary>
    /// <remarks>三段共用一格，值在 <see cref="CombatFeel.LightWeight"/>。</remarks>
    public HitWeight Weight => CombatFeel.LightWeight;

    /// <summary>引擎在本段判定框命中目标时调它一次。</summary>
    /// <remarks>
    /// 命中检测在引擎层（形状查询加纵深容差），规则层拿不到目标，所以「这一段打中没有」只能由
    /// 引擎回传。续段要求本段命中过；打空则这个标志一直为假，续段窗按了也不续。
    ///
    /// 标志在每段起手清零，所以每一段都要各自打中才能再续。
    /// </remarks>
    public void RegisterHit() => _hitLanded = true;

    /// <summary>推进一帧。</summary>
    /// <param name="lightPressed">本帧刚按下轻攻击，取边沿。</param>
    /// <param name="isOnFloor">角色此刻是否在地面，由引擎给。</param>
    public void Tick(bool lightPressed, bool isOnFloor)
    {
        // 输入缓冲：记住最近几帧内按下过的攻击键。续段窗只在后摇末尾开着几帧，狂点时按键很难正好
        // 落在窗内，实机表现是「一直第一段、偶尔才连上第二段」；缓冲让窗开前一点按下的也算数。
        // 只用于续段，不用于起手（起手仍认当帧边沿，Begin 会清缓冲），所以上一段的残留按键不会在
        // 回到待机时凭空起一段，单次点击也不会被缓冲误连到第二段。
        _buffer = lightPressed ? CombatFeel.InputBufferFrames : Math.Max(0, _buffer - 1);

        if (!_attacking)
        {
            if (lightPressed)
            {
                Begin(isOnFloor);
            }

            return;
        }

        if (_startedAirborne && isOnFloor)
        {
            Reset();
            return;
        }

        _frameInPhase++;

        switch (Phase)
        {
            case AttackPhase.Startup:
                if (_frameInPhase >= CombatFeel.LightStartupFrames)
                {
                    EnterPhase(AttackPhase.Active);
                }

                break;

            case AttackPhase.Active:
                if (_frameInPhase >= CombatFeel.LightActiveFrames)
                {
                    EnterPhase(AttackPhase.Recovery);
                }

                break;

            case AttackPhase.Recovery:
                // 认缓冲而不只认当帧边沿：窗开前几帧内按过就算，见 Tick 顶部那段。续段还要命中确认：
                // 本段打空则续段窗按了也不续，走完后摇。
                if (_buffer > 0 && IsComboWindowOpen && _hitLanded)
                {
                    // 续段消费掉缓冲，下一段从零重新攒 —— 否则一次按键可能连推两段。
                    _buffer = 0;
                    _step++;
                    EnterPhase(AttackPhase.Startup);
                }
                else if (_frameInPhase >= CombatFeel.LightRecoveryFrames)
                {
                    Reset();
                }

                break;

            default:
                throw new InvalidOperationException($"未知相位：{Phase}");
        }
    }

    /// <summary>本相已经过了几个物理帧。表现层读它来同步，不自己计时。</summary>
    public int FrameInPhase => _frameInPhase;

    /// <summary>碰撞之后立刻打断空中连段，不推进计时。</summary>
    public void AfterMove(bool onFloor)
    {
        if (_startedAirborne && onFloor)
        {
            Reset();
        }
    }

    private void Begin(bool isOnFloor)
    {
        _attacking = true;
        _step = 0;
        _startedAirborne = !isOnFloor;
        // 起手这一下不留缓冲：否则它会一路撑到续段窗，把「点一下」变成「自动连到第二段」。
        _buffer = 0;
        EnterPhase(AttackPhase.Startup);
    }

    private void EnterPhase(AttackPhase phase)
    {
        Phase = phase;
        _frameInPhase = 0;
        // 每段起手清命中标志：起手与续段都经这里进 Startup，所以每一段都要各自打中才能再续。
        if (phase == AttackPhase.Startup) _hitLanded = false;
    }

    private void Reset()
    {
        _attacking = false;
        Phase = AttackPhase.Startup;
        _step = 0;
        _frameInPhase = 0;
        _startedAirborne = false;
        _hitLanded = false;
        _buffer = 0;
    }

    private bool HasNextStep => _step + 1 < CombatFeel.LightChainLength;
}
