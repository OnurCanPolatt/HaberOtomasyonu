using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace HaberOtomasyon
{
    /// <summary>
    /// Vast.ai instance başlatma/durdurma/hazır olma bekleme mantığı.
    /// TTS ve Lipsync scriptleri bu sınıfı ortak kullanır.
    /// </summary>
    public class VastAiClient
    {
        private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        private readonly string _instanceId;
        private readonly int _internalPort;

        public VastAiClient(string apiKey, string instanceId, int internalPort)
        {
            _instanceId = instanceId;
            _internalPort = internalPort;
            _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        public async Task StartAsync()
        {
            var body = new JsonObject { ["state"] = "running" };
            var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            var response = await _http.PutAsync($"https://console.vast.ai/api/v0/instances/{_instanceId}/", content);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Instance başlatılamadı: {response.StatusCode} - {text}");
            Console.WriteLine("  [VastAi] Başlatma isteği gönderildi.");
        }

        public async Task StopAsync()
        {
            var body = new JsonObject { ["state"] = "stopped" };
            var content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
            var response = await _http.PutAsync($"https://console.vast.ai/api/v0/instances/{_instanceId}/", content);
            var text = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
                Console.WriteLine($"  [VastAi] Uyarı: Durdurma isteği başarısız olabilir: {response.StatusCode} - {text}");
            else
                Console.WriteLine("  [VastAi] Durdurma isteği gönderildi.");
        }

        /// <summary>Instance "running" olana kadar bekler, ComfyUI'nin dış IP:port adresini döner.</summary>
        public async Task<string> WaitUntilRunningAsync(int timeoutSeconds = 300)
        {
            var start = DateTime.UtcNow;
            while (true)
            {
                if ((DateTime.UtcNow - start).TotalSeconds > timeoutSeconds)
                    throw new Exception("Zaman aşımı: instance 'running' durumuna geçmedi.");

                var response = await _http.GetAsync($"https://console.vast.ai/api/v0/instances/{_instanceId}/");
                if (response.IsSuccessStatusCode)
                {
                    var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
                    var instance = json["instances"] ?? json["instance"] ?? json;
                    string? actualStatus = instance?["actual_status"]?.ToString();
                    string? publicIp = instance?["public_ipaddr"]?.ToString();

                    if (actualStatus == "running" && !string.IsNullOrEmpty(publicIp))
                    {
                        int mappedPort = ExtractMappedPort(instance!, _internalPort);
                        if (mappedPort > 0)
                            return $"http://{publicIp}:{mappedPort}";

                        Console.WriteLine("  [VastAi] Port tespit edilemedi. Ham veri: " + instance!.ToJsonString());
                        throw new Exception("Port bilgisi bulunamadı.");
                    }
                }
                Console.WriteLine("  [VastAi] Bekleniyor (instance henüz hazır değil)...");
                await Task.Delay(10_000);
            }
        }

        private static int ExtractMappedPort(JsonNode instance, int internalPort)
        {
            var ports = instance["ports"]?.AsObject();
            if (ports == null) return -1;

            string key = $"{internalPort}/tcp";
            if (ports.ContainsKey(key))
            {
                var arr = ports[key]!.AsArray();
                if (arr.Count > 0)
                {
                    var hostPort = arr[0]!["HostPort"]?.ToString();
                    if (int.TryParse(hostPort, out int p)) return p;
                }
            }
            return -1;
        }
    }
}
