using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace HaberOtomasyon
{
    /// <summary>Bir TTS üretiminin sonucu: birleşik tam ses dosyası + cümle sınırlarını koruyan ayrı parçalar.</summary>
    public class TtsResult
    {
        public string MergedAudioPath { get; set; } = "";
        /// <summary>Her biri TAM bir cümleye karşılık gelen, sırayla üretilmiş ses dosyaları.
        /// Lipsync bu sınırları kullanarak asla cümle ortasından kesmez.</summary>
        public List<string> SentenceChunkPaths { get; set; } = new();
    }

    public class TtsRunner
    {
        private readonly ComfyUiClient _comfy;

        public TtsRunner(ComfyUiClient comfy)
        {
            _comfy = comfy;
        }

        public async Task<TtsResult> GenerateAsync(string newsText, string referenceVoicePath, string outputPath,
            string tempSubfolderName = "temp_tts")
        {
            if (!File.Exists(Config.TtsWorkflowPath))
                throw new Exception($"TTS workflow bulunamadı: {Config.TtsWorkflowPath}");

            Console.WriteLine("[TTS] Referans ses yükleniyor...");
            string uploadedVoiceName = await _comfy.UploadFileAsync(referenceVoicePath);

            var textChunks = SplitTextIntoChunks(newsText, maxChars: 200);
            Console.WriteLine($"[TTS] Metin {textChunks.Count} cümle/parçaya bölündü.");

            var generatedAudioFiles = new List<string>();
            string tempDirectory = Path.Combine(Path.GetDirectoryName(outputPath)!, tempSubfolderName);
            Directory.CreateDirectory(tempDirectory);

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

            Console.WriteLine("[TTS] Ses parçaları birleştiriliyor (tam bülten sesi için)...");
            MediaUtils.ConcatFiles(generatedAudioFiles, outputPath, reencodeAudio: true);

            // ÖNEMLİ: temp klasörü ARTIK SİLMİYORUZ - Lipsync bu cümle parçalarını
            // (sınırlarını koruyarak) kullanacak, loop'un cümle ortasında değil
            // sadece cümle bitiminde (noktada) yenilenmesi için bu şart.
            return new TtsResult
            {
                MergedAudioPath = outputPath,
                SentenceChunkPaths = generatedAudioFiles
            };
        }

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
                chunks.Add(currentChunk.Trim() + ".");

            return chunks;
        }
    }
}
