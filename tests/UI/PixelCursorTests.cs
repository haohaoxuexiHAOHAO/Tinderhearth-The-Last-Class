using Tinderhearth.Rules.UI;
using Xunit;

namespace Tinderhearth.Rules.Tests.UI;

/// <summary>鼠标指针：按几倍放大、热点放大后落在哪、素材清单里带热点的正好是换上去的那三张。</summary>
/// <remarks>
/// 指针看着多大由作者实机看；这里判的是看不出来的：热点偏到图外引擎只报一行错、指针照旧是系统的，
/// 生成器多画了一张指针而引擎层没换上，画面上什么都不缺。
/// </remarks>
public class PixelCursorTests
{
    [Theory]
    [InlineData(1.0, 1)]
    [InlineData(3.0, 3)]
    [InlineData(2.5, 2)]
    [InlineData(0.5, 1)]
    public void 倍数取画面倍数的下整而且至少一倍(double viewScale, int expected)
    {
        Assert.Equal(expected, PixelCursor.ScaleOf(viewScale));
    }

    [Fact]
    public void 放大之后不超过引擎收的上限()
    {
        var scale = PixelCursor.ScaleOf(1000.0);

        Assert.All(PixelCursor.All, p =>
            Assert.True(Math.Max(p.Size.Width, p.Size.Height) * scale <= PixelCursor.MaxSide,
                $"{p.FilePath} 放大 {scale} 倍超过 {PixelCursor.MaxSide}"));
    }

    [Fact]
    public void 素材清单里带热点的正好是换上去的那三张()
    {
        var withHotspot = UIArtManifest.Pieces.Where(p => p.Hotspot is not null).Select(p => p.FilePath).ToHashSet();

        Assert.Equal(withHotspot, PixelCursor.All.Select(p => p.FilePath).ToHashSet());
    }

    [Fact]
    public void 热点放大之后仍在放大后的图里()
    {
        foreach (var scale in Enumerable.Range(1, PixelCursor.ScaleOf(1000.0)))
        {
            Assert.All(PixelCursor.All, p =>
            {
                var hotspot = PixelCursor.HotspotOf(p, scale);
                Assert.InRange(hotspot.X, 0, (p.Size.Width * scale) - 1);
                Assert.InRange(hotspot.Y, 0, (p.Size.Height * scale) - 1);
            });
        }
    }

    [Fact]
    public void 不是指针的件问热点就抛()
    {
        Assert.Throws<ArgumentException>(() => PixelCursor.HotspotOf(MiniFont.Sheet, 1));
    }
}
