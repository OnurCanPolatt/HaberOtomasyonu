using System;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using haber_otomasyon;

namespace HaberOtomasyon
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            var vast = new VastAiClient(Config.VastApiKey, Config.VastInstanceId, Config.ComfyUiInternalPort);

            try
            {
                // =========================================================================
                // === [1/6] GPU INSTANCE BAŞLATILIYOR (Ollama ve ComfyUI için) ===
                // =========================================================================
                Console.WriteLine("=== [1/6] GPU instance başlatılıyor ===");
                await vast.StartAsync();

                Console.WriteLine("=== [2/6] Instance'ın ayağa kalkması bekleniyor ===");
                string serverUrl = await vast.WaitUntilRunningAsync(Config.InstanceBootTimeoutSeconds);
                Console.WriteLine($"  Sunucu adresi (ComfyUI): {serverUrl}");

                // Vast.ai API'sinden 8288 (Ollama) için atanan GERÇEK dış portlu adresi çekiyoruz
                string ollamaUrl = await vast.GetServiceUrlAsync(Config.OllamaInternalPort);
                Console.WriteLine($"  Ollama adresi (Dinamik): {ollamaUrl}");


                // =========================================================================
                // === [3/6] RSS HABERİ ÇEKİLİR VE OLLAMA (LLM) İLE DÜZENLENİR ===
                // =========================================================================
                string newsText;
                string? overrideText = GetArg(args, "--text");

                if (!string.IsNullOrEmpty(overrideText))
                {
                    Console.WriteLine("\n=== Parametre ile verilen özel haber metni kullanılıyor ===");
                    newsText = overrideText;
                }
                else
                {
                    Console.WriteLine("\n=== RSS Otomasyonu & LLM Editör Çalıştırılıyor ===");

                    var scraper = new NewsScraper();
                    var llmEditor = new NewsEditorLlm(ollamaUrl, Config.OllamaModelName, Config.OpenButtonToken);

                    // --- OLLAMA SAĞLIK KONTROLÜ (RETRY / POLLING) ---
                    Console.WriteLine("  [LLM] Ollama servisinin hazır olması bekleniyor...");
                    await llmEditor.WaitUntilReadyAsync(timeoutSeconds: 120);

                    Console.WriteLine("  1. RSS akışından haberler çekiliyor...");
                    var headlines = await scraper.GetHeadlinesAsync(Config.NewsRssUrl);

                    if (headlines.Count == 0)
                        throw new Exception("RSS akışından hiç haber çekilemedi!");

                    var selectedNews = headlines[0];
                    
                    // HTML encoding temizliği (örnek: &#x27; -> ')
                    string cleanTitle = WebUtility.HtmlDecode(selectedNews.Title);
                    Console.WriteLine($"  2. Seçilen Haber: {cleanTitle}");

                    Console.WriteLine("  3. Haber detay metni kazınıyor (Scraping)...");
                    var article = await scraper.ScrapeArticleAsync(selectedNews.Link);

                    Console.WriteLine("  4. Ollama ile spiker metni üretiliyor...");
                    newsText = await llmEditor.RewriteNewsForTtsAsync(article.FullText);

                    // Üretilen metni kaydediyoruz
                    await File.WriteAllTextAsync(Config.NewsTextFilePath, newsText);
                    Console.WriteLine($"  5. Üretilen spiker metni kaydedildi: {Config.NewsTextFilePath}");
                }

                Console.WriteLine($"\nKullanılacak Metin ({newsText.Length} Karakter):\n\"{newsText}\"\n");


                // =========================================================================
                // === [4/6] COMFYUI İŞLEMLERİ (TTS & LIPSYNC) ===
                // =========================================================================
                var comfy = new ComfyUiClient(serverUrl, Config.OpenButtonToken);

                Console.WriteLine("=== ComfyUI'nin hazır olması bekleniyor ===");
                await comfy.WaitUntilReadyAsync(Config.ComfyUiReadyTimeoutSeconds);

                Console.WriteLine("=== Ses üretiliyor (TTS) ===");
                var tts = new TtsRunner(comfy);
                string generatedAudioPath = await tts.GenerateAsync(
                    newsText,
                    Config.ReferenceVoicePath,
                    Config.GeneratedAudioOutputPath);
                Console.WriteLine($"  Üretilen ses: {generatedAudioPath}");

                // MEMORY TEMİZLEME (TTS modellerini VRAM'den boşaltır)
                await comfy.FreeMemoryAsync();

                // =========================================================================
                // === [5/6] LIPSYNC VİDEOSU ÜRETİLİYOR ===
                // =========================================================================
                Console.WriteLine("=== [5/6] Lipsync videosu üretiliyor ===");
                var lipsync = new LipsyncRunner(comfy);
                string tempAudioDir = Path.Combine(Path.GetDirectoryName(Config.GeneratedAudioOutputPath)!, "temp_tts");

                string finalVideoPath = await lipsync.GenerateBatchAsync(
                    Config.ReferenceImagePath,
                    tempAudioDir,
                    Config.FinalVideoOutputPath);

                // =========================================================================
                // === [6/6] GPU INSTANCE DURDURULUYOR ===
                // =========================================================================
                Console.WriteLine("=== [6/6] GPU instance durduruluyor ===");
                await vast.StopAsync();

                Console.WriteLine($"\nBitti! ✅  Final video: {finalVideoPath}");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nHATA: {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine($"  İç hata: {ex.InnerException.Message}");

                Console.WriteLine("Güvenlik için GPU'yu yine de durdurmayı deniyorum...");
                try
                {
                    await vast.StopAsync();
                }
                catch { /* zaten kapalı olabilir */ }

                return 1;
            }
        }

        static string? GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}