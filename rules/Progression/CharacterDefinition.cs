using Tinderhearth.Rules.Foundation.Content;

namespace Tinderhearth.Rules.Progression;

/// <summary>
/// 一个角色的定义，来自数据文件而不是代码。主角、学生与敌人都是它，一份文件一个角色。
/// </summary>
/// <remarks>
/// 全部内容一律数据外置，是 mod 与 DLC 的公共地基，角色是其中第一项（设计仓 README.md 的
/// 「mod 的扩展边界」与「发行与平台」）。
///
/// 注意 <see cref="DisplayNameKey"/> 是键不是名字：角色的显示名走文本表，所以数据外置与文本
/// 外置这两条预留在这里是同一件事的两半。谁往这里塞一个中文名字面量，就等于同时破了两条。
///
/// 打仗要的那几格（四项属性、行动力、普通攻击、防御与战斗智慧）跟着打一场仗那份需求加进来（设计仓台账 GP-179）；
/// 面板怎么从属性算在 <see cref="Combat.CombatPanel"/>。敌我不在定义上，是站位那一侧的事：同一个角色可以这一场在对面、
/// 那一场是帮手。
///
/// 除了标识，每一格都用 <c>required</c>，缺了载入被拒，理由同 <see cref="ItemDefinition"/>。标识照旧在 <see cref="Parse"/>
/// 里核：没写与写法不对走同一条报错，那一条要指出取法在哪。
/// </remarks>
public sealed record CharacterDefinition
{
    public const string ContentDirectory = "characters";

    /// <summary>稳定标识：存档、站位与名册都按它引用。没写时是空串，由 <see cref="Parse"/> 拒掉。</summary>
    public string Id { get; init; } = "";

    /// <summary>显示名在文本表里的键。</summary>
    public required string DisplayNameKey
    {
        get;
        init => field = NonEmpty(value, nameof(DisplayNameKey));
    }

    /// <summary>
    /// 出生特质的标识列表。条数没有上限，限制靠互斥组（同一组最多一条），具体校验归特质系统。
    /// </summary>
    public required IReadOnlyList<string> TraitIds
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(TraitIds), "角色定义的 TraitIds 不得为 null，没有就写 []");
    }

    /// <summary>四项属性。</summary>
    public required CharacterAttributes Attributes
    {
        get;
        init => field = value ?? throw new ArgumentNullException(nameof(Attributes), "角色定义的 Attributes 不得为 null");
    }

    /// <summary>行动力起步值：每回合回满到这个数（工作区根 desc/角色面板.md 的「面板上的每一项」）。</summary>
    public required int ActionPoints
    {
        get;
        init => field = value > 0
            ? value
            : throw new ArgumentOutOfRangeException(nameof(ActionPoints), $"角色定义的 ActionPoints 必须为正，实际 {value}");
    }

    /// <summary>他的普通攻击是哪一招，指一份技能定义；在不在册由 <see cref="Combat.CombatContent"/> 核。</summary>
    public required string BasicAttackSkillId
    {
        get;
        init => field = NonEmpty(value, nameof(BasicAttackSkillId));
    }

    /// <summary>他的防御是哪一招，理由同 <see cref="BasicAttackSkillId"/>。</summary>
    public required string DefendSkillId
    {
        get;
        init => field = NonEmpty(value, nameof(DefendSkillId));
    }

    /// <summary>战斗智慧在哪一档。主角写 <see cref="Progression.CombatWisdom.Unpredictable"/>。</summary>
    public required CombatWisdom CombatWisdom { get; init; }

    /// <summary>解析一份角色定义，并核标识的写法。</summary>
    /// <remarks>
    /// 标识取名字的拼音：全小写、不带声调、不分隔，同音撞了的那个在末尾接数字；取法在设计仓
    /// production/界面绘制约定.md 的「文件放哪、叫什么」一节。这里核的只是写法 —— 一串小写字母，末尾
    /// 可以接数字。核它是为了让「拼音相同」就等于「字符串相同」：混进大写、声调或分隔符的那一份，会和
    /// 另一份同音的绕开 <see cref="ContentCatalog.ResolveById"/> 的撞名检查，而那不报错。
    ///
    /// 是不是真的拼音判不了（一个英文单词也全是小写字母），那一半靠写的人。
    /// </remarks>
    public static CharacterDefinition Parse(string json, string whatForDiagnostics)
    {
        var definition = ContentJson.Parse<CharacterDefinition>(json, whatForDiagnostics);
        if (!IsWellFormedId(definition.Id))
        {
            throw new InvalidDataException(
                $"{whatForDiagnostics} 的标识「{(definition.Id.Length == 0 ? "（没写）" : definition.Id)}」写法不对：只许小写字母，"
                    + "同音撞了才在末尾接数字（例如 yunyi2）。取法在设计仓 production/界面绘制约定.md 的"
                    + "「文件放哪、叫什么」一节");
        }

        return definition;
    }

    /// <summary>一串小写字母，末尾可以接数字。</summary>
    private static bool IsWellFormedId(string id)
    {
        if (string.IsNullOrEmpty(id))
        {
            return false;
        }

        var letters = 0;
        while (letters < id.Length && char.IsAsciiLetterLower(id[letters]))
        {
            letters++;
        }

        return letters > 0 && id[letters..].All(char.IsAsciiDigit);
    }

    private static string NonEmpty(string value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException($"角色定义的 {field} 不得为空", field)
            : value;
}
