using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ScoopCmdPaletteExtension;

internal sealed partial class MainPage : DynamicListPage, IDisposable
{
    private readonly Scoop _scoop = new();
    private string _currentSearchText = string.Empty;
    private IListItem[] _results = [];
    private readonly Lock _resultsLock = new();
    private CancellationTokenSource? _searchCancellationTokenSource;
    IconInfo ScoopIcon { get; } = IconHelpers.FromRelativePath("Assets\\ice_cream_emoji.svg");
    private static readonly CompositeFormat MsgNoResultsFound = CompositeFormat.Parse(Properties.Resources.MsgNoResultsFound);


    public MainPage()
    {
        Icon = ScoopIcon;
        Title = "Scoop";
        Name = "Search";
        ShowDetails = true;
    }

    public void Dispose()
    {
        _scoop.Dispose();
        _searchCancellationTokenSource?.Dispose();
    }

    public override IListItem[] GetItems()
    {
        lock (_resultsLock)
        {
            return _currentSearchText.Length > 0 ? _results : [
                new ListItem(new ManageBucketsPage(_scoop)) {
                    Title = Properties.Resources.TitleManageBuckets,
                    Icon = new IconInfo("\uE74C"),
                },
                new ListItem(new ManageAppsPage(_scoop)) {
                    Title = Properties.Resources.TitleManageApps,
                    Icon = new IconInfo("\uE71D"),
                },
                new ListItem {
                   Title = Properties.Resources.OpenScoopHomepage,
                   Command = new OpenUrlCommand("https://scoop.sh/"),
                }
            ];
        }
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        Debug.WriteLine($"UpdateSearchText: {newSearch}");

        if (oldSearch == newSearch)
        {
            return;
        }

        _searchCancellationTokenSource?.Cancel();

        lock (_resultsLock)
        {
            _currentSearchText = newSearch;
            EmptyContent = new CommandItem()
            {
                Title = Properties.Resources.TitleScoopSearch,
                Subtitle = string.Format(CultureInfo.CurrentCulture, MsgNoResultsFound, newSearch),
                Icon = ScoopIcon,
            };
        }

        if (string.IsNullOrEmpty(newSearch))
        {
            UpdateResults([]);
            _searchCancellationTokenSource = null;
            return;
        }

        _searchCancellationTokenSource = new CancellationTokenSource();
        var token = _searchCancellationTokenSource.Token;

        SearchScoopAsync(newSearch, token).ContinueWith(task =>
        {
            if (task.IsCanceled)
                return;
            UpdateResults(task.Result);
        }, token);
    }

    private void UpdateResults(IListItem[] results)
    {
        lock (_resultsLock)
        {
            _results = results;
        }
        RaiseItemsChanged(_results.Length);
    }

    // Search scoop
    public async Task<IListItem[]> SearchScoopAsync(string searchText, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            IsLoading = true;
            return [];
        }

        try
        {
            IsLoading = true;
            cancellationToken.ThrowIfCancellationRequested();
            var results = await _scoop.SearchAsync(searchText, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            return await Task.WhenAll([
                .. results.Select(async result =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ListItem(new InstallCommand(_scoop, result))
                    {
                        Title = result.Name,
                        Subtitle = result.Description,
                        Tags = [
                            ..result.Metadata.OfficialRepository ? [new Tag() {
                                Text = await _scoop.GetBucketNameFromRepoAsync(result.Metadata.Repository).WaitAsync(cancellationToken),
                                Icon = IconHelpers.FromRelativePath("Assets\\icon_checkmark.svg"),
                            }] : Array.Empty<Tag>(),
                            new Tag() {
                                Text = result.Version,
                                ToolTip = "Version",
                            }
                        ],
                        Icon = Helpers.GetFavicon(result.Homepage),
                        Details = new Details() {
                            Title = result.Name,
                            Body = result.Notes,
                            Metadata = [
                                new DetailsElement() {
                                    Key = Properties.Resources.PackageMetadataRepository,
                                    Data = new DetailsLink() {
                                        Text = result.Metadata.OfficialRepository ? await _scoop.GetBucketNameFromRepoAsync(result.Metadata.Repository).WaitAsync(cancellationToken) : result.Metadata.Repository,
                                        Link = new Uri(result.Metadata.Repository),
                                    }
                                },
                                new DetailsElement() {
                                    Key = Properties.Resources.PackageMetadataFilePath,
                                    Data = new DetailsLink() {
                                        Text = result.Metadata.FilePath,
                                        Link = new Uri($"{result.Metadata.Repository}/blob/{result.Metadata.Sha}/{result.Metadata.FilePath}"),
                                    }
                                },
                                new DetailsElement() {
                                    Key = Properties.Resources.PackageMetadataHomepage,
                                    Data = new DetailsLink() {
                                        Text = result.Homepage,
                                        Link = new Uri(result.Homepage),
                                    }
                                },
                                // Only add License tag if result.License is not null or empty
                                ..(string.IsNullOrEmpty(result.License) ? Array.Empty<DetailsElement>() : [
                                    new DetailsElement() {
                                        Key = Properties.Resources.PackageMetadataLicense,
                                        Data = new DetailsLink() {
                                            Text = result.License,
                                            Link = !result.License.Contains(',') ? new Uri($"https://spdx.org/licenses/{result.License}.html") : null,
                                        }
                                    }
                                ]),
                            ]
                        }
                    };
                })
            ]);
        }
        catch (OperationCanceledException)
        {
            Debug.WriteLine($"Scoop search cancelled for: {searchText}", cancellationToken);
            return [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error searching Scoop: {ex.Message}", cancellationToken);
            ToastStatusMessage toast = new(new StatusMessage
            {
                Message = $"Error searching Scoop: {ex.Message}",
                State = MessageState.Error,
            });
            return [];
        }
        finally
        {
            IsLoading = false;
        }
    }
}
