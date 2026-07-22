using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace haber_otomasyon;

public class NewsEditorLlm
{
    private readonly HttpClient _httpClient;
    private readonly string _ollamaBaseUrl;
    private readonly string _modelName;

    public NewsEditorLlm(string ollamaBaseUrl, string modelName = "qwen2.5:7b", string? openButtonToken = null)
    {
        _httpClient = new HttpClient();
        _httpClient.Timeout = TimeSpan.FromMinutes(3);
        _ollamaBaseUrl = ollamaBaseUrl.TrimEnd('/');
        _modelName = modelName;

        if (!string.IsNullOrEmpty(openButtonToken))
            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", openButtonToken);
    }

    /// <summary>
    /// Ollama servisinin portu açıp cevap verip vermediğini döngüsel olarak kontrol eder.
    /// </summary>
    public async Task WaitUntilReadyAsync(int timeoutSeconds = 120)
    {
        var start = DateTime.UtcNow;
        int attempt = 0;
        string healthEndpoint = $"{_ollamaBaseUrl}/api/tags";

        while (true)
        {
            attempt++;
            if ((DateTime.UtcNow - start).TotalSeconds > timeoutSeconds)
                throw new Exception($"[LLM] Zaman aşımı: Ollama servisi {timeoutSeconds} saniye içinde yanıt vermedi.");

            try
            {
                var response = await _httpClient.GetAsync(healthEndpoint);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine("  [LLM] Ollama servisi hazır ve yanıt veriyor! ✅");
                    return;
                }
                Console.WriteLine($"  [LLM] Deneme {attempt}: HTTP {(int)response.StatusCode} - Bekleniyor...");
            }
            catch
            {
                Console.WriteLine($"  [LLM] Deneme {attempt}: Sunucu henüz ulaşılamaz durumda, bekleniyor...");
            }

            // Her 4 saniyede bir tekrar dene
            await Task.Delay(4000);
        }
    }

    public async Task<string> RewriteNewsForTtsAsync(string rawNewsText)
    {
        var endpoint = $"{_ollamaBaseUrl}/api/generate";

        string systemPrompt = @"Sen profesyonel bir haber spikerisin ve metin yazarısın.
Sana verilecek haber metnini, bir yapay zeka seslendiricisinin (TTS) en doğru Türkçe telaffuzla okuyabilmesi için yeniden düzenleyeceksin.

KURALLAR:
1. SADECE spikerin okuyacağı nihai konuşma metnini döndür. Ekstra açıklama, 'İşte metin:' gibi giriş cümleleri, tırnak işaretleri, başlıklar veya parantez içi spiker notları (örn: [Görsel girer]) KESİNLİKLE ekleme.
2. YABANCI İSİMLER VE KAVRAMLAR: Tüm yabancı kişi, kurum, şehir ve marka isimlerini Türkçe okunuşlarına (fonetiklerine) dönüştür. 
   - Örnek: 'Donald Trump' -> 'Danıld Tramp'
   - Örnek: 'Joe Biden' -> 'Co Baydın'
   - Örnek: 'Washington' -> 'Vaşington'
   - Örnek: 'New York' -> 'Nü York'
3. SAYILAR VE KISALTMALAR: Metindeki TÜM sayıları, yılları, sıralamaları ve kısaltmaları istisnasız harflerle/yazıyla yaz.
   - Örnek: '4.' -> 'Dördüncü'
   - Örnek: '1995' -> 'Bin dokuz yüz doksan beş'
   - Örnek: '%50' -> 'Yüzde elli'
   - Örnek: 'ABD' -> 'A B D' veya 'Amerika Birleşik Devletleri'
4. NOKTALAMA VE VURGU: Seslendirmenin doğal duraklamalar yapabilmesi için virgül ve nokta kullanımına çok dikkat et. Karmaşık ve aşırı uzun cümleleri ikiye böl.
5. TEMİZLİK: Haber kalıntılarını ('BBC Türkçe'yi takip edin', 'Resim kaynağı', 'Detaylar için tıklayın', haber saati vb.) tamamen temizle.
6. UZUNLUK: Metni yaklaşık 30-45 saniyelik bir seslendirme akışına uygun tut (ortalama 80-120 kelime).";

        string userPrompt = $"Haber Metni:\n{rawNewsText}";

        var requestBody = new
        {
            model = _modelName,
            prompt = $"{systemPrompt}\n\n{userPrompt}",
            stream = false
        };

        string jsonPayload = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

        Console.WriteLine("  [LLM] Haber metni Ollama (LLM) ile spiker diline dönüştürülüyor...");

        try
        {
            var response = await _httpClient.PostAsync(endpoint, content);

            if (!response.IsSuccessStatusCode)
            {
                string errBody = await response.Content.ReadAsStringAsync();
                throw new Exception($"Ollama isteği başarısız: HTTP {(int)response.StatusCode} - {errBody}");
            }

            string jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);

            if (doc.RootElement.TryGetProperty("response", out var responseProp))
            {
                string editedText = responseProp.GetString()?.Trim() ?? string.Empty;
                return editedText;
            }

            throw new Exception("Ollama yanıtında 'response' alanı bulunamadı.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  [LLM] Ollama İsteği Başarısız: {ex.Message}");
            throw;
        }
    }
}