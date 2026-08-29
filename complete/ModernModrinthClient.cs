using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Launcher
{
    public class ModrinthClient
    {
        private readonly HttpClient _httpClient;

        public ModrinthClient()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "MentizLauncher/1.0 (by Mentiz)");
            _httpClient.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<ModSearchResult2> SearchModsAsync(string query, string minecraftVersion, int offset = 0, int limit = 20)
        {
            var facets = new List<List<string>>
            {
                new List<string> { "project_type:mod" }
            };

            if (!string.IsNullOrEmpty(minecraftVersion))
                facets.Add(new List<string> { $"versions:{minecraftVersion}" });

            var facetsJson = JsonSerializer.Serialize(facets);

            // Если запрос пустой - сортируем по популярности (downloads)
            var index = string.IsNullOrEmpty(query) ? "downloads" : "relevance";

            var url = $"https://api.modrinth.com/v2/search?query={Uri.EscapeDataString(query)}&facets={Uri.EscapeDataString(facetsJson)}&index={index}&offset={offset}&limit={limit}";

            System.Diagnostics.Debug.WriteLine($"[API] {url}");

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadFromJsonAsync<ModSearchResponse>();

            System.Diagnostics.Debug.WriteLine($"[API] Total hits: {result?.TotalHits ?? 0}");

            return new ModSearchResult2
            {
                Mods = result?.Hits ?? new List<ModSearchResult>(),
                TotalHits = result?.TotalHits ?? 0
            };
        }

        public async Task<ModDetails> GetProjectDetailsAsync(string projectId)
        {
            var url = $"https://api.modrinth.com/v2/project/{projectId}";

            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var project = await response.Content.ReadFromJsonAsync<ModrinthProject>();

            if (project == null) return new ModDetails();

            return new ModDetails
            {
                project_id = project.Id,
                title = project.Title,
                description = project.Description,
                author = project.Author,
                downloads = project.Downloads,
                followers = project.Followers,
                icon_url = project.IconUrl,
                gallery = project.Gallery?.Select(g => g.Url).ToList() ?? new List<string>(),
                version = project.GameVersions?.FirstOrDefault() ?? "",
                loader = project.Loaders?.FirstOrDefault() ?? ""
            };
        }

        public async Task<List<ModVersion>> GetModVersionsAsync(string projectId, string minecraftVersion, string loader = "fabric")
        {
            var url = $"https://api.modrinth.com/v2/project/{projectId}/version";
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var versions = await response.Content.ReadFromJsonAsync<List<ModVersion>>();

            return versions?
                .Where(v => v.GameVersions.Contains(minecraftVersion) &&
                           v.Loaders.Contains(loader.ToLower()))
                .OrderByDescending(v => v.DatePublished)
                .ToList() ?? new List<ModVersion>();
        }

        public async Task DownloadModWithProgressAsync(string url, string savePath, string projectId, Action<int> progressCallback)
        {
            var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? 0;
            var downloadedBytes = 0L;

            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(savePath, FileMode.Create);

            var buffer = new byte[81920];
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloadedBytes += bytesRead;

                if (totalBytes > 0)
                {
                    var progress = (int)((downloadedBytes * 100) / totalBytes);
                    progressCallback(progress);
                }
            }

            progressCallback(100);
        }
    }

    // Модели данных
    public class ModSearchResponse
    {
        [JsonPropertyName("hits")]
        public List<ModSearchResult> Hits { get; set; } = new();

        [JsonPropertyName("total_hits")]
        public int TotalHits { get; set; }
    }

    public class ModSearchResult2
    {
        public List<ModSearchResult> Mods { get; set; } = new();
        public int TotalHits { get; set; }
    }

    public class ModSearchResult
    {
        [JsonPropertyName("project_id")]
        public string ProjectId { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("icon_url")]
        public string IconUrl { get; set; } = "";

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("slug")]
        public string Slug { get; set; } = "";
    }

    public class ModrinthProject
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("description")]
        public string Description { get; set; } = "";

        [JsonPropertyName("author")]
        public string Author { get; set; } = "";

        [JsonPropertyName("downloads")]
        public long Downloads { get; set; }

        [JsonPropertyName("followers")]
        public long Followers { get; set; }

        [JsonPropertyName("icon_url")]
        public string IconUrl { get; set; } = "";

        [JsonPropertyName("gallery")]
        public List<GalleryItem> Gallery { get; set; } = new();

        [JsonPropertyName("game_versions")]
        public List<string> GameVersions { get; set; } = new();

        [JsonPropertyName("loaders")]
        public List<string> Loaders { get; set; } = new();
    }

    public class GalleryItem
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";
    }

    public class ModDetails
    {
        public string project_id { get; set; } = "";
        public string title { get; set; } = "";
        public string description { get; set; } = "";
        public string author { get; set; } = "";
        public long downloads { get; set; }
        public long followers { get; set; }
        public string icon_url { get; set; } = "";
        public List<string> gallery { get; set; } = new();
        public string version { get; set; } = "";
        public string loader { get; set; } = "";
    }

    public class ModVersion
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("version_number")]
        public string VersionNumber { get; set; } = "";

        [JsonPropertyName("game_versions")]
        public List<string> GameVersions { get; set; } = new();

        [JsonPropertyName("loaders")]
        public List<string> Loaders { get; set; } = new();

        [JsonPropertyName("files")]
        public List<ModFile> Files { get; set; } = new();

        [JsonPropertyName("date_published")]
        public DateTime DatePublished { get; set; }
    }

    public class ModFile
    {
        [JsonPropertyName("url")]
        public string Url { get; set; } = "";

        [JsonPropertyName("filename")]
        public string Filename { get; set; } = "";

        [JsonPropertyName("primary")]
        public bool Primary { get; set; }
    }
}