namespace Tinderhearth.Rules.Tests.Progression;

/// <summary>测试用的角色定义文本：打仗要的那几格都填上，只换标识、显示名与特质。</summary>
/// <remarks>
/// 角色定义每一格都是必填的，只想测标识或覆盖语义的用例照样要写全，所以写全的那一份收在这里，各处不各抄一份。
/// 标识传 <c>null</c> 就不写那个键，测「没写标识」用。
/// </remarks>
internal static class TestCharacters
{
    /// <summary>一份写全了的角色定义。</summary>
    public static string Json(string? id, string displayNameKey, string traitIdsJson = "[]")
    {
        var idPart = id is null ? "" : $"\"id\": \"{id}\", ";
        return "{ " + idPart
            + $"\"displayNameKey\": \"{displayNameKey}\", \"traitIds\": {traitIdsJson}, "
            + "\"attributes\": { \"strength\": 5, \"agility\": 5, \"constitution\": 5, \"intellect\": 5 }, "
            + "\"actionPoints\": 5, \"basicAttackSkillId\": \"basic_attack\", \"defendSkillId\": \"defend\", "
            + "\"combatWisdom\": \"Dull\" }";
    }
}
