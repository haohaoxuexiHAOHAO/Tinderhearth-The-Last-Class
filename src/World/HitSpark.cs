using Godot;

namespace Tinderhearth.World;

/// <summary>
/// 命中点上的打击火花：播一遍 4 帧动画就自己退场。素材是借来的测试占位，正式像素特效以后替换。
/// </summary>
/// <remarks>
/// 用手绘多帧图而不是代码画几何：这类打击火花是一坨亮块崩成碎片再散开、全程不旋转，过程化几何
/// 做不出那个质感 —— 试过的过程化版本看着像转的风车。
///
/// 它自己播、播完自己释放，不受战斗推进的冻结影响：命中当帧爆出来，顿帧冻住角色时它照样在动，
/// 正好把眼睛吸到命中点。
/// </remarks>
public partial class HitSpark : Node2D
{
    private const string SheetPath = "res://assets/downloaded/fists-of-fury/spark.png";
    private const int Frames = 4;
    private const int FrameSize = 48;
    private const double Fps = 30.0;
    private static readonly Color HeavyTint = new("ffb347");
    private const float HeavyScale = 1.6f;

    /// <summary>这一下是不是重击。重击的火花画得更大、也染暖，好与轻击一眼分得开。</summary>
    /// <remarks>
    /// 染色靠 <c>Modulate</c> 的乘法：素材是白的，白乘暖色就得到暖色。这只是占位手段，
    /// 正式美术会给轻重各一套图。
    /// </remarks>
    public bool Heavy { get; init; }

    public override void _Ready()
    {
        var texture = GD.Load<Texture2D>(SheetPath);
        if (texture == null || texture.GetHeight() != FrameSize
            || texture.GetWidth() != FrameSize * Frames)
        {
            // 缺图或尺寸对不上就不画火花、直接退场。顿帧与震屏不受影响。
            QueueFree();
            return;
        }

        var frames = new SpriteFrames();
        frames.AddAnimation("spark");
        frames.SetAnimationLoopMode("spark", SpriteFrames.LoopMode.None);
        frames.SetAnimationSpeed("spark", Fps);
        for (var i = 0; i < Frames; i++)
        {
            frames.AddFrame("spark", new AtlasTexture
            {
                Atlas = texture,
                Region = new Rect2(i * FrameSize, 0, FrameSize, FrameSize),
            });
        }

        var sprite = new AnimatedSprite2D { SpriteFrames = frames };
        if (Heavy)
        {
            sprite.Modulate = HeavyTint;
            sprite.Scale = new Vector2(HeavyScale, HeavyScale);
        }

        AddChild(sprite);
        sprite.AnimationFinished += QueueFree;
        sprite.Play("spark");
    }
}
