using System;
using System.Collections.Generic;
using System.Linq;
using MediaBrowser.Controller.Entities;

namespace MediaInfoKeeper.Web.Handler {
    internal static class VersionItemResolver {
        public static BaseItem Resolve(BaseItem item, string mediaSourceId) {
            if (item == null || string.IsNullOrWhiteSpace(mediaSourceId)) return item;
            if (item is not IHasMediaSources) return item;

            var mediaInfoService = Plugin.MediaInfoService;
            if (mediaInfoService == null) return item;

            var source = mediaInfoService.GetStaticMediaSources(item, true)
                .FirstOrDefault(s => string.Equals(s?.Id, mediaSourceId, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(source?.ItemId)) return item;

            var versionItem = ResolveItemId(source.ItemId);
            return versionItem ?? item;
        }

        public static List<Video> ResolveTargetVideos(
            IEnumerable<string> ids,
            string mediaSourceId,
            Func<IEnumerable<string>, List<BaseItem>> expandToTargetItems) {
            var idList = ids?
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToList() ?? new List<string>();
            if (idList.Count == 0) return new List<Video>();

            if (!string.IsNullOrWhiteSpace(mediaSourceId) && idList.Count == 1) {
                var item = ResolveItemId(idList[0]);
                var resolved = Resolve(item, mediaSourceId);
                if (resolved is Video video) {
                    return new List<Video> { video };
                }
            }

            return expandToTargetItems(idList).OfType<Video>().ToList();
        }

        private static BaseItem ResolveItemId(string itemId) {
            var libraryManager = Plugin.LibraryManager;
            if (libraryManager == null || string.IsNullOrWhiteSpace(itemId)) return null;

            if (long.TryParse(itemId, out var internalId)) {
                return libraryManager.GetItemById(internalId);
            }

            if (Guid.TryParse(itemId, out var guid)) {
                return libraryManager.GetItemById(guid);
            }

            return null;
        }
    }
}
