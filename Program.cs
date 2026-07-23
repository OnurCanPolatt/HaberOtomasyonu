using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;

namespace HaberOtomasyon
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var vast = new VastAiClient(Config.VastApiKey, Config.VastInstanceId, Config.ComfyUiInternalPort);

            try
            {
                Console.WriteLine("=== [1/6] GPU instance başlatılıyor ===");
                await vast.StartAsync();

                Console.WriteLine("=== [2/6] Instance'ın ayağa kalkması bekleniyor ===");
                string serverUrl = await vast.WaitUntilRunningAsync(Config.InstanceBootTimeoutSeconds);
                Console.WriteLine($"  Sunucu adresi (ComfyUI): {serverUrl}");

                string ollamaUrl = await vast.GetServiceUrlAsync(Config.OllamaInternalPort);
                Console.WriteLine($"  Ollama adresi: {ollamaUrl}");

                var comfy = new ComfyUiClient(serverUrl, Config.OpenButtonToken);
                var tts = new TtsRunner(comfy);
                var lipsync = new LipsyncRunner(comfy);

                Console.WriteLine("=== [3/6] RSS akışından en güncel ve ilgi çekici haber seçiliyor ve işleniyor ===");

                string outputDir = Path.GetDirectoryName(Config.FinalVideoOutputPath)!;
                Directory.CreateDirectory(outputDir);

                var segments = new List<NewsSegmentResult>();
                var perSegmentAudioPaths = new List<string>();
                var perSegmentVideoPaths = new List<string>();

                string? overrideText = GetArg(args, "--text");

                if (!string.IsNullOrEmpty(overrideText))
                {
                    Console.WriteLine("  Parametre ile verilen özel metin kullanılıyor...");
                    await ProcessOneNewsSegmentAsync(
                        title: "Özel Metin",
                        rewrittenText: overrideText,
                        imageUrls: new List<string>(),
                        index: 0,
                        tts, lipsync, segments, perSegmentAudioPaths, perSegmentVideoPaths, outputDir);
                }
                else
                {
                    var scraper = new NewsScraper();
                    var llmEditor = new NewsEditorLlm(ollamaUrl, Config.OllamaModelName, Config.OpenButtonToken);

                    Console.WriteLine("  [LLM] Ollama servisinin hazır olması bekleniyor...");
                    await llmEditor.WaitUntilReadyAsync(timeoutSeconds: 120);

                    Console.WriteLine("  RSS akışından başlıklar taranıyor...");
                    var headlines = await scraper.GetHeadlinesAsync(Config.NewsRssUrl);

                    if (headlines.Count == 0)
                        throw new Exception("RSS akışından hiç haber çekilemedi!");

                    var newsItem = headlines[0];
                    string cleanTitle = WebUtility.HtmlDecode(newsItem.Title);
                    Console.WriteLine($"\n--- Seçilen En Güncel Haber: {cleanTitle} ---");

                    var article = await scraper.ScrapeArticleAsync(newsItem.Link);

                    if (article.ImageUrls.Count == 0)
                    {
                        Console.WriteLine("  [Uyarı] Haberde hiç görsel bulunamadı, varsayılan görsel kullanılacak.");
                    }
                    else
                    {
                        Console.WriteLine($"  [Bilgi] Habere ait {article.ImageUrls.Count} adet görsel toplandı, slayt için hazırlanacak.");
                    }

                    string rewrittenText = await llmEditor.RewriteSingleArticleAsync(
                        cleanTitle,
                        article.FullText);

                    Console.WriteLine($"  [LLM] Düzenlenmiş Metin:\n\"{rewrittenText}\"\n");

                    await ProcessOneNewsSegmentAsync(
                        cleanTitle, rewrittenText, article.ImageUrls, 0,
                        tts, lipsync, segments, perSegmentAudioPaths, perSegmentVideoPaths, outputDir);
                }

                if (segments.Count == 0)
                    throw new Exception("Haber işlenemedi!");

                var textLog = new System.Text.StringBuilder();
                foreach (var seg in segments)
                {
                    textLog.AppendLine($"=== {seg.Title} ===");
                    textLog.AppendLine(seg.RewrittenText);
                }
                await File.WriteAllTextAsync(Config.NewsTextFilePath, textLog.ToString());
                Console.WriteLine($"  Metin kaydedildi: {Config.NewsTextFilePath}");

                Console.WriteLine("\n=== [4/6] Ses dosyası hazırlanıyor ===");
                string fullAudioPath = Path.Combine(outputDir, "full_bulletin_audio.flac");
                MediaUtils.ConcatFiles(perSegmentAudioPaths, fullAudioPath, reencodeAudio: true);

                Console.WriteLine("=== [5/6] Lipsync videosu hazırlanıyor ===");
                string fullLipsyncVideoPath = Path.Combine(outputDir, "full_bulletin_lipsync.mp4");
                File.Copy(perSegmentVideoPaths[0], fullLipsyncVideoPath, overwrite: true);

                Console.WriteLine("=== [6/6] Dikey (9:16) Reels videosu oluşturuluyor (Görseller slayt olarak ekleniyor) ===");
                var videoComposer = new VideoComposer();
                string finalVideoPath = await videoComposer.ComposeReelsAsync(
                    segments,
                    fullLipsyncVideoPath,
                    fullAudioPath,
                    Config.FinalVideoOutputPath);

                Console.WriteLine("=== GPU instance durduruluyor ===");
                await vast.StopAsync();

                Console.WriteLine($"\nBitti! ✅  Final dikey reels video: {finalVideoPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nHATA: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"  İç hata: {ex.InnerException.Message}");

                Console.WriteLine("Güvenlik için GPU'yu durduruyorum...");
                try { await vast.StopAsync(); } catch { }

                return 1;
            }
        }

        private static async Task ProcessOneNewsSegmentAsync(
            string title, string rewrittenText, List<string> imageUrls, int index,
            TtsRunner tts, LipsyncRunner lipsync,
            List<NewsSegmentResult> segments, List<string> audioPaths, List<string> videoPaths,
            string outputDir)
        {
            Console.WriteLine($"  Ses üretiliyor (TTS)...");
            string segmentAudioPath = Path.Combine(outputDir, $"segment_{index}_audio.flac");
            var ttsResult = await tts.GenerateAsync(rewrittenText, Config.ReferenceVoicePath, segmentAudioPath,
                tempSubfolderName: $"temp_tts_{index}");

            Console.WriteLine($"  Lipsync videosu üretiliyor...");
            string segmentVideoPath = Path.Combine(outputDir, $"segment_{index}_video.mp4");
            await lipsync.GenerateFromSentenceChunksAsync(
                Config.ReferenceImagePath, ttsResult.SentenceChunkPaths, segmentVideoPath,
                tempSubfolderName: $"temp_lipsync_{index}");

            double actualVideoDuration = MediaUtils.GetDurationSeconds(segmentVideoPath);
            Console.WriteLine($"  Video süresi: {actualVideoDuration:F1} saniye");

            try
            {
                var tempDir = Path.Combine(outputDir, $"temp_tts_{index}");
                if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
            }
            catch { }

            segments.Add(new NewsSegmentResult
            {
                Title = title,
                RewrittenText = rewrittenText,
                ImageUrls = imageUrls,
                AudioDurationSeconds = actualVideoDuration
            });
            audioPaths.Add(segmentAudioPath);
            videoPaths.Add(segmentVideoPath);
        }

        static string? GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}