using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.ServiceModel.Syndication;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using HtmlAgilityPack;

namespace HaberOtomasyon
{
    public class NewsHeadline
    {
        public string Title { get; set; } = "";
        public string Link { get; set; } = "";
        public DateTimeOffset PublishDate { get; set; }
    }

    public class NewsArticle
    {
        public string Title { get; set; } = "";
        public string FullText { get; set; } = "";
        public List<string> ImageUrls { get; set; } = new();
    }

    /// <summary>
    /// RSS feed okuma ve makale sayfasından tam metin + görsel çekme (web scraping).
    /// </summary>
    public class NewsScraper
    {
        private readonly HttpClient _http = new HttpClient();

        public NewsScraper()
        {
            // Bazı siteler User-Agent olmadan isteği reddedebiliyor, tarayıcı gibi görünelim
            _http.DefaultRequestHeaders.UserAgent.ParseAdd(
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
        }

        /// <summary>RSS feed'ini okur, başlık + link listesini döner (en yeniden en eskiye).</summary>
        public async Task<List<NewsHeadline>> GetHeadlinesAsync(string rssUrl)
        {
            var xml = await _http.GetStringAsync(rssUrl);
            using var stringReader = new System.IO.StringReader(xml);
            using var xmlReader = XmlReader.Create(stringReader);
            var feed = SyndicationFeed.Load(xmlReader);

            var result = feed.Items
                .Select(item => new NewsHeadline
                {
                    Title = item.Title?.Text ?? "",
                    Link = item.Links.FirstOrDefault()?.Uri?.ToString() ?? "",
                    PublishDate = item.PublishDate
                })
                .Where(h => !string.IsNullOrEmpty(h.Link))
                .OrderByDescending(h => h.PublishDate)
                .ToList();

            Console.WriteLine($"  [NewsScraper] RSS'ten {result.Count} başlık okundu.");
            return result;
        }

        /// <summary>
        /// Bir makale sayfasını indirir, tam metni ve görselleri ayıklar.
        /// Seçici olarak hash'lenmiş class isimleri yerine kararlı yapılar kullanır:
        /// main[role='main'] içindeki tüm &lt;p&gt; ve &lt;img&gt; etiketleri.
        /// </summary>
public async Task<NewsArticle> ScrapeArticleAsync(string articleUrl)
        {
            var html = await _http.GetStringAsync(articleUrl);

            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var mainNode = doc.DocumentNode.SelectSingleNode("//main");
            if (mainNode == null)
                throw new Exception($"Makale gövdesi bulunamadı (main yok): {articleUrl}");

            // 1. İstenmeyen öneri, en çok okunanlar veya reklam section'larını DOM'dan tamamen siliyoruz
            var unwantedSections = mainNode.SelectNodes(".//section[contains(@class, 'topic-discovery') or contains(@class, 'most-read') or contains(@class, 'advertisement') or contains(@class, 'social-links')]");
            if (unwantedSections != null)
            {
                foreach (var sec in unwantedSections)
                {
                    sec.Remove(); // Çöp/öneri alanları silindi, böylece içindeki metin ve görseller de yok oldu
                }
            }

            // Başlık - genelde ilk <h1>
            string title = doc.DocumentNode.SelectSingleNode("//h1")?.InnerText.Trim() ?? "";

            // 2. Artık arındırılmış mainNode içerisindeki paragrafları alıyoruz
            var paragraphNodes = mainNode.SelectNodes(".//p");
            var paragraphs = new List<string>();
            if (paragraphNodes != null)
            {
                foreach (var p in paragraphNodes)
                {
                    string text = HtmlEntity.DeEntitize(p.InnerText).Trim();
                    
                    // Çok kısa/boş satırları, yasal uyarıları veya çöp kalıntıları ele
                    if (text.Length > 15 && 
                        !text.StartsWith("Fotoğraf kaynağı") && 
                        !text.Contains("BBC Türkçe") && 
                        !text.StartsWith("Abone ol"))
                    {
                        paragraphs.Add(text);
                    }
                }
            }

            // 3. Görselleri sadece temizlenmiş alan içindeki <figure> etiketlerinden topluyoruz
            var imageNodes = mainNode.SelectNodes(".//figure//img");
            var imageUrls = new List<string>();
            if (imageNodes != null)
            {
                foreach (var img in imageNodes)
                {
                    string? src = img.GetAttributeValue("src", null);
                    if (string.IsNullOrEmpty(src))
                        src = img.GetAttributeValue("data-src", null);

                    if (!string.IsNullOrEmpty(src) && !imageUrls.Contains(src))
                        imageUrls.Add(src);
                }
            }

            var article = new NewsArticle
            {
                Title = title,
                FullText = string.Join("\n\n", paragraphs),
                ImageUrls = imageUrls
            };

            Console.WriteLine($"  [NewsScraper] Makale çekildi: \"{title}\" " +
                               $"({paragraphs.Count} temiz paragraf, {imageUrls.Count} geçerli görsel)");
            return article;
        }    }
}
