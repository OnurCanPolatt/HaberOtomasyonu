using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace HaberOtomasyon
{
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

        public async Task WaitUntilReadyAsync(int timeoutSeconds = 120)
        {
            var endpoint = $"{_ollamaBaseUrl}/api/tags";
            var startTime = DateTime.UtcNow;

            while ((DateTime.UtcNow - startTime).TotalSeconds < timeoutSeconds)
            {
                try
                {
                    var response = await _httpClient.GetAsync(endpoint);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch
                {
                    // Servis henüz ayağa kalkmadı, denemeye devam ediyor
                }

                await Task.Delay(3000);
            }

            throw new TimeoutException($"Ollama servisi {timeoutSeconds} saniye içinde hazır hale gelmedi.");
        }

        public async Task<string> RewriteSingleArticleAsync(string title, string rawArticleText, bool isFirstInBulletin, bool isLastInBulletin)
        {
            var endpoint = $"{_ollamaBaseUrl}/api/generate";

            string systemPrompt = @"Sen profesyonel, diksiyonu kusursuz bir haber spikerisin. Sana verilen ham haber metnini ve başlığını sosyal medya video formatı (Reels) için yeniden yazacaksın.

KESİN KURALLAR VE FORMATLAR:
1. DOĞRUDAN GİRİŞ: 'Bugün sizlere...', 'İşte haberler...', 'Bu haberi anlatayım...' veya alakasız herhangi bir konuyla (örneğin voleybol vb.) KESİNLİKLE BAŞLAMA. Doğrudan haberin ana öznesi ve başlığıyla (örneğin 'ABD ile Suudi Arabistan arasında imzalanan nükleer anlaşma...') konuya gir.
2. SAYILARI YAZIYLA YAZ: Metin içinde geçen tüm sayıları, skorları ve tarihleri rakamla değil, seslendirme motorunun (TTS) doğru okuyabilmesi için mutlaka **yazıyla** ifade et (Örn: '26 Temmuz' yerine 'yirmi altı Temmuz', '3-1' yerine 'üç bir', '2026' yerine 'iki bin yirmi altı').
3. YABANCI İSİMLERİ FONETİK YAZ: Yabancı isim ve terimleri Türkçe okunuşlarına göre uyarla.
4. SÜRE VE UZUNLUK: Metin tam olarak 1 dakika sürecek uzunlukta (yaklaşık 140-160 kelime arası) olmalıdır. Detayları boğmadan, akıcı bir hikaye akışıyla aktar.
5. ÇÖP METİNLERİ TEMİZLE: 'WhatsApp'tan takip edin', 'Resim kaynağı', 'Okuma süresi' gibi yönlendirmeleri tamamen ayıkla. Sadece haber metnini döndür.";

            string userPrompt = $"Haber Başlığı: {title}\n\nHaber Metni:\n{rawArticleText}";

            var requestBody = new
            {
                model = _modelName,
                prompt = $"{systemPrompt}\n\n{userPrompt}",
                stream = false
            };

            string jsonPayload = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(endpoint, content);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Ollama isteği başarısız: {(int)response.StatusCode}");

            string jsonResponse = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(jsonResponse);

            if (!doc.RootElement.TryGetProperty("response", out var responseProp))
                throw new Exception("Ollama yanıtında 'response' alanı bulunamadı.");

            string editedText = responseProp.GetString()?.Trim() ?? string.Empty;

            editedText = Regex.Replace(editedText, @"^(İşte\s+metin[:\-]?\s*[\""]?|[""']|Haber[:\-]?\s*)", "", RegexOptions.IgnoreCase).Trim();
            if (editedText.EndsWith("\"") || editedText.EndsWith("'"))
                editedText = editedText.Substring(0, editedText.Length - 1).Trim();

            return editedText;
        }

        public async Task<string> RewriteSingleArticleAsync(string title, string rawArticleText)
        {
            return await RewriteSingleArticleAsync(title, rawArticleText, true, true);
        }
    }
}