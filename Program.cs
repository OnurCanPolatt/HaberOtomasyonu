using System;
using System.IO;
using System.Threading.Tasks;

namespace HaberOtomasyon
{
    class Program
    {
        static async Task<int> Main(string[] args)
        {
            // Haber metni, LLM tarafından üretilip diske kaydedilen dosyadan okunuyor.
            // İstersen --text ile geçici olarak override edebilirsin, ama normal akışta
            // bu dosya her seferinde LLM'in yeni çıktısıyla güncellenmiş olacak.
            string newsText = GetArg(args, "--text") ?? await ReadNewsTextAsync();

            var vast = new VastAiClient(Config.VastApiKey, Config.VastInstanceId, Config.ComfyUiInternalPort);

            try
            {
                Console.WriteLine("=== [1/6] GPU instance başlatılıyor ===");
                await vast.StartAsync();

                Console.WriteLine("=== [2/6] Instance'ın ayağa kalkması bekleniyor ===");
                string serverUrl = await vast.WaitUntilRunningAsync(Config.InstanceBootTimeoutSeconds);
                Console.WriteLine($"  Sunucu adresi: {serverUrl}");

                var comfy = new ComfyUiClient(serverUrl, Config.OpenButtonToken);

                Console.WriteLine("=== [3/6] ComfyUI'nin hazır olması bekleniyor ===");
                await comfy.WaitUntilReadyAsync(Config.ComfyUiReadyTimeoutSeconds);

                Console.WriteLine("=== [4/6] Ses üretiliyor (TTS) ===");
                var tts = new TtsRunner(comfy);
                string generatedAudioPath = await tts.GenerateAsync(
                    newsText,
                    Config.ReferenceVoicePath,
                    Config.GeneratedAudioOutputPath);
                Console.WriteLine($"  Üretilen ses: {generatedAudioPath}");
                
                //MEMORY TEMİZLEME
                await comfy.FreeMemoryAsync();

                Console.WriteLine("=== [5/6] Lipsync videosu üretiliyor ===");
                var lipsync = new LipsyncRunner(comfy);
                string tempAudioDir = Path.Combine(Path.GetDirectoryName(Config.GeneratedAudioOutputPath)!, "temp_tts");

                string finalVideoPath = await lipsync.GenerateBatchAsync(
                    Config.ReferenceImagePath,
                    tempAudioDir,            // Parçalanmış seslerin bulunduğu klasör
                    Config.FinalVideoOutputPath);

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
                try { await vast.StopAsync(); } catch { /* zaten kapalı olabilir */ }
                return 1;
            }
        }

        static string? GetArg(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }

        static async Task<string> ReadNewsTextAsync()
        {
            if (!File.Exists(Config.NewsTextFilePath))
                throw new Exception(
                    $"Haber metni dosyası bulunamadı: {Config.NewsTextFilePath}\n" +
                    $"LLM'in ürettiği metni bu dosyaya kaydetmen gerekiyor (düz .txt, UTF-8).");

            string text = await File.ReadAllTextAsync(Config.NewsTextFilePath);
            text = text.Trim();

            if (string.IsNullOrWhiteSpace(text))
                throw new Exception($"Haber metni dosyası boş: {Config.NewsTextFilePath}");

            Console.WriteLine($"  Haber metni okundu ({text.Length} karakter): \"{text.Substring(0, Math.Min(60, text.Length))}...\"");
            return text;
        }
    }
}
