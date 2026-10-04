namespace Tinderhearth.Rules.Combat;

/// <summary>连段的类型。</summary>
public enum ComboKind
{
    /// <summary>没有在出招。</summary>
    None,

    /// <summary>轻攻击连段。</summary>
    Light,

    /// <summary>重攻击连段。</summary>
    Heavy,
}

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

/// <summary>轻重攻击的连段状态机。逐帧推进，纯逻辑、不碰引擎。</summary>
/// <remarks>
/// 一段攻击走三相：前摇、命中（判定框只在这几帧开）、后摇。后摇末尾有一个取消窗，窗内按同一种
/// 攻击就取消剩余后摇、续下一段；窗外或者按到连段末段就回到待机。
///
/// 一条连段要么全轻要么全重，窗内只认同一种攻击键，另一种被忽略；也不做取消到别的动作。空中起手
/// 的连段一落地就打断 —— 落地取消是动作游戏常规手感，也让「跳起来平 A 到落地」有个明确的收束。
///
/// 「同一次挥击只结算一次」不在这里，那是引擎层每次挥击各自维护已命中集合的事。本机只负责说清楚
/// 这一帧判定框开不开（<see cref="IsHitActive"/>）与取消窗此刻开不开（<see cref="IsComboWindowOpen"/>）。
/// </remarks>
public sealed class ComboStateMachine
{
    private int _step;
    private int _frameInPhase;
    private bool _startedAirborne;
    private bool _hitLanded;
    private int _lightBuffer;
    private int _heavyBuffer;

    /// <summary>当前连段类型，待机时是 <see cref="ComboKind.None"/>。</summary>
    public ComboKind Kind { get; private set; } = ComboKind.None;

    /// <summary>当前相位，只在出招时有意义。</summary>
    public AttackPhase Phase { get; private set; } = AttackPhase.Startup;

    /// <summary>当前在连段的第几段，0 起。待机时是 0。</summary>
    public int Step => _step;

    /// <summary>正在出招吗，含前摇、命中与后摇。出招期间地面移动被定住，见 <see cref="MotorState"/>。</summary>
    public bool IsAttacking => Kind != ComboKind.None;

    /// <summary>这一帧判定框开不开 —— 只有命中相开。</summary>
    public bool IsHitActive => IsAttacking && Phase == AttackPhase.Active;

    /// <summary>后摇末尾的取消窗此刻开着吗（而且还有下一段可续）。</summary>
    public bool IsComboWindowOpen =>
        IsAttacking
        && Phase == AttackPhase.Recovery
        && HasNextStep
        && _frameInPhase > RecoveryFrames - ComboWindowFrames;

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
    /// <param name="heavyPressed">本帧刚按下重攻击，取边沿。</param>
    /// <param name="isOnFloor">角色此刻是否在地面，由引擎给。</param>
    public void Tick(bool lightPressed, bool heavyPressed, bool isOnFloor)
    {
        // 输入缓冲：记住最近几帧内按下过的攻击键。续段窗只在后摇末尾开着几帧，狂点时按键很难正好
        // 落在窗内，实机表现是「一直第一段、偶尔才连上第二段」；缓冲让窗开前一点按下的也算数。
        // 只用于续段，不用于起手（起手仍认当帧边沿，Begin 会清缓冲），所以上一段的残留按键不会在
        // 回到待机时凭空起一段，单次点击也不会被缓冲误连到第二段。
        _lightBuffer = lightPressed ? CombatFeel.InputBufferFrames : Math.Max(0, _lightBuffer - 1);
        _heavyBuffer = heavyPressed ? CombatFeel.InputBufferFrames : Math.Max(0, _heavyBuffer - 1);

        if (Kind == ComboKind.None)
        {
            // 两键同帧按下时轻攻击优先：轻是基本招、起手更快，更贴近玩家「先戳一下」的预期。
            if (lightPressed)
            {
                Begin(ComboKind.Light, isOnFloor);
            }
            else if (heavyPressed)
            {
                Begin(ComboKind.Heavy, isOnFloor);
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
                if (_frameInPhase >= StartupFrames)
                {
                    EnterPhase(AttackPhase.Active);
                }

                break;

            case AttackPhase.Active:
                if (_frameInPhase >= ActiveFrames)
                {
                    EnterPhase(AttackPhase.Recovery);
                }

                break;

            case AttackPhase.Recovery:
                // 认缓冲而不只认当帧边沿：窗开前几帧内按过同一个键就算，见 Tick 顶部那段。
                var samePress = Kind == ComboKind.Light ? _lightBuffer > 0 : _heavyBuffer > 0;
                // 续段要命中确认，轻重都适用：本段打空则续段窗按了也不续，走完后摇。重击当前只有
                // 一段、没有续段，这道门对它不触发；以后加重击连段时即生效。
                var hitConfirmed = _hitLanded;
                if (samePress && IsComboWindowOpen && hitConfirmed)
                {
                    // 续段消费掉缓冲，下一段从零重新攒 —— 否则一次按键可能连推两段。
                    _lightBuffer = 0;
                    _heavyBuffer = 0;
                    _step++;
                    EnterPhase(AttackPhase.Startup);
                }
                else if (_frameInPhase >= RecoveryFrames)
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

    private void Begin(ComboKind kind, bool isOnFloor)
    {
        Kind = kind;
        _step = 0;
        _startedAirborne = !isOnFloor;
        // 起手这一下不留缓冲：否则它会一路撑到续段窗，把「点一下」变成「自动连到第二段」。
        _lightBuffer = 0;
        _heavyBuffer = 0;
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
        Kind = ComboKind.None;
        Phase = AttackPhase.Startup;
        _step = 0;
        _frameInPhase = 0;
        _startedAirborne = false;
        _hitLanded = false;
        _lightBuffer = 0;
        _heavyBuffer = 0;
    }

    private bool HasNextStep => _step + 1 < ChainLength;

    private int ChainLength =>
        Kind == ComboKind.Light ? CombatFeel.LightChainLength : CombatFeel.HeavyChainLength;

    private int StartupFrames =>
        Kind == ComboKind.Light ? CombatFeel.LightStartupFrames : CombatFeel.HeavyStartupFrames;

    private int ActiveFrames =>
        Kind == ComboKind.Light ? CombatFeel.LightActiveFrames : CombatFeel.HeavyActiveFrames;

    private int RecoveryFrames =>
        Kind == ComboKind.Light ? CombatFeel.LightRecoveryFrames : CombatFeel.HeavyRecoveryFrames;

    private int ComboWindowFrames =>
        Kind == ComboKind.Light ? CombatFeel.LightComboWindowFrames : CombatFeel.HeavyComboWindowFrames;
}
