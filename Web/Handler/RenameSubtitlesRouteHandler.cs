using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using MediaBrowser.Controller.Entities;

namespace MediaInfoKeeper.Web.Handler {
    internal sealed class RenameSubtitlesRouteHandler {
        private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase) {
            ".ass", ".ssa", ".srt", ".sup", ".vtt", ".sub", ".smi", ".pgs", ".ttml", ".dfxp", ".idx"
        };

        private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase) {
            ".strm", ".mkv", ".mp4", ".avi", ".mov", ".ts", ".m2ts", ".wmv", ".flv", ".webm", ".iso"
        };

        private static readonly Regex SeasonEpisodePattern =
            new(@"[Ss](\d{1,4})[Ee](\d{1,4})", RegexOptions.Compiled);

        private static readonly Regex EpisodeOnlyPattern =
            new(@"[Ee][Pp]?\s*(\d{1,4})", RegexOptions.Compiled);

        private static readonly Regex AltSeasonEpisodePattern =
            new(@"(\d{1,2})x(\d{1,4})", RegexOptions.Compiled);

        private static readonly Regex ChineseSeasonEpisodePattern =
            new(@"第\s*(\d{1,4})\s*季\s*第\s*(\d{1,4})\s*集", RegexOptions.Compiled);

        private static readonly Regex ChineseEpisodePattern =
            new(@"第\s*(\d{1,4})\s*集", RegexOptions.Compiled);

        private static readonly Regex StandaloneNumberPattern =
            new(@"(?:^|[\s._-])(0?[1-9]\d?)(?:[\s._-]|$)", RegexOptions.Compiled);

        private readonly Func<IEnumerable<string>, List<BaseItem>> _expandToTargetItems;

        public RenameSubtitlesRouteHandler(Func<IEnumerable<string>, List<BaseItem>> expandToTargetItems) {
            _expandToTargetItems = expandToTargetItems;
        }

        public MediaInfoMenuResponse Handle(RenameSubtitlesRequest request) {
            var response = new MediaInfoMenuResponse();
            var logger = Plugin.Instance.Logger;

            if (request?.Ids == null || request.Ids.Length == 0) {
                response.Message = "未选择条目";
                return response;
            }

            var targetItems = VersionItemResolver.ResolveTargetVideos(
                request.Ids,
                request.MediaSourceId,
                _expandToTargetItems);
            response.Total = targetItems.Count;

            if (targetItems.Count == 0) {
                response.Message = "没有可处理的视频条目";
                return response;
            }

            var processedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var renamedItems = new List<BaseItem>();
            var totalRenamed = 0;

            foreach (var item in targetItems) {
                response.Processed++;
                try {
                    var folder = item.ContainingFolderPath ?? Path.GetDirectoryName(item.Path);
                    if (string.IsNullOrWhiteSpace(folder) || !processedDirs.Add(folder)) continue;

                    var result = RenameMismatchedSubtitles(folder);
                    totalRenamed += result.Renamed;
                    response.Succeeded += result.Renamed;
                    response.Failed += result.Failed;
                    if (result.Renamed > 0) renamedItems.Add(item);
                }
                catch (Exception ex) {
                    response.Failed++;
                    logger?.Error($"字幕重命名失败: {item.Path ?? item.Name} - {ex.Message}");
                }
            }

            if (totalRenamed > 0) {
                var scanned = ForceRefreshExternalFiles(renamedItems.Count > 0 ? renamedItems : targetItems);
                response.Message = scanned > 0
                    ? $"已重命名 {totalRenamed} 个字幕文件 · 已刷新外挂"
                    : $"已重命名 {totalRenamed} 个字幕文件 · 外挂未刷新";
            }
            else {
                response.Message = "未发现需要重命名的字幕文件";
            }

            logger?.Info(
                $"RenameSubtitles result: total={response.Total}, processed={response.Processed}, succeeded={response.Succeeded}, failed={response.Failed}, message={response.Message}");
            return response;
        }

        private static int ForceRefreshExternalFiles(IEnumerable<BaseItem> items) {
            if (Plugin.ExternalFiles == null || !Plugin.ExternalFiles.IsAvailable) return 0;

            var refreshOptions = Plugin.ExternalFiles.GetRefreshOptions();
            var seen = new HashSet<long>();
            var succeeded = 0;

            foreach (var item in items ?? Enumerable.Empty<BaseItem>()) {
                if (item == null || !seen.Add(item.InternalId)) continue;

                try {
                    Plugin.ExternalFiles
                        .UpdateExternalFiles(item, refreshOptions, true, CancellationToken.None)
                        .GetAwaiter()
                        .GetResult();
                    succeeded++;
                }
                catch (Exception ex) {
                    Plugin.Instance.Logger?.Error($"重命名后刷新外挂失败: {item.Path ?? item.Name}");
                    Plugin.Instance.Logger?.Error(ex.Message);
                }
            }

            return succeeded;
        }

        private static RenameSummary RenameMismatchedSubtitles(string folder) {
            var logger = Plugin.Instance.Logger;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return new RenameSummary(0, 0);

            var videoMap = BuildVideoSeasonEpisodeMap(folder);
            var videoNames = new HashSet<string>(videoMap.Values, StringComparer.OrdinalIgnoreCase);

            var renamed = 0;
            var failed = 0;

            foreach (var file in Directory.GetFiles(folder)) {
                var ext = Path.GetExtension(file);
                if (!SubtitleExtensions.Contains(ext)) continue;

                var fileName = Path.GetFileName(file);
                if (IsAlreadyMatchedToVideo(fileName, videoNames)) continue;

                var targetVideoName = FindMatchingVideo(fileName, videoMap);
                if (targetVideoName == null) {
                    logger?.Warn($"无法匹配字幕到任何视频，跳过: {fileName}");
                    continue;
                }

                var lang = DetectLanguageTag(fileName);
                var newName = string.IsNullOrEmpty(lang)
                    ? targetVideoName + ext
                    : $"{targetVideoName}.{lang}{ext}";
                var newPath = Path.Combine(folder, newName);

                if (string.Equals(file, newPath, StringComparison.OrdinalIgnoreCase)) continue;

                if (File.Exists(newPath)) {
                    logger?.Warn($"目标文件已存在，跳过: {fileName} -> {newName}");
                    continue;
                }

                try {
                    File.Move(file, newPath);
                    logger?.Info($"字幕已重命名: {fileName} -> {newName}");
                    renamed++;
                }
                catch (Exception ex) {
                    logger?.Error($"重命名失败: {fileName} -> {newName}: {ex.Message}");
                    failed++;
                }
            }

            return new RenameSummary(renamed, failed);
        }

        private static bool IsAlreadyMatchedToVideo(string fileName, HashSet<string> videoNames) {
            foreach (var videoName in videoNames) {
                if (fileName.Length < videoName.Length) continue;
                if (!fileName.StartsWith(videoName, StringComparison.OrdinalIgnoreCase)) continue;
                if (fileName.Length == videoName.Length) return true;
                if (fileName[videoName.Length] == '.') return true;
            }

            return false;
        }

        private static Dictionary<string, string> BuildVideoSeasonEpisodeMap(string folder) {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var file in Directory.GetFiles(folder)) {
                var ext = Path.GetExtension(file);
                if (!VideoExtensions.Contains(ext)) continue;

                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name)) continue;

                var seMatch = SeasonEpisodePattern.Match(name);
                if (seMatch.Success) {
                    var s = int.Parse(seMatch.Groups[1].Value);
                    var e = int.Parse(seMatch.Groups[2].Value);
                    map[$"S{s:D2}E{e:D2}"] = name;
                }
                else {
                    map[name] = name;
                }
            }

            return map;
        }

        private static string FindMatchingVideo(string subtitleFileName, Dictionary<string, string> videoMap) {
            if (videoMap.Count == 0) return null;

            var name = Path.GetFileNameWithoutExtension(subtitleFileName);
            if (string.IsNullOrWhiteSpace(name)) return null;

            var seMatch = SeasonEpisodePattern.Match(name);
            if (seMatch.Success) {
                var key = $"S{int.Parse(seMatch.Groups[1].Value):D2}E{int.Parse(seMatch.Groups[2].Value):D2}";
                if (videoMap.TryGetValue(key, out var vn)) return vn;
            }

            var altMatch = AltSeasonEpisodePattern.Match(name);
            if (altMatch.Success) {
                var key = $"S{int.Parse(altMatch.Groups[1].Value):D2}E{int.Parse(altMatch.Groups[2].Value):D2}";
                if (videoMap.TryGetValue(key, out var vn)) return vn;
            }

            var cnSeMatch = ChineseSeasonEpisodePattern.Match(name);
            if (cnSeMatch.Success) {
                var key = $"S{int.Parse(cnSeMatch.Groups[1].Value):D2}E{int.Parse(cnSeMatch.Groups[2].Value):D2}";
                if (videoMap.TryGetValue(key, out var vn)) return vn;
            }

            var episodeCandidates = new List<int>();
            var epMatch = EpisodeOnlyPattern.Match(name);
            if (epMatch.Success) episodeCandidates.Add(int.Parse(epMatch.Groups[1].Value));

            var chMatch = ChineseEpisodePattern.Match(name);
            if (chMatch.Success) episodeCandidates.Add(int.Parse(chMatch.Groups[1].Value));

            var numMatch = StandaloneNumberPattern.Match(name);
            if (numMatch.Success) {
                var num = int.Parse(numMatch.Groups[1].Value);
                if (num <= 99) episodeCandidates.Add(num);
            }

            foreach (var episode in episodeCandidates.Distinct()) {
                var matches = FindVideosByEpisode(videoMap, episode);
                if (matches.Count == 1) return matches[0];
            }

            var standalone = videoMap
                .Where(kvp => !SeasonEpisodePattern.IsMatch(kvp.Key))
                .Select(kvp => kvp.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (standalone.Count == 1) return standalone[0];

            return null;
        }

        private static List<string> FindVideosByEpisode(Dictionary<string, string> videoMap, int episode) {
            return videoMap
                .Where(kvp => {
                    var se = SeasonEpisodePattern.Match(kvp.Key);
                    if (se.Success) return int.Parse(se.Groups[2].Value) == episode;

                    var ep = EpisodeOnlyPattern.Match(kvp.Key);
                    return ep.Success && int.Parse(ep.Groups[1].Value) == episode;
                })
                .Select(kvp => kvp.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string DetectLanguageTag(string fileName) {
            if (string.IsNullOrWhiteSpace(fileName)) return "zh";

            var name = fileName.ToLowerInvariant();

            if (ContainsAny(name, "chs&en", "gb&en", "简英", "中英", "双语", "简繁", "中英双字", "简英双语"))
                return "zh&en";
            if (ContainsAny(name, "cht&en", "繁英", "繁英双语", "big5&en"))
                return "zh-Hant&en";
            if (ContainsAny(name, "cht", "繁体", "繁中", "big5", "zh-hant", "zh_hant", "粤语", "广东话"))
                return "zh-Hant";
            if (ContainsAny(name, "chs", "简体", "简中", "中字", "中文", "zh-cn", "zh_cn", "zh-hans") ||
                HasToken(name, "chi"))
                return "zh";
            if (ContainsAny(name, "jpn", "japanese", "日语", "日文", "日字") || HasToken(name, "ja", "jp"))
                return "ja";
            if (ContainsAny(name, "kor", "korean", "韩语", "韩文", "韩字") || HasToken(name, "ko", "kr"))
                return "ko";
            if (ContainsAny(name, "eng", "english", "英语", "英文", "英字") || HasToken(name, "en"))
                return "en";
            if (ContainsAny(name, "fre", "french", "fra", "法语", "法文", "法字") || HasToken(name, "fr"))
                return "fr";
            if (ContainsAny(name, "spa", "spanish", "西班牙语", "西语") || HasToken(name, "es"))
                return "es";
            if (ContainsAny(name, "ger", "german", "deu", "德语", "德文", "德字") || HasToken(name, "de"))
                return "de";

            return "zh";
        }

        private static bool ContainsAny(string text, params string[] values) {
            foreach (var value in values) {
                if (text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            return false;
        }

        private static bool HasToken(string text, params string[] tokens) {
            foreach (var token in tokens) {
                var pattern = $@"(?:^|[\s._\-\[\(]){Regex.Escape(token)}(?:$|[\s._\-\]\),])";
                if (Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase)) return true;
            }

            return false;
        }

        private readonly record struct RenameSummary(int Renamed, int Failed);
    }
}
