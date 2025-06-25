using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Linq;

namespace ScoopCmdPaletteExtension
{
    internal partial class ManageAppsPage : ListPage
    {
        private readonly Scoop _scoop;

        public ManageAppsPage(Scoop scoop)
        {
            IsLoading = true;
            Title = Properties.Resources.TitleManageApps;
            Icon = new IconInfo("\uE71D");
            _scoop = scoop;
        }

        public override IListItem[] GetItems()
        {
            ScoopApp[] apps = FetchInstalledApps() ?? [];
            IsLoading = false;
            return [.. apps.Select(app => new ListItem(new NoOpCommand())
            {
                Title = app.Name,
                Subtitle = app.Version,
                Icon = new IconInfo("\uE71D"),
                Section = app.Source,
            })];
        }

        private ScoopApp[] FetchInstalledApps()
        {
            try
            {
                return _scoop.GetInstalledAppsAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                new ToastStatusMessage(new StatusMessage
                {
                    Message = $"Error fetching installed apps: {ex.Message}",
                    State = MessageState.Error,
                }).Show();
                return [];
            }
        }
    }
}
