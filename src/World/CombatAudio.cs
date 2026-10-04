using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>
/// 命中反馈的声音：打击音、受击音、挥空音，每次播放把音高随机抖一点。素材是借来的测试占位。
/// </summary>
/// <remarks>
/// 声音必须和视觉落在命中同一帧上。顿帧单独存在会被读成卡顿 —— 实机反馈过重击「像多延迟了
/// 一会」，那就是只有顿帧、没有声音的样子。
///
/// 缺音频文件不抛、静默不响。这批 wav 不进发行包，发行导出会把它们排除掉，那时 <c>GD.Load</c>
/// 拿到 null 是正常情形而不是缺陷；抛异常会让训练房在发行包里直接打不开。缺了就不响，顿帧、
/// 白闪与打击火花照旧，与 <see cref="HitSpark"/> 缺图时的处置一致。
///
/// 两条占位阶段的省法：不用 <c>AudioStreamPlayer2D</c>（单人侧视下镜头跟着主角走，声源与听者
/// 基本重合，位置音只剩左右声道噪声）；一类声音只有一个播放器（连段打得快时后一下会掐掉前
/// 一下）。等同屏多敌人要区分远近、要叠响时再改。
/// </remarks>
public partial class CombatAudio : Node
{
    private const string Dir = "res://assets/downloaded/fists-of-fury";

    /// <summary>打击音的两个变体，每次命中随机取一个。</summary>
    /// <remarks>
    /// 同一个采样重复响听起来像机器；两个采样再配上音高抖动，才像真打了两下。
    /// </remarks>
    private static readonly string[] HitClips = ["hit-1", "hit-2"];

    private readonly AudioStreamPlayer _hit = new();
    private readonly AudioStreamPlayer _hurt = new();
    private readonly AudioStreamPlayer _miss = new();
    private readonly AudioStream?[] _hitStreams = new AudioStream?[HitClips.Length];

    /// <summary>打击音的两个变体都载上了没有。少一个就整类不响，也用来解释「怎么没声」。</summary>
    public bool HitAvailable { get; private set; }

    /// <summary>受击音载上了没有。</summary>
    public bool HurtAvailable { get; private set; }

    /// <summary>挥空音载上了没有。</summary>
    public bool MissAvailable { get; private set; }

    /// <summary>最近一次播放实际用的音高（1.0 是原速），用来确认音高抖动真的在发生。</summary>
    public double LastPitch { get; private set; } = 1.0;

    /// <summary>打击音播过几次。</summary>
    public int HitPlays { get; private set; }

    /// <summary>挥空音播过几次。</summary>
    public int MissPlays { get; private set; }

    /// <summary>打击音此刻在播没有。</summary>
    /// <remarks>
    /// 只拿来打诊断日志。它取决于跑的那台机器有没有音频设备，所以不能当成「声音接对了」的证据。
    /// </remarks>
    public bool HitPlaying => _hit.Playing;

    public override void _Ready()
    {
        // 世界暂停时也要能响：顿帧期间战斗推进停住，而这一下的声音正是那一刻要听到的。
        ProcessMode = ProcessModeEnum.Always;
        for (var i = 0; i < HitClips.Length; i++)
        {
            _hitStreams[i] = Load(HitClips[i]);
        }
        HitAvailable = Array.TrueForAll(_hitStreams, s => s != null);
        _hurt.Stream = Load("grunt");
        HurtAvailable = _hurt.Stream != null;
        _miss.Stream = Load("miss");
        MissAvailable = _miss.Stream != null;
        AddChild(_hit);
        AddChild(_hurt);
        AddChild(_miss);
        GD.Print($"[战斗音效] 打击音={HitAvailable} 受击音={HurtAvailable} 挥空音={MissAvailable}"
            + $" 音高抖动=±{CombatFeel.AudioPitchJitterPercent}%");
    }

    /// <summary>命中当帧：打击音与受击音一起响，和顿帧、白闪、火花落在同一帧。</summary>
    /// <remarks>轻重击目前共用这一批采样，只靠音高抖动分不出轻重；分轻重两套采样等正式音效。</remarks>
    public void PlayHit()
    {
        if (!HitAvailable)
        {
            return;
        }
        _hit.Stream = _hitStreams[GD.RandRange(0, HitClips.Length - 1)];
        Play(_hit);
        HitPlays++;
        if (HurtAvailable)
        {
            Play(_hurt);
        }
    }

    /// <summary>挥空音：一次挥击的判定窗走完了却一个都没碰到。</summary>
    /// <remarks>打空也要有反馈，否则玩家分不清「没打中」和「这一下压根没出去」。</remarks>
    public void PlayMiss()
    {
        if (!MissAvailable)
        {
            return;
        }
        Play(_miss);
        MissPlays++;
    }

    /// <summary>抖一点音高播一次。抖动幅度是百分比，取自 <see cref="CombatFeel.AudioPitchJitterPercent"/>。</summary>
    private void Play(AudioStreamPlayer player)
    {
        var jitter = CombatFeel.AudioPitchJitterPercent / 100.0;
        LastPitch = 1.0 + GD.RandRange(-jitter, jitter);
        player.PitchScale = (float)LastPitch;
        player.Play();
    }

    /// <summary>载一个采样。缺了返回 <c>null</c> 而不抛，理由见类说明。</summary>
    private static AudioStream? Load(string name)
    {
        var path = $"{Dir}/{name}.wav";
        return ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
    }
}
