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

        // Model adını varsayılan olarak llama3.1:8b yapıyoruz
        public NewsEditorLlm(string ollamaBaseUrl, string modelName = "llama3.1:8b", string? openButtonToken = null)
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
                    // Servis henüz ayağa kalkmadı
                }

                await Task.Delay(3000);
            }

            throw new TimeoutException($"Ollama servisi {timeoutSeconds} saniye içinde hazır hale gelmedi.");
        }

public async Task<string> RewriteSingleArticleAsync(string title, string rawArticleText, bool isFirstInBulletin, bool isLastInBulletin)
{
    var endpoint = $"{_ollamaBaseUrl}/api/chat";

    // 1. GÜVENLİK KİLİDİ: Ham metin çok uzunsa (örneğin binlerce kelime), 
    // LLM'e boğulmaması ve hızlı/özet çalışması için önce ilk 500 kelimesini alıyoruz.
    var rawWords = rawArticleText.Split(new[] { ' ', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
    if (rawWords.Length > 500)
    {
        rawArticleText = string.Join(" ", rawWords.Take(500));
    }

    string systemPrompt = @"You are a strict text transformation engine for Reels voice-over and video scripts. Never chat, never add intro/outro, never add titles. 

CRITICAL RULES:
1. SUMMARIZE the given news article concisely, keeping it strictly around 150 to 180 words (ideal for a 60-second voice-over). Drop unnecessary details, focus only on the core news.
2. Convert ALL numbers, digits, and dates into Turkish words (e.g., write 28 as yirmi sekiz, 2025 as iki bin yirmi beş). ABSOLUTELY NO DIGITS (0-9) ARE ALLOWED IN THE OUTPUT.
3. Clean up punctuation marks that interfere with text-to-speech engine.
4. Adapt foreign proper names according to Turkish phonetic spelling.
5. Output ONLY the summarized and transformed clean news text in Turkish.";

    string userPrompt = $"Haber Başlığı: {title}\n\nHaber Metni:\n{rawArticleText}";

    var requestBody = new
    {
        model = _modelName,
        messages = new[]
        {
            new { role = "system", content = systemPrompt },
            new { role = "user", content = userPrompt }
        },
        stream = false,
        options = new
        {
            temperature = 0.0,
            num_predict = 300 // Token/kelime patlamasını önlemek için çıkış sınırını da dengeliyoruz
        }
    };

    string jsonPayload = JsonSerializer.Serialize(requestBody);
    var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

    var response = await _httpClient.PostAsync(endpoint, content);
    if (!response.IsSuccessStatusCode)
        throw new Exception($"Ollama isteği başarısız: {(int)response.StatusCode}");

    string jsonResponse = await response.Content.ReadAsStringAsync();
    using var doc = JsonDocument.Parse(jsonResponse);

    if (!doc.RootElement.TryGetProperty("message", out var messageProp) ||
        !messageProp.TryGetProperty("content", out var contentProp))
    {
        throw new Exception("Ollama yanıtında 'message.content' alanı bulunamadı.");
    }

    string editedText = contentProp.GetString()?.Trim() ?? string.Empty;

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
