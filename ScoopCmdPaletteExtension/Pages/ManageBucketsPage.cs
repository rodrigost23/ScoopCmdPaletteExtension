using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using System.Linq;

namespace ScoopCmdPaletteExtension
{
    internal partial class ManageBucketsPage : ListPage
    {
        private readonly Scoop _scoop;
        public ManageBucketsPage(Scoop scoop)
        {
            IsLoading = true;
            Title = Properties.Resources.TitleManageBuckets;
            Icon = new IconInfo("\uE74C");
            _scoop = scoop;
        }

        public override IListItem[] GetItems()
        {
            ScoopBucket[] buckets = _scoop.GetBucketsAsync().GetAwaiter().GetResult() ?? [];
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
