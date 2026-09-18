using Godot;
using Tinderhearth.Rules.Combat;

namespace Tinderhearth.World;

/// <summary>
/// 命中反馈的声音（`GP-20`）：打击音、受击音、挥空音，每次播放音高随机浮动。**素材是借来的测试
/// 占位，正式音效归 `ART-1`。**
/// </summary>
/// <remarks>
/// **声音是打击感的承重件，不是装饰。** 一项实证把影响打击感最强的三项列为顿帧、声音契合、镜头
/// 控制；而顿帧单独存在会被读成「卡顿／延迟」—— 命中当帧必须同时有视觉爆点与声音才读成力量。
/// 实机反馈重击「像多延迟了一会」正是
/// 顿帧「裸」着的表现。
///
/// **缺文件不抛、静默不响。** 这与 HUD 那边「缺素材就抛」的口径相反，是有理由的：这批 wav 在登记
/// 表里是「可进发行包=false」，发行导出会把它们排除掉（`ENG-12`），那时 <c>GD.Load</c> 拿到 null
/// 是**正常情形**而不是缺陷。抛异常会让训练房在发行包里直接打不开。缺了就不响，顿帧、白闪与打击
/// 特效照旧 —— 与 <see cref="HitSpark"/> 缺图时的处置同一口径。
///
/// **不用 <c>AudioStreamPlayer2D</c>。** 位置音会带来左右声道漂移，而训练房是单人侧视、镜头跟着
/// 主角走，声源与听者基本重合，位置音剩下的只有 panning 噪声。将来同屏多敌人要区分远近再换。
///
/// **一类声音一个播放器**：连段打得快时后一下会掐掉前一下。占位阶段接受这个代价 —— 真要叠响得做
/// 播放器池与叠加上限，那属于 `ART-1` 的混音口径，现在做等于先猜一遍。
/// </remarks>
public partial class CombatAudio : Node
{
    private const string Dir = "res://assets/downloaded/fists-of-fury";

    /// <summary>打击音的两个变体：每次命中随机取一个。**一个采样重复响就是机器味**，两个加音高抖动才像打了两下。</summary>
    private static readonly string[] HitClips = ["hit-1", "hit-2"];

    private readonly AudioStreamPlayer _hit = new();
    private readonly AudioStreamPlayer _hurt = new();
    private readonly AudioStreamPlayer _miss = new();
    private readonly AudioStream?[] _hitStreams = new AudioStream?[HitClips.Length];

    /// <summary>打击音（两个变体都在）加载成功了没有。给探针当量具，也用来解释「怎么不响」。</summary>
    public bool HitAvailable { get; private set; }

    /// <summary>受击音加载成功了没有。</summary>
    public bool HurtAvailable { get; private set; }

    /// <summary>挥空音加载成功了没有。</summary>
    public bool MissAvailable { get; private set; }

    /// <summary>最近一次播放用的音高。给探针核「抖动真的发生了」。</summary>
    public double LastPitch { get; private set; } = 1.0;

    /// <summary>播过几次打击音。给探针核「命中当帧真的响了」。</summary>
    public int HitPlays { get; private set; }

    /// <summary>播过几次挥空音。</summary>
    public int MissPlays { get; private set; }

    /// <summary>
    /// 打击音此刻在播没有。**只作诊断打印，不当判据** —— 它取决于跑它的机器有没有音频设备，
    /// 当判据会让门禁随环境变红（同「一种量具的没报错不是反证」那条教训）。
    /// </summary>
    public bool HitPlaying => _hit.Playing;

    public override void _Ready()
    {
        // 世界暂停时也要能响：顿帧期间战斗推进停住，但这一下的声音正是那一刻要听到的。
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
        GD.Print($"[GP20] audio hit={HitAvailable} hurt={HurtAvailable} miss={MissAvailable}"
            + $" jitter=±{CombatFeel.AudioPitchJitterPercent}%");
    }

    /// <summary>命中当帧：打击音 + 受击音一起响，与顿帧、白闪、火花同一帧。</summary>
    /// <remarks>轻重目前用同一批采样，只靠音高区分不出轻重 —— 分轻重两套采样归 `ART-1`。</remarks>
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

    /// <summary>挥空：一次挥击的 Active 窗走完且零命中。打空也要有反馈，否则玩家分不清「没打中」与「攻击没出去」。</summary>
    public void PlayMiss()
    {
        if (!MissAvailable)
        {
            return;
        }
        Play(_miss);
        MissPlays++;
    }

    /// <summary>随机音高播一次。抖动幅度取自 <see cref="CombatFeel.AudioPitchJitterPercent"/>。</summary>
    private void Play(AudioStreamPlayer player)
    {
        var jitter = CombatFeel.AudioPitchJitterPercent / 100.0;
        LastPitch = 1.0 + GD.RandRange(-jitter, jitter);
        player.PitchScale = (float)LastPitch;
        player.Play();
    }

    /// <summary>载一个采样。**缺了返回 null 而不抛**，理由见类注释。</summary>
    private static AudioStream? Load(string name)
    {
        var path = $"{Dir}/{name}.wav";
        return ResourceLoader.Exists(path) ? GD.Load<AudioStream>(path) : null;
    }
}
