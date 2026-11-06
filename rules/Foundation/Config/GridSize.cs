namespace Tinderhearth.Rules.Foundation.Config;

/// <summary>一张网格几格宽、几格高，量纲是格。</summary>
/// <remarks>
/// 从配置里读，所以是位置参数：缺了的那一项拿 0 填上，由读它的那一处判成「必须为正」当场报错（<see cref="GameConfig"/>）。
/// </remarks>
/// <param name="Width">几格宽。</param>
/// <param name="Height">几格高。</param>
public readonly record struct GridSize(int Width, int Height)
{
    /// <summary>一共几格。</summary>
    public int Cells => Width * Height;

    /// <summary>写成「宽×高」。</summary>
    public override string ToString() => $"{Width}×{Height}";
}
