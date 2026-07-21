using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace HaberOtomasyon
{
    public class LipsyncRunner
    {
        private readonly ComfyUiClient _comfy;

        public LipsyncRunner(ComfyUiClient comfy)
        {
            _comfy = comfy;
        }

        public async Task<string> GenerateBatchAsync(string imagePath, string tempAudioDir, string finalOutputPath)
        {
            if (!File.Exists(Config.LipsyncWorkflowPath))
                throw new Exception($"Lipsync workflow bulunamadı: {Config.LipsyncWorkflowPath}");

            var audioFiles = Directory.GetFiles(tempAudioDir, "part_*.flac")
                                      .OrderBy(f => int.Parse(Path.GetFileNameWithoutExtension(f).Replace("part_", "")))
                                      .ToList();

            if (audioFiles.Count == 0)
                throw new Exception("İşlenecek ses parçası bulunamadı.");

            Console.WriteLine($"[Lipsync] Görsel yükleniyor...");
            string imageName = await _comfy.UploadFileAsync(imagePath);

            string tempVideoDir = Path.Combine(Path.GetDirectoryName(finalOutputPath)!, "temp_video");
            Directory.CreateDirectory(tempVideoDir);
            var generatedVideoFiles = new List<string>();

            for (int i = 0; i < audioFiles.Count; i++)
            {
                Console.WriteLine($"[Lipsync] Parça {i + 1}/{audioFiles.Count} videosu üretiliyor...");
                
                string audioName = await _comfy.UploadFileAsync(audioFiles[i]);
                var workflow = JsonNode.Parse(await File.ReadAllTextAsync(Config.LipsyncWorkflowPath))!.AsObject();

                workflow[Config.LipsyncNodeIdImage]!["inputs"]!["image"] = imageName;
                workflow[Config.LipsyncNodeIdAudio]!["inputs"]!["audio"] = audioName;

                string promptId = await _comfy.QueuePromptAsync(workflow);
                var historyEntry = await _comfy.WaitForResultAsync(promptId, Config.JobTimeoutSeconds);

                string chunkVideoPath = Path.Combine(tempVideoDir, $"vpart_{i}.mp4");
                await _comfy.DownloadNodeOutputAsync(historyEntry, Config.LipsyncNodeIdVideoCombine, chunkVideoPath, "gifs", "videos");
                
                generatedVideoFiles.Add(chunkVideoPath);

                // Her video parçasından sonra VRAM'i temizliyoruz
                await _comfy.FreeMemoryAsync();
            }

            Console.WriteLine("[Lipsync] Tüm video parçaları birleştiriliyor...");
            MergeVideoFiles(generatedVideoFiles, finalOutputPath);

            // Temizlik
            try 
            { 
                Directory.Delete(tempVideoDir, true); 
                Directory.Delete(tempAudioDir, true);
            } catch { }

            return finalOutputPath;
        }

        private void MergeVideoFiles(List<string> filePaths, string outputPath)
        {
            string listFilePath = Path.Combine(Path.GetDirectoryName(outputPath)!, "videos.txt");
            var lines = filePaths.Select(f => $"file '{f.Replace("\\", "/")}'");
            File.WriteAllLines(listFilePath, lines);

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                Arguments = $"-y -f concat -safe 0 -i \"{listFilePath}\" -c copy \"{outputPath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using (var process = Process.Start(startInfo))
            {
                process?.WaitForExit();
            }

            if (File.Exists(listFilePath)) File.Delete(listFilePath);
        }
    }
}