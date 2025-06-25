using Microsoft.CommandPalette.Extensions.Toolkit;
using System;

namespace ScoopCmdPaletteExtension
{
    internal static class Helpers
    {

        public static IconInfo GetFavicon(string homepage)
        {
            return new IconInfo($"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(homepage)}&sz=24");
        }
    }
}