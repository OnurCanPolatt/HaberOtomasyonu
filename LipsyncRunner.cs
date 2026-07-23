using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace HaberOtomasyon
{
    public class LipsyncRunner
    {
        private readonly ComfyUiClient _comfy;

        // FLOAT modeli çok uzun sesleri (40sn+) tek seferde işlerken VRAM (OOM) hatası veriyordu.
        // Bu yüzden gruplar bu süreyi geçmeyecek şekilde oluşturuluyor - AMA HER ZAMAN
        // tam cümle sınırlarında (asla cümle ortasında) bölünerek.
        private const double MaxGroupSeconds = 15.0;

        public LipsyncRunner(ComfyUiClient comfy)
        {
            _comfy = comfy;
        }

        /// <summary>
        /// TTS'in ürettiği cümle-cümle ses parçalarını alır. Art arda gelen cümleleri
        /// MaxGroupSeconds'ı aşmayacak şekilde gruplar (ama HİÇBİR ZAMAN bir cümleyi ortadan bölmez),
        /// her grup için ayrı lipsync videosu üretir, sonunda sırayla birleştirir.
        /// Böylece FLOAT'ın hareket döngüsü/loop'u SADECE cümle bitişlerinde (noktalarda) yenilenir,
        /// kelime ortasında asla sıçrama olmaz.
        /// </summary>
        public async Task<string> GenerateFromSentenceChunksAsync(
            string imagePath, List<string> orderedSentenceAudioChunks, string outputVideoPath,
            string tempSubfolderName = "temp_lipsync")
        {
            if (!File.Exists(Config.LipsyncWorkflowPath))
                throw new Exception($"Lipsync workflow bulunamadı: {Config.LipsyncWorkflowPath}");

            if (orderedSentenceAudioChunks.Count == 0)
                throw new Exception("Lipsync için hiç ses parçası verilmedi.");

            string workDir = Path.Combine(Path.GetDirectoryName(outputVideoPath)!, tempSubfolderName);
            Directory.CreateDirectory(workDir);

            var groupedAudioFiles = GroupChunksByDuration(orderedSentenceAudioChunks, workDir);
            Console.WriteLine($"[Lipsync] {orderedSentenceAudioChunks.Count} cümle, {groupedAudioFiles.Count} gruba " +
                               "toplandı (her grup TAM cümle sınırında bitiyor).");

            Console.WriteLine("[Lipsync] Görsel yükleniyor...");
            string imageName = await _comfy.UploadFileAsync(imagePath);

            var generatedVideoFiles = new List<string>();

            for (int i = 0; i < groupedAudioFiles.Count; i++)
            {
                Console.WriteLine($"[Lipsync] Grup {i + 1}/{groupedAudioFiles.Count} videosu üretiliyor...");

                string audioName = await _comfy.UploadFileAsync(groupedAudioFiles[i]);
                var workflow = JsonNode.Parse(await File.ReadAllTextAsync(Config.LipsyncWorkflowPath))!.AsObject();

                workflow[Config.LipsyncNodeIdImage]!["inputs"]!["image"] = imageName;
                workflow[Config.LipsyncNodeIdAudio]!["inputs"]!["audio"] = audioName;

                string promptId = await _comfy.QueuePromptAsync(workflow);
                var historyEntry = await _comfy.WaitForResultAsync(promptId, Config.JobTimeoutSeconds);

                string chunkVideoPath = Path.Combine(workDir, $"vpart_{i}.mp4");
                await _comfy.DownloadNodeOutputAsync(historyEntry, Config.LipsyncNodeIdVideoCombine, chunkVideoPath, "gifs", "videos");

                generatedVideoFiles.Add(chunkVideoPath);

                await _comfy.FreeMemoryAsync();
            }

            if (generatedVideoFiles.Count == 1)
                File.Copy(generatedVideoFiles[0], outputVideoPath, overwrite: true);
            else
                MediaUtils.ConcatFiles(generatedVideoFiles, outputVideoPath);

            try { Directory.Delete(workDir, true); } catch { }

            return outputVideoPath;
        }

        /// <summary>
        /// Art arda gelen cümle ses dosyalarını, toplam süre MaxGroupSeconds'ı aşana kadar
        /// aynı gruba ekler (concat ile birleştirir). Bir cümle asla ikiye bölünmez -
        /// bir sonraki cümle grubu aşacaksa, o cümle YENİ bir grupla başlar.
        /// </summary>
        private List<string> GroupChunksByDuration(List<string> chunkPaths, string workDir)
        {
            var groups = new List<string>();
            var currentGroupFiles = new List<string>();
            double currentGroupDuration = 0;
            int groupIndex = 0;

            void FlushGroup()
            {
                if (currentGroupFiles.Count == 0) return;

                string groupPath = Path.Combine(workDir, $"group_{groupIndex}.flac");
                if (currentGroupFiles.Count == 1)
                    File.Copy(currentGroupFiles[0], groupPath, overwrite: true);
                else
                    MediaUtils.ConcatFiles(currentGroupFiles, groupPath, reencodeAudio: true);

                groups.Add(groupPath);
                groupIndex++;
                currentGroupFiles.Clear();
                currentGroupDuration = 0;
            }

            foreach (var chunkPath in chunkPaths)
            {
                double chunkDuration = MediaUtils.GetDurationSeconds(chunkPath);

                if (currentGroupDuration + chunkDuration > MaxGroupSeconds && currentGroupFiles.Count > 0)
                    FlushGroup();

                currentGroupFiles.Add(chunkPath);
                currentGroupDuration += chunkDuration;
            }
            FlushGroup();

            return groups;
        }
    }
}
