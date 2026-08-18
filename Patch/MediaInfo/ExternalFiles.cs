using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Persistence;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.MediaInfo;

namespace MediaInfoKeeper.Patch {
    public class ExternalFiles {
        private static readonly HashSet<string> ProbeExtensions = new(StringComparer.OrdinalIgnoreCase) {
            ".sub",
            ".smi",
            ".sami",
            ".mpl"
        };

        private readonly object audioTrackResolver;
        private readonly object ffProbeSubtitleInfo;
        private readonly IFileSystem fileSystem;
        private readonly MethodInfo getExternalTracksMethod;
        private readonly IItemRepository itemRepository;
        private readonly ILibraryManager libraryManager;

        private readonly ILogger logger;
        private readonly object subtitleResolver;
        private readonly MethodInfo updateExternalSubtitleStreamMethod;

        public ExternalFiles(
            ILibraryManager libraryManager,
            IFileSystem fileSystem,
            IMediaProbeManager mediaProbeManager,
            ILocalizationManager localizationManager,
            IItemRepository itemRepository) {
            logger = Plugin.Instance.Logger;
            this.libraryManager = libraryManager;
            this.fileSystem = fileSystem;
            this.itemRepository = itemRepository;

            try {
                var embyProvidersAssembly = Assembly.Load("Emby.Providers");
                var embyProvidersVersion = embyProvidersAssembly.GetName().Version;
                var audioTrackResolverType =
                    embyProvidersAssembly.GetType("Emby.Providers.MediaInfo.AudioTrackResolver");
                var subtitleResolverType = embyProvidersAssembly.GetType("Emby.Providers.MediaInfo.SubtitleResolver");
                var baseTrackResolverType = embyProvidersAssembly.GetType("Emby.Providers.MediaInfo.BaseTrackResolver");
                var ffProbeSubtitleInfoType =
                    embyProvidersAssembly.GetType("Emby.Providers.MediaInfo.FFProbeSubtitleInfo");
                var localizationManagerType = Assembly.Load("MediaBrowser.Model")
                    .GetType("MediaBrowser.Model.Globalization.ILocalizationManager");
                var fileSystemType = Assembly.Load("MediaBrowser.Model")
                    .GetType("MediaBrowser.Model.IO.IFileSystem");
                var libraryManagerType = Assembly.Load("MediaBrowser.Controller")
                    .GetType("MediaBrowser.Controller.Library.ILibraryManager");
                var libraryOptionsType = Assembly.Load("MediaBrowser.Model")
                    .GetType("MediaBrowser.Model.Configuration.LibraryOptions");
                var baseItemType = Assembly.Load("MediaBrowser.Controller")
                    .GetType("MediaBrowser.Controller.Entities.BaseItem");
                var mediaStreamType = Assembly.Load("MediaBrowser.Model")
                    .GetType("MediaBrowser.Model.Entities.MediaStream");
                var metadataRefreshOptionsType = Assembly.Load("MediaBrowser.Controller")
                    .GetType("MediaBrowser.Controller.Providers.MetadataRefreshOptions");
                var directoryServiceType = Assembly.Load("MediaBrowser.Controller")
                    .GetType("MediaBrowser.Controller.Providers.IDirectoryService");
                var namingOptionsType = libraryManager.GetNamingOptions()?.GetType();

                if (audioTrackResolverType == null ||
                    subtitleResolverType == null ||
                    baseTrackResolverType == null ||
                    ffProbeSubtitleInfoType == null ||
                    localizationManagerType == null ||
                    fileSystemType == null ||
                    libraryManagerType == null ||
                    libraryOptionsType == null ||
                    baseItemType == null ||
                    mediaStreamType == null ||
                    metadataRefreshOptionsType == null ||
                    directoryServiceType == null ||
                    namingOptionsType == null) {
                    PatchLog.InitFailed(logger, nameof(ExternalFiles), "关键运行时类型缺失");
                    return;
                }

                audioTrackResolver = Activator.CreateInstance(
                    audioTrackResolverType,
                    localizationManager,
                    fileSystem,
                    libraryManager);
                if (audioTrackResolver == null) {
                    PatchLog.InitFailed(logger, nameof(ExternalFiles), "AudioTrackResolver 初始化失败");
                    return;
                }

                subtitleResolver = Activator.CreateInstance(
                    subtitleResolverType,
                    localizationManager,
                    fileSystem,
                    libraryManager);
                if (subtitleResolver == null) {
                    PatchLog.InitFailed(logger, nameof(ExternalFiles), "SubtitleResolver 初始化失败");
                    return;
                }

                getExternalTracksMethod = PatchMethodResolver.Resolve(
                    baseTrackResolverType,
                    embyProvidersVersion,
                    new MethodSignatureProfile {
                        Name = "BaseTrackResolver.GetExternalTracks",
                        MethodName = "GetExternalTracks",
                        BindingFlags = BindingFlags.Instance | BindingFlags.Public,
                        ParameterTypes = new[] {
                            baseItemType,
                            typeof(int),
                            directoryServiceType,
                            libraryOptionsType,
                            namingOptionsType,
                            typeof(bool)
                        }
                    },
                    logger,
                    nameof(ExternalFiles));

                ffProbeSubtitleInfo = Activator.CreateInstance(ffProbeSubtitleInfoType, mediaProbeManager);
                if (ffProbeSubtitleInfo == null) {
                    PatchLog.InitFailed(logger, nameof(ExternalFiles), "FFProbeSubtitleInfo 初始化失败");
                    return;
                }

                updateExternalSubtitleStreamMethod = PatchMethodResolver.Resolve(
                    ffProbeSubtitleInfoType,
                    embyProvidersVersion,
                    new MethodSignatureProfile {
                        Name = "FFProbeSubtitleInfo.UpdateExternalSubtitleStream",
                        MethodName = "UpdateExternalSubtitleStream",
                        BindingFlags = BindingFlags.Instance | BindingFlags.Public,
                        ParameterTypes = new[] {
                            baseItemType,
                            mediaStreamType,
                            metadataRefreshOptionsType,
                            libraryOptionsType,
                            typeof(CancellationToken)
                        },
                        ReturnType = typeof(Task<bool>)
                    },
                    logger,
                    nameof(ExternalFiles));
            }
            catch (Exception ex) {
                PatchLog.InitFailed(logger, nameof(ExternalFiles), ex.Message);
                logger.Debug(ex.StackTrace);
            }
        }

        public bool IsAvailable =>
            audioTrackResolver != null &&
            subtitleResolver != null &&
            getExternalTracksMethod != null &&
            ffProbeSubtitleInfo != null &&
            updateExternalSubtitleStreamMethod != null;

        public MetadataRefreshOptions GetRefreshOptions() {
            return new MetadataRefreshOptions(new DirectoryService(logger, fileSystem)) {
                EnableRemoteContentProbe = true,
                MetadataRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllMetadata = false,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllImages = false,
                EnableThumbnailImageExtraction = false,
                EnableSubtitleDownloading = false
            };
        }

        public bool HasExternalFilesChanged(BaseItem item, IDirectoryService directoryService, bool clearCache) {
            if (item == null || !IsAvailable) return false;

            try {
                return HasExternalStreamChanged(item, directoryService, clearCache, MediaStreamType.Subtitle) ||
                       HasExternalStreamChanged(item, directoryService, clearCache, MediaStreamType.Audio);
            }
            catch (Exception ex) {
                logger.Warn($"外挂文件变更检测失败: {item.Path ?? item.Name}");
                logger.Warn(ex.Message);
                logger.Debug(ex.StackTrace);
                return false;
            }
        }

        public async Task UpdateExternalFiles(
            BaseItem item,
            MetadataRefreshOptions refreshOptions,
            bool clearCache,
            CancellationToken cancellationToken) {
            if (item == null || !IsAvailable) return;

            var directoryService = refreshOptions.DirectoryService;
            var currentStreams = item.GetMediaStreams()
                .FindAll(stream =>
                    !(stream.IsExternal &&
                      stream.Protocol == MediaProtocol.File &&
                      (stream.Type == MediaStreamType.Subtitle || stream.Type == MediaStreamType.Audio)));
            var nextIndex = currentStreams.Count == 0 ? 0 : currentStreams.Max(stream => stream.Index) + 1;
            var externalSubtitleStreams = GetExternalSubtitleStreams(item, nextIndex, directoryService, clearCache);

            var discoveredPaths = new HashSet<string>(
                externalSubtitleStreams.Select(s => Path.GetFileName(s.Path)),
                StringComparer.OrdinalIgnoreCase);
            var fallbackStreams = DiscoverMissingSubtitleFiles(item, nextIndex + externalSubtitleStreams.Count, discoveredPaths);
            if (fallbackStreams.Count > 0) {
                logger?.Info($"兜底发现 {fallbackStreams.Count} 个字幕文件: {string.Join(", ", fallbackStreams.Select(s => Path.GetFileName(s.Path)))}");
                externalSubtitleStreams.AddRange(fallbackStreams);
            }

            nextIndex = (currentStreams.Count == 0 ? 0 : currentStreams.Max(stream => stream.Index) + 1)
                        + externalSubtitleStreams.Count;
            var externalAudioStreams = GetExternalAudioStreams(item, nextIndex, directoryService, clearCache);

            await UpdateStreams(item, externalSubtitleStreams, refreshOptions, cancellationToken, "字幕")
                .ConfigureAwait(false);
            await UpdateStreams(item, externalAudioStreams, refreshOptions, cancellationToken, "音轨")
                .ConfigureAwait(false);

            currentStreams.AddRange(externalSubtitleStreams);
            currentStreams.AddRange(externalAudioStreams);
            itemRepository.SaveMediaStreams(item.InternalId, currentStreams, cancellationToken);
        }

        private bool HasExternalStreamChanged(
            BaseItem item,
            IDirectoryService directoryService,
            bool clearCache,
            MediaStreamType streamType) {
            var currentSet = new HashSet<string>(
                item.GetMediaStreams()
                    .Where(stream =>
                        stream.IsExternal &&
                        stream.Type == streamType &&
                        !string.IsNullOrWhiteSpace(stream.Path))
                    .Select(stream => NormalizePath(stream.Path)),
                StringComparer.OrdinalIgnoreCase);

            var newSet = new HashSet<string>(
                GetExternalStreams(item, 0, directoryService, clearCache, streamType)
                    .Where(stream => !string.IsNullOrWhiteSpace(stream.Path))
                    .Select(stream => NormalizePath(stream.Path)),
                StringComparer.OrdinalIgnoreCase);

            return !currentSet.SetEquals(newSet);
        }

        private List<MediaStream> GetExternalSubtitleStreams(
            BaseItem item,
            int startIndex,
            IDirectoryService directoryService,
            bool clearCache) {
            return GetExternalStreams(item, startIndex, directoryService, clearCache, MediaStreamType.Subtitle);
        }

        private List<MediaStream> GetExternalAudioStreams(
            BaseItem item,
            int startIndex,
            IDirectoryService directoryService,
            bool clearCache) {
            return GetExternalStreams(item, startIndex, directoryService, clearCache, MediaStreamType.Audio);
        }

        private List<MediaStream> GetExternalStreams(
            BaseItem item,
            int startIndex,
            IDirectoryService directoryService,
            bool clearCache,
            MediaStreamType streamType) {
            if (string.IsNullOrWhiteSpace(item?.Path)) return new List<MediaStream>();

            if (string.IsNullOrWhiteSpace(item.ContainingFolderPath) || !Directory.Exists(item.ContainingFolderPath))
                return new List<MediaStream>();

            var libraryOptions = libraryManager.GetLibraryOptions(item);
            var namingOptions = libraryManager.GetNamingOptions();
            var resolver = streamType == MediaStreamType.Audio ? audioTrackResolver : subtitleResolver;
            var externalStreams = getExternalTracksMethod.Invoke(
                resolver,
                new object[] {
                    item,
                    startIndex,
                    directoryService,
                    libraryOptions,
                    namingOptions,
                    clearCache
                }) as List<MediaStream>;

            if (externalStreams == null) return new List<MediaStream>();

            return externalStreams
                .Where(stream =>
                    stream != null &&
                    stream.Type == streamType &&
                    !string.IsNullOrWhiteSpace(stream.Path))
                .Select(stream => {
                    stream.IsExternal = true;
                    stream.Protocol = MediaProtocol.File;
                    return stream;
                })
                .ToList();
        }

        private async Task UpdateStreams(
            BaseItem item,
            List<MediaStream> streams,
            MetadataRefreshOptions refreshOptions,
            CancellationToken cancellationToken,
            string streamLabel) {
            foreach (var stream in streams) {
                cancellationToken.ThrowIfCancellationRequested();

                var extension = Path.GetExtension(stream.Path);
                if (!string.IsNullOrWhiteSpace(extension) &&
                    (stream.Type == MediaStreamType.Audio || ProbeExtensions.Contains(extension))) {
                    bool updated;
                    using (FfProcessGuard.Allow()) {
                        updated = await UpdateExternalSubtitleStream(item, stream, refreshOptions, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    if (!updated) logger.Warn($"外挂{streamLabel}探测未返回结果: {stream.Path}");
                }

                logger.Info($"外挂{streamLabel}已处理: {stream.Path}");
            }
        }

        private Task<bool> UpdateExternalSubtitleStream(
            BaseItem item,
            MediaStream subtitleStream,
            MetadataRefreshOptions refreshOptions,
            CancellationToken cancellationToken) {
            var libraryOptions = libraryManager.GetLibraryOptions(item);
            return (Task<bool>)updateExternalSubtitleStreamMethod.Invoke(
                ffProbeSubtitleInfo,
                new object[] {
                    item,
                    subtitleStream,
                    refreshOptions,
                    libraryOptions,
                    cancellationToken
                });
        }

        private static readonly HashSet<string> SubtitleExtensions = new(StringComparer.OrdinalIgnoreCase) {
            ".ass", ".ssa", ".srt", ".sup", ".vtt", ".sub", ".smi", ".pgs", ".idx", ".ttml", ".dfxp", ".xml"
        };

        private static string GetCodecFromExtension(string extension) {
            if (string.IsNullOrWhiteSpace(extension)) return "sub";
            var ext = extension.TrimStart('.').ToLowerInvariant();
            // .sup → pgs (PGS/SUP 格式)
            if (ext == "sup") return "pgs";
            // .sub → subrip (也可能 idx/sub 组合中的 sub，但单独 .sub 常见 subrip)
            if (ext == "sub") return "subrip";
            if (ext == "ttml" || ext == "dfxp") return "ttml";
            return ext;
        }

        private List<MediaStream> DiscoverMissingSubtitleFiles(
            BaseItem item,
            int startIndex,
            HashSet<string> alreadyDiscovered) {
            var result = new List<MediaStream>();
            if (item == null || string.IsNullOrWhiteSpace(item.ContainingFolderPath)) return result;

            try {
                var videoName = Path.GetFileNameWithoutExtension(item.Path);
                if (string.IsNullOrWhiteSpace(videoName)) return result;

                var files = Directory.GetFiles(item.ContainingFolderPath);
                foreach (var file in files) {
                    var fileName = Path.GetFileName(file);
                    if (alreadyDiscovered.Contains(fileName)) continue;

                    var ext = Path.GetExtension(file);
                    if (!SubtitleExtensions.Contains(ext)) continue;

                    // 字幕文件名必须以视频名为前缀（Emby 命名约定）
                    if (!fileName.StartsWith(videoName, StringComparison.OrdinalIgnoreCase)) continue;

                    var codec = GetCodecFromExtension(ext);
                    result.Add(new MediaStream {
                        Type = MediaStreamType.Subtitle,
                        Index = startIndex++,
                        Path = file,
                        Codec = codec,
                        IsExternal = true,
                        Protocol = MediaProtocol.File
                    });
                }
            }
            catch (Exception ex) {
                logger?.Warn($"兜底扫描字幕文件失败: {ex.Message}");
            }

            return result;
        }

        private static string NormalizePath(string path) {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;

            return path.Trim();
        }
    }
}
