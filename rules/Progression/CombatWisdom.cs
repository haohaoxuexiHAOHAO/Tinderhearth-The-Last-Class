namespace Tinderhearth.Rules.Progression;

/// <summary>战斗智慧：一个角色自己行动时有多会打。</summary>
/// <remarks>
/// 八档从弱到强照工作区根 desc/战斗规则.md 的「战斗智慧」排。数据里按名字写，所以中间插一档不会悄悄改掉旧数据的含义
/// （理由同 <see cref="Foundation.Content.ContentJson.Options"/>）。
///
/// <see cref="Unpredictable"/> 不在那八档里：主角总由玩家操作，面板上照工作区根 desc/角色面板.md 写「变幻无常」。
/// </remarks>
public enum CombatWisdom
{
    /// <summary>愚笨，最低一档。</summary>
    Dull,

    /// <summary>迟钝。</summary>
    Slow,

    /// <summary>平常。</summary>
    Ordinary,

    /// <summary>机敏。</summary>
    Alert,

    /// <summary>聪慧。</summary>
    Clever,

    /// <summary>睿智。</summary>
    Wise,

    /// <summary>卓绝。</summary>
    Outstanding,

    /// <summary>超凡，最高一档。</summary>
    Transcendent,

    /// <summary>变幻无常：只给主角，不是 AI 的一档。</summary>
    Unpredictable,
}
