using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using MediaInfoKeeper.Common;
using MediaInfoKeeper.Store;
using MediaInfoKeeper.Web.Handler;

namespace MediaInfoKeeper.Web {
    [Unauthenticated]
    public class PluginWebResourceService : IService, IRequiresRequest {
        private readonly ClearIntroRouteHandler _clearIntroHandler;
        private readonly DeleteMediaInfoPersistRouteHandler _deletePersistHandler;
        private readonly ExtractMediaInfoRouteHandler _extractHandler;
        private readonly IItemRepository _itemRepository;
        private readonly IJsonSerializer _jsonSerializer;
        private readonly ILibraryManager _libraryManager;
        private readonly IHttpResultFactory _resultFactory;
        private readonly RenameSubtitlesRouteHandler _renameSubtitlesHandler;
        private readonly ScanExternalFilesRouteHandler _scanExternalFilesHandler;
        private readonly ScanIntroRouteHandler _scanIntroHandler;
        private readonly SetIntroRouteHandler _setIntroHandler;
        private readonly SubhdService _subhdService;

        public PluginWebResourceService(
            IHttpResultFactory resultFactory,
            ILibraryManager libraryManager,
            IItemRepository itemRepository,
            IJsonSerializer jsonSerializer) {
            _resultFactory = resultFactory;
            _libraryManager = libraryManager;
            _itemRepository = itemRepository;
            _jsonSerializer = jsonSerializer;
            _extractHandler = new ExtractMediaInfoRouteHandler(Plugin.LibraryService.ExpandItem);
            _deletePersistHandler =
                new DeleteMediaInfoPersistRouteHandler(Plugin.LibraryService.ExpandItem, libraryManager,
                    itemRepository);
            _scanIntroHandler = new ScanIntroRouteHandler(Plugin.LibraryService.ExpandItem);
            _scanExternalFilesHandler = new ScanExternalFilesRouteHandler(Plugin.LibraryService.ExpandItem);
            _renameSubtitlesHandler = new RenameSubtitlesRouteHandler(Plugin.LibraryService.ExpandItem);
            _setIntroHandler =
                new SetIntroRouteHandler(Plugin.LibraryService.ExpandItem, libraryManager, itemRepository);
            _clearIntroHandler =
                new ClearIntroRouteHandler(Plugin.LibraryService.ExpandItem, libraryManager, itemRepository);
            _subhdService = new SubhdService();
        }

        public IRequest Request { get; set; }

        public object Get(MediaInfoKeeperJsRequest request) {
            return _resultFactory.GetResult(Request,
                GetStreamBytes(PluginWebResourceLoader.MediaInfoKeeperJs), "application/x-javascript");
        }

        public object Get(EdeJsRequest request) {
            return _resultFactory.GetResult(Request,
                GetStreamBytes(PluginWebResourceLoader.EdeJs), "application/x-javascript");
        }

        public object Get(ShortcutMenuRequest request) {
            return _resultFactory.GetResult(PluginWebResourceLoader.ModifiedShortcutsString.AsSpan(),
                "application/x-javascript");
        }

        public object Get(RefreshDialogRequest request) {
            return _resultFactory.GetResult(PluginWebResourceLoader.ModifiedRefreshDialogString.AsSpan(),
                "application/x-javascript");
        }

        public object Get(DanmuRawRequest request) {
            if (request == null || string.IsNullOrWhiteSpace(request.ItemId)) return CreateEmptyDanmuResult();

            var logger = Plugin.Instance?.Logger;
            if (Plugin.Instance?.Options?.MetaData?.ScrapersEditor?.Danmu?.EnableDanmuApi != true) {
                logger?.Debug("弹幕API: 已禁用，返回空结果");
                return CreateEmptyDanmuResult();
            }

            var item = _libraryManager.GetItemById(request.ItemId);
            if (item == null || string.IsNullOrWhiteSpace(item.ContainingFolderPath) ||
                string.IsNullOrWhiteSpace(item.FileNameWithoutExtension))
                return CreateEmptyDanmuResult();

            if (Plugin.DanmuService?.IsSupportedItem(item) != true) {
                logger?.Debug(
                    $"弹幕API: 非视频条目，跳过 itemId={request.ItemId} item={item.FileName} type={item.GetType().Name}");
                return _resultFactory.GetResult(Request, ReadOnlyMemory<byte>.Empty, "application/xml");
            }

            var danmuXmlPath = Path.Combine(item.ContainingFolderPath, item.FileNameWithoutExtension + ".xml");
            var localExists = File.Exists(danmuXmlPath);
            var alwaysFetchLatest =
                Plugin.Instance?.Options?.MetaData?.ScrapersEditor?.Danmu?.AlwaysFetchLatestDanmu == true;
            var modeLabel = alwaysFetchLatest ? "始终获取最新" : "本地优先";
            var logContext = $"mode={modeLabel} itemId={request.ItemId} item={item.FileName}";

            if (!alwaysFetchLatest && localExists) {
                logger?.Debug($"弹幕API: 本地命中，直接返回 {logContext} path={danmuXmlPath}");
                return _resultFactory.GetStaticFileResult(Request, danmuXmlPath).GetAwaiter()
                    .GetResult();
            }

            if (!alwaysFetchLatest) {
                logger?.Debug($"弹幕API: 本地未命中且未启用获取最新，返回空结果 {logContext} path={danmuXmlPath}");
                return CreateEmptyDanmuResult();
            }

            if (Plugin.DanmuService?.IsSupportedItem(item) == true && Plugin.DanmuService.IsEnabled)
                try {
                    if (Plugin.DanmuService.TryGetCachedDanmuXmlBytes(item, out var cachedXmlBytes))
                        return _resultFactory.GetResult(Request, (ReadOnlyMemory<byte>)cachedXmlBytes,
                            "application/xml");

                    using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                    var fetchResult = Plugin.DanmuService
                        .FetchDanmuXmlDetailedForApiAsync(item, cancellationTokenSource.Token)
                        .GetAwaiter()
                        .GetResult();
                    var xmlBytes = fetchResult?.XmlBytes;
                    if (xmlBytes != null && xmlBytes.Length > 0) {
                        try {
                            var directory = Path.GetDirectoryName(danmuXmlPath);
                            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

                            File.WriteAllBytes(danmuXmlPath, xmlBytes);

                            logger?.Debug($"弹幕API: 最新弹幕拉取成功并写入本地 {logContext} path={danmuXmlPath}");
                        }
                        catch (Exception ex) {
                            logger?.Debug($"弹幕API: 拉取成功但写入本地失败 {logContext} path={danmuXmlPath} error={ex.Message}");
                            logger?.Debug(ex.StackTrace);
                        }

                        return _resultFactory.GetResult(Request, (ReadOnlyMemory<byte>)xmlBytes, "application/xml");
                    }

                    if (string.IsNullOrWhiteSpace(fetchResult?.Reason)) return CreateEmptyDanmuResult();

                    logger?.Debug($"弹幕API: 网络拉取结果为空 {logContext} reason={fetchResult.Reason}");
                }
                catch (Exception ex) {
                    logger?.Debug($"弹幕API: 网络拉取失败 {logContext} error={ex.Message}");
                    logger?.Debug(ex.StackTrace);
                }

            if (alwaysFetchLatest && localExists) {
                logger?.Debug($"弹幕API: 获取最新失败或超时，回退本地 {logContext} path={danmuXmlPath}");
                return _resultFactory.GetStaticFileResult(Request, danmuXmlPath).GetAwaiter()
                    .GetResult();
            }

            logger?.Debug($"弹幕API: 无可用弹幕，返回空结果 {logContext}");
            return CreateEmptyDanmuResult();
        }

        private object CreateEmptyDanmuResult() {
            return _resultFactory.GetResult(Request, ReadOnlyMemory<byte>.Empty, "application/xml");
        }

        private static ReadOnlyMemory<byte> GetStreamBytes(MemoryStream stream) {
            return stream == null ? ReadOnlyMemory<byte>.Empty : new ReadOnlyMemory<byte>(stream.ToArray());
        }

        public MediaInfoMenuResponse Post(ExtractMediaInfoRequest request) {
            return _extractHandler.Handle(request);
        }

        public MediaInfoMenuResponse Post(DeleteMediaInfoPersistRequest request) {
            return _deletePersistHandler.Handle(request);
        }

        public MediaInfoMenuResponse Post(ScanIntroRequest request) {
            return _scanIntroHandler.Handle(request);
        }

        public MediaInfoMenuResponse Post(ScanExternalFilesRequest request) {
            return _scanExternalFilesHandler.Handle(request);
        }

        public MediaInfoMenuResponse Post(RenameSubtitlesRequest request) {
            return _renameSubtitlesHandler.Handle(request);
        }

        public MediaInfoMenuResponse Post(SetIntroRequest request) {
            return _setIntroHandler.Handle(request);
        }

        public MediaInfoMenuResponse Post(ClearIntroRequest request) {
            return _clearIntroHandler.Handle(request);
        }

        public SearchSubhdResponse Post(SearchSubhdRequest request) {
            var response = new SearchSubhdResponse { Subtitles = new List<SubhdSubtitleItem>() };

            if (request?.Ids == null || request.Ids.Length == 0) {
                response.Message = "未选择条目";
                return response;
            }

            var itemId = request.Ids[0];
            var item = _libraryManager.GetItemById(itemId);
            if (item == null) {
                if (long.TryParse(itemId, out var internalId)) {
                    item = _libraryManager.GetItemById(internalId);
                }
            }

            if (item == null) {
                response.Message = "未找到媒体条目";
                return response;
            }

            item = VersionItemResolver.Resolve(item, request.MediaSourceId);

            var searchQuery = BuildSearchQuery(item);
            response.SearchQuery = searchQuery;
            response.ItemName = BuildDisplayItemName(item);

            if (string.IsNullOrWhiteSpace(searchQuery)) {
                response.Message = "无法构建搜索关键词";
                return response;
            }

            var localEpisodes = GetLocalEpisodes(item);
            FillEpisodeInventory(response, localEpisodes);

            try {
                var candidates = BuildSearchQueryCandidates(item, searchQuery);
                var merged = new List<SubhdSubtitleItem>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var hitQuery = searchQuery;
                var aggregateAllCandidates = item is Season || item is Series || item is Episode;

                foreach (var query in candidates) {
                    MergeSearchResults(_subhdService.SearchAsync(query).GetAwaiter().GetResult(), merged, seen);
                    if (merged.Count > 0 && !aggregateAllCandidates) {
                        hitQuery = query;
                        break;
                    }
                }

                FillMissingEpisodeSubtitles(item, localEpisodes, merged, seen);

                if (aggregateAllCandidates && merged.Count > 0) {
                    hitQuery = string.Join(" | ", candidates);
                }

                response.Subtitles = merged.OrderByDescending(s => s.Downloads).ToList();
                response.SearchQuery = hitQuery;
                if (response.Subtitles.Count > 0) {
                    var covered = CountCoveredLocalEpisodes(merged, localEpisodes);
                    response.Message = localEpisodes.Count > 0
                        ? $"找到 {response.Subtitles.Count} 条字幕，覆盖库内 {covered}/{localEpisodes.Count} 集"
                        : $"找到 {response.Subtitles.Count} 条字幕";
                } else {
                    response.Message = "未找到匹配的字幕";
                }
            } catch (Exception ex) {
                Plugin.Instance.Logger.Error($"SubHD search error: {ex.Message}");
                response.Message = $"搜索出错: {ex.Message}";
            }

            return response;
        }

        public MediaInfoMenuResponse Post(DownloadSubhdRequest request) {
            var response = new MediaInfoMenuResponse { Total = 1, Processed = 1 };

            if (string.IsNullOrWhiteSpace(request?.Id) || string.IsNullOrWhiteSpace(request?.SubId)) {
                response.Message = "参数不完整";
                response.Failed = 1;
                return response;
            }

            var item = _libraryManager.GetItemById(request.Id);
            if (item == null && long.TryParse(request.Id, out var internalId)) {
                item = _libraryManager.GetItemById(internalId);
            }

            if (item == null) {
                response.Message = "未找到媒体条目";
                response.Failed = 1;
                return response;
            }

            item = VersionItemResolver.Resolve(item, request.MediaSourceId);

            var targetItem = item;
            if (request.SeasonNumber.HasValue && request.EpisodeNumber.HasValue) {
                long seriesId = 0;
                if (item is Episode epItem) seriesId = epItem.SeriesId;
                else if (item is Season seasonItem) seriesId = seasonItem.SeriesId;
                else if (item is Series seriesItem) seriesId = seriesItem.InternalId;

                if (seriesId != 0) {
                    var epQuery = new InternalItemsQuery {
                        SeriesIds = new[] { seriesId },
                        Recursive = true,
                        IncludeItemTypes = new[] { nameof(Episode) }
                    };
                    var targetEp = _libraryManager.GetItemsResult(epQuery).Items
                        .OfType<Episode>()
                        .FirstOrDefault(e => e.ParentIndexNumber == request.SeasonNumber && e.IndexNumber == request.EpisodeNumber);
                    if (targetEp != null) {
                        targetItem = targetEp;
                    }
                }
            }

            var mediaDir = targetItem.ContainingFolderPath ?? Path.GetDirectoryName(targetItem.Path);
            if (string.IsNullOrWhiteSpace(mediaDir)) {
                response.Message = "无法确定媒体目录";
                response.Failed = 1;
                return response;
            }

            var baseFilename = !string.IsNullOrWhiteSpace(request.Filename)
                ? request.Filename
                : Path.GetFileNameWithoutExtension(targetItem.Path);

            try {
                var result = _subhdService.DownloadAsync(
                        request.SubId,
                        mediaDir,
                        baseFilename,
                        request.SeasonNumber,
                        request.EpisodeNumber,
                        saveBestOnly: true)
                    .GetAwaiter().GetResult();
                response.Succeeded = 1;
                var scanned = _scanExternalFilesHandler.ForceUpdateItems(new[] { targetItem });
                response.Message = scanned > 0
                    ? $"{result} · 已刷新外挂"
                    : $"{result} · 外挂未刷新（可手动刷新外挂文件）";
            } catch (Exception ex) {
                Plugin.Instance.Logger.Error($"SubHD download error: {ex.Message}");
                response.Failed = 1;
                response.Message = ex.Message;
            }

            return response;
        }

        public MediaInfoMenuResponse Post(DownloadSubhdBatchRequest request) {
            var response = new MediaInfoMenuResponse();

            if (string.IsNullOrWhiteSpace(request?.Id)) {
                response.Message = "参数不完整";
                response.Failed = 1;
                return response;
            }

            var item = _libraryManager.GetItemById(request.Id);
            if (item == null && long.TryParse(request.Id, out var internalId)) {
                item = _libraryManager.GetItemById(internalId);
            }

            if (item == null) {
                response.Message = "未找到媒体条目";
                response.Failed = 1;
                return response;
            }

            var localEpisodes = GetLocalEpisodes(item);
            if (request.SelectedSeasons != null && request.SelectedSeasons.Length > 0) {
                var selected = new HashSet<int>(request.SelectedSeasons);
                localEpisodes = localEpisodes
                    .Where(e => selected.Contains(e.ParentIndexNumber ?? 0))
                    .ToList();
            }
            if (request.SkipExisting) {
                localEpisodes = localEpisodes
                    .Where(e => !SubhdService.HasExternalSubtitle(e.Path))
                    .ToList();
            }
            if (localEpisodes.Count == 0) {
                response.Message = request.SkipExisting
                    ? "所选季度没有缺少字幕的单集"
                    : "库内没有可下载字幕的单集";
                response.Failed = 1;
                return response;
            }

            var hinted = new Dictionary<(int Season, int Episode), string>();
            var hintCount = Math.Min(request.SubIds?.Length ?? 0, request.EpisodeNumbers?.Length ?? 0);
            if (request.SeasonNumbers != null) {
                hintCount = Math.Min(hintCount, request.SeasonNumbers.Length);
            }
            for (var i = 0; i < hintCount; i++) {
                var epNum = request.EpisodeNumbers[i];
                var seasonNum = request.SeasonNumbers != null && i < request.SeasonNumbers.Length
                    ? request.SeasonNumbers[i]
                    : 0;
                var subId = request.SubIds[i];
                if (epNum <= 0 || string.IsNullOrWhiteSpace(subId)) continue;
                hinted[(seasonNum, epNum)] = subId;
            }

            response.Total = localEpisodes.Count;
            var ok = 0;
            var fail = 0;
            var errors = new List<string>();
            var downloadedItems = new List<BaseItem>();

            foreach (var targetEp in localEpisodes) {
                var epNum = targetEp.IndexNumber.Value;
                var seasonNum = targetEp.ParentIndexNumber ?? 0;
                if (!hinted.TryGetValue((seasonNum, epNum), out var subId) &&
                    (seasonNum <= 0 || !hinted.TryGetValue((0, epNum), out subId))) {
                    subId = SearchBestSubId(item, seasonNum, epNum);
                }

                if (string.IsNullOrWhiteSpace(subId)) {
                    fail++;
                    errors.Add($"S{seasonNum:D2}E{epNum:D2}: 未找到字幕");
                    continue;
                }

                var mediaDir = targetEp.ContainingFolderPath ?? Path.GetDirectoryName(targetEp.Path);
                if (string.IsNullOrWhiteSpace(mediaDir)) {
                    fail++;
                    errors.Add($"S{seasonNum:D2}E{epNum:D2}: 无法确定媒体目录");
                    continue;
                }

                var baseFilename = Path.GetFileNameWithoutExtension(targetEp.Path);
                try {
                    _subhdService.DownloadAsync(
                            subId,
                            mediaDir,
                            baseFilename,
                            seasonNum > 0 ? seasonNum : null,
                            epNum,
                            saveBestOnly: true)
                        .GetAwaiter().GetResult();
                    ok++;
                    downloadedItems.Add(targetEp);
                } catch (Exception ex) {
                    fail++;
                    errors.Add($"S{seasonNum:D2}E{epNum:D2}: {ex.Message}");
                    Plugin.Instance.Logger.Error($"[SubHD批量] S{seasonNum:D2}E{epNum:D2} 失败: {ex.Message}");
                }
            }

            response.Succeeded = ok;
            response.Failed = fail;
            response.Processed = ok + fail;
            response.Message = fail == 0
                ? $"批量下载完成：{ok} 集"
                : $"批量下载：成功 {ok}，失败 {fail}";
            if (errors.Count > 0) {
                response.Message += " · " + string.Join(" · ", errors.Take(3));
                if (errors.Count > 3) {
                    response.Message += $" · 另有 {errors.Count - 3} 条失败";
                }
            }

            if (downloadedItems.Count > 0) {
                var scanned = _scanExternalFilesHandler.ForceUpdateItems(downloadedItems);
                response.Message += scanned > 0
                    ? $" · 已刷新外挂 {scanned} 集"
                    : " · 外挂未刷新（可手动刷新外挂文件）";
            }

            return response;
        }

        private List<Episode> GetLocalEpisodes(BaseItem item) {
            var seriesId = GetSeriesId(item);
            if (seriesId == 0) return new List<Episode>();

            var query = new InternalItemsQuery {
                SeriesIds = new[] { seriesId },
                Recursive = true,
                IncludeItemTypes = new[] { nameof(Episode) }
            };
            var all = _libraryManager.GetItemsResult(query).Items.OfType<Episode>().ToList();

            int? seasonFilter = null;
            if (item is Season seasonItem && seasonItem.IndexNumber.GetValueOrDefault() > 0) {
                seasonFilter = seasonItem.IndexNumber;
            } else if (item is Episode episodeItem && episodeItem.ParentIndexNumber.GetValueOrDefault() > 0) {
                seasonFilter = episodeItem.ParentIndexNumber;
            }

            return all
                .Where(e => e.IndexNumber.GetValueOrDefault() > 0)
                .Where(e => seasonFilter == null || (e.ParentIndexNumber ?? 0) == seasonFilter.Value)
                .GroupBy(e => (Season: e.ParentIndexNumber ?? 0, Episode: e.IndexNumber.Value))
                .Select(g => g.First())
                .OrderBy(e => e.ParentIndexNumber ?? 0)
                .ThenBy(e => e.IndexNumber.Value)
                .ToList();
        }

        private static void FillEpisodeInventory(SearchSubhdResponse response, List<Episode> localEpisodes) {
            response.Seasons = localEpisodes
                .GroupBy(e => e.ParentIndexNumber ?? 0)
                .OrderBy(g => g.Key)
                .Select(g => new SubhdSeasonSummary {
                    SeasonNumber = g.Key,
                    EpisodeCount = g.Count(),
                    WithSubtitles = g.Count(e => SubhdService.HasExternalSubtitle(e.Path))
                })
                .ToList();
            response.TotalSeasons = response.Seasons.Count;
            response.TotalEpisodes = localEpisodes.Count;
            response.EpisodesWithSubtitles = response.Seasons.Sum(s => s.WithSubtitles);
        }

        private static long GetSeriesId(BaseItem item) {
            if (item is Episode ep) return ep.SeriesId;
            if (item is Season season) return season.SeriesId;
            if (item is Series series) return series.InternalId;
            return 0;
        }

        private static string GetSeriesName(BaseItem item) {
            if (item is Episode ep) return (ep.SeriesName ?? "").Trim();
            if (item is Season season) return (season.SeriesName ?? "").Trim();
            if (item is Series series) return (series.Name ?? "").Trim();
            return (item?.Name ?? "").Trim();
        }

        private List<string> GetSeriesSearchNames(BaseItem item) {
            var names = new List<string>();
            void Add(string value) {
                if (string.IsNullOrWhiteSpace(value)) return;
                var trimmed = value.Trim();
                if (!names.Contains(trimmed, StringComparer.OrdinalIgnoreCase)) {
                    names.Add(trimmed);
                }
            }

            Add(GetSeriesName(item));
            var seriesId = GetSeriesId(item);
            if (seriesId == 0) return names;

            if (_libraryManager.GetItemById(seriesId) is Series series) {
                Add(series.Name);
                Add(series.OriginalTitle);
                Add(series.SortName);
            }

            return names;
        }

        private static void MergeSearchResults(
            IEnumerable<SubhdSubtitleItem> results,
            List<SubhdSubtitleItem> merged,
            HashSet<string> seen) {
            if (results == null) return;
            foreach (var subtitle in results) {
                if (subtitle == null || string.IsNullOrWhiteSpace(subtitle.SubId)) continue;
                if (seen.Add(subtitle.SubId)) merged.Add(subtitle);
            }
        }

        private void FillMissingEpisodeSubtitles(
            BaseItem contextItem,
            List<Episode> localEpisodes,
            List<SubhdSubtitleItem> merged,
            HashSet<string> seen) {
            if (contextItem == null || localEpisodes == null || localEpisodes.Count == 0) return;

            var covered = new HashSet<(int Season, int Episode)>();
            foreach (var subtitle in merged) {
                var parsed = SubhdService.TryParseSeasonEpisode(subtitle?.Title);
                if (parsed.HasValue) covered.Add(parsed.Value);
            }

            foreach (var episode in localEpisodes) {
                var key = (Season: episode.ParentIndexNumber ?? 1, Episode: episode.IndexNumber.Value);
                if (covered.Contains(key)) continue;

                var best = SearchBestSubtitle(contextItem, key.Season, key.Episode);
                if (best == null || string.IsNullOrWhiteSpace(best.SubId)) continue;
                if (seen.Add(best.SubId)) merged.Add(best);
                covered.Add(key);
            }
        }

        private string SearchBestSubId(BaseItem contextItem, int seasonNumber, int episodeNumber) {
            return SearchBestSubtitle(contextItem, seasonNumber, episodeNumber)?.SubId;
        }

        private SubhdSubtitleItem SearchBestSubtitle(BaseItem contextItem, int seasonNumber, int episodeNumber) {
            if (contextItem == null || episodeNumber <= 0) return null;

            var season = seasonNumber > 0 ? seasonNumber : 1;
            foreach (var seriesName in GetSeriesSearchNames(contextItem)) {
                var queries = new[] {
                    $"{seriesName} S{season:D2}E{episodeNumber:D2}",
                    $"{seriesName} S{season:D2}"
                };

                foreach (var query in queries) {
                    List<SubhdSubtitleItem> results;
                    try {
                        results = _subhdService.SearchAsync(query).GetAwaiter().GetResult();
                    } catch (Exception ex) {
                        Plugin.Instance.Logger.Error($"SubHD fallback search failed query={query}: {ex.Message}");
                        continue;
                    }

                    var best = PickBestSubtitleForEpisode(results, season, episodeNumber);
                    if (best != null) return best;

                    if (query.IndexOf($"E{episodeNumber:D2}", StringComparison.OrdinalIgnoreCase) >= 0) {
                        best = results?
                            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.SubId))
                            .OrderByDescending(s => s.Downloads)
                            .FirstOrDefault();
                        if (best != null) return best;
                    }
                }
            }

            return null;
        }

        private static SubhdSubtitleItem PickBestSubtitleForEpisode(
            IEnumerable<SubhdSubtitleItem> results,
            int seasonNumber,
            int episodeNumber) {
            if (results == null) return null;

            return results
                .Where(s => s != null && !string.IsNullOrWhiteSpace(s.SubId))
                .Select(s => (Item: s, Parsed: SubhdService.TryParseSeasonEpisode(s.Title)))
                .Where(x => x.Parsed.HasValue &&
                            x.Parsed.Value.episode == episodeNumber &&
                            (seasonNumber <= 0 || x.Parsed.Value.season == seasonNumber))
                .OrderByDescending(x => x.Item.Downloads)
                .Select(x => x.Item)
                .FirstOrDefault();
        }

        private static int CountCoveredLocalEpisodes(List<SubhdSubtitleItem> subtitles, List<Episode> localEpisodes) {
            if (subtitles == null || localEpisodes == null || localEpisodes.Count == 0) return 0;

            var covered = new HashSet<(int Season, int Episode)>();
            foreach (var subtitle in subtitles) {
                var parsed = SubhdService.TryParseSeasonEpisode(subtitle?.Title);
                if (parsed.HasValue) covered.Add(parsed.Value);
            }

            return localEpisodes.Count(e => covered.Contains((e.ParentIndexNumber ?? 0, e.IndexNumber.Value)) ||
                                            covered.Contains((e.ParentIndexNumber ?? 1, e.IndexNumber.Value)));
        }

        private static string BuildSearchQuery(BaseItem item) {
            var name = item.Name?.Trim();
            if (string.IsNullOrWhiteSpace(name)) return "";

            var year = item.ProductionYear > 0 ? item.ProductionYear.ToString() : "";

            if (item is Episode ep) {
                var seriesName = ep.SeriesName ?? "";
                var season = ep.ParentIndexNumber > 0 ? $"S{ep.ParentIndexNumber:D2}" : "";
                if (!string.IsNullOrWhiteSpace(seriesName)) {
                    return $"{seriesName} {season}".Trim();
                }
            }

            if (item is Season seasonItem) {
                var seriesName = seasonItem.SeriesName ?? "";
                var seasonNum = seasonItem.IndexNumber > 0 ? $"S{seasonItem.IndexNumber:D2}" : "";
                if (!string.IsNullOrWhiteSpace(seriesName)) {
                    return $"{seriesName} {seasonNum}".Trim();
                }
            }

            if (item is Series) {
                return name;
            }

            return $"{name} {year}".Trim();
        }

        private static List<string> BuildSearchQueryCandidates(BaseItem item, string primaryQuery) {
            var candidates = new List<string>();
            void Add(string q) {
                if (string.IsNullOrWhiteSpace(q)) return;
                if (!candidates.Contains(q, StringComparer.OrdinalIgnoreCase)) candidates.Add(q);
            }

            Add(primaryQuery);
            var itemName = item?.Name?.Trim() ?? "";

            if (item is Episode ep) {
                var series = (ep.SeriesName ?? "").Trim();
                var seasonNumber = ep.ParentIndexNumber > 0 ? ep.ParentIndexNumber : 1;
                if (!string.IsNullOrWhiteSpace(series)) {
                    Add($"{series} S{seasonNumber:D2}");
                    Add(series);
                }
            } else if (item is Season season) {
                var series = (season.SeriesName ?? "").Trim();
                var seasonNumber = season.IndexNumber > 0 ? season.IndexNumber : 1;
                if (!string.IsNullOrWhiteSpace(series)) {
                    Add($"{series} S{seasonNumber:D2}");
                    Add(series);
                }
            } else if (item is Series seriesItem) {
                Add(seriesItem.Name?.Trim());
            } else {
                Add(itemName);
            }

            return candidates;
        }

        private static string BuildDisplayItemName(BaseItem item) {
            if (item is Episode ep) {
                var seriesName = (ep.SeriesName ?? "").Trim();
                var episodeName = (ep.Name ?? "").Trim();
                var season = ep.ParentIndexNumber.HasValue && ep.ParentIndexNumber.Value > 0
                    ? ep.ParentIndexNumber.Value.ToString("D2")
                    : "01";
                var episode = ep.IndexNumber.HasValue ? ep.IndexNumber.Value.ToString("D2") : "--";
                return string.IsNullOrWhiteSpace(seriesName)
                    ? $"{episodeName} S{season}E{episode}".Trim()
                    : $"{seriesName} S{season}E{episode} {episodeName}".Trim();
            }

            if (item is Season seasonItem) {
                var seriesName = (seasonItem.SeriesName ?? "").Trim();
                var season = seasonItem.IndexNumber.HasValue && seasonItem.IndexNumber.Value > 0
                    ? seasonItem.IndexNumber.Value.ToString("D2")
                    : "01";
                return string.IsNullOrWhiteSpace(seriesName) ? $"Season {season}" : $"{seriesName} S{season}";
            }

            return item?.Name ?? "";
        }

        public DebugMediaInfoResponse Get(DebugMediaInfoRequest request) {
            if (request == null || request.InternalId <= 0)
                return new DebugMediaInfoResponse {
                    Found = false,
                    Message = "invalid internalId"
                };

            var item = _libraryManager.GetItemById(request.InternalId);
            if (item == null)
                return new DebugMediaInfoResponse {
                    Found = false,
                    Message = "item not found"
                };

            var mediaInfoPath = MediaInfoDocument.GetMediaInfoJsonPath(item);
            var streams = item.GetMediaStreams().ToList();
            var primaryMediaSource = Plugin.MediaInfoService
                .GetStaticMediaSources(item, false)
                .FirstOrDefault();
            var directoryService = new DirectoryService(Plugin.SharedLogger, Plugin.FileSystem);
            var primaryImage = BuildPrimaryImageInfo(item);
            var chapterImages = BuildChapterImagesInfo(item);
            var thumbnailSets = BuildThumbnailSetsInfo(item, directoryService);

            return new DebugMediaInfoResponse {
                Found = true,
                Message = "ok",
                Item = new DebugItemInfo {
                    InternalId = item.InternalId,
                    Type = item.GetType().Name,
                    Name = item.Name,
                    Path = item.Path,
                    FileName = item.FileName,
                    ContainingFolderPath = item.ContainingFolderPath,
                    ItemId = item.Id.ToString(),
                    ParentId = item.ParentId,
                    ImageDisplayParentId = item.ImageDisplayParentId,
                    IsShortcut = item.IsShortcut,
                    IsRemote = primaryMediaSource?.IsRemote,
                    ExtraType = item.ExtraType?.ToString(),
                    HasMediaInfo = Plugin.MediaInfoService.HasMediaInfo(item),
                    HasCover = Plugin.LibraryService?.HasCover(item) == true,
                    HasPrimaryImage = item.HasImage(ImageType.Primary),
                    IsRefreshedRecently = Plugin.LibraryService?.IsItemRefreshedRecently(item) == true,
                    MediaStreamCount = streams.Count,
                    AudioStreamCount = streams.Count(i => i.Type == MediaStreamType.Audio),
                    VideoStreamCount = streams.Count(i => i.Type == MediaStreamType.Video),
                    SubtitleStreamCount = streams.Count(i => i.Type == MediaStreamType.Subtitle),
                    RunTimeTicks = item.RunTimeTicks,
                    Size = item.Size,
                    Container = item.Container,
                    Width = item.Width,
                    Height = item.Height,
                    DateCreated = item.DateCreated == default
                        ? null
                        : ConfiguredDateTime.ToConfiguredOffset(item.DateCreated).ToString("O"),
                    DateModified = item.DateModified == default
                        ? null
                        : ConfiguredDateTime.ToConfiguredOffset(item.DateModified).ToString("O"),
                    DateLastRefreshed = item.DateLastRefreshed == default
                        ? null
                        : ConfiguredDateTime.ToConfiguredOffset(item.DateLastRefreshed).ToString("O"),
                    PremiereDate = item.PremiereDate.HasValue
                        ? ConfiguredDateTime.ToConfiguredOffset(item.PremiereDate.Value).ToString("O")
                        : null,
                    ProductionYear = item.ProductionYear,
                    OfficialRating = item.OfficialRating,
                    SupportsThumbnails = item is Video itemVideo ? itemVideo.SupportsThumbnails : null
                },
                MediaInfoJson = new DebugFileInfo {
                    Path = mediaInfoPath,
                    Exists = File.Exists(mediaInfoPath),
                    Content = ReadJsonFile<List<MediaInfoDocument>>(mediaInfoPath)
                },
                PrimaryImage = primaryImage,
                ChapterImages = chapterImages,
                ThumbnailSets = thumbnailSets
            };
        }

        private DebugPrimaryImageInfo BuildPrimaryImageInfo(BaseItem item) {
            var primaryImage = item.GetImageInfo(ImageType.Primary, 0);
            var displayParentId = item.ImageDisplayParentId;
            var displayParent = displayParentId == 0 || displayParentId == item.InternalId
                ? null
                : _libraryManager.GetItemById(displayParentId);
            var displayParentPrimaryImage = displayParent?.GetImageInfo(ImageType.Primary, 0);

            return new DebugPrimaryImageInfo {
                HasPrimaryImage = item.HasImage(ImageType.Primary),
                PrimaryImagePath = primaryImage?.Path,
                PrimaryImagePathExists = FileExists(primaryImage?.Path),
                ImageDisplayParentId = displayParentId,
                HasDisplayParentPrimaryImage = displayParent?.HasImage(ImageType.Primary) == true,
                DisplayParentPrimaryImagePath = displayParentPrimaryImage?.Path,
                DisplayParentPrimaryImagePathExists = FileExists(displayParentPrimaryImage?.Path)
            };
        }

        private DebugChapterImagesInfo BuildChapterImagesInfo(BaseItem item) {
            var chapters = _itemRepository.GetChapters(item) ?? new List<ChapterInfo>();
            var entries = chapters
                .Select(chapter => new DebugChapterImageEntry {
                    Name = chapter.Name,
                    MarkerType = chapter.MarkerType.ToString(),
                    StartPositionTicks = chapter.StartPositionTicks,
                    ImagePath = chapter.ImagePath,
                    ImagePathExists = FileExists(chapter.ImagePath),
                    ImageTag = chapter.ImageTag,
                    ImageDateModified = chapter.ImageDateModified == default
                        ? null
                        : ConfiguredDateTime.ToConfiguredOffset(chapter.ImageDateModified).ToString("O")
                })
                .ToArray();

            return new DebugChapterImagesInfo {
                ChapterCount = chapters.Count,
                ChaptersWithImagePath = entries.Count(i => !string.IsNullOrWhiteSpace(i.ImagePath)),
                ExistingImageFiles = entries.Count(i => i.ImagePathExists),
                Entries = entries
            };
        }

        private DebugThumbnailSetsInfo BuildThumbnailSetsInfo(BaseItem item, IDirectoryService directoryService) {
            if (item is not Video video)
                return new DebugThumbnailSetsInfo {
                    SupportsThumbnails = false,
                    Count = 0,
                    Entries = Array.Empty<DebugThumbnailSetEntry>()
                };

            var thumbnailSets = Video.GetThumbnailSetInfos(
                                    video.Path,
                                    video.Id,
                                    directoryService,
                                    0,
                                    false)
                                ?? Array.Empty<ThumbnailSetInfo>();

            return new DebugThumbnailSetsInfo {
                SupportsThumbnails = video.SupportsThumbnails,
                Count = thumbnailSets.Length,
                Entries = thumbnailSets
                    .Select(set => new DebugThumbnailSetEntry {
                        Path = set.Path,
                        Exists = DirectoryExists(set.Path) || FileExists(set.Path),
                        IsDirectory = DirectoryExists(set.Path),
                        Width = set.Width,
                        IntervalSeconds = set.IntervalSeconds
                    })
                    .ToArray()
            };
        }

        private static bool FileExists(string path) {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }

        private static bool DirectoryExists(string path) {
            return !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
        }

        private T ReadJsonFile<T>(string path) where T : class {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            try {
                return _jsonSerializer.DeserializeFromFile<T>(path);
            }
            catch (Exception) {
                return null;
            }
        }
    }
}
