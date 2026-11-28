using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Progression;

namespace Tinderhearth.Rules.Combat;

/// <summary>打仗要读的那几样内容：角色、技能与战斗配置。读完核角色指的招在不在册。</summary>
/// <remarks>
/// 启动时那一次核对与战斗场景走同一处，理由同 <see cref="ItemCatalog"/>：两处各读一份的话，坏掉的那一份要进了战斗才报错，
/// 导出冒烟判不到。指不到的招一次全报，一行一处，免得改完一处重跑才看见下一处。
/// </remarks>
public sealed class CombatContent
{
    private CombatContent(
        IReadOnlyDictionary<string, CharacterDefinition> characters,
        IReadOnlyDictionary<string, SkillDefinition> skills,
        CombatConfig config)
    {
        Characters = characters;
        Skills = skills;
        Config = config;
    }

    /// <summary>全部角色定义，按标识取。</summary>
    public IReadOnlyDictionary<string, CharacterDefinition> Characters { get; }

    /// <summary>全部技能定义，按标识取。</summary>
    public IReadOnlyDictionary<string, SkillDefinition> Skills { get; }

    /// <summary>战斗那一份配置。</summary>
    public CombatConfig Config { get; }

    /// <summary>角色与技能要在文本表里有的那几条。</summary>
    public IReadOnlyList<string> TextKeys =>
    [
        .. Characters.Values.OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => c.DisplayNameKey),
        .. Skills.Values.OrderBy(s => s.Id, StringComparer.Ordinal).SelectMany(s => s.TextKeys),
    ];

    /// <summary>从内容目录里读角色、技能与战斗配置，再核引用。</summary>
    public static CombatContent Load(ContentCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var characters = catalog.ResolveById(
            CharacterDefinition.ContentDirectory, e => CharacterDefinition.Parse(e.Text, e.RelativePath), c => c.Id);
        var skills = catalog.ResolveById(
            SkillDefinition.ContentDirectory, e => SkillDefinition.Parse(e.Text, e.RelativePath), s => s.Id);
        return Build(characters, skills, CombatConfig.Load(catalog));
    }

    /// <summary>核每个角色指的普通攻击与防御都在册；不在册的一次全报。</summary>
    public static CombatContent Build(
        IReadOnlyDictionary<string, CharacterDefinition> characters,
        IReadOnlyDictionary<string, SkillDefinition> skills,
        CombatConfig config)
    {
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(skills);
        ArgumentNullException.ThrowIfNull(config);

        var missing = new List<string>();
        foreach (var character in characters.Values.OrderBy(c => c.Id, StringComparer.Ordinal))
        {
            if (!skills.ContainsKey(character.BasicAttackSkillId))
            {
                missing.Add($"角色 {character.Id} 的普通攻击「{character.BasicAttackSkillId}」");
            }

            if (!skills.ContainsKey(character.DefendSkillId))
            {
                missing.Add($"角色 {character.Id} 的防御「{character.DefendSkillId}」");
            }
        }

        return missing.Count == 0
            ? new CombatContent(characters, skills, config)
            : throw new InvalidDataException(
                $"角色指的招有 {missing.Count} 处不在 {SkillDefinition.ContentDirectory}/ 里：\n{string.Join("\n", missing)}");
    }

    /// <summary>这个角色的面板，照他的属性与配置算。</summary>
    public CombatPanel PanelOf(CharacterDefinition character)
    {
        ArgumentNullException.ThrowIfNull(character);
        return CombatPanel.From(character.Attributes, Config.Panel);
    }
}
