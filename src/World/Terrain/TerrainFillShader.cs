using System.Text;

namespace Tinderhearth.World.Terrain;

/// <summary>地面纹理那段着色器：显示层里某种地形的主色，换成那种地形纹理图上同一世界位置的像素。</summary>
/// <remarks>
/// 按世界坐标取，所以纹理跨格、跨图块连续，按纹理图自己的边长重复。只换主色那些像素、不混色，画出来的每个像素
/// 都来自纹理图或双网格图本身；比颜色按 8 位整数比，浮点颜色直接比相等很脆。规矩在设计仓
/// production/场景绘制约定.md 的「双网格」一节。
///
/// 每种要铺的地形各一个取样器、一个主色，代码按铺几种现写一段，不用取样器数组：GLSL ES 3.00 只许拿常量下标
/// 取样器数组，按循环变量去取不保证编得过。
/// </remarks>
public static class TerrainFillShader
{
    /// <summary>代码挂上去的材质带着这一条元数据，好与作者自己挂的材质分开。</summary>
    public const string OwnerMeta = "terrain_fill";

    /// <summary>第 <paramref name="slot"/> 种要铺的地形，它那张纹理图的参数名。</summary>
    public static string TextureParam(int slot) => $"fill_{slot}";

    /// <summary>第 <paramref name="slot"/> 种要铺的地形，它主色的参数名，值是 8 位 RGB。</summary>
    public static string KeyParam(int slot) => $"key_{slot}";

    /// <summary>铺 <paramref name="slots"/> 种地形的那一段着色器代码。</summary>
    public static string Code(int slots)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(slots, 1);

        var code = new StringBuilder();
        code.AppendLine("shader_type canvas_item;");
        code.AppendLine("varying vec2 world_pos;");
        for (var slot = 0; slot < slots; slot++)
        {
            code.AppendLine($"uniform sampler2D {TextureParam(slot)} : filter_nearest, repeat_enable;");
            code.AppendLine($"uniform ivec3 {KeyParam(slot)};");
        }

        code.AppendLine("void vertex() {");
        code.AppendLine("    world_pos = (MODEL_MATRIX * vec4(VERTEX, 0.0, 1.0)).xy;");
        code.AppendLine("}");
        code.AppendLine("void fragment() {");
        code.AppendLine("    ivec3 c = ivec3(round(texture(TEXTURE, UV).rgb * 255.0));");
        code.AppendLine("    vec2 p = floor(world_pos);");
        for (var slot = 0; slot < slots; slot++)
        {
            var branch = slot == 0 ? "if" : "else if";
            var fill = TextureParam(slot);
            code.AppendLine($"    {branch} (all(equal(c, {KeyParam(slot)}))) {{");
            code.AppendLine($"        COLOR.rgb = texelFetch({fill}, ivec2(mod(p, vec2(textureSize({fill}, 0)))), 0).rgb;");
            code.AppendLine("    }");
        }

        code.AppendLine("}");
        return code.ToString();
    }
}
