using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MediaInfoKeeper.Web.Handler {
    internal sealed class SubhdService {
        private static readonly HttpClient _http = CreateHttpClient();
        private const string BASE_URL = "https://subhd.tv";
        private const string SEARCH_URL = BASE_URL + "/search/{0}";
        private const string PREPARE_DL_URL = BASE_URL + "/api/sub/prepare-download";
        private const string DOWN_API_URL = BASE_URL + "/api/sub/down";
        private static readonly Regex SearchTotalPattern = new(@"共\s*(\d+)\s*条", RegexOptions.Compiled);
        private static readonly Regex SearchBlockSplitPattern =
            new(@"<div class=""bg-white shadow-sm rounded-3 mb-4"">", RegexOptions.Compiled);
        private static readonly Regex SearchSubLinkPattern =
            new(@"href\s*=\s*[""'](/a/([A-Za-z0-9]+))[""'][^>]*>\s*([^<]+)", RegexOptions.Compiled);
        private static readonly Regex SearchAltPattern = new(@"alt=""([^""]+)""", RegexOptions.Compiled);
        private static readonly Regex SearchGroupPattern =
            new(@"<span class=""rounded p-1[^""]*""[^>]*>([^<]+)</span>", RegexOptions.Compiled);
        private static readonly Regex SearchTagPattern =
            new(@"<span class=""p-1 fw-bold"">([^<]+)</span>", RegexOptions.Compiled);
        private static readonly Regex SearchFormatPattern =
            new(@"<span class=""p-1 text-secondary"">([^<]+)</span>", RegexOptions.Compiled);
        private static readonly Regex SearchDownloadsPattern =
            new(@"bi-download[^>]*>[\s\S]*?<span class=""align-text-top me-3"">(\d+)</span>", RegexOptions.Compiled);

        private static HttpClient CreateHttpClient() {
            var handler = new HttpClientHandler {
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                CookieContainer = new CookieContainer()
            };
            var client = new HttpClient(handler);
            client.DefaultRequestHeaders.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9");
            client.DefaultRequestHeaders.Add("Accept",
                "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8");
            client.Timeout = TimeSpan.FromSeconds(30);
            return client;
        }

        static SubhdService() {
            try {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            } catch {
            }
        }

        public async Task<List<SubhdSubtitleItem>> SearchAsync(string query) {
            var results = new List<SubhdSubtitleItem>();
            try {
                var url = string.Format(SEARCH_URL, Uri.EscapeDataString(query));
                var html = await _http.GetStringAsync(url);

                var totalMatch = SearchTotalPattern.Match(html);
                if (totalMatch.Success && int.Parse(totalMatch.Groups[1].Value) == 0) {
                    return results;
                }

                var blocks = SearchBlockSplitPattern.Split(html);
                foreach (var block in blocks) {
                    var item = ParseSearchResultBlock(block);
                    if (item != null) {
                        results.Add(item);
                    }
                }
            } catch (Exception ex) {
                Plugin.Instance.Logger.Error($"SubHD search failed: {ex.Message}");
            }

            results = results.OrderByDescending(s => s.Downloads).ToList();
            return results;
        }

        private SubhdSubtitleItem ParseSearchResultBlock(string block) {
            try {
                var subMatches = SearchSubLinkPattern.Matches(block);
                if (subMatches.Count == 0) return null;

                var subId = subMatches[0].Groups[2].Value;
                var mainTitle = WebUtility.HtmlDecode(subMatches[0].Groups[3].Value.Trim());
                var title = subMatches.Count >= 2
                    ? WebUtility.HtmlDecode(subMatches[1].Groups[3].Value.Trim())
                    : mainTitle;

                var movieName = "";
                var altMatch = SearchAltPattern.Match(block);
                if (altMatch.Success) {
                    movieName = WebUtility.HtmlDecode(altMatch.Groups[1].Value.Trim());
                }

                var group = "";
                var groupMatch = SearchGroupPattern.Match(block);
                if (groupMatch.Success) {
                    group = WebUtility.HtmlDecode(groupMatch.Groups[1].Value.Trim());
                }

                var tags = new List<string>();
                foreach (Match tm in SearchTagPattern.Matches(block)) {
                    var tag = WebUtility.HtmlDecode(tm.Groups[1].Value.Trim());
                    if (!string.IsNullOrWhiteSpace(tag)) tags.Add(tag);
                }

                var format = "";
                var fmtMatch = SearchFormatPattern.Match(block);
                if (fmtMatch.Success) {
                    format = WebUtility.HtmlDecode(fmtMatch.Groups[1].Value.Trim());
                }

                var downloads = 0;
                var dlMatch = SearchDownloadsPattern.Match(block);
                if (dlMatch.Success) int.TryParse(dlMatch.Groups[1].Value, out downloads);

                return new SubhdSubtitleItem {
                    SubId = subId,
                    Title = title,
                    MovieName = movieName,
                    MovieYear = "",
                    Group = group,
                    Uploader = "",
                    Tags = tags,
                    Format = format,
                    Size = "",
                    Downloads = downloads,
                    Rating = 0
                };
            } catch (Exception ex) {
                Plugin.Instance.Logger.Error($"SubHD search entry parse error: {ex.Message}");
                return null;
            }
        }

        public async Task<string> DownloadAsync(string subId, string mediaDirectory, string baseFilename, int? seasonNumber = null, int? episodeNumber = null, bool saveBestOnly = false) {
            string tempDir = null;
            try {
            var subDetailUrl = BASE_URL + "/a/" + subId;
            var downPageUrl = BASE_URL + "/down/" + subId;

            var preparePayload = JsonSerializer.Serialize(new { sid = subId });
            var prepareContent = new StringContent(preparePayload, Encoding.UTF8, "application/json");
            var prepareReq = new HttpRequestMessage(HttpMethod.Post, PREPARE_DL_URL) {
                Content = prepareContent
            };
            prepareReq.Headers.Referrer = new Uri(subDetailUrl);
            var prepareRes = await _http.SendAsync(prepareReq);
            prepareRes.EnsureSuccessStatusCode();
            var prepareJson = await prepareRes.Content.ReadAsStringAsync();
            var prepareData = JsonDocument.Parse(prepareJson).RootElement;
            if (!prepareData.GetProperty("success").GetBoolean()) {
                throw new Exception("SubHD prepare-download failed");
            }

            var downPageReq = new HttpRequestMessage(HttpMethod.Get, downPageUrl);
            downPageReq.Headers.Referrer = new Uri(subDetailUrl);
            var downPageRes = await _http.SendAsync(downPageReq);
            downPageRes.EnsureSuccessStatusCode();

            var downPayload = JsonSerializer.Serialize(new { sid = subId });
            var downContent = new StringContent(downPayload, Encoding.UTF8, "application/json");
            var downReq = new HttpRequestMessage(HttpMethod.Post, DOWN_API_URL) {
                Content = downContent
            };
            downReq.Headers.Referrer = new Uri(downPageUrl);
            var downRes = await _http.SendAsync(downReq);
            downRes.EnsureSuccessStatusCode();
            var downJson = await downRes.Content.ReadAsStringAsync();
            var downData = JsonDocument.Parse(downJson).RootElement;
            if (!downData.GetProperty("success").GetBoolean() || !downData.GetProperty("pass").GetBoolean()) {
                var msg = "";
                if (downData.TryGetProperty("msg", out var msgEl)) msg = msgEl.GetString();
                throw new Exception("SubHD download failed: " + msg);
            }
            var dlUrl = downData.GetProperty("url").GetString();
            if (string.IsNullOrWhiteSpace(dlUrl)) {
                throw new Exception("SubHD download failed: empty download url");
            }

            tempDir = Path.Combine(Path.GetTempPath(), "subhd_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            var extGuess = Path.GetExtension(dlUrl);
            if (string.IsNullOrEmpty(extGuess) || extGuess.Length > 5 || extGuess.Contains("?")) extGuess = ".zip";
            var tempFile = Path.Combine(tempDir, "download" + extGuess);

            var dlBytes = await _http.GetByteArrayAsync(dlUrl);
            await File.WriteAllBytesAsync(tempFile, dlBytes);

            var magic = dlBytes.Length >= 4
                ? string.Join(" ", dlBytes.Take(4).Select(b => b.ToString("X2")))
                : "(空)";
            Plugin.Instance.Logger.Info($"SubHD 下载: url={dlUrl} size={dlBytes.Length} magic={magic}");

            var subtitleFiles = new List<(string path, string langSuffix, string sourceName)>();
            var isZip = dlBytes.Length > 2 && dlBytes[0] == 0x50 && dlBytes[1] == 0x4B;
            var isRar = dlBytes.Length > 3 && dlBytes[0] == 0x52 && dlBytes[1] == 0x61 && dlBytes[2] == 0x72 && dlBytes[3] == 0x21;
            var is7z = dlBytes.Length > 2 && dlBytes[0] == 0x37 && dlBytes[1] == 0x7A;
            var isHtml = dlBytes.Length > 0 && (dlBytes[0] == 0x3C);

            if (isHtml) {
                throw new Exception("下载返回 HTML 页面（可能被 Cloudflare 拦截），非字幕文件");
            }

            if (isZip) {
                Encoding zipEnc = Encoding.UTF8;
                try { zipEnc = Encoding.GetEncoding(936); } catch { }
                var extractDir = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractDir);
                using (var archive = ZipFile.Open(tempFile, ZipArchiveMode.Read, zipEnc)) {
                    var idx = 0;
                    foreach (var entry in archive.Entries) {
                        if (string.IsNullOrEmpty(entry.Name)) continue;
                        var ext = Path.GetExtension(entry.Name).ToLowerInvariant();
                        if (!IsSubtitleExt(ext)) continue;
                        var destPath = Path.Combine(extractDir, "subtitle_" + idx + ext);
                        entry.ExtractToFile(destPath, true);
                        subtitleFiles.Add((destPath, DetectLanguageSuffix(entry.Name), entry.Name));
                        idx++;
                    }
                }
            } else if (isRar || is7z) {
                var extractDir = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractDir);
                using var archiveStream = File.OpenRead(tempFile);
                using (var archive = ArchiveFactory.OpenArchive(archiveStream, new ReaderOptions())) {
                    var idx = 0;
                    foreach (var entry in archive.Entries) {
                        if (entry.IsDirectory) continue;
                        var entryName = entry.Key?.ToString() ?? "";
                        var ext = Path.GetExtension(entryName).ToLowerInvariant();
                        if (!IsSubtitleExt(ext)) continue;
                        var destPath = Path.Combine(extractDir, "subtitle_" + idx + ext);
                        entry.WriteToFile(destPath, new ExtractionOptions { Overwrite = true });
                        subtitleFiles.Add((destPath, DetectLanguageSuffix(entryName), entryName));
                        idx++;
                    }
                }
            } else {
                var ext = Path.GetExtension(tempFile).ToLowerInvariant();
                if (IsSubtitleExt(ext)) {
                    subtitleFiles.Add((tempFile, "", Path.GetFileName(tempFile)));
                }
            }

            if (subtitleFiles.Count == 0) {
                throw new Exception($"未找到字幕文件（magic={magic} size={dlBytes.Length}）");
            }

            if (episodeNumber.HasValue && episodeNumber.Value > 0) {
                subtitleFiles = SelectEpisodeFiles(subtitleFiles, seasonNumber, episodeNumber.Value);
                if (subtitleFiles.Count == 0) {
                    throw new Exception($"压缩包内未找到目标分集字幕 S{(seasonNumber ?? 0):D2}E{episodeNumber.Value:D2}");
                }
            }

            if (saveBestOnly && subtitleFiles.Count > 1) {
                subtitleFiles = new List<(string path, string langSuffix, string sourceName)> {
                    subtitleFiles.OrderByDescending(GetSubtitlePreferenceScore).First()
                };
            }

            var savedFiles = new List<string>();
            var usedSuffixes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (subFile, langSuffix, _) in subtitleFiles) {
                var subExt = Path.GetExtension(subFile).ToLowerInvariant();
                var finalLang = string.IsNullOrEmpty(langSuffix) ? ".zh" : langSuffix;
                var suffixKey = finalLang + subExt;
                if (!usedSuffixes.Add(suffixKey)) continue;

                var targetName = baseFilename + suffixKey;
                var targetPath = Path.Combine(mediaDirectory, targetName);
                if (File.Exists(targetPath) && AreSameSubtitleFile(subFile, targetPath)) {
                    continue;
                }

                File.Copy(subFile, targetPath, overwrite: true);
                savedFiles.Add(targetName);
            }

            return string.Join(", ", savedFiles);
            } finally {
                if (tempDir != null) {
                    try {
                        Directory.Delete(tempDir, true);
                        Plugin.Instance.Logger.Info($"SubHD 已清理临时目录: {tempDir}");
                    } catch (Exception ex) {
                        Plugin.Instance.Logger.Warn($"SubHD 清理临时目录失败: {ex.Message}");
                    }
                }
            }
        }

        private static bool AreSameSubtitleFile(string sourcePath, string targetPath) {
            try {
                var sourceInfo = new FileInfo(sourcePath);
                var targetInfo = new FileInfo(targetPath);
                if (!sourceInfo.Exists || !targetInfo.Exists) return false;
                if (sourceInfo.Length != targetInfo.Length) return false;
                if (sourceInfo.Length == 0) return true;

                using var source = File.OpenRead(sourcePath);
                using var target = File.OpenRead(targetPath);
                var sourceBuffer = new byte[8192];
                var targetBuffer = new byte[8192];
                while (true) {
                    var sourceRead = source.Read(sourceBuffer, 0, sourceBuffer.Length);
                    var targetRead = target.Read(targetBuffer, 0, targetBuffer.Length);
                    if (sourceRead != targetRead) return false;
                    if (sourceRead == 0) return true;
                    for (var i = 0; i < sourceRead; i++) {
                        if (sourceBuffer[i] != targetBuffer[i]) return false;
                    }
                }
            } catch {
                return false;
            }
        }

        private static bool IsSubtitleExt(string ext) {
            ext = (ext ?? "").ToLowerInvariant();
            return ext == ".ass" || ext == ".srt" || ext == ".ssa" || ext == ".sub" ||
                   ext == ".vtt" || ext == ".sup" || ext == ".idx" || ext == ".pgs" ||
                   ext == ".ttml" || ext == ".dfxp" || ext == ".smi" || ext == ".sami";
        }

        private static string DetectLanguageSuffix(string filename) {
            var name = (filename ?? "").ToLowerInvariant();

            if (name.Contains("chs&en") || name.Contains("gb&en") || name.Contains("简英") ||
                name.Contains("中英") || name.Contains("双语") || name.Contains("简繁") ||
                name.Contains("中英双字") || name.Contains("简英双语")) {
                return ".zh&en";
            }
            if (name.Contains("cht&en") || name.Contains("繁英") || name.Contains("繁英双语") ||
                name.Contains("big5&en")) {
                return ".zh-Hant&en";
            }
            if (name.Contains("cht") || name.Contains("繁体") || name.Contains("繁中") ||
                name.Contains("big5") || name.Contains("zh-hant") || name.Contains("zh_hant")) {
                return ".zh-Hant";
            }
            if (name.Contains("chs") || name.Contains("简体") || name.Contains("简中") ||
                name.Contains("中字") || name.Contains("chi")) {
                return ".zh";
            }
            if (name.Contains("eng") || name.Contains("english") || name.Contains("英文")) {
                return ".en";
            }
            return "";
        }

        private static List<(string path, string langSuffix, string sourceName)> SelectEpisodeFiles(
            List<(string path, string langSuffix, string sourceName)> subtitleFiles,
            int? seasonNumber,
            int episodeNumber) {
            var exact = new List<(string path, string langSuffix, string sourceName)>();
            var episodeOnly = new List<(string path, string langSuffix, string sourceName)>();
            var unknown = new List<(string path, string langSuffix, string sourceName)>();

            foreach (var file in subtitleFiles) {
                var parsed = TryParseSeasonEpisode(file.sourceName);
                if (!parsed.HasValue) {
                    unknown.Add(file);
                    continue;
                }

                if (parsed.Value.episode != episodeNumber) {
                    continue;
                }

                if (seasonNumber.HasValue && seasonNumber.Value > 0) {
                    if (parsed.Value.season == seasonNumber.Value) {
                        exact.Add(file);
                    } else {
                        episodeOnly.Add(file);
                    }
                } else {
                    episodeOnly.Add(file);
                }
            }

            if (exact.Count > 0) return exact;
            if (episodeOnly.Count > 0) return episodeOnly;
            return unknown;
        }

        internal static (int season, int episode)? TryParseSeasonEpisode(string text) {
            var t = text ?? "";
            var m = Regex.Match(t, @"S(\d{1,2})\s*E(\d{1,2})", RegexOptions.IgnoreCase);
            if (m.Success) {
                return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            }

            m = Regex.Match(t, @"(\d{1,2})x(\d{1,2})", RegexOptions.IgnoreCase);
            if (m.Success) {
                return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
            }

            return null;
        }

        private static int GetSubtitlePreferenceScore((string path, string langSuffix, string sourceName) item) {
            var lang = item.langSuffix ?? "";
            var ext = Path.GetExtension(item.path).ToLowerInvariant();
            var langScore = lang switch {
                ".zh&en" => 500,
                ".zh-Hant&en" => 450,
                ".zh" => 400,
                ".zh-Hant" => 350,
                ".en" => 200,
                _ => 100
            };
            var extScore = ext switch {
                ".ass" => 30,
                ".ssa" => 20,
                ".srt" => 10,
                _ => 0
            };
            return langScore + extScore;
        }
    }
}
