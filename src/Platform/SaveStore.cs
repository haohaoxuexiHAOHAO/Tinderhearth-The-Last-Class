using System.Text;

namespace Tinderhearth.Platform;

/// <summary>存档那个文件的读与写。写是一次原子替换：要么换成完整的新一份，要么一字不动。</summary>
/// <remarks>
/// 这里刻意不用 Godot 的 FileAccess 与 DirAccess：4.7.2 的 DirAccess.RenameAbsolute 在 Windows 上是先删掉目标、再改名，
/// 删完改名之前断电，盘上一份存档都没有；FileAccess.Flush 也只把数据交给系统缓存。.NET 那一套能落盘、能一次带覆盖地改名。
/// 源码出处与只读实测在设计仓 spec/prd-sleep-advances-day.md 的「技术考虑」一节。
///
/// 只做一位存档。存档位那一组、存档位摘要与临时续玩点还没有，归设计仓台账 GP-54。
/// </remarks>
public sealed class SaveStore
{
    /// <summary>第一位存档的文件名。</summary>
    public const string FirstSlotFileName = "slot_01.json";

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    /// <param name="directory">存档目录的本机绝对路径（<c>user://saves</c> 换算过来的那一个）。</param>
    public SaveStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        Directory = directory;
        Path = System.IO.Path.Combine(directory, FirstSlotFileName);
    }

    /// <summary>存档目录。</summary>
    public string Directory { get; }

    /// <summary>那一位存档文件的绝对路径，报错与打日志都写它，玩家在文件管理器里点得到。</summary>
    public string Path { get; }

    /// <summary>读出存档全文；还没有存档时为 <c>null</c>。文件在却读不出来时照常抛出去。</summary>
    public string? ReadOrNull() => File.Exists(Path) ? File.ReadAllText(Path, Utf8NoBom) : null;

    /// <summary>写一份新的存档：同目录写临时文件、落盘、一次带覆盖的改名。中间哪一步失败，原来那一份一字不动。</summary>
    /// <returns>写下去的字节数。</returns>
    /// <remarks>
    /// 临时文件必须与目标同目录：跨盘的改名会退化成复制加删除，又回到写一半的风险上。改名之前失败时把临时文件删掉，
    /// 删不掉也不要紧，下一次写会覆盖它。
    /// </remarks>
    public int Write(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var bytes = Utf8NoBom.GetBytes(text);
        var temporary = Path + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, Path, overwrite: true);
            return bytes.Length;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(temporary);
            throw new IOException($"存档写不进 {Path}：{e.Message}", e);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // 删不掉就留着：它不是存档，下一次写会覆盖它。
        }
    }
}
