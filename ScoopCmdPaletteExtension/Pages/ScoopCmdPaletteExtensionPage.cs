// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ScoopCmdPaletteExtension;

internal sealed partial class ScoopCmdPaletteExtensionPage : DynamicListPage, IDisposable
{
    private readonly Scoop _scoop = new();
    private IListItem[] _results = [];
    private readonly Lock _resultsLock = new();
    private CancellationTokenSource? _searchCancellationTokenSource;
    IconInfo ScoopIcon { get; } = IconHelpers.FromRelativePath("Assets\\ice_cream_emoji.svg");

    public ScoopCmdPaletteExtensionPage()
    {
        Icon = ScoopIcon;
        Title = "Scoop";
        Name = "Search";
        ShowDetails = true;
        EmptyContent = new CommandItem()
        {
            Title = "Try searching for a Scoop package.",
            Icon = ScoopIcon,
        };
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
            return _results;
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
            var results = await _scoop.SearchAsync(searchText).WaitAsync(cancellationToken);
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
                        Icon = new IconInfo($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(result.Homepage)}&sz=24"),
                        Details = new Details() {
                            Title = result.Name,
                            Body = result.Notes,
                            Metadata = [
                                new DetailsElement() {
                                    Key = "Repository",
                                    Data = new DetailsLink() {
                                        Text = result.Metadata.OfficialRepository ? await _scoop.GetBucketNameFromRepoAsync(result.Metadata.Repository).WaitAsync(cancellationToken) : result.Metadata.Repository,
                                        Link = new Uri(result.Metadata.Repository),
                                    }
                                },
                                new DetailsElement() {
                                    Key = "File path",
                                    Data = new DetailsLink() {
                                        Text = result.Metadata.FilePath,
                                        Link = new Uri($"{result.Metadata.Repository}/blob/{result.Metadata.Sha}/{result.Metadata.FilePath}"),
                                    }
                                },
                                new DetailsElement() {
                                    Key = "Homepage",
                                    Data = new DetailsLink() {
                                        Text = result.Homepage,
                                        Link = new Uri(result.Homepage),
                                    }
                                },
                                // Only add License tag if result.License is not null or empty
                                ..(string.IsNullOrEmpty(result.License) ? Array.Empty<DetailsElement>() : [
                                    new DetailsElement() {
                                        Key = "License",
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
            Debug.WriteLine($"Scoop search cancelled for: {searchText}");
            return [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error searching Scoop: {ex.Message}");
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

    internal partial class InstallCommand : InvokableCommand
    {
        private readonly Scoop _scoop;
        private readonly ScoopSearchResultItem _package;
        public InstallCommand(Scoop scoop, ScoopSearchResultItem package)
        {
            Name = "Install";
            Icon = new("\uEBD3");
            _scoop = scoop;
            _package = package;
        }

        public override ICommandResult Invoke()
        {
            ProgressState progressState = new() { IsIndeterminate = false, ProgressPercent = 10 };
            ToastStatusMessage toast = new(new StatusMessage
            {
                Message = $"Checking bucket before installation...",
                State = MessageState.Info,
                Progress = progressState,
            })
            {
                Duration = -1,
            };
            toast.Show();
            string pkg = _package.Name;
            string repository = _package.Metadata.Repository;
            string filePath = _package.Metadata.FilePath;
            string fullPath = new Uri(new Uri(repository), filePath).ToString();
            try
            {
                ScoopBucket? bucket = _scoop.GetInstalledBucketFromSource(repository);
                progressState.ProgressPercent = 25;
                toast.Message.Message = $"Updating Scoop...";
                _scoop.UpdateAsync().Wait();
                progressState.ProgressPercent = 50;

                if (bucket == null)
                {
                    string bucketName = _scoop.GetBucketNameFromRepoAsync(repository).GetAwaiter().GetResult();
                    return CommandResult.Confirm(new ConfirmationArgs
                    {
                        Title = $"Bucket \"{bucketName}\" is not installed.",
                        Description = $"Do you want to install it from the repository \"{repository}\"?",
                        PrimaryCommand = new AnonymousCommand(() =>
                        {
                            toast.Message.Message = $"Installing bucket \"{bucketName}\" from repository \"{repository}\"...";
                            toast.Show();
                            try
                            {
                                _scoop.InstallBucketAsync(repository, bucketName).Wait();
                                progressState.ProgressPercent = 75;
                                DoInstall(toast, $"{bucketName}/{pkg}");
                            }
                            catch (Exception ex)
                            {
                                toast.Message.State = MessageState.Error;
                                toast.Message.Message = ex.Message;
                                toast.Show();
                            }
                        })
                        {
                            Name = "Install Bucket",
                            Result = CommandResult.KeepOpen()
                        },
                    });
                }

                DoInstall(toast, $"{bucket.Name}/{pkg}");
                return CommandResult.KeepOpen();
            }
            catch (Exception ex)
            {
                toast.Message.State = MessageState.Error;
                toast.Message.Message = ex.Message;
                toast.Show();
                return CommandResult.KeepOpen();
            }
        }

        private void DoInstall(ToastStatusMessage toast, string packageName)
        {
            toast.Message.Message = $"Installing package \"{packageName}\"...";
            toast.Message.State = MessageState.Info;
            toast.Message.Progress = new ProgressState { IsIndeterminate = true };
            toast.Show();
            try
            {
                _scoop.InstallAsync(packageName).Wait();
                toast.Message.Message = $"Package \"{packageName}\" installed successfully.";
                toast.Message.State = MessageState.Success;
                toast.Message.Progress = new ProgressState { IsIndeterminate = false, ProgressPercent = 100 };
                toast.Show();
            }
            catch (Exception ex)
            {
                toast.Message.State = MessageState.Error;
                toast.Message.Message = $"Error installing package \"{packageName}\": {ex.Message}";
                toast.Show();
                return;
            }
        }
    }
}
