using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Logging;
using MediaInfoKeeper.Options;
using MediaInfoKeeper.Web;
using MediaInfoKeeper.Web.Handler;

namespace MediaInfoKeeper.Services {
    internal sealed class ItemAddedSubhdService {
        private static readonly SemaphoreSlim DownloadGate = new(1, 1);
        private static readonly TimeSpan DownloadSpacing = TimeSpan.FromSeconds(3);

        private readonly ILibraryManager libraryManager;
        private readonly ILogger logger;
        private readonly SubhdService subhdService = new();

        public ItemAddedSubhdService(ILibraryManager libraryManager, ILogger logger) {
            this.libraryManager = libraryManager;
            this.logger = logger;
        }

        public async Task TryDownloadAsync(long itemId, MainPageOptions.ItemAddedTaskEditorOptions options) {
            if (options?.ItemAddedSubhdDownloadEnabled != true) return;

            var item = libraryManager.GetItemById(itemId);
            if (item is not Video video) return;
            if (video.ExtraType != null) return;
            if (video is not Movie && video is not Episode) return;

            var libraryScopeKeys = Plugin.LibraryService.GetItemLibraryScopeKeys(video);
            if (!Plugin.LibraryService.IsLibraryScopeMatch(
                    libraryScopeKeys,
                    options.ItemAddedSubhdDownloadLibraries)) {
                return;
            }

            if (Plugin.MediaInfoService == null || !Plugin.MediaInfoService.HasMediaInfo(video)) {
                logger?.Info($"入库搜字幕跳过（媒体信息未就绪）: {video.FileName ?? video.Path}");
                return;
            }

            if (HasChineseExternalSubtitle(video) || HasChineseEmbeddedSubtitle(video)) {
                logger?.Info($"入库搜字幕跳过（已有中字）: {video.FileName ?? video.Path}");
                return;
            }

            await DownloadGate.WaitAsync().ConfigureAwait(false);
            try {
                item = libraryManager.GetItemById(itemId);
                if (item is not Video fresh) return;
                video = fresh;

                if (Plugin.MediaInfoService == null || !Plugin.MediaInfoService.HasMediaInfo(video)) {
                    logger?.Info($"入库搜字幕跳过（媒体信息未就绪）: {video.FileName ?? video.Path}");
                    return;
                }

                if (HasChineseExternalSubtitle(video) || HasChineseEmbeddedSubtitle(video)) {
                    logger?.Info($"入库搜字幕跳过（已有中字）: {video.FileName ?? video.Path}");
                    return;
                }

                var best = await FindBestSimplifiedSubtitleAsync(video).ConfigureAwait(false);
                if (best == null || string.IsNullOrWhiteSpace(best.SubId)) {
                    logger?.Info($"入库搜字幕未找到: {video.FileName ?? video.Path}");
                    return;
                }

                var mediaDir = video.ContainingFolderPath ?? Path.GetDirectoryName(video.Path);
                if (string.IsNullOrWhiteSpace(mediaDir) || !Directory.Exists(mediaDir)) {
                    logger?.Info($"入库搜字幕跳过（媒体目录不可用）: {video.FileName ?? video.Path}");
                    return;
                }

                var baseFilename = Path.GetFileNameWithoutExtension(video.Path);
                if (string.IsNullOrWhiteSpace(baseFilename)) return;

                int? season = null;
                int? episode = null;
                if (video is Episode ep) {
                    season = ep.ParentIndexNumber;
                    episode = ep.IndexNumber;
                }

                var result = await subhdService.DownloadAsync(
                        best.SubId,
                        mediaDir,
                        baseFilename,
                        season,
                        episode,
                        saveBestOnly: true)
                    .ConfigureAwait(false);

                ForceRefreshExternal(video);
                logger?.Info($"入库搜字幕完成: {video.FileName ?? video.Path} -> {result}");
            }
            catch (Exception ex) {
                logger?.Error($"入库搜字幕失败: {itemId}");
                logger?.Error(ex.Message);
                logger?.Debug(ex.StackTrace);
            }
            finally {
                try {
                    await Task.Delay(DownloadSpacing).ConfigureAwait(false);
                }
                catch {
                }

                DownloadGate.Release();
            }
        }

        private async Task<SubhdSubtitleItem> FindBestSimplifiedSubtitleAsync(Video video) {
            var queries = BuildQueries(video);
            SubhdSubtitleItem best = null;
            var bestScore = int.MinValue;

            foreach (var query in queries) {
                List<SubhdSubtitleItem> results;
                try {
                    results = await subhdService.SearchAsync(query).ConfigureAwait(false);
                }
                catch (Exception ex) {
                    logger?.Warn($"入库搜字幕查询失败 query={query}: {ex.Message}");
                    continue;
                }

                if (results == null || results.Count == 0) continue;

                List<SubhdSubtitleItem> candidates;
                if (video is Episode ep && ep.IndexNumber.GetValueOrDefault() > 0) {
                    var season = ep.ParentIndexNumber.GetValueOrDefault(1);
                    var episode = ep.IndexNumber.Value;
                    candidates = results
                        .Where(s => {
                            var parsed = SubhdService.TryParseSeasonEpisode(s?.Title);
                            return parsed.HasValue &&
                                   parsed.Value.episode == episode &&
                                   (season <= 0 || parsed.Value.season == season);
                        })
                        .ToList();
                    if (candidates.Count == 0) continue;
                }
                else {
                    candidates = results;
                }

                foreach (var candidate in candidates) {
                    if (candidate == null || string.IsNullOrWhiteSpace(candidate.SubId)) continue;
                    var score = ScoreSimplified(candidate);
                    if (score > bestScore) {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best != null && bestScore >= 400) break;
            }

            return best;
        }

        private static List<string> BuildQueries(Video video) {
            var queries = new List<string>();
            void Add(string value) {
                if (string.IsNullOrWhiteSpace(value)) return;
                var trimmed = value.Trim();
                if (!queries.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) queries.Add(trimmed);
            }

            if (video is Episode ep) {
                var series = (ep.SeriesName ?? "").Trim();
                var season = ep.ParentIndexNumber.GetValueOrDefault(1);
                var episode = ep.IndexNumber.GetValueOrDefault();
                if (!string.IsNullOrWhiteSpace(series)) {
                    if (episode > 0) Add($"{series} S{season:D2}E{episode:D2}");
                    Add($"{series} S{season:D2}");
                    Add(series);
                }
            }
            else {
                var name = (video.Name ?? "").Trim();
                var original = (video.OriginalTitle ?? "").Trim();
                var year = video.ProductionYear > 0 ? video.ProductionYear.ToString() : "";
                Add($"{name} {year}".Trim());
                Add(name);
                if (!string.Equals(original, name, StringComparison.OrdinalIgnoreCase)) {
                    Add($"{original} {year}".Trim());
                    Add(original);
                }
            }

            return queries;
        }

        private static int ScoreSimplified(SubhdSubtitleItem item) {
            var tags = item.Tags == null ? "" : string.Join(" ", item.Tags);
            var title = $"{item.Title} {tags} {item.Format}".ToLowerInvariant();
            var score = item.Downloads;

            if (ContainsAny(title, "简英", "中英", "双语", "chs&en", "简体双语")) score += 500;
            else if (ContainsAny(title, "繁英", "cht&en", "繁英双语", "big5&en")) score += 80;
            else if (ContainsAny(title, "繁体", "繁中", "cht", "zh-tw", "zh-hant", "big5")) score += 50;
            else if (ContainsAny(title, "简体", "简中", "chs", "zh-cn", "zh-hans", "中字")) score += 400;
            else if (ContainsAny(title, "中文") || HasLanguageToken(title, "chi", "zh")) score += 250;

            if (ContainsAny(title, "ass")) score += 30;
            if (ContainsAny(title, "srt")) score += 20;
            return score;
        }

        private static void ForceRefreshExternal(BaseItem item) {
            if (Plugin.ExternalFiles == null || !Plugin.ExternalFiles.IsAvailable || item == null) return;

            try {
                var refreshOptions = Plugin.ExternalFiles.GetRefreshOptions();
                Plugin.ExternalFiles
                    .UpdateExternalFiles(item, refreshOptions, true, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception ex) {
                Plugin.Instance.Logger?.Error($"入库搜字幕后刷新外挂失败: {item.Path ?? item.Name}");
                Plugin.Instance.Logger?.Error(ex.Message);
            }
        }

        internal static bool HasChineseExternalSubtitle(BaseItem item) {
            if (item == null || string.IsNullOrWhiteSpace(item.Path)) return false;
            var dir = item.ContainingFolderPath ?? Path.GetDirectoryName(item.Path);
            var name = Path.GetFileNameWithoutExtension(item.Path);
            if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name) || !Directory.Exists(dir)) return false;

            try {
                foreach (var file in Directory.EnumerateFiles(dir, name + ".*")) {
                    if (!SubhdService.IsSubtitleExtension(Path.GetExtension(file))) continue;
                    if (IsChineseSubtitleFileName(Path.GetFileName(file))) return true;
                }
            }
            catch {
            }

            return false;
        }

        internal static bool HasChineseEmbeddedSubtitle(BaseItem item) {
            try {
                var streams = item?.GetMediaStreams();
                if (streams == null) return false;
                return streams.Any(stream =>
                    stream != null &&
                    stream.Type == MediaStreamType.Subtitle &&
                    !stream.IsExternal &&
                    (IsChineseLanguageTag(stream.Language) ||
                     IsChineseSubtitleFileName(stream.Title) ||
                     IsChineseSubtitleFileName(stream.DisplayTitle)));
            }
            catch {
                return false;
            }
        }

        private static bool IsChineseLanguageTag(string value) {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var text = value.Trim().ToLowerInvariant();
            return text.StartsWith("zh", StringComparison.Ordinal) ||
                   text is "chi" or "chs" or "cht" or "cmn" or "yue" or "zho" ||
                   text.Contains("chinese", StringComparison.Ordinal);
        }

        private static bool IsChineseSubtitleFileName(string value) {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var text = value.ToLowerInvariant();
            return ContainsAny(text,
                       "zh-hans", "zh-hant", "zh-cn", "zh-tw", "zh&en", "chs", "cht", "chinese",
                       "简体", "简中", "繁体", "繁中", "中字", "中英", "简英", "双语", "中文") ||
                   HasLanguageToken(text, "chi", "zh");
        }

        private static bool HasLanguageToken(string text, params string[] tokens) {
            foreach (var token in tokens) {
                var pattern = $@"(?:^|[\s._\-\[\(]){System.Text.RegularExpressions.Regex.Escape(token)}(?:$|[\s._\-\]\),])";
                if (System.Text.RegularExpressions.Regex.IsMatch(text, pattern,
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase)) {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsAny(string text, params string[] values) {
            foreach (var value in values) {
                if (text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }
    }
}
