using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using MediaBrowser.Controller.Entities;

namespace MediaInfoKeeper.Web.Handler {
    internal sealed class RenameSubtitlesRouteHandler {
        private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase) {
            ".ass", ".ssa", ".srt", ".sup", ".vtt", ".sub", ".smi", ".pgs", ".ttml", ".dfxp"
        };

        private static readonly Regex SeasonEpisodePattern =
            new(@"[Ss](\d{1,4})[Ee](\d{1,4})", RegexOptions.Compiled);

        private static readonly Regex EpisodeOnlyPattern =
            new(@"[Ee][Pp]?\s*(\d{1,4})", RegexOptions.Compiled);

        private static readonly Regex AltSeasonEpisodePattern =
            new(@"(\d{1,2})x(\d{1,4})", RegexOptions.Compiled);

        private static readonly Regex ChineseEpisodePattern =
            new(@"第\s*(\d{1,4})\s*集", RegexOptions.Compiled);

        private static readonly Regex StandaloneNumberPattern =
            new(@"(?:^|[\s._-])(0?[1-9]\d?)(?:[\s._-]|$)", RegexOptions.Compiled);
        private static readonly Regex ZhHansLanguagePattern =
            new(@"[\[\(\._-](chs|chi|zh[-_ ]?cn|chinese[-_ ]?simplified)[\]\)\._-]|简中|简体|中文|chs|chi|zh[-_ ]?cn|简英|中英",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex ZhHantLanguagePattern =
            new(@"[\[\(\._-](cht|zh[-_ ]?tw|chinese[-_ ]?traditional)[\]\)\._-]|繁中|繁体|cht|zh[-_ ]?tw|粤语|广东话",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex JpnLanguagePattern =
            new(@"[\[\(\._-](jpn|ja|jp|japanese)[\]\)\._-]|日语|日文|日字",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex KorLanguagePattern =
            new(@"[\[\(\._-](kor|ko|kr|korean)[\]\)\._-]|韩语|韩文|韩字",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex EngLanguagePattern =
            new(@"[\[\(\._-](eng|en|english)[\]\)\._-]|英语|英文|英字",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex FreLanguagePattern =
            new(@"[\[\(\._-](fre|fr|french|fra)[\]\)\._-]|法语|法文|法字",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SpaLanguagePattern =
            new(@"[\[\(\._-](spa|es|spanish)[\]\)\._-]|西班牙语|西语",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex GerLanguagePattern =
            new(@"[\[\(\._-](ger|de|german|deu)[\]\)\._-]|德语|德文|德字",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

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

            var targetItems = _expandToTargetItems(request.Ids).OfType<Video>().ToList();
            response.Total = targetItems.Count;

            if (targetItems.Count == 0) {
                response.Message = "没有可处理的视频条目";
                return response;
            }

            var processedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var totalRenamed = 0;
            foreach (var item in targetItems) {
                response.Processed++;
                try {
                    var folder = Path.GetDirectoryName(item.Path);
                    if (string.IsNullOrWhiteSpace(folder) || processedDirs.Contains(folder)) continue;

                    var result = RenameMismatchedSubtitles(folder);
                    totalRenamed += result.Renamed;
                    response.Succeeded += result.Renamed;
                    response.Failed += result.Failed;
                    processedDirs.Add(folder);
                }
                catch (Exception ex) {
                    response.Failed++;
                    logger?.Error(string.Format("字幕重命名失败: {0} - {1}", item.Path ?? item.Name, ex.Message));
                }
            }

            response.Message = totalRenamed > 0
                ? string.Format("已重命名 {0} 个字幕文件", totalRenamed)
                : "未发现需要重命名的字幕文件";
            logger?.Info(string.Format(
                "RenameSubtitles result: total={0}, processed={1}, succeeded={2}, failed={3}, message={4}",
                response.Total, response.Processed, response.Succeeded, response.Failed, response.Message));
            return response;
        }

        private static RenameSummary RenameMismatchedSubtitles(string folder) {
            var logger = Plugin.Instance.Logger;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return new RenameSummary(0, 0);

            var videoMap = BuildVideoSeasonEpisodeMap(folder);
            var videoNames = new HashSet<string>(videoMap.Values, StringComparer.OrdinalIgnoreCase);

            var renamed = 0;
            var failed = 0;
            var files = Directory.GetFiles(folder);
            foreach (var file in files) {
                var ext = Path.GetExtension(file);
                if (!SubtitleExtensions.Contains(ext)) continue;

                var fileName = Path.GetFileName(file);

                var alreadyMatched = false;
                foreach (var vn in videoNames) {
                    if (fileName.StartsWith(vn, StringComparison.OrdinalIgnoreCase)) {
                        alreadyMatched = true;
                        break;
                    }
                }
                if (alreadyMatched) continue;

                var targetVideoName = FindMatchingVideo(fileName, videoMap);
                if (targetVideoName == null) {
                    logger?.Warn(string.Format("无法匹配字幕到任何视频，跳过: {0}", fileName));
                    continue;
                }

                var lang = DetectLanguage(fileName);
                var newName = string.Format("{0}.{1}{2}", targetVideoName, lang, ext);
                var newPath = Path.Combine(folder, newName);

                if (File.Exists(newPath)) {
                    logger?.Warn(string.Format("目标文件已存在，跳过: {0} -> {1}", fileName, newName));
                    continue;
                }

                try {
                    File.Move(file, newPath);
                    logger?.Info(string.Format("字幕已重命名: {0} -> {1}", fileName, newName));
                    renamed++;
                }
                catch (Exception ex) {
                    logger?.Error(string.Format("重命名失败: {0} -> {1}: {2}", fileName, newName, ex.Message));
                    failed++;
                }
            }

            return new RenameSummary(renamed, failed);
        }

        private static Dictionary<string, string> BuildVideoSeasonEpisodeMap(string folder) {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var videoExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
                ".strm", ".mkv", ".mp4", ".avi", ".mov", ".ts", ".m2ts", ".wmv", ".flv", ".webm", ".iso"
            };

            foreach (var file in Directory.GetFiles(folder)) {
                var ext = Path.GetExtension(file);
                if (!videoExts.Contains(ext)) continue;

                var name = Path.GetFileNameWithoutExtension(file);
                if (string.IsNullOrWhiteSpace(name)) continue;

                var seMatch = SeasonEpisodePattern.Match(name);
                if (seMatch.Success) {
                    var s = int.Parse(seMatch.Groups[1].Value);
                    var e = int.Parse(seMatch.Groups[2].Value);
                    var key = string.Format("S{0:D2}E{1:D2}", s, e);
                    map[key] = name;
                } else {
                    map[name] = name;
                }
            }

            return map;
        }

        private static string FindMatchingVideo(string subtitleFileName,
            Dictionary<string, string> videoMap) {
            if (videoMap.Count == 0) return null;

            var name = Path.GetFileNameWithoutExtension(subtitleFileName);
            if (string.IsNullOrWhiteSpace(name)) return null;

            var seMatch = SeasonEpisodePattern.Match(name);
            if (seMatch.Success) {
                var s = int.Parse(seMatch.Groups[1].Value);
                var e = int.Parse(seMatch.Groups[2].Value);
                var key = string.Format("S{0:D2}E{1:D2}", s, e);
                if (videoMap.TryGetValue(key, out var vn)) return vn;
            }

            var altMatch = AltSeasonEpisodePattern.Match(name);
            if (altMatch.Success) {
                var s = int.Parse(altMatch.Groups[1].Value);
                var e = int.Parse(altMatch.Groups[2].Value);
                var key = string.Format("S{0:D2}E{1:D2}", s, e);
                if (videoMap.TryGetValue(key, out var vn)) return vn;
            }

            var epMatch = EpisodeOnlyPattern.Match(name);
            if (epMatch.Success) {
                var ep = int.Parse(epMatch.Groups[1].Value);
                var matches = videoMap
                    .Where(kvp => {
                        var epInKey = EpisodeOnlyPattern.Match(kvp.Key);
                        return epInKey.Success && int.Parse(epInKey.Groups[1].Value) == ep;
                    })
                    .Select(kvp => kvp.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (matches.Count == 1) return matches[0];
            }

            var chMatch = ChineseEpisodePattern.Match(name);
            if (chMatch.Success) {
                var ep = int.Parse(chMatch.Groups[1].Value);
                var matches = videoMap
                    .Where(kvp => {
                        var epInKey = EpisodeOnlyPattern.Match(kvp.Key);
                        return epInKey.Success && int.Parse(epInKey.Groups[1].Value) == ep;
                    })
                    .Select(kvp => kvp.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (matches.Count == 1) return matches[0];
            }

            var numMatch = StandaloneNumberPattern.Match(name);
            if (numMatch.Success) {
                var num = int.Parse(numMatch.Groups[1].Value);
                if (num <= 99) {
                    var matches = videoMap
                        .Where(kvp => {
                            var epInKey = EpisodeOnlyPattern.Match(kvp.Key);
                            return epInKey.Success && int.Parse(epInKey.Groups[1].Value) == num;
                        })
                        .Select(kvp => kvp.Value)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (matches.Count == 1) return matches[0];
                }
            }

            var standalone = videoMap
                .Where(kvp => !SeasonEpisodePattern.IsMatch(kvp.Key))
                .ToList();
            if (standalone.Count == 1) return standalone[0].Value;

            return null;
        }

        private static string DetectLanguage(string fileName) {
            if (string.IsNullOrWhiteSpace(fileName)) return "chi";

            var lower = fileName.ToLowerInvariant();

            if (ZhHansLanguagePattern.IsMatch(lower))
                return "chi";
            if (ZhHantLanguagePattern.IsMatch(lower))
                return "chi";
            if (JpnLanguagePattern.IsMatch(lower))
                return "jpn";
            if (KorLanguagePattern.IsMatch(lower))
                return "kor";
            if (EngLanguagePattern.IsMatch(lower))
                return "eng";
            if (FreLanguagePattern.IsMatch(lower))
                return "fre";
            if (SpaLanguagePattern.IsMatch(lower))
                return "spa";
            if (GerLanguagePattern.IsMatch(lower))
                return "ger";

            return "chi";
        }

        private readonly record struct RenameSummary(int Renamed, int Failed);
    }
}
