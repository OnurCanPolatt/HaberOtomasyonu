using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;

namespace HaberOtomasyon
{
    /// <summary>
    /// FFmpeg/FFprobe ile ilgili tekrar eden işlemler (süre ölçme, dosya birleştirme)
    /// tek yerde toplandı - TtsRunner, LipsyncRunner ve VideoComposer bunu ortak kullanır.
    /// </summary>
    public static class MediaUtils
    {
        public static double GetDurationSeconds(string filePath)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffprobe",
                Arguments = $"-v error -show_entries format=duration -of default=noprint_wrappers=1:nokey=1 \"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(startInfo);
            string output = p?.StandardOutput.ReadToEnd() ?? "";
            p?.WaitForExit();

            if (double.TryParse(output.Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out double duration))
                return duration;
            return 0;
        }

        /// <summary>Aynı türden (hepsi ses veya hepsi video) dosyaları sırayla uç uca ekler.</summary>
        public static void ConcatFiles(List<string> filePaths, string outputPath, bool reencodeAudio = false)
        {
            string listFilePath = Path.Combine(Path.GetTempPath(), $"concat_{Guid.NewGuid():N}.txt");
            var lines = filePaths.Select(f => $"file '{f.Replace("\\", "/")}'");
            File.WriteAllLines(listFilePath, lines);

            string codecArgs = reencodeAudio ? "-c:a flac" : "-c copy";

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -f concat -safe 0 -i \"{listFilePath}\" {codecArgs} \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(startInfo);
            p?.WaitForExit();

            if (File.Exists(listFilePath)) File.Delete(listFilePath);
        }
    }
}
