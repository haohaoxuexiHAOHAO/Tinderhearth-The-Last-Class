using Tinderhearth.Rules.Foundation.Content;
using Tinderhearth.Rules.Progression;
using Tinderhearth.Rules.Tests.Foundation;
using Tinderhearth.Rules.Tests.UI;

namespace Tinderhearth.Rules.Tests.Progression;

/// <summary>工程里那批物品数据：类别、两条轴的档位与物品。只读不写。</summary>
/// <remarks>
/// 读法与启动、农场同一处（<see cref="ItemCatalog.Load"/>），色板读仓库里那一份。作物那几份端到端也读它：作物要指到
/// 在册的物品，读作物之前得先有这一份。
/// </remarks>
internal static class ShippedItems
{
    /// <summary>工程里那几个内容目录的全部文件，路径写成内容目录里的相对路径。</summary>
    public static Dictionary<string, string> Files()
    {
        string[] directories =
        [
            ItemCategory.ContentDirectory,
            ItemTier.ContentDirectoryOf(ItemTierAxis.Rarity),
            ItemTier.ContentDirectoryOf(ItemTierAxis.Quality),
            ItemDefinition.ContentDirectory,
        ];
        var files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var directory in directories)
        {
            foreach (var path in Directory.GetFiles(Path.Combine(RepoFiles.Root(), "data", directory), "*.json")
                         .Order(StringComparer.Ordinal))
            {
                files[$"{directory}/{Path.GetFileName(path)}"] = File.ReadAllText(path);
            }
        }

        return files;
    }

    /// <summary>读工程里那一份，按仓库里的色板核。</summary>
    public static ItemCatalog Load()
    {
        var catalog = new ContentCatalog();
        catalog.AddSource(new InMemoryContentSource("base", Files()));
        return ItemCatalog.Load(catalog, PaletteFile.ReadPalette().BandOf);
    }
}
