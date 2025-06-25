using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

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

        public async Task<Dictionary<string, string>> GetOfficialBucketsAsync(CancellationToken cancellationToken = default)
        {
            const string BUCKETS_JSON_URL = "https://raw.githubusercontent.com/ScoopInstaller/Scoop/master/buckets.json";
            using var response = await _httpClient.GetAsync(BUCKETS_JSON_URL, cancellationToken);
            response.EnsureSuccessStatusCode();
            var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonSerializer.DeserializeAsync(stream, ScoopJsonContext.Default.DictionaryStringString, cancellationToken)
                ?? throw new InvalidOperationException("Failed to deserialize official buckets.");
        }

        public async Task<ScoopSearchResultItem[]> SearchAsync(string query, CancellationToken cancellationToken = default)
        {
            string apiKey = await GetApiKey() ?? throw new InvalidOperationException("API key could not be retrieved.");

            var request = new HttpRequestMessage(HttpMethod.Post, SEARCH_URL)
            {
                Content = JsonContent.Create(new ScoopSearchPayload { Search = query }, ScoopJsonContext.Default.ScoopSearchPayload)
            };

            request.Headers.Add("api-key", apiKey);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            response.EnsureSuccessStatusCode();

            ScoopSearchResult? searchResult = await response.Content.ReadFromJsonAsync(ScoopJsonContext.Default.ScoopSearchResult, cancellationToken);

            return searchResult?.Value ?? [];
        }

        public record CommandResult
        {
            public string Stdout { get; init; } = "";
            public string Stderr { get; init; } = "";
            public JsonDocument? Json { get; init; }
        }

        public static async Task<CommandResult> RunCommandAsync(string args, bool jsonOutput = false, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (jsonOutput)
            {
                args += " | ConvertTo-Json -Depth 3";
            }

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

            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException($"Scoop command failed with exit code {process.ExitCode}. Stderr: {string.Join('\n', stderrBuffer)}");
            }

            return new CommandResult
            {
                Stdout = string.Join('\n', stdoutBuffer),
                Stderr = string.Join('\n', stderrBuffer),
                Json = jsonOutput
                    ? JsonDocument.Parse(string.Join('\n', stdoutBuffer))
                    : null
            };
        }

        public async static Task<ScoopBucket[]> GetBucketsAsync(CancellationToken cancellationToken = default)
        {
            var output = await RunCommandAsync("bucket list", jsonOutput: true, cancellationToken);
            return output.Json?.Deserialize(ScoopJsonContext.Default.ScoopBucketArray) ?? [];
        }

        public async static Task<ScoopBucket?> GetInstalledBucketFromSourceAsync(string source, CancellationToken cancellationToken = default)
        {
            ScoopBucket[] buckets = await GetBucketsAsync(cancellationToken);
            foreach (var bucket in buckets)
            {
                if (bucket.Source.Equals(source, StringComparison.OrdinalIgnoreCase))
                {
                    return bucket;
                }
            }

            return null;
        }

        public async Task<string> GetBucketNameFromRepoAsync(string repository, CancellationToken cancellationToken = default)
        {
            // Check if repository is in official bucket list and return
            var officialBuckets = await GetOfficialBucketsAsync(cancellationToken);

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

        public static async Task UpdateAsync(CancellationToken cancellationToken = default)
        {
            await RunCommandAsync("update", cancellationToken: cancellationToken);
        }

        public async Task InstallBucketAsync(string repository, string? name = null, CancellationToken cancellationToken = default)
        {
            name ??= await GetBucketNameFromRepoAsync(repository, cancellationToken);

            var officialBuckets = await GetOfficialBucketsAsync(cancellationToken);
            string installParam = officialBuckets.ContainsKey(name) ? name : $"{name} {repository}";
            await RunCommandAsync($"bucket add {installParam}", cancellationToken: cancellationToken);
        }

        public static async Task InstallAsync(string packageName, CancellationToken cancellationToken = default)
        {
            await RunCommandAsync($"install {packageName}", cancellationToken: cancellationToken);
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
        public int RepositoryStars { get; set; }
        public string Commited { get; set; } = string.Empty;
        public string Sha { get; set; } = string.Empty;
    }


    [JsonSerializable(typeof(ScoopSearchPayload))]
    [JsonSerializable(typeof(ScoopSearchResult))]
    [JsonSerializable(typeof(ScoopBucket[]))]
    [JsonSerializable(typeof(Dictionary<string, string>))]
    internal partial class ScoopJsonContext : JsonSerializerContext
    {
    }
}
