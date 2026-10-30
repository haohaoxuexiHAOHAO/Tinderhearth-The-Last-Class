using System.Reflection;
using Tinderhearth.Rules.Economy;

namespace Tinderhearth.Rules.Tests.Economy;

/// <summary>把一片地每一格的全部公开属性写成一行字，逐项比两片地时比这几行。</summary>
/// <remarks>
/// 读的是地块全部公开属性，所以给地块加一个属性不用改这里；对不上时断言打出来的那一行一眼看得出是哪一格哪一项。
/// </remarks>
internal static class PlotSnapshot
{
    /// <summary>一片地，按格排好，一格一行。</summary>
    public static List<string> Of(WorldState world) =>
        [.. world.Plots.Cells.Select(c => $"{c.Cell} {Describe(c.Plot)}")];

    /// <summary>一格地的全部公开属性，按属性名排好。</summary>
    public static string Describe(Plot plot) => string.Join(
        " ",
        typeof(Plot).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}={p.GetValue(plot) ?? "null"}"));
}
