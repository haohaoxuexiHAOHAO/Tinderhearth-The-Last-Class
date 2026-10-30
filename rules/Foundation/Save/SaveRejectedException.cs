namespace Tinderhearth.Rules.Foundation.Save;

/// <summary>一份存档为什么打不开。几种分得开，界面说的话各不一样。</summary>
public enum SaveRejection
{
    /// <summary>读不出来：不是 JSON，或者最外层的形状不对。</summary>
    Unreadable = 1,

    /// <summary>版本低于最低兼容版本：这一版要求重开档，界面要指向更新公告。</summary>
    BelowMinimumVersion = 2,

    /// <summary>版本比当前的新：这份存档是更新的游戏写的。</summary>
    NewerVersion = 3,

    /// <summary>与当前同一版却缺字段：不是版本演进，是文件被写坏了。</summary>
    MissingField = 4,

    /// <summary>有一个认不出的分片：它不许被悄悄丢掉。</summary>
    UnknownShard = 5,
}

/// <summary>一份存档被拒：带着是哪一种，以及说清是哪一处的那句话。</summary>
/// <remarks>
/// 被拒发生在任何一个分片交给持有者之前，所以被拒时不会读出半份世界。几种的分法与处置出自设计仓
/// decisions/ADR-0015-存档的版本与缺字段口径.md 与 decisions/ADR-0017-版本更新对旧存档的两种后果.md。
/// </remarks>
public sealed class SaveRejectedException(SaveRejection reason, string message) : Exception(message)
{
    /// <summary>是哪一种。</summary>
    public SaveRejection Reason { get; } = reason;
}
