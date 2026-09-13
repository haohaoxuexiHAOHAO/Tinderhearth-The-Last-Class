using Godot;

namespace Tinderhearth.World;

/// <summary>
/// 命中点的打击特效（`GP-20` 占位）：播一遍 4 帧手绘冲击火花即消。**素材是借来的测试占位，待替换成正式像素特效。**
/// </summary>
/// <remarks>
/// 为什么用手绘多帧图、不用代码画几何：拳皇/街霸一类的打击火花都是**手绘多帧像素动画**（一坨亮块 → 崩成碎片
/// → 散点 → 消失，不旋转），过程化几何做不出那个质感 —— 2026-09-12 的过程化版本被作者否掉，看着像转的风车。
/// 这里用 `fists-of-fury` 借来的 4 帧火花（`assets/downloaded/fists-of-fury/spark.png`，登记为测试占位、不进
/// 发行包）当占位，正式像素火花以后替换（`GP-20`）。
///
/// 自行播放、播完即释放（<see cref="AnimatedSprite2D"/> 的 <c>AnimationFinished</c>），**不进战斗循环的顿帧
/// gate**：命中当帧爆出，顿帧冻住角色时它照样在动，正好把眼睛吸到命中点。轻击白色、重击代码染暖色区分。
/// </remarks>
public partial class HitSpark : Node2D
{
    private const string SheetPath = "res://assets/downloaded/fists-of-fury/spark.png";
    private const int Frames = 4;
    private const int FrameSize = 48;
    private const double Fps = 30.0;
    private static readonly Color HeavyTint = new("ffb347");
    private const float HeavyScale = 1.6f;

    /// <summary>重击的火花**更大更暖**（白 × 暖 = 暖），与轻击拉开区分 —— 否则同尺寸白火花看不出是重击。占位手段，正式美术分轻重两套。</summary>
    public bool Heavy { get; init; }

    public override void _Ready()
    {
        var texture = GD.Load<Texture2D>(SheetPath);
        if (texture == null || texture.GetHeight() != FrameSize
            || texture.GetWidth() != FrameSize * Frames)
        {
            // 缺图或尺寸不符：不画特效、直接退场，命中的顿帧/震屏不受影响。
            QueueFree();
            return;
        }

        var frames = new SpriteFrames();
        frames.AddAnimation("spark");
        frames.SetAnimationLoop("spark", false);
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
