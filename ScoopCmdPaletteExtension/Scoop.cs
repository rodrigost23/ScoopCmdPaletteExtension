using ABI.System;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Windows.Media.Protection.PlayReady;

namespace ScoopCmdPaletteExtension
{
    internal class Scoop : IDisposable
    {
        private readonly HttpClient _httpClient = new();
        private string? _apiKey;
        private const string SEARCH_URL = "https://scoopsearch.search.windows.net/indexes/apps/docs/search?api-version=2020-06-30";

        public void Dispose()
        {
            _httpClient.Dispose();
        }

        private async Task<string?> GetApiKey()
        {
            if (_apiKey != null)
            {
                return _apiKey;
            }

            using var httpClient = new HttpClient();
            string envContent = await httpClient.GetStringAsync("https://raw.githubusercontent.com/ScoopInstaller/scoopinstaller.github.io/main/.env");

            // Example: VITE_APP_AZURESEARCH_KEY = "abcdef123456"
            foreach (var line in envContent.Split('\n'))
            {
                var trimmedLine = line.Trim();
                if (trimmedLine.StartsWith("VITE_APP_AZURESEARCH_KEY", StringComparison.Ordinal))
                {
                    var parts = trimmedLine.Split('=', 2);
                    if (parts.Length == 2)
                    {
                        // Remove possible quotes and whitespace
                        _apiKey = parts[1].Trim().Trim('"');
                    }
                }
            }

            return _apiKey;
        }

        public async Task<ScoopSearchResultItem[]> SearchAsync(string query)
        {
            string apiKey = await GetApiKey() ?? throw new InvalidOperationException("API key could not be retrieved.");

            var request = new HttpRequestMessage(HttpMethod.Post, SEARCH_URL)
            {
                Content = JsonContent.Create(new ScoopSearchPayload { Search = query }, ScoopJsonContext.Default.ScoopSearchPayload)
            };

            request.Headers.Add("api-key", apiKey);

            var response = await _httpClient.SendAsync(request);

            response.EnsureSuccessStatusCode();

            var searchResult = await response.Content.ReadFromJsonAsync<ScoopSearchResult>(ScoopJsonContext.Default.ScoopSearchResult);

            if (searchResult == null || searchResult.Value == null)
            {
                return [];
            }

            return searchResult.Value;
        }
    }

    internal class ScoopSearchPayload
    {
        [JsonPropertyName("count")]
        public bool Count { get; set; } = true;
        [JsonPropertyName("search")]
        public string Search { get; set; } = string.Empty;
        [JsonPropertyName("searchMode")]
        public string SearchMode { get; set; } = "all";
        [JsonPropertyName("filter")]
        public string Filter { get; set; } = "Metadata/DuplicateOf eq null";
        [JsonPropertyName("orderby")]
        public string OrderBy { get; set; } = "search.score() desc, Metadata/OfficialRepositoryNumber desc,NameSortable asc";
        [JsonPropertyName("skip")]
        public int Skip { get; set; } = 0;
        [JsonPropertyName("top")]
        public int Top { get; set; } = 20;
        [JsonPropertyName("select")]
        public string Select = "Id,Name,NamePartial,NameSuffix,Description,Notes,Homepage,License,Version,Metadata/Repository,Metadata/FilePath,Metadata/OfficialRepository,Metadata/RepositoryStars,Metadata/Committed,Metadata/Sha";
        [JsonPropertyName("highlight")]
        public string Highlight = "Name,NamePartial,NameSuffix,Description,Version,License,Metadata/Repository";
        [JsonPropertyName("highlightPreTag")]
        public string HighlightPreTag = "<mark>";
        [JsonPropertyName("highlightPostTag")]
        public string HighlightPostTag = "</mark>";
    }

    internal class ScoopSearchResult
    {
        [JsonPropertyName("value")]
        public ScoopSearchResultItem[] Value { get; set; } = [];
    }

    internal class ScoopSearchResultItem
    {
        public string Name { get; set; } = string.Empty;
        public string Homepage { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }


    [JsonSerializable(typeof(ScoopSearchPayload))]
    [JsonSerializable(typeof(ScoopSearchResult))]
    internal partial class ScoopJsonContext : JsonSerializerContext
    {
    }
}
