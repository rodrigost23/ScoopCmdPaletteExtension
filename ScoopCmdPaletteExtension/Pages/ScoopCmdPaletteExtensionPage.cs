// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace ScoopCmdPaletteExtension;

internal sealed partial class ScoopCmdPaletteExtensionPage : DynamicListPage
{
    private Scoop _scoop = new();
    private IListItem[] _results = [];

    public ScoopCmdPaletteExtensionPage()
    {
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        Title = "Scoop";
        Name = "Search";
    }

    public override IListItem[] GetItems()
    {
        return _results.Length > 0 ? _results : [
            new ListItem(new OpenUrlCommand("https://scoop.sh"))
                {
                    Title = "Open Scoop home page (2)",
                }
        ];
    }

    public override void UpdateSearchText(string oldSearch, string newSearch)
    {
        Debug.WriteLine($"UpdateSearchText: {newSearch}");

        if (oldSearch == newSearch)
        {
            return;
        }

        if (string.IsNullOrEmpty(newSearch))
        {
            _results = [];
            RaiseItemsChanged(0);
            return;
        }

        var results = SearchScoop(newSearch);

        _results = results;
        RaiseItemsChanged(_results.Length);

    }

    // Search scoop
    public IListItem[] SearchScoop(string searchText)
    {
        if (string.IsNullOrEmpty(searchText))
        {
            IsLoading = true;
            return [];
        }

        try
        {
            IsLoading = true;
            var results = _scoop.SearchAsync(searchText).GetAwaiter().GetResult();
            return results.Select(r => new ListItem(new OpenUrlCommand(r.Homepage))
            {
                Title = r.Name,
                Subtitle = r.Description,
                Icon = new IconInfo($"{r.Homepage}/favicon.ico"),
            }).ToArray();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error searching Scoop: {ex.Message}");
            return [];
        }
        finally
        {
            IsLoading = false;
        }

    }
}
