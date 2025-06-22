using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Management.Automation;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ScoopCmdPaletteExtension
{
    internal partial class Scoop : IDisposable
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

        public async Task<Dictionary<string, string>> GetOfficialBucketsAsync()
        {
            const string BUCKETS_JSON_URL = "https://raw.githubusercontent.com/ScoopInstaller/Scoop/master/buckets.json";
            using var response = await _httpClient.GetAsync(BUCKETS_JSON_URL);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync();
            return JsonSerializer.Deserialize(stream, ScoopJsonContext.Default.DictionaryStringString)
                ?? throw new InvalidOperationException("Failed to deserialize official buckets.");
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

        public static async Task<(string stdout, string stderr)> RunCommandAsync(string args)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-Command \"scoop {args}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = new Process { StartInfo = startInfo };

            var stdoutBuffer = new List<string>();
            var stderrBuffer = new List<string>();

            process.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    Debug.WriteLine(e.Data);
                    stdoutBuffer.Add(e.Data);
                }
            };
            process.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    Debug.WriteLine(e.Data);
                    stderrBuffer.Add(e.Data);
                }
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Scoop command failed with exit code {process.ExitCode}. Stderr: {string.Join('\n', stderrBuffer)}");
            }

            return (string.Join('\n', stdoutBuffer), string.Join('\n', stderrBuffer));
        }

        public ScoopBucket[] GetBuckets()
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-Command \"scoop bucket list | ConvertTo-Json -Depth 3\"",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            string? output = process?.StandardOutput.ReadToEnd();
            process?.WaitForExit();

            // Deserialize JSON output
            var buckets = output != null ? JsonSerializer.Deserialize(output, ScoopJsonContext.Default.ScoopBucketArray) : null;

            return buckets ?? [];
        }

        public ScoopBucket? GetInstalledBucketFromSource(string source)
        {
            ScoopBucket[] buckets = GetBuckets();
            foreach (var bucket in buckets)
            {
                if (bucket.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                {
                    return bucket;
                }
            }

            return null;
        }

        public async Task<string> GetBucketNameFromRepo(string repository)
        {
            // Check if repository is in official bucket list and return
            var officialBuckets = await GetOfficialBucketsAsync();

            foreach (var kvp in officialBuckets)
            {
                if (string.Equals(kvp.Value.Trim(), repository.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Key;
                }
            }

            // Get the bucket name from the repository URL
            var segments = new Uri(repository).Segments;

            if (segments.Length >= 3)
            {
                string owner = segments[1].TrimEnd('/');
                string repo = segments[2].TrimEnd('/');
                string ownerRepo = $"{owner}_{repo}";

                return ownerRepo;
            }

            throw new ArgumentException("Invalid repository URL format.", nameof(repository));
        }

        public async Task UpdateAsync()
        {
            await RunCommandAsync("update");
        }

        public async Task InstallBucketAsync(string repository, string? name = null)
        {
            name ??= await GetBucketNameFromRepo(repository);

            var officialBuckets = await GetOfficialBucketsAsync();
            string installParam = officialBuckets.ContainsKey(name) ? name : $"{name} {repository}";
            await RunCommandAsync($"bucket add {installParam}");
        }

        public async Task InstallAsync(string packageName)
        {
            await RunCommandAsync($"install {packageName}");
        }
    }

    internal class ScoopBucket
    {
        public string Name { get; set; } = string.Empty;
        public string Source { get; set; } = string.Empty;
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
        public string Version { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public string License { get; set; } = string.Empty;
        public ScoopSearchResultMetadata Metadata { get; set; } = new ScoopSearchResultMetadata();
    }

    internal class ScoopSearchResultMetadata
    {
        public string Repository { get; set; } = string.Empty;
        public bool OfficialRepository { get; set; }
        public string FilePath { get; set; } = string.Empty;
    }


    [JsonSerializable(typeof(ScoopSearchPayload))]
    [JsonSerializable(typeof(ScoopSearchResult))]
    [JsonSerializable(typeof(ScoopBucket[]))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    internal partial class ScoopJsonContext : JsonSerializerContext
    {
    }
}
