using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace HaberOtomasyon
{
    public class TtsRunner
    {
        private readonly ComfyUiClient _comfy;

        public TtsRunner(ComfyUiClient comfy)
        {
            _comfy = comfy;
        }

        public async Task<string> GenerateAsync(string newsText, string referenceVoicePath, string outputPath)
        {
            if (!File.Exists(Config.TtsWorkflowPath))
                throw new Exception($"TTS workflow bulunamadı: {Config.TtsWorkflowPath}");

            Console.WriteLine("[TTS] Referans ses yükleniyor...");
            string uploadedVoiceName = await _comfy.UploadFileAsync(referenceVoicePath);

            // 1. Metni Chatterbox'ın çökmesini önleyecek küçük parçalara bölüyoruz
            var textChunks = SplitTextIntoChunks(newsText, maxChars: 200);
            Console.WriteLine($"[TTS] Haber metni {textChunks.Count} parçaya bölündü.");

            var generatedAudioFiles = new List<string>();
            string tempDirectory = Path.Combine(Path.GetDirectoryName(outputPath)!, "temp_tts");
            Directory.CreateDirectory(tempDirectory);

            // 2. Her parçayı sırayla ComfyUI'a gönderiyoruz
            for (int i = 0; i < textChunks.Count; i++)
            {
                Console.WriteLine($"[TTS] Parça {i + 1}/{textChunks.Count} işleniyor...");
                
                var workflow = JsonNode.Parse(await File.ReadAllTextAsync(Config.TtsWorkflowPath))!.AsObject();

                workflow[Config.TtsNodeIdAudioPrompt]!["inputs"]!["audio"] = uploadedVoiceName;
                workflow[Config.TtsNodeIdMultilingual]!["inputs"]!["text"] = textChunks[i];

                string promptId = await _comfy.QueuePromptAsync(workflow);
                var historyEntry = await _comfy.WaitForResultAsync(promptId, Config.JobTimeoutSeconds);

                string chunkAudioPath = Path.Combine(tempDirectory, $"part_{i}.flac");
                await _comfy.DownloadNodeOutputAsync(historyEntry, Config.TtsNodeIdPreviewAudio, chunkAudioPath, "audio", "files", "gifs");
                
                generatedAudioFiles.Add(chunkAudioPath);
            }

            // 3. Elde edilen tüm ses dosyalarını FFmpeg ile tek bir dosyada birleştiriyoruz
            Console.WriteLine("[TTS] Ses parçaları tek bir dosyada birleştiriliyor...");
            MergeAudioFiles(generatedAudioFiles, outputPath);
            return outputPath;
        }

        /// <summary>
        /// Uzun metni noktalama işaretlerinden bölerek güvenli boyutlarda parçalar oluşturur.
        /// </summary>
        private List<string> SplitTextIntoChunks(string text, int maxChars = 200)
        {
            var sentences = text.Split(new[] { '.', '!', '?' }, StringSplitOptions.RemoveEmptyEntries);
            var chunks = new List<string>();
            string currentChunk = "";

            foreach (var sentence in sentences)
            {
                string trimmed = sentence.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;

                if ((currentChunk + " " + trimmed).Length > maxChars && !string.IsNullOrEmpty(currentChunk))
                {
                    chunks.Add(currentChunk.Trim() + ".");
                    currentChunk = trimmed;
                }
                else
                {
                    currentChunk += (string.IsNullOrEmpty(currentChunk) ? "" : " ") + trimmed;
                }
            }

            if (!string.IsNullOrEmpty(currentChunk))
            {
                chunks.Add(currentChunk.Trim() + ".");
            }

            return chunks;
        }

        /// <summary>
        /// FFmpeg kullanarak parçalanmış ses dosyalarını uç uca ekler.
        /// </summary>
        private void MergeAudioFiles(List<string> filePaths, string outputPath)
        {
            string listFilePath = Path.Combine(Path.GetDirectoryName(outputPath)!, "files.txt");
    
            // FFmpeg concat listesi oluştur
            var lines = filePaths.Select(f => $"file '{f.Replace("\\", "/")}'");
            File.WriteAllLines(listFilePath, lines);

            var startInfo = new ProcessStartInfo
            {
                FileName = "ffmpeg",
                // NOT: -c copy yerine -c:a flac kullanarak tüm parçaları doğru zaman akışıyla birleştiriyoruz
                Arguments = $"-y -f concat -safe 0 -i \"{listFilePath}\" -c:a flac \"{outputPath}\"",
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