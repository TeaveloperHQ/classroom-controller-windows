using System;
using System.Collections.Generic;
using System.IO;

namespace ClassroomController.Services;

/// <summary>
/// 에이전트에서 받아온 BLOCK_LOG 엔트리를 %AppData%/ClassroomController/block_log.txt 에 append.
/// S Pen 지우개 오탐 등 튜닝을 위한 로컬 수집용.
/// </summary>
public static class BlockLogService
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ClassroomController");

    public static string FilePath { get; } = Path.Combine(Dir, "block_log.txt");

    private static readonly object WriteLock = new();

    public static void Append(string deviceName, IReadOnlyList<string> entries)
    {
        if (entries.Count == 0) return;
        try
        {
            Directory.CreateDirectory(Dir);
            var day = DateTime.Now.ToString("yyyy-MM-dd");
            lock (WriteLock)
            {
                using var sw = File.AppendText(FilePath);
                foreach (var entry in entries)
                    sw.WriteLine($"{day} {deviceName}\t{entry}");
            }
        }
        catch
        {
            // 쓰기 실패는 조용히 무시.
        }
    }
}
