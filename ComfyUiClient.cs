using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace HaberOtomasyon
{
    /// <summary>
    /// ComfyUI'nin kendisiyle konuşan jenerik istemci. Hangi workflow olduğunu bilmez,
    /// sadece "dosya yükle", "prompt gönder", "sonucu bekle", "çıktıyı indir" yapar.
    /// TTS ve Lipsync scriptleri bu sınıfı ortak kullanır.
    /// </summary>
    public class ComfyUiClient
    {
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        private readonly string _serverUrl;

        public ComfyUiClient(string serverUrl, string openButtonToken)
        {
            _serverUrl = serverUrl;
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openButtonToken);
        }

        public async Task WaitUntilReadyAsync(int timeoutSeconds = 480)
        {
            var start = DateTime.UtcNow;
            int attempt = 0;
            while (true)
            {
                attempt++;
                if ((DateTime.UtcNow - start).TotalSeconds > timeoutSeconds)
                    throw new Exception("Zaman aşımı: ComfyUI cevap vermedi.");

                try
                {
                    var response = await _http.GetAsync($"{_serverUrl}/system_stats");
                    if (response.IsSuccessStatusCode)
                    {
                        Console.WriteLine("  [ComfyUI] Hazır.");
                        return;
                    }
                    Console.WriteLine($"  [ComfyUI] Deneme {attempt}: HTTP {(int)response.StatusCode}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [ComfyUI] Deneme {attempt}: {ex.GetType().Name}: {ex.Message}");
                }
                await Task.Delay(8_000);
            }
        }

        public async Task<string> UploadFileAsync(string filepath)
        {
            using var form = new MultipartFormDataContent();
            var bytes = await File.ReadAllBytesAsync(filepath);
            var fileContent = new ByteArrayContent(bytes);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(fileContent, "image", Path.GetFileName(filepath));

            var response = await _http.PostAsync($"{_serverUrl}/upload/image", form);
            response.EnsureSuccessStatusCode();

            var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
            string uploadedName = json["name"]!.ToString();
            Console.WriteLine($"  [ComfyUI] Yüklendi: {Path.GetFileName(filepath)} -> '{uploadedName}'");
            return uploadedName;
        }

        public async Task<string> QueuePromptAsync(JsonObject workflow)
        {
            var payload = new JsonObject { ["prompt"] = workflow };
            var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

            var response = await _http.PostAsync($"{_serverUrl}/prompt", content);
            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Prompt gönderilemedi: {response.StatusCode} - {body}");

            var json = JsonNode.Parse(body)!;
            if (json["error"] != null)
                throw new Exception($"ComfyUI hata döndü: {json["error"]!.ToJsonString()}");

            string promptId = json["prompt_id"]!.ToString();
            Console.WriteLine($"  [ComfyUI] Prompt ID: {promptId}");
            return promptId;
        }

        public async Task<JsonObject> WaitForResultAsync(string promptId, int timeoutSeconds = 1800, int pollSeconds = 4)
        {
            var start = DateTime.UtcNow;
            while (true)
            {
                if ((DateTime.UtcNow - start).TotalSeconds > timeoutSeconds)
                    throw new Exception("Zaman aşımı - işlem çok uzun sürdü.");

                var response = await _http.GetAsync($"{_serverUrl}/history/{promptId}");
                if (response.IsSuccessStatusCode)
                {
                    var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
                    if (json[promptId] != null)
                    {
                        var entry = json[promptId]!.AsObject();
                        var status = entry["status"]?.AsObject();
                        bool completed = status?["completed"]?.GetValue<bool>() ?? false;
                        string? statusStr = status?["status_str"]?.ToString();

                        if (completed)
                        {
                            Console.WriteLine("  [ComfyUI] Tamamlandı.");
                            return entry;
                        }
                        if (statusStr == "error")
                            throw new Exception($"İşlem hatası: {status!.ToJsonString()}");
                    }
                }
                await Task.Delay(pollSeconds * 1000);
            }
        }

        /// <summary>
        /// History çıktısındaki bir node'un ürettiği dosyayı diske indirir.
        /// outputKey: genelde "gifs" (video) veya "audio"/"files" (ses) - node'un ürettiği tipe göre değişir.
        /// </summary>
        public async Task DownloadNodeOutputAsync(JsonObject historyEntry, string nodeId, string outputPath, params string[] possibleKeys)
        {
            var outputs = historyEntry["outputs"]?.AsObject();
            var nodeOutput = outputs?[nodeId]?.AsObject();

            if (nodeOutput == null)
                throw new Exception($"Node {nodeId} çıktısı bulunamadı.");

            JsonArray? files = null;
            foreach (var key in possibleKeys)
            {
                files = nodeOutput[key]?.AsArray();
                if (files != null && files.Count > 0) break;
            }

            if (files == null || files.Count == 0)
                throw new Exception($"Node {nodeId} için dosya bulunamadı (aranan alanlar: {string.Join(", ", possibleKeys)}).");

            var fileInfo = files[0]!.AsObject();
            string filename = fileInfo["filename"]!.ToString();
            string subfolder = fileInfo["subfolder"]?.ToString() ?? "";
            string type = fileInfo["type"]?.ToString() ?? "output";

            Console.WriteLine($"  [ComfyUI] İndiriliyor: {filename}");
            string url = $"{_serverUrl}/view?filename={Uri.EscapeDataString(filename)}&subfolder={Uri.EscapeDataString(subfolder)}&type={type}";

            var bytes = await _http.GetByteArrayAsync(url);

            var outDir = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
                Directory.CreateDirectory(outDir);

            await File.WriteAllBytesAsync(outputPath, bytes);
            Console.WriteLine($"  [ComfyUI] Kaydedildi: {Path.GetFullPath(outputPath)}");
        }/// <summary>
        /// ComfyUI üzerindeki tüm yüklü modelleri VRAM'den tamamen temizler.
        /// TTS ve Lipsync modellerinin bellekte çakışmasını önlemek için kullanılır.
        /// </summary>
        public async Task FreeMemoryAsync()
        {
            Console.WriteLine("  [ComfyUI] VRAM belleği boşaltılıyor (Unload Models)...");
    
            var payload = new JsonObject 
            { 
                ["unload_models"] = true, 
                ["free_memory"] = true 
            };
    
            var content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");
    
            try
            {
                var response = await _http.PostAsync($"{_serverUrl}/free", content);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("  [ComfyUI] VRAM başarıyla temizlendi.");
                }
                else
                {
                    Console.WriteLine($"  [ComfyUI] VRAM temizleme uyarısı: HTTP {(int)response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  [ComfyUI] VRAM temizlenirken hata oluştu: {ex.Message}");
            }
        }
    }
    
}
