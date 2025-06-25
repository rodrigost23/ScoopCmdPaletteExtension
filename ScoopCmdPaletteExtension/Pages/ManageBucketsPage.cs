using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Linq;

namespace ScoopCmdPaletteExtension
{
    internal partial class ManageBucketsPage : ListPage
    {
        public ManageBucketsPage()
        {
            IsLoading = true;
            Title = Properties.Resources.TitleManageBuckets;
            Icon = new IconInfo("\uE74C");
        }

        public override IListItem[] GetItems()
        {
            ScoopBucket[] buckets = Scoop.GetBucketsAsync().GetAwaiter().GetResult() ?? [];
            IsLoading = false;
            return [.. buckets.Select(bucket => new ListItem(new NoOpCommand())
            {
                Title = bucket.Name,
                Subtitle = bucket.Source,
                Icon = new IconInfo("\uE74C"),
            })];
        }
    }
}
