using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace HaberOtomasyon
{
    public class NewsSegmentResult
    {
        public string Title { get; set; } = "";
        public string RewrittenText { get; set; } = "";
        public List<string> ImageUrls { get; set; } = new();
        public double AudioDurationSeconds { get; set; }
    }

    public class VideoComposer
    {
        public async Task<string> ComposeReelsAsync(
            List<NewsSegmentResult> segments,
            string fullLipsyncVideoPath,
            string fullAudioPath,
            string finalOutputPath)
        {
            // Projenin çalıştığı dizinde "HaberGorselleri" adında kalıcı bir klasör açar
            string workDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HaberGorselleri");
            Directory.CreateDirectory(workDir);

            try
            {
                var seg = segments[0];
                Console.WriteLine(
                    $"[VideoComposer] Habere ait görseller hazırlanıyor (Toplam {seg.ImageUrls.Count} görsel): \"{seg.Title}\"");

                // Habere ait tüm görselleri indiriyoruz (limit sınırını genişlettik)
                var localImages = await DownloadMediaFilesAsync(seg.ImageUrls, workDir, prefix: "img_");
                string topVisualPath = Path.Combine(workDir, "top_visual_full.mp4");

                if (localImages.Count == 0)
                {
                    CreateBlankVideo(topVisualPath, 1080, 948, seg.AudioDurationSeconds);
                }
                else
                {
                    // Tüm görsellerin toplam süreye eşit oranda paylaştırılması
                    CreateTimedSlideshowFromImages(localImages, topVisualPath, seg.AudioDurationSeconds);
                }

                Console.WriteLine("[VideoComposer] Şık çerçeve boşlukları ve dikey birleştirme yapılıyor...");
                MergeVerticalReelsWithStyle(topVisualPath, fullLipsyncVideoPath, fullAudioPath, finalOutputPath);

                Console.WriteLine($"[VideoComposer] Final Reels hazır: {finalOutputPath}");
                return finalOutputPath;
            }
            finally
            {
            }
        }

        private async Task<List<string>> DownloadMediaFilesAsync(List<string> urls, string outputDir, string prefix)
        {
            using var client = new HttpClient();
            var localFiles = new List<string>();
            int index = 0;

            // Haberdeki tüm geçerli görselleri topluyoruz
            foreach (var url in urls)
            {
                try
                {
                    var uri = new Uri(url);
                    string ext = Path.GetExtension(uri.AbsolutePath);
                    if (string.IsNullOrEmpty(ext) || ext.Length > 5) ext = ".jpg";

                    string localPath = Path.Combine(outputDir, $"{prefix}{index}{ext}");
                    var bytes = await client.GetByteArrayAsync(url);
                    await File.WriteAllBytesAsync(localPath, bytes);
                    localFiles.Add(localPath);
                    index++;
                }
                catch
                {
                }
            }

            Console.WriteLine($"  [VideoComposer] Toplam {localFiles.Count} görsel indirildi ve klasöre kaydedildi.");
            return localFiles;
        }

        private void CreateBlankVideo(string outputPath, int width, int height, double duration)
        {
            duration = Math.Max(duration, 1.0);
            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -f lavfi -i color=c=black:s={width}x{height}:r=30 " +
                            $"-t {duration.ToString(System.Globalization.CultureInfo.InvariantCulture)} " +
                            $"-c:v libx264 -pix_fmt yuv420p \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(startInfo);
            p?.WaitForExit();
        }

        private void CreateTimedSlideshowFromImages(List<string> imagePaths, string outputPath, double totalDuration)
        {
            totalDuration = Math.Max(totalDuration, 1.0);
            string listFile = Path.Combine(Path.GetDirectoryName(outputPath)!, $"images_{Guid.NewGuid():N}.txt");

            // Süreyi indirilen görsel sayısına eşit olarak bölüyoruz (Örn: 60sn / 5 görsel = her biri 12sn kalır)
            double durationPerImage = totalDuration / imagePaths.Count;

            var lines = new List<string>();
            foreach (var img in imagePaths)
            {
                lines.Add($"file '{img.Replace("\\", "/")}'");
                lines.Add($"duration {durationPerImage.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            }

            // FFmpeg concat demuxer gereksinimi için son görseli tekrar ekliyoruz
            if (imagePaths.Count > 0)
            {
                lines.Add($"file '{imagePaths.Last().Replace("\\", "/")}'");
            }

            File.WriteAllLines(listFile, lines);

            int fpsForZoom = 30;
            int framesPerImage = (int)(durationPerImage * fpsForZoom);

            string vf = $"scale=1000:880:force_original_aspect_ratio=decrease," +
                        $"pad=1000:880:(ow-iw)/2:(oh-ih)/2:color=0x1a1a1a," +
                        $"pad=1080:948:(ow-iw)/2:(oh-ih)/2:color=0x111111," +
                        $"zoompan=z='min(zoom+0.0008,1.06)':d={framesPerImage}:s=1080x948:fps={fpsForZoom}";

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -f concat -safe 0 -i \"{listFile}\" " +
                            $"-vf \"{vf}\" " +
                            $"-c:v libx264 -pix_fmt yuv420p -t {totalDuration.ToString(System.Globalization.CultureInfo.InvariantCulture)} \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(startInfo);
            p?.WaitForExit();

            if (File.Exists(listFile)) File.Delete(listFile);
        }

        private void MergeVerticalReelsWithStyle(string topVideo, string bottomVideo, string audioPath,
            string outputPath)
        {
            string filterComplex = "[0:v]scale=1080:948,fps=30[v0];" +
                                   "[1:v]scale=1000:900:force_original_aspect_ratio=decrease," +
                                   "pad=1000:900:(ow-iw)/2:(oh-ih)/2:color=0x1a1a1a," +
                                   "pad=1080:960:(ow-iw)/2:(oh-ih)/2:color=0x111111,fps=30[v1];" +
                                   "color=c=black:s=1080x12:r=30[divider];" +
                                   "[v0][divider][v1]vstack=inputs=3[v]";

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -i \"{topVideo}\" -i \"{bottomVideo}\" -i \"{audioPath}\" " +
                            $"-filter_complex \"{filterComplex}\" " +
                            $"-map \"[v]\" -map 2:a -c:v libx264 -c:a aac -b:a 192k -shortest \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var p = Process.Start(startInfo);
            p?.WaitForExit();
        }
    }
}