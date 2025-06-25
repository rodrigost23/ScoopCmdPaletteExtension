using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System;
using System.Linq;

namespace ScoopCmdPaletteExtension
{
    internal partial class ManageAppsPage : ListPage
    {
        public ManageAppsPage()
        {
            IsLoading = true;
            Title = Properties.Resources.TitleManageApps;
            Icon = new IconInfo("\uE71D");
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

        private static ScoopApp[] FetchInstalledApps()
        {
            try
            {
                return Scoop.GetInstalledAppsAsync().GetAwaiter().GetResult();
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
