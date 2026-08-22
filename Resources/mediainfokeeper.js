define(['connectionManager', 'globalize', 'loading', 'toast', 'confirm'], function (connectionManager, globalize, loading, toast, confirm) {
    const commandSourceState = {
        registered: false
    };

    function getSupportedItems(options) {
        const items = (options && options.items) ? options.items.filter(item => !!item) : [];
        if (!items.length) {
            return [];
        }

        const user = options && options.user;
        const users = options && options.users ? Object.values(options.users) : [];
        const firstUser = users.length ? users[0] : null;
        const hasAdminInfo = (user && user.Policy) || (firstUser && firstUser.Policy);
        const isAdmin = (user && user.Policy && user.Policy.IsAdministrator) ||
            (firstUser && firstUser.Policy && firstUser.Policy.IsAdministrator);

        if (hasAdminInfo && !isAdmin) {
            return [];
        }

        const supportedTypes = {
            Movie: true,
            Episode: true,
            Season: true,
            Series: true,
            Video: true,
            Audio: true,
            MusicAlbum: true,
            MusicArtist: true,
            MusicGenre: true
        };
        if (!items.every(item => supportedTypes[item.Type])) {
            return [];
        }

        return items;
    }

    function getLibraryItems(options) {
        const items = (options && options.items) ? options.items.filter(item => !!item) : [];
        if (!items.length) {
            return [];
        }

        const user = options && options.user;
        const users = options && options.users ? Object.values(options.users) : [];
        const firstUser = users.length ? users[0] : null;
        const hasAdminInfo = (user && user.Policy) || (firstUser && firstUser.Policy);
        const isAdmin = (user && user.Policy && user.Policy.IsAdministrator) ||
            (firstUser && firstUser.Policy && firstUser.Policy.IsAdministrator);

        if (hasAdminInfo && !isAdmin) {
            return [];
        }

        if (!items.every(isLibraryItem)) {
            return [];
        }

        return items;
    }

    function isLibraryItem(item) {
        if (!item || typeof item !== 'object') {
            return false;
        }

        if (Array.isArray(item.Locations) && (item.ItemId || item.Guid || item.Id)) {
            return true;
        }

        if (item.Type === 'CollectionFolder') {
            return true;
        }

        return !!item.CollectionType && !item.Type && (item.ItemId || item.Guid || item.Id);
    }

    function getCommandName() {
        const locale = (globalize.getCurrentLocale() || '').toLowerCase();
        return locale === 'zh-cn'
            ? '提取媒体信息'
            : (['zh-hk', 'zh-tw'].includes(locale) ? '提取媒體信息' : 'Extract MediaInfo');
    }

    function getDeleteCommandName() {
        const locale = (globalize.getCurrentLocale() || '').toLowerCase();
        return locale === 'zh-cn'
            ? '删除媒体信息'
            : (['zh-hk', 'zh-tw'].includes(locale) ? '刪除媒體信息' : 'Delete MediaInfo');
    }

    function getScanIntroCommandName() {
        const locale = (globalize.getCurrentLocale() || '').toLowerCase();
        return locale === 'zh-cn'
            ? '扫描片头'
            : (['zh-hk', 'zh-tw'].includes(locale) ? '掃描片頭' : 'Scan Intro');
    }

    function getScanExternalFilesCommandName() {
        const locale = (globalize.getCurrentLocale() || '').toLowerCase();
        return locale === 'zh-cn'
            ? '扫描外挂文件'
            : (['zh-hk', 'zh-tw'].includes(locale) ? '掃描外掛文件' : 'Scan External Files');
    }

    function getSetIntroCommandName() {
        const locale = (globalize.getCurrentLocale() || '').toLowerCase();
        return locale === 'zh-cn'
            ? '设置片头片尾'
            : (['zh-hk', 'zh-tw'].includes(locale) ? '設置片頭片尾' : 'Set Intro/Credits');
    }

    function getClearIntroCommandName() {
        const locale = (globalize.getCurrentLocale() || '').toLowerCase();
        return locale === 'zh-cn'
            ? '删除片头片尾'
            : (['zh-hk', 'zh-tw'].includes(locale) ? '刪除片頭片尾' : 'Delete Intro/Credits');
    }

    function getCopyLibraryCommandName() {
        const locale = (globalize.getCurrentLocale() || '').toLowerCase();
        return locale === 'zh-cn'
            ? '复制媒体库'
            : (['zh-hk', 'zh-tw'].includes(locale) ? '複製媒體庫' : 'Duplicate Library');
    }

    function getSubhdSearchCommandName() {
        const locale = (globalize.getCurrentLocale() || '').toLowerCase();
        return locale === 'zh-cn'
            ? '搜索外挂字幕'
            : (['zh-hk', 'zh-tw'].includes(locale) ? '搜索外挂字幕' : 'Search SubHD Subtitles');
    }

    function getRenameSubtitlesCommandName() {
        const locale = (globalize.getCurrentLocale() || '').toLowerCase();
        return locale === 'zh-cn'
            ? '字幕外挂命名'
            : (['zh-hk', 'zh-tw'].includes(locale) ? '字幕外挂命名' : 'Rename Subtitles');
    }

    function getResultMessage(result, action) {
        const normalized = normalizeResult(result);
        const isDelete = action === 'delete';
        const isScanIntro = action === 'scan_intro';
        const isScanExternalFiles = action === 'scan_external_files';
        const isSetIntro = action === 'set_intro';
        const isClearIntro = action === 'clear_intro';
        const isRename = action === 'rename_subtitles';
        if (!result) {
            return (isDelete ? getDeleteCommandName() : (isScanIntro ? getScanIntroCommandName() : (isScanExternalFiles ? getScanExternalFilesCommandName() : (isSetIntro ? getSetIntroCommandName() : (isClearIntro ? getClearIntroCommandName() : (isRename ? getRenameSubtitlesCommandName() : getCommandName())))))) + ' finished';
        }

        const locale = (globalize.getCurrentLocale() || '').toLowerCase();
        if (!normalized.hasStats) {
            if (locale === 'zh-cn') {
                return (isDelete ? '删除完成' : (isScanIntro ? '扫描完成' : (isScanExternalFiles ? '扫描完成' : (isSetIntro ? '设置完成' : (isClearIntro ? '清除完成' : (isRename ? '命名完成' : '提取完成')))))) + '（返回体无统计字段，请看日志）';
            }
            if (['zh-hk', 'zh-tw'].includes(locale)) {
                return (isDelete ? '刪除完成' : (isScanIntro ? '掃描完成' : (isScanExternalFiles ? '掃描完成' : (isSetIntro ? '設置完成' : (isClearIntro ? '清除完成' : (isRename ? '命名完成' : '提取完成')))))) + '（返回體無統計字段，請看日誌）';
            }
            return 'Completed (no stats in response, check server logs)';
        }

        if (locale === 'zh-cn') {
            const prefix = isDelete ? '删除完成' : (isScanIntro ? '扫描完成' : (isScanExternalFiles ? '扫描完成' : (isSetIntro ? '设置完成' : (isClearIntro ? '清除完成' : (isRename ? '命名完成' : '提取完成')))));
            return prefix + `：成功 ${normalized.succeeded}，失败 ${normalized.failed}，跳过 ${normalized.skipped}`;
        }

        if (['zh-hk', 'zh-tw'].includes(locale)) {
            const prefix = isDelete ? '刪除完成' : (isScanIntro ? '掃描完成' : (isScanExternalFiles ? '掃描完成' : (isSetIntro ? '設置完成' : (isClearIntro ? '清除完成' : (isRename ? '命名完成' : '提取完成')))));
            return prefix + `：成功 ${normalized.succeeded}，失敗 ${normalized.failed}，跳過 ${normalized.skipped}`;
        }

        return `Completed: success ${normalized.succeeded}, failed ${normalized.failed}, skipped ${normalized.skipped}`;
    }

    function tryParseJson(value) {
        if (typeof value !== 'string') {
            return null;
        }
        const normalized = value.replace(/^\uFEFF/, '').trim();
        if (!normalized) {
            return null;
        }
        try {
            return JSON.parse(normalized);
        } catch (_) {
            return null;
        }
    }

    function extractPayload(result) {
        if (result == null) {
            return {};
        }

        if (Array.isArray(result)) {
            if (!result.length) {
                return {};
            }
            for (const item of result) {
                const extractedItem = extractPayload(item);
                if (extractedItem && (
                    extractedItem.Succeeded != null || extractedItem.succeeded != null ||
                    extractedItem.Failed != null || extractedItem.failed != null ||
                    extractedItem.Skipped != null || extractedItem.skipped != null)) {
                    return extractedItem;
                }
            }
            return extractPayload(result[0]);
        }

        if (typeof result === 'string') {
            return tryParseJson(result) || {};
        }

        if (typeof result !== 'object') {
            return {};
        }

        if (result.Succeeded != null || result.succeeded != null ||
            result.Failed != null || result.failed != null ||
            result.Skipped != null || result.skipped != null) {
            return result;
        }

        const nestedCandidates = [
            result.responseJSON,
            result.data,
            result.response,
            result.result,
            result.body,
            result.content,
            result.Content,
            result.value
        ];

        for (const candidate of nestedCandidates) {
            if (candidate == null) {
                continue;
            }
            const extracted = extractPayload(candidate);
            if (extracted && (
                extracted.Succeeded != null || extracted.succeeded != null ||
                extracted.Failed != null || extracted.failed != null ||
                extracted.Skipped != null || extracted.skipped != null)) {
                return extracted;
            }
        }

        const textCandidates = [result.responseText, result.text, result.Text];
        for (const text of textCandidates) {
            const parsed = tryParseJson(text);
            if (parsed) {
                return extractPayload(parsed);
            }
        }

        return result;
    }

    function normalizeResult(result) {
        const payload = extractPayload(result);
        const succeededRaw = payload.Succeeded ?? payload.succeeded ?? payload.Success ?? payload.success ?? 0;
        const failedRaw = payload.Failed ?? payload.failed ?? payload.Error ?? payload.error ?? 0;
        const skippedRaw = payload.Skipped ?? payload.skipped ?? 0;
        const hasStats =
            payload.Succeeded != null || payload.succeeded != null ||
            payload.Success != null || payload.success != null ||
            payload.Failed != null || payload.failed != null ||
            payload.Skipped != null || payload.skipped != null;
        return {
            succeeded: Number.isFinite(Number(succeededRaw)) ? Number(succeededRaw) : 0,
            failed: Number.isFinite(Number(failedRaw)) ? Number(failedRaw) : 0,
            skipped: Number.isFinite(Number(skippedRaw)) ? Number(skippedRaw) : 0,
            hasStats: hasStats
        };
    }

    function getErrorMessage(action, err) {
        const isDelete = action === 'delete';
        const isScanIntro = action === 'scan_intro';
        const isScanExternalFiles = action === 'scan_external_files';
        const isSetIntro = action === 'set_intro';
        const isClearIntro = action === 'clear_intro';
        const isCopyLibrary = action === 'copy_library';
        const isRename = action === 'rename_subtitles';
        const commandName = isDelete ? getDeleteCommandName() : (isScanIntro ? getScanIntroCommandName() : (isScanExternalFiles ? getScanExternalFilesCommandName() : (isSetIntro ? getSetIntroCommandName() : (isClearIntro ? getClearIntroCommandName() : (isCopyLibrary ? getCopyLibraryCommandName() : (isRename ? getRenameSubtitlesCommandName() : getCommandName()))))));
        const detail = (err && (err.message || err.statusText || err.responseText)) ? ` (${err.message || err.statusText || err.responseText})` : '';
        return commandName + ' failed' + detail;
    }

    function postJson(apiClient, endpoint, body) {
        const url = apiClient.getUrl(endpoint);
        return apiClient.ajax({
            type: 'POST',
            url: url,
            data: JSON.stringify(body || {}),
            contentType: 'application/json',
            dataType: 'json'
        }).then(function (result) {
            return result || {};
        });
    }

    function postJsonAllowEmpty(apiClient, endpoint, body) {
        const url = apiClient.getUrl(endpoint);
        return apiClient.ajax({
            type: 'POST',
            url: url,
            data: JSON.stringify(body || {}),
            contentType: 'application/json'
        }).then(function (result) {
            return result || {};
        });
    }

    function getLocale() {
        return (globalize.getCurrentLocale() || '').toLowerCase();
    }

    function translateCopySuccess(name) {
        const locale = getLocale();
        if (locale === 'zh-cn') {
            return `媒体库已创建：${name}`;
        }

        if (['zh-hk', 'zh-tw'].includes(locale)) {
            return `媒體庫已建立：${name}`;
        }

        return `Library created: ${name}`;
    }

    function translateCopyEmpty() {
        const locale = getLocale();
        if (locale === 'zh-cn') {
            return '请选择一个媒体库';
        }

        if (['zh-hk', 'zh-tw'].includes(locale)) {
            return '請選擇一個媒體庫';
        }

        return 'Please select one library';
    }

    function translateCopyFailedNoOptions() {
        const locale = getLocale();
        if (locale === 'zh-cn') {
            return '未获取到媒体库配置，无法复制';
        }

        if (['zh-hk', 'zh-tw'].includes(locale)) {
            return '未取得媒體庫設定，無法複製';
        }

        return 'Library settings are unavailable';
    }

    function buildDuplicateLibraryName(sourceName) {
        return `${sourceName || 'Library'}_副本`;
    }

    function getLibraryLookupKey(item) {
        if (!item) {
            return '';
        }

        return item.Guid || item.Id || item.ItemId || '';
    }

    function getLibraryInfoQueryId(item) {
        if (!item) {
            return '';
        }

        return item.Guid || item.Id || '';
    }

    function getCommandItemId(item) {
        if (!item) {
            return '';
        }

        if (isLibraryItem(item)) {
            return item.ItemId || item.Id || item.Guid || '';
        }

        return item.Id || item.ItemId || item.Guid || '';
    }

    function isVersionAwareItem(item) {
        return item && (item.Type === 'Movie' || item.Type === 'Video' || item.Type === 'Episode');
    }

    function fetchItemMediaSources(item) {
        var apiClient = connectionManager.currentApiClient();
        var itemId = getCommandItemId(item);
        if (!apiClient || !itemId) {
            return Promise.resolve(item || null);
        }

        var userId = typeof apiClient.getCurrentUserId === 'function' ? apiClient.getCurrentUserId() : null;
        var endpoint = userId ? ('Users/' + userId + '/Items/' + itemId) : ('Items/' + itemId);
        var query = new URLSearchParams({ Fields: 'MediaSources,UserData' }).toString();
        return apiClient.ajax({
            type: 'GET',
            url: apiClient.getUrl(endpoint) + '?' + query,
            dataType: 'json'
        }).catch(function () {
            return item;
        });
    }

    function detectSelectedMediaSourceId(fullItem, sources) {
        if (!sources || sources.length <= 1) {
            return sources && sources[0] ? sources[0].Id : null;
        }

        var userData = fullItem && fullItem.UserData;
        if (userData && userData.LastPlayedMediaSourceId &&
            sources.some(function (s) { return s.Id === userData.LastPlayedMediaSourceId; })) {
            return userData.LastPlayedMediaSourceId;
        }

        var detailRoot = document.querySelector('.detailPageMainContent, .itemDetailPage, .page[type="itemdetail"]');
        if (detailRoot) {
            var selects = detailRoot.querySelectorAll('select[is="emby-select"], emby-select select, select.emby-select');
            for (var i = 0; i < selects.length; i++) {
                var select = selects[i];
                var value = select.value;
                if (value && sources.some(function (s) { return s.Id === value; })) {
                    return value;
                }
                var selected = select.selectedOptions && select.selectedOptions[0];
                if (!selected) continue;
                var label = (selected.textContent || '').trim();
                if (!label) continue;
                var byName = sources.find(function (s) {
                    var name = (s.Name || '').trim();
                    return name && (label === name || label.indexOf(name) >= 0 || name.indexOf(label) >= 0);
                });
                if (byName) return byName.Id;
            }
        }

        return null;
    }

    function showMediaSourcePickerDialog(sources) {
        return Emby.importModule('./modules/dialoghelper/dialoghelper.js').then(function (mod) {
            var dialogHelper = mod && mod.default ? mod.default : mod;
            var html = '<div class="formDialogHeader">' +
                '<button type="button" is="emby-dialogclosebutton" closetype="cancel"></button>' +
                '<h3 class="formDialogHeaderTitle">选择版本</h3>' +
                '</div>' +
                '<div class="formDialogContent"><div class="dialogContentInner padded-left padded-right">' +
                '<p class="secondaryText" style="font-size:13px;margin-bottom:10px">检测到多个版本，请选择要操作的版本：</p>' +
                '<div style="max-height:45vh;overflow-y:auto">';

            sources.forEach(function (source, index) {
                var name = source.Name || ('版本 ' + (index + 1));
                var path = source.Path || '';
                var folder = path.replace(/[\\/][^\\/]+$/, '');
                html += '<label style="display:block;padding:10px 0;border-bottom:1px solid #333;cursor:pointer">' +
                    '<input type="radio" name="subhdMediaSource" value="' + escapeHtml(source.Id) + '"' +
                    (index === 0 ? ' checked' : '') + ' style="margin-right:8px">' +
                    '<span style="font-weight:bold">' + escapeHtml(name) + '</span>' +
                    (folder ? '<br><span style="font-size:12px;color:#888;margin-left:22px">' + escapeHtml(folder) + '</span>' : '') +
                    '</label>';
            });

            html += '</div>' +
                '<div style="display:flex;justify-content:flex-end;gap:10px;padding-top:14px">' +
                '<button type="button" id="mediaSourcePickCancel" class="emby-button">取消</button>' +
                '<button type="button" id="mediaSourcePickOk" class="emby-button raised">确定</button>' +
                '</div></div></div>';

            var dlg = dialogHelper.createDialog({ size: 'small', removeOnClose: true });
            dlg.classList.add('formDialog');
            dlg.innerHTML = html;

            return new Promise(function (resolve, reject) {
                dlg.querySelector('#mediaSourcePickCancel').addEventListener('click', function () {
                    dialogHelper.close(dlg);
                    reject(new Error('cancelled'));
                });
                dlg.querySelector('#mediaSourcePickOk').addEventListener('click', function () {
                    var checked = dlg.querySelector('input[name="subhdMediaSource"]:checked');
                    dialogHelper.close(dlg);
                    if (!checked || !checked.value) {
                        reject(new Error('no selection'));
                        return;
                    }
                    resolve(checked.value);
                });
                dialogHelper.open(dlg);
            });
        });
    }

    function resolveMediaSourceContext(items) {
        if (!items || items.length !== 1 || !isVersionAwareItem(items[0])) {
            return Promise.resolve({
                ids: (items || []).map(getCommandItemId).filter(Boolean),
                mediaSourceId: null
            });
        }

        return fetchItemMediaSources(items[0]).then(function (fullItem) {
            var sources = (fullItem && fullItem.MediaSources) || [];
            if (sources.length <= 1) {
                return {
                    ids: [getCommandItemId(items[0])],
                    mediaSourceId: sources[0] ? sources[0].Id : null
                };
            }

            var detected = detectSelectedMediaSourceId(fullItem, sources);
            if (detected) {
                return {
                    ids: [getCommandItemId(items[0])],
                    mediaSourceId: detected
                };
            }

            return showMediaSourcePickerDialog(sources).then(function (mediaSourceId) {
                return {
                    ids: [getCommandItemId(items[0])],
                    mediaSourceId: mediaSourceId
                };
            });
        });
    }

    function getLibraryDisplayName(item) {
        if (!item) {
            return '';
        }

        return item.Name || item.name || item.ItemId || item.Id || item.Guid || '';
    }

    function getLibraryLocations(item) {
        return item && Array.isArray(item.Locations)
            ? item.Locations.filter(path => typeof path === 'string' && path.trim())
            : [];
    }

    function cloneLibraryOptions(library) {
        if (!library || !library.LibraryOptions) {
            return null;
        }

        try {
            return JSON.parse(JSON.stringify(library.LibraryOptions));
        } catch (_) {
            return null;
        }
    }

    function mergeLibraryInfo(sourceItems, fetchedItems) {
        const fetchedLookup = new Map();
        (fetchedItems || []).forEach(function (item) {
            const key = getLibraryLookupKey(item);
            if (key) {
                fetchedLookup.set(key, item);
            }
        });

        return sourceItems.map(function (item) {
            const fetched = fetchedLookup.get(getLibraryLookupKey(item));
            return fetched ? Object.assign({}, item, fetched) : item;
        });
    }

    function fetchLibraryInfos(items) {
        const apiClient = connectionManager.currentApiClient();
        if (!apiClient || !items || !items.length) {
            return Promise.resolve(items || []);
        }

        const ids = items
            .map(getLibraryInfoQueryId)
            .filter(Boolean);

        if (!ids.length) {
            return Promise.resolve(items);
        }

        const query = new URLSearchParams({Ids: ids.join(',')}).toString();
        const url = `${apiClient.getUrl('Library/VirtualFolders/Query')}?${query}`;

        return apiClient.ajax({
            type: 'GET',
            url: url,
            dataType: 'json'
        }).then(function (result) {
            const fetchedItems = result && Array.isArray(result.Items) ? result.Items : [];
            return mergeLibraryInfo(items, fetchedItems);
        }).catch(function () {
            return items;
        });
    }

    function ticksToTimeString(ticks) {
        const ticksNumber = Number(ticks);
        if (!Number.isFinite(ticksNumber) || ticksNumber < 0) {
            return null;
        }

        const totalMilliseconds = Math.floor(ticksNumber / 10000);
        const hours = Math.floor(totalMilliseconds / 3600000);
        const minutes = Math.floor((totalMilliseconds % 3600000) / 60000);
        const seconds = Math.floor((totalMilliseconds % 60000) / 1000);
        const milliseconds = totalMilliseconds % 1000;

        const hh = String(hours).padStart(2, '0');
        const mm = String(minutes).padStart(2, '0');
        const ss = String(seconds).padStart(2, '0');
        const mmm = String(milliseconds).padStart(3, '0');
        return `${hh}:${mm}:${ss}.${mmm}`;
    }

    function getMarkerTicksFromItem(item) {
        if (!item || !Array.isArray(item.Chapters)) {
            return null;
        }

        let introStartTicks = null;
        let introEndTicks = null;
        let creditsStartTicks = null;

        for (const chapter of item.Chapters) {
            if (!chapter || chapter.StartPositionTicks == null) {
                continue;
            }

            if (chapter.MarkerType === 'IntroStart' || chapter.MarkerType === 7) {
                introStartTicks = chapter.StartPositionTicks;
            } else if (chapter.MarkerType === 'IntroEnd' || chapter.MarkerType === 8) {
                introEndTicks = chapter.StartPositionTicks;
            } else if (chapter.MarkerType === 'CreditsStart' || chapter.MarkerType === 9) {
                creditsStartTicks = chapter.StartPositionTicks;
            }
        }

        if (introStartTicks == null && introEndTicks == null && creditsStartTicks == null) {
            return null;
        }

        return {introStartTicks, introEndTicks, creditsStartTicks};
    }

    function getExistingMarkerTicks(apiClient, episodeItem) {
        const fromCurrentItem = getMarkerTicksFromItem(episodeItem);
        if (fromCurrentItem) {
            return Promise.resolve(fromCurrentItem);
        }

        if (!episodeItem || !episodeItem.Id) {
            return Promise.resolve(null);
        }

        const userId = typeof apiClient.getCurrentUserId === 'function' ? apiClient.getCurrentUserId() : null;
        const endpoint = userId ? `Users/${userId}/Items/${episodeItem.Id}` : `Items/${episodeItem.Id}`;
        const query = new URLSearchParams({Fields: 'Chapters'}).toString();
        const url = `${apiClient.getUrl(endpoint)}?${query}`;

        return apiClient.ajax({
            type: 'GET',
            url: url,
            dataType: 'json'
        }).then(function (item) {
            return getMarkerTicksFromItem(item);
        }).catch(function () {
            return null;
        });
    }

    const api = {
        extractMediaInfo: function (ids) {
            if (!ids || !ids.length) {
                return Promise.resolve();
            }

            const commandName = getCommandName();
            return confirm({
                text: globalize.translate('AreYouSureToContinue'),
                title: commandName,
                confirmText: commandName,
                primary: 'cancel'
            }).then(function () {
                loading.show();
                const apiClient = connectionManager.currentApiClient();
                return postJson(apiClient, 'MediaInfoKeeper/Items/ExtractMediaInfo', {Ids: ids}).then(function (result) {
                    toast(getResultMessage(result, 'extract'));
                }).catch(function (err) {
                    toast(getErrorMessage('extract', err));
                }).finally(function () {
                    loading.hide();
                });
            });
        },

        deleteMediaInfoPersist: function (ids) {
            if (!ids || !ids.length) {
                return Promise.resolve();
            }

            const commandName = getDeleteCommandName();
            return confirm({
                text: globalize.translate('AreYouSureToContinue'),
                title: commandName,
                confirmText: globalize.translate('Delete'),
                primary: 'cancel'
            }).then(function () {
                loading.show();
                const apiClient = connectionManager.currentApiClient();
                return postJson(apiClient, 'MediaInfoKeeper/Items/DeleteMediaInfoPersist', {Ids: ids}).then(function (result) {
                    toast(getResultMessage(result, 'delete'));
                }).catch(function (err) {
                    toast(getErrorMessage('delete', err));
                }).finally(function () {
                    loading.hide();
                });
            });
        },

        scanIntro: function (ids) {
            if (!ids || !ids.length) {
                return Promise.resolve();
            }

            const commandName = getScanIntroCommandName();
            return confirm({
                text: globalize.translate('AreYouSureToContinue'),
                title: commandName,
                confirmText: commandName,
                primary: 'cancel'
            }).then(function () {
                loading.show();
                const apiClient = connectionManager.currentApiClient();
                return postJson(apiClient, 'MediaInfoKeeper/Items/ScanIntro', {Ids: ids}).then(function (result) {
                    toast(getResultMessage(result, 'scan_intro'));
                }).catch(function (err) {
                    toast(getErrorMessage('scan_intro', err));
                }).finally(function () {
                    loading.hide();
                });
            });
        },

        scanExternalFiles: function (ids, mediaSourceId) {
            if (!ids || !ids.length) {
                return Promise.resolve();
            }

            const commandName = getScanExternalFilesCommandName();
            return confirm({
                text: globalize.translate('AreYouSureToContinue'),
                title: commandName,
                confirmText: commandName,
                primary: 'cancel'
            }).then(function () {
                loading.show();
                const apiClient = connectionManager.currentApiClient();
                return postJson(apiClient, 'MediaInfoKeeper/Items/ScanExternalFiles', {
                    Ids: ids,
                    MediaSourceId: mediaSourceId || null
                }).then(function (result) {
                    toast(getResultMessage(result, 'scan_external_files'));
                }).catch(function (err) {
                    toast(getErrorMessage('scan_external_files', err));
                }).finally(function () {
                    loading.hide();
                });
            });
        },

        renameSubtitles: function (ids, mediaSourceId) {
            if (!ids || !ids.length) {
                return Promise.resolve();
            }

            const commandName = getRenameSubtitlesCommandName();
            return confirm({
                text: globalize.translate('AreYouSureToContinue'),
                title: commandName,
                confirmText: commandName,
                primary: 'cancel'
            }).then(function () {
                loading.show();
                const apiClient = connectionManager.currentApiClient();
                return postJson(apiClient, 'MediaInfoKeeper/Items/RenameSubtitles', {
                    Ids: ids,
                    MediaSourceId: mediaSourceId || null
                }).then(function (result) {
                    toast(getResultMessage(result, 'rename_subtitles'));
                }).catch(function (err) {
                    toast(getErrorMessage('rename_subtitles', err));
                }).finally(function () {
                    loading.hide();
                });
            });
        },

        setIntro: function (ids, items) {
            if (!ids || !ids.length) {
                return Promise.resolve();
            }

            const commandName = getSetIntroCommandName();
            const locale = (globalize.getCurrentLocale() || '').toLowerCase();
            const selectedItems = Array.isArray(items) ? items.filter(Boolean) : [];
            const defaultTimeValue = '00:00:00.000';

            function timeToSeconds(timeStr) {
                const parts = timeStr.split(':');
                if (parts.length === 3) {
                    const hours = parseFloat(parts[0]) || 0;
                    const minutes = parseFloat(parts[1]) || 0;
                    const seconds = parseFloat(parts[2]) || 0;
                    return hours * 3600 + minutes * 60 + seconds;
                }
                return 0;
            }

            return new Promise(function (resolve) {
                const dialogHtml = `
                    <div class="dialogContainer" style="position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: rgba(0,0,0,0.7); display: flex; align-items: center; justify-content: center; z-index: 9999;">
                        <div class="formDialogContent smoothScrollY" style="background: #101010; border-radius: 8px; padding: 24px; max-width: 90%; width: 500px; max-height: 90vh; overflow-y: auto;">
                            <h3 style="margin: 0 0 20px 0; color: #fff; font-size: 1.5em;">${locale === 'zh-cn' ? '设置片头片尾时间' : (['zh-hk', 'zh-tw'].includes(locale) ? '設置片頭片尾時間' : 'Set Intro/Credits Time')}</h3>
                            <div class="inputContainer" style="margin-bottom: 16px;">
                                <label style="display: block; margin-bottom: 8px; color: #fff; font-size: 0.9em;">${locale === 'zh-cn' ? '片头开始时间' : (['zh-hk', 'zh-tw'].includes(locale) ? '片頭開始時間' : 'Intro Start Time')}</label>
                                <input type="text" id="introStartTime" class="emby-input" value="${defaultTimeValue}" placeholder="${defaultTimeValue}" style="width: 100%; padding: 10px; background: #1f1f1f; border: 1px solid #333; color: #fff; border-radius: 4px; font-size: 16px; box-sizing: border-box;">
                            </div>
                            <div class="inputContainer" style="margin-bottom: 16px;">
                                <label style="display: block; margin-bottom: 8px; color: #fff; font-size: 0.9em;">${locale === 'zh-cn' ? '片头结束时间' : (['zh-hk', 'zh-tw'].includes(locale) ? '片頭結束時間' : 'Intro End Time')}</label>
                                <input type="text" id="introEndTime" class="emby-input" value="${defaultTimeValue}" placeholder="${defaultTimeValue}" style="width: 100%; padding: 10px; background: #1f1f1f; border: 1px solid #333; color: #fff; border-radius: 4px; font-size: 16px; box-sizing: border-box;">
                            </div>
                            <div class="inputContainer" style="margin-bottom: 16px;">
                                <label style="display: block; margin-bottom: 8px; color: #fff; font-size: 0.9em;">${locale === 'zh-cn' ? '片尾开始时间（可选）' : (['zh-hk', 'zh-tw'].includes(locale) ? '片尾開始時間（可選）' : 'Credits Start Time (Optional)')}</label>
                                <input type="text" id="creditsStartTime" class="emby-input" value="" placeholder="${defaultTimeValue}" style="width: 100%; padding: 10px; background: #1f1f1f; border: 1px solid #333; color: #fff; border-radius: 4px; font-size: 16px; box-sizing: border-box;">
                            </div>
                            <div style="margin-top: 24px; display: flex; gap: 10px; flex-wrap: wrap;">
                                <button id="cancelSetIntro" class="emby-button emby-button-cancel" style="flex: 1; min-width: 100px; padding: 12px 20px; background: #333; color: #fff; border: none; border-radius: 4px; cursor: pointer; font-size: 14px; display: flex; justify-content: center; align-items: center;">${globalize.translate('Cancel')}</button>
                                <button id="confirmSetIntro" class="emby-button emby-button-submit" style="flex: 1; min-width: 100px; padding: 12px 20px; background: #53B54C; color: #fff; border: none; border-radius: 4px; cursor: pointer; font-size: 14px; display: flex; justify-content: center; align-items: center;">${commandName}</button>
                            </div>
                        </div>
                    </div>
                `;

                const dialog = document.createElement('div');
                dialog.innerHTML = dialogHtml;
                document.body.appendChild(dialog);

                const cancelBtn = dialog.querySelector('#cancelSetIntro');
                const confirmBtn = dialog.querySelector('#confirmSetIntro');
                const startInput = dialog.querySelector('#introStartTime');
                const endInput = dialog.querySelector('#introEndTime');
                const creditsInput = dialog.querySelector('#creditsStartTime');
                const shouldPrefill = selectedItems.length === 1 && selectedItems[0].Type === 'Episode';

                if (shouldPrefill) {
                    const apiClient = connectionManager.currentApiClient();
                    getExistingMarkerTicks(apiClient, selectedItems[0]).then(function (markerTicks) {
                        if (!markerTicks) {
                            return;
                        }

                        if (markerTicks.introStartTicks != null && startInput.value === defaultTimeValue) {
                            const formattedStart = ticksToTimeString(markerTicks.introStartTicks);
                            if (formattedStart) {
                                startInput.value = formattedStart;
                            }
                        }

                        if (markerTicks.introEndTicks != null && endInput.value === defaultTimeValue) {
                            const formattedEnd = ticksToTimeString(markerTicks.introEndTicks);
                            if (formattedEnd) {
                                endInput.value = formattedEnd;
                            }
                        }

                        if (markerTicks.creditsStartTicks != null && creditsInput.value === '') {
                            const formattedCredits = ticksToTimeString(markerTicks.creditsStartTicks);
                            if (formattedCredits) {
                                creditsInput.value = formattedCredits;
                            }
                        }
                    });
                }

                cancelBtn.addEventListener('click', function () {
                    document.body.removeChild(dialog);
                    resolve();
                });

                confirmBtn.addEventListener('click', function () {
                    const startSeconds = timeToSeconds(startInput.value);
                    const endSeconds = timeToSeconds(endInput.value);
                    const creditsValue = (creditsInput.value || '').trim();
                    const creditsSeconds = creditsValue ? timeToSeconds(creditsValue) : null;

                    if (startSeconds >= endSeconds) {
                        toast(locale === 'zh-cn' ? '开始时间必须小于结束时间' : (['zh-hk', 'zh-tw'].includes(locale) ? '開始時間必須小於結束時間' : 'Start time must be less than end time'));
                        return;
                    }

                    if (creditsSeconds != null && endSeconds >= creditsSeconds) {
                        toast(locale === 'zh-cn' ? '片尾开始时间必须大于片头结束时间' : (['zh-hk', 'zh-tw'].includes(locale) ? '片尾開始時間必須大於片頭結束時間' : 'Credits start time must be greater than intro end time'));
                        return;
                    }

                    const introStartTicks = Math.round(startSeconds * 10000000);
                    const introEndTicks = Math.round(endSeconds * 10000000);
                    const creditsStartTicks = creditsSeconds != null ? Math.round(creditsSeconds * 10000000) : null;

                    document.body.removeChild(dialog);
                    loading.show();
                    const apiClient = connectionManager.currentApiClient();
                    return postJson(apiClient, 'MediaInfoKeeper/Items/SetIntro', {
                        Ids: ids,
                        IntroStartTicks: introStartTicks,
                        IntroEndTicks: introEndTicks,
                        CreditsStartTicks: creditsStartTicks
                    }).then(function (result) {
                        toast(getResultMessage(result, 'set_intro'));
                        resolve();
                    }).catch(function (err) {
                        toast(getErrorMessage('set_intro', err));
                        resolve();
                    }).finally(function () {
                        loading.hide();
                    });
                });
            });
        },

        clearIntro: function (ids) {
            if (!ids || !ids.length) {
                return Promise.resolve();
            }

            const commandName = getClearIntroCommandName();
            const locale = (globalize.getCurrentLocale() || '').toLowerCase();

            return new Promise(function (resolve) {
                const dialogHtml = `
                    <div class="dialogContainer" style="position: fixed; top: 0; left: 0; right: 0; bottom: 0; background: rgba(0,0,0,0.7); display: flex; align-items: center; justify-content: center; z-index: 9999;">
                        <div class="formDialogContent smoothScrollY" style="background: #101010; border-radius: 8px; padding: 24px; max-width: 90%; width: 500px; max-height: 90vh; overflow-y: auto;">
                            <h3 style="margin: 0 0 20px 0; color: #fff; font-size: 1.5em;">${locale === 'zh-cn' ? '删除片头片尾' : (['zh-hk', 'zh-tw'].includes(locale) ? '刪除片頭片尾' : 'Delete Intro/Credits')}</h3>
                            <p style="margin: 0 0 24px 0; color: #ccc; font-size: 14px;">${locale === 'zh-cn' ? '确定要删除选中项目的片头片尾标记吗？' : (['zh-hk', 'zh-tw'].includes(locale) ? '確定要刪除選中項目的片頭片尾標記嗎？' : 'Are you sure you want to delete intro/credits markers for selected items?')}</p>
                            <div style="margin-top: 24px; display: flex; gap: 10px; flex-wrap: wrap;">
                                <button id="cancelClearIntro" class="emby-button emby-button-cancel" style="flex: 1; min-width: 100px; padding: 12px 20px; background: #333; color: #fff; border: none; border-radius: 4px; cursor: pointer; font-size: 14px; display: flex; justify-content: center; align-items: center;">${globalize.translate('Cancel')}</button>
                                <button id="confirmClearIntro" class="emby-button emby-button-submit" style="flex: 1; min-width: 100px; padding: 12px 20px; background: #53B54C; color: #fff; border: none; border-radius: 4px; cursor: pointer; font-size: 14px; display: flex; justify-content: center; align-items: center;">${commandName}</button>
                            </div>
                        </div>
                    </div>
                `;

                const dialog = document.createElement('div');
                dialog.innerHTML = dialogHtml;
                document.body.appendChild(dialog);

                const cancelBtn = dialog.querySelector('#cancelClearIntro');
                const confirmBtn = dialog.querySelector('#confirmClearIntro');

                cancelBtn.addEventListener('click', function () {
                    document.body.removeChild(dialog);
                    resolve();
                });

                confirmBtn.addEventListener('click', function () {
                    document.body.removeChild(dialog);
                    loading.show();
                    const apiClient = connectionManager.currentApiClient();
                    return postJson(apiClient, 'MediaInfoKeeper/Items/ClearIntro', {Ids: ids}).then(function (result) {
                        toast(getResultMessage(result, 'clear_intro'));
                        resolve();
                    }).catch(function (err) {
                        toast(getErrorMessage('clear_intro', err));
                        resolve();
                    }).finally(function () {
                        loading.hide();
                    });
                });
            });
        },

        copyLibrary: function (items) {
            const libraries = Array.isArray(items) ? items.filter(isLibraryItem) : [];
            if (libraries.length !== 1) {
                toast(translateCopyEmpty());
                return Promise.resolve();
            }

            return fetchLibraryInfos(libraries).then(function (resolvedLibraries) {
                const sourceLibrary = resolvedLibraries[0];
                const libraryOptions = cloneLibraryOptions(sourceLibrary);
                const paths = getLibraryLocations(sourceLibrary);
                const newName = buildDuplicateLibraryName(getLibraryDisplayName(sourceLibrary));

                if (!libraryOptions) {
                    toast(translateCopyFailedNoOptions());
                    return;
                }

                loading.show();
                const apiClient = connectionManager.currentApiClient();
                return postJsonAllowEmpty(apiClient, 'Library/VirtualFolders', {
                    Name: newName,
                    CollectionType: sourceLibrary.CollectionType || libraryOptions.ContentType,
                    RefreshLibrary: false,
                    Paths: paths,
                    LibraryOptions: libraryOptions
                }).then(function () {
                    toast(translateCopySuccess(newName));
                }).catch(function (err) {
                    toast(getErrorMessage('copy_library', err));
                }).finally(function () {
                    loading.hide();
                });
            }).catch(function (err) {
                toast(getErrorMessage('copy_library', err));
            });
        }
    };

    // ========== SubHD 字幕搜索 ==========
    function searchSubhd(item, mediaSourceId) {
        var itemId = item.Id || item.Guid || item.ItemId || '';

        loading.show();

        var apiClient = connectionManager.currentApiClient();
        if (!apiClient) {
            loading.hide();
            toast('无法获取API连接');
            return;
        }

        var url = apiClient.getUrl('MediaInfoKeeper/Items/SearchSubhd');
        apiClient.ajax({
            type: 'POST',
            url: url,
            data: JSON.stringify({
                Ids: [itemId],
                MediaSourceId: mediaSourceId || null
            }),
            contentType: 'application/json',
            dataType: 'json'
        }).then(function (result) {
            loading.hide();
            if (!result || !result.Subtitles) {
                toast(result && result.Message ? result.Message : '搜索无结果');
                return;
            }
            showSubhdDialog(item, result, mediaSourceId);
        }).catch(function (err) {
            loading.hide();
            toast('字幕搜索失败: ' + ((err && err.message) || (err && err.statusText) || '网络错误'));
        });
    }

    function showSubhdDialog(item, searchResult, mediaSourceId) {
        var subtitles = searchResult.Subtitles || [];
        var itemName = searchResult.ItemName || item.Name || '';
        var targetSeason = item && item.Type === 'Season'
            ? Number(item.IndexNumber)
            : Number(item && item.ParentIndexNumber);
        var targetEpisode = Number(item && item.IndexNumber);
        var isEpisodeItem = item && item.Type === 'Episode' && Number.isFinite(targetEpisode) && targetEpisode > 0;
        var isSeasonItem = item && item.Type === 'Season' && Number.isFinite(targetSeason) && targetSeason > 0;

        // 解析季号+集号，检测是否为剧集（用于一键批量下载整季）
        var episodeBest = {};
        var episodeMeta = {};
        function parseSeasonEpisodeFromTitle(title) {
            var t = title || '';
            var m = /S(\d{1,2})\s*E(\d{1,2})/i.exec(t);
            if (m) {
                return { season: parseInt(m[1], 10), ep: parseInt(m[2], 10) };
            }
            m = /(\d{1,2})x(\d{1,2})/i.exec(t);
            if (m) {
                return { season: parseInt(m[1], 10), ep: parseInt(m[2], 10) };
            }
            m = /第(\d{1,2})季第(\d{1,2})集/.exec(t);
            if (m) {
                return { season: parseInt(m[1], 10), ep: parseInt(m[2], 10) };
            }
            return null;
        }
        subtitles.forEach(function (s) {
            var parsed = parseSeasonEpisodeFromTitle(s.Title || '');
            if (parsed) {
                var seasonNum = parsed.season;
                var epNum = parsed.ep;
                var key = seasonNum + '-' + epNum;
                var cur = episodeBest[key];
                if (!cur || (s.Downloads || 0) > (cur.Downloads || 0)) {
                    episodeBest[key] = s;
                    episodeMeta[key] = { season: seasonNum, ep: epNum };
                }
            }
        });
        var epKeys = Object.keys(episodeBest).sort(function (a, b) {
            var sa = parseInt(a.split('-')[0], 10), ea = parseInt(a.split('-')[1], 10);
            var sb = parseInt(b.split('-')[0], 10), eb = parseInt(b.split('-')[1], 10);
            return (sa - sb) || (ea - eb);
        });
        if (isSeasonItem) {
            epKeys = epKeys.filter(function (key) {
                return parseInt(key.split('-')[0], 10) === targetSeason;
            });
        }
        var localEpisodeCount = Number(searchResult.TotalEpisodes) || 0;
        var localSeasonCount = Number(searchResult.TotalSeasons) || 0;
        var episodesWithSubs = Number(searchResult.EpisodesWithSubtitles) || 0;
        var inventorySeasons = Array.isArray(searchResult.Seasons) ? searchResult.Seasons : [];
        var isSeriesItem = item && item.Type === 'Series';
        var isSeries = epKeys.length >= 1 || localEpisodeCount >= 1;
        var selectedCount = localEpisodeCount > 0 ? localEpisodeCount : epKeys.length;

        function getDownloadCount(subtitleItem) {
            var raw = subtitleItem && subtitleItem.Downloads;
            var n = Number(raw);
            return Number.isFinite(n) ? n : 0;
        }

        function compareByDownloadsDesc(a, b) {
            return getDownloadCount(b) - getDownloadCount(a);
        }

        function matchesScope(title) {
            var parsed = parseSeasonEpisodeFromTitle(title || '');
            if (!parsed) return !isEpisodeItem && !isSeasonItem;
            if (isEpisodeItem) {
                var seasonMatched = !Number.isFinite(targetSeason) || targetSeason <= 0 || parsed.season === targetSeason;
                return seasonMatched && parsed.ep === targetEpisode;
            }
            if (isSeasonItem) {
                return parsed.season === targetSeason;
            }
            return true;
        }

        var displaySubtitles = subtitles.filter(function (s) {
            return matchesScope((s && s.Title) || '');
        });
        displaySubtitles.sort(compareByDownloadsDesc);

        function seasonLabel(seasonNumber) {
            return seasonNumber === 0 ? '特别篇' : ('第 ' + seasonNumber + ' 季');
        }

        function renderSubtitleRow(s) {
            var tags = (s.Tags || []).join(' · ');
            var movieInfo = s.MovieName ? s.MovieName : '';
            if (s.MovieYear) movieInfo += ' (' + s.MovieYear + ')';
            var row = '<div class="subhd-item" style="padding:10px 0;border-bottom:1px solid #333;cursor:pointer"' +
                ' data-subid="' + escapeHtml(s.SubId) + '"' +
                ' data-title="' + escapeHtml(s.Title || '') + '">' +
                '<div style="font-weight:bold;margin-bottom:3px">' + (s.Title || s.SubId) + '</div>' +
                '<div style="font-size:12px;color:#999">';
            if (movieInfo) row += escapeHtml(movieInfo) + ' · ';
            if (s.Group) row += '<span style="color:#28a745">' + escapeHtml(s.Group) + '</span> · ';
            if (tags) row += escapeHtml(tags);
            row += '</div><div style="font-size:12px;color:#666;margin-top:2px">';
            if (s.Format) row += escapeHtml(s.Format) + ' · ';
            if (s.Size) row += escapeHtml(s.Size) + ' · ';
            if (s.Uploader) row += escapeHtml(s.Uploader) + ' · ';
            if (s.Downloads) row += '⬇' + s.Downloads;
            row += '</div></div>';
            return row;
        }

        var scopeHint = '';
        if (isEpisodeItem) {
            var seasonCode = Number.isFinite(targetSeason) && targetSeason > 0
                ? ('S' + ('0' + targetSeason).slice(-2) + 'E' + ('0' + targetEpisode).slice(-2))
                : ('E' + ('0' + targetEpisode).slice(-2));
            scopeHint = '当前集 ' + seasonCode + ' · ' + displaySubtitles.length + ' 条';
        } else if (isSeasonItem) {
            scopeHint = seasonLabel(targetSeason) + ' · 库内 ' + localEpisodeCount + ' 集 · ' + displaySubtitles.length + ' 条';
        } else if (isSeriesItem) {
            scopeHint = '库内 ' + (localSeasonCount || inventorySeasons.length || 0) + ' 季 · ' +
                localEpisodeCount + ' 集（已有字幕 ' + episodesWithSubs + ' 集）';
        }

        var html = '<div class="formDialogHeader">' +
            '<button type="button" is="emby-dialogclosebutton" closetype="cancel"></button>' +
            '<h3 class="formDialogHeaderTitle">字幕搜索</h3>' +
            '</div>' +
            '<div class="formDialogContent"><div class="dialogContentInner padded-left padded-right">' +
            '<style>' +
            '.subhd-actions{display:flex;gap:10px;flex-wrap:wrap;padding:8px 0 14px}' +
            '.subhd-btn{flex:1;min-width:148px;padding:10px 14px;border:none;border-radius:4px;cursor:pointer;font-size:13px;line-height:1.3}' +
            '.subhd-btn:disabled{opacity:.45;cursor:default}' +
            '.subhd-btn-primary{background:#52B54B;color:#fff}' +
            '.subhd-btn-secondary{background:#2a2a2a;color:#ddd}' +
            '.subhd-season-box{margin:2px 0 4px;padding:8px 10px;background:#181818;border:1px solid #2c2c2c;border-radius:6px}' +
            '.subhd-item:hover{background:#1a1a1a}' +
            '</style>' +
            '<p class="secondaryText">' + escapeHtml(itemName) + '</p>' +
            '<p class="secondaryText" style="font-size:12px">搜索词: ' + escapeHtml(searchResult.SearchQuery || '') + '</p>';
        if (scopeHint) {
            html += '<p class="secondaryText" style="font-size:12px">' + escapeHtml(scopeHint) + '</p>';
        }

        if (isSeries && !isEpisodeItem) {
            if (isSeriesItem && inventorySeasons.length > 1) {
                html += '<div id="subhdSeasonPicker" class="subhd-season-box" style="font-size:13px">';
                inventorySeasons.forEach(function (season) {
                    var seasonNo = Number(season.SeasonNumber) || 0;
                    var missing = Math.max(0, (Number(season.EpisodeCount) || 0) - (Number(season.WithSubtitles) || 0));
                    html += '<label style="display:flex;align-items:center;gap:8px;padding:5px 0;cursor:pointer">' +
                        '<input type="checkbox" class="subhd-season-check" data-season="' + seasonNo + '" checked>' +
                        seasonLabel(seasonNo) + ' · ' + (season.EpisodeCount || 0) + ' 集' +
                        (missing > 0 ? '（缺 ' + missing + ' 集字幕）' : '（字幕已齐）') +
                        '</label>';
                });
                html += '</div>';
            }
            html += '<div class="subhd-actions">' +
                '<button type="button" id="subhdBatchMissingBtn" class="subhd-btn subhd-btn-primary">只下载缺少的字幕</button>' +
                '<button type="button" id="subhdBatchAllBtn" class="subhd-btn subhd-btn-secondary">全部重新下载</button>' +
                '</div>';
        }

        if (!displaySubtitles.length) {
            html += '<p style="padding:2em 0;text-align:center">' + (searchResult.Message || '未找到字幕') + '</p>';
        } else {
            html += '<div style="max-height:50vh;overflow-y:auto;padding-right:6px">';
            if (isSeriesItem) {
                var grouped = {};
                displaySubtitles.forEach(function (s) {
                    var parsed = parseSeasonEpisodeFromTitle(s.Title || '');
                    var key = parsed ? String(parsed.season) : 'other';
                    if (!grouped[key]) grouped[key] = [];
                    grouped[key].push(s);
                });
                Object.keys(grouped).sort(function (a, b) {
                    if (a === 'other') return 1;
                    if (b === 'other') return -1;
                    return parseInt(a, 10) - parseInt(b, 10);
                }).forEach(function (key) {
                    var heading = key === 'other' ? '未识别季度' : seasonLabel(parseInt(key, 10));
                    html += '<div style="padding:10px 0 4px;font-size:12px;color:#aaa;border-bottom:1px solid #444">' +
                        escapeHtml(heading) + ' · ' + grouped[key].length + ' 条</div>';
                    grouped[key].forEach(function (s) {
                        html += renderSubtitleRow(s);
                    });
                });
            } else {
                displaySubtitles.forEach(function (s) {
                    html += renderSubtitleRow(s);
                });
            }
            html += '</div>';
        }

        html += '</div></div>';

        Emby.importModule('./modules/dialoghelper/dialoghelper.js').then(function (mod) {
            var dialogHelper = mod && mod.default ? mod.default : mod;
            var dlg = dialogHelper.createDialog({ size: 'medium-tall', removeOnClose: true });
            dlg.classList.add('formDialog');
            dlg.innerHTML = html;

            var missingBtn = dlg.querySelector('#subhdBatchMissingBtn');
            var allBtn = dlg.querySelector('#subhdBatchAllBtn');
            function selectedSeasonNumbers() {
                var checks = dlg.querySelectorAll('.subhd-season-check');
                if (!checks.length) {
                    return isSeasonItem && Number.isFinite(targetSeason) ? [targetSeason] : [];
                }
                return Array.prototype.filter.call(checks, function (el) { return el.checked; })
                    .map(function (el) { return parseInt(el.getAttribute('data-season'), 10); })
                    .filter(function (n) { return Number.isFinite(n); });
            }
            function selectedEpisodeCount(skipExisting) {
                var selected = selectedSeasonNumbers();
                if (!inventorySeasons.length) {
                    return skipExisting ? Math.max(0, selectedCount - episodesWithSubs) : selectedCount;
                }
                if (!selected.length && dlg.querySelectorAll('.subhd-season-check').length) return 0;
                if (!selected.length) {
                    return skipExisting ? Math.max(0, selectedCount - episodesWithSubs) : selectedCount;
                }
                return inventorySeasons.reduce(function (sum, season) {
                    if (selected.indexOf(Number(season.SeasonNumber) || 0) < 0) return sum;
                    var total = Number(season.EpisodeCount) || 0;
                    var owned = Number(season.WithSubtitles) || 0;
                    return sum + (skipExisting ? Math.max(0, total - owned) : total);
                }, 0);
            }
            function refreshBatchButtons() {
                var missingCount = selectedEpisodeCount(true);
                var allCount = selectedEpisodeCount(false);
                if (missingBtn) {
                    missingBtn.textContent = '只下载缺少的字幕  ·  ' + missingCount + ' 集';
                    missingBtn.disabled = missingCount <= 0;
                }
                if (allBtn) {
                    allBtn.textContent = '全部重新下载  ·  ' + allCount + ' 集';
                    allBtn.disabled = allCount <= 0;
                }
            }
            function startBatchDownload(skipExisting) {
                var selected = selectedSeasonNumbers();
                var filteredKeys = epKeys;
                if (selected.length) {
                    filteredKeys = epKeys.filter(function (key) {
                        return selected.indexOf(parseInt(key.split('-')[0], 10)) >= 0;
                    });
                }
                    downloadSubhdBatch(item, filteredKeys, episodeBest, episodeMeta, dlg, selected, skipExisting, mediaSourceId);
            }
            dlg.querySelectorAll('.subhd-season-check').forEach(function (el) {
                el.addEventListener('change', refreshBatchButtons);
            });
            refreshBatchButtons();
            if (missingBtn) {
                missingBtn.addEventListener('click', function () { startBatchDownload(true); });
            }
            if (allBtn) {
                allBtn.addEventListener('click', function () { startBatchDownload(false); });
            }

            var items = dlg.querySelectorAll('.subhd-item');
            items.forEach(function (el) {
                el.addEventListener('click', function () {
                    var subId = el.getAttribute('data-subid');
                    var title = el.getAttribute('data-title');
                    downloadSubhd(item, subId, title, dlg, mediaSourceId);
                });
            });

            dialogHelper.open(dlg);
        });
    }

    function downloadSubhd(item, subId, subTitle, dialog, mediaSourceId) {
        var itemId = item.Id || item.Guid || item.ItemId || '';
        var apiClient = connectionManager.currentApiClient();

        loading.show();
        if (dialog) {
            Emby.importModule('./modules/dialoghelper/dialoghelper.js').then(function (mod) {
                var dh = mod && mod.default ? mod.default : mod;
                dh.close(dialog);
            });
        }

        var seasonNumber = null;
        var episodeNumber = null;
        if (item && item.Type === 'Episode') {
            if (item.ParentIndexNumber > 0) seasonNumber = item.ParentIndexNumber;
            if (item.IndexNumber > 0) episodeNumber = item.IndexNumber;
        }
        var mEp = /S(\d{1,2})\s*E(\d{1,2})/i.exec(subTitle || '');
        if (mEp) {
            seasonNumber = parseInt(mEp[1], 10);
            episodeNumber = parseInt(mEp[2], 10);
        } else {
            mEp = /(\d{1,2})x(\d{1,2})/i.exec(subTitle || '');
            if (mEp) {
                seasonNumber = parseInt(mEp[1], 10);
                episodeNumber = parseInt(mEp[2], 10);
            } else {
                mEp = /第(\d{1,2})季第(\d{1,2})集/.exec(subTitle || '');
                if (mEp) {
                    seasonNumber = parseInt(mEp[1], 10);
                    episodeNumber = parseInt(mEp[2], 10);
                }
            }
        }

        apiClient.ajax({
            type: 'POST',
            url: apiClient.getUrl('MediaInfoKeeper/Items/DownloadSubhd'),
            data: JSON.stringify({
                Id: itemId,
                SubId: subId,
                Filename: '',
                SeasonNumber: seasonNumber,
                EpisodeNumber: episodeNumber,
                MediaSourceId: mediaSourceId || null
            }),
            contentType: 'application/json',
            dataType: 'json'
        }).then(function (result) {
            loading.hide();
            if (result && result.Succeeded) {
                toast('字幕下载完成: ' + (result.Message || ''));
            } else {
                toast('下载失败: ' + (result && result.Message ? result.Message : '未知错误'));
            }
        }).catch(function (err) {
            loading.hide();
            toast('下载失败: ' + ((err && err.message) || (err && err.statusText) || '网络错误'));
        });
    }

    function downloadSubhdBatch(item, epKeys, episodeBest, episodeMeta, dialog, selectedSeasons, skipExisting, mediaSourceId) {
        var itemId = item.Id || item.Guid || item.ItemId || '';
        var subIds = epKeys.map(function (k) { return episodeBest[k].SubId; });
        var seasonNumbers = epKeys.map(function (k) { return episodeMeta[k].season; });
        var episodeNumbers = epKeys.map(function (k) { return episodeMeta[k].ep; });
        var apiClient = connectionManager.currentApiClient();

        loading.show();
        if (dialog) {
            Emby.importModule('./modules/dialoghelper/dialoghelper.js').then(function (mod) {
                var dh = mod && mod.default ? mod.default : mod;
                dh.close(dialog);
            });
        }

        apiClient.ajax({
            type: 'POST',
            url: apiClient.getUrl('MediaInfoKeeper/Items/DownloadSubhdBatch'),
            data: JSON.stringify({
                Id: itemId,
                SubIds: subIds,
                SeasonNumbers: seasonNumbers,
                EpisodeNumbers: episodeNumbers,
                SelectedSeasons: selectedSeasons || [],
                SkipExisting: !!skipExisting,
                MediaSourceId: mediaSourceId || null
            }),
            contentType: 'application/json',
            dataType: 'json'
        }).then(function (result) {
            loading.hide();
            toast(result && result.Message ? result.Message : '批量下载完成');
        }).catch(function (err) {
            loading.hide();
            toast('批量下载失败: ' + ((err && err.message) || (err && err.statusText) || '网络错误'));
        });
    }

    function escapeHtml(text) {
        if (!text) return '';
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }
    // ========== SubHD 字幕搜索结束 ==========

    function buildCommandSource() {
        return {
            getCommands: function (options) {
                const items = getSupportedItems(options);
                const libraryItems = getLibraryItems(options);
                const commands = [];

                if (libraryItems.length === 1) {
                    commands.push({name: getCopyLibraryCommandName(), id: 'copy_library', icon: 'content_copy'});
                    commands.push({name: getCommandName(), id: 'extract_media_info', icon: '4k'});
                    commands.push({
                        name: getDeleteCommandName(),
                        id: 'delete_media_info_persist',
                        icon: 'delete_forever'
                    });
                    commands.push({name: getScanIntroCommandName(), id: 'scan_intro', icon: 'graphic_eq'});
                    commands.push({
                        name: getScanExternalFilesCommandName(),
                        id: 'scan_external_files',
                        icon: 'subtitles'
                    });
                }

                if (!items.length) {
                    return commands;
                }

                commands.push({name: getCommandName(), id: 'extract_media_info', icon: '4k'});
                commands.push({name: getDeleteCommandName(), id: 'delete_media_info_persist', icon: 'delete_forever'});

                const introSupportedTypes = {Episode: true, Season: true, Series: true};
                if (items.every(item => introSupportedTypes[item.Type])) {
                    commands.push({name: getScanIntroCommandName(), id: 'scan_intro', icon: 'graphic_eq'});
                    commands.push({name: getSetIntroCommandName(), id: 'set_intro', icon: 'schedule'});
                    commands.push({name: getClearIntroCommandName(), id: 'clear_intro', icon: 'delete_forever'});
                }

                const externalFilesSupportedTypes = {
                    Movie: true,
                    Episode: true,
                    Season: true,
                    Series: true,
                    Video: true
                };
                if (items.every(item => externalFilesSupportedTypes[item.Type])) {
                    commands.push({
                        name: getScanExternalFilesCommandName(),
                        id: 'scan_external_files',
                        icon: 'subtitles'
                    });
                    commands.push({
                        name: getRenameSubtitlesCommandName(),
                        id: 'rename_subtitles',
                        icon: 'drive_file_rename_outline'
                    });
                }

                const subhdSupportedTypes = {Movie: true, Episode: true, Season: true, Series: true, Video: true};
                if (items.length === 1 && subhdSupportedTypes[items[0].Type]) {
                    commands.push({
                        name: getSubhdSearchCommandName(),
                        id: 'search_subhd',
                        icon: 'search'
                    });
                }

                return commands;
            },
            executeCommand: function (command, items) {
                if (!items || !items.length) {
                    return;
                }

                if (command === 'copy_library') {
                    return api.copyLibrary(items);
                }

                const ids = items.map(getCommandItemId).filter(Boolean);
                if (!ids.length) {
                    return;
                }

                if (command === 'extract_media_info') {
                    return api.extractMediaInfo(ids);
                }

                if (command === 'delete_media_info_persist') {
                    return api.deleteMediaInfoPersist(ids);
                }

                if (command === 'scan_intro') {
                    return api.scanIntro(ids);
                }

                if (command === 'scan_external_files') {
                    return resolveMediaSourceContext(items).then(function (ctx) {
                        return api.scanExternalFiles(ctx.ids, ctx.mediaSourceId);
                    }).catch(function () {});
                }

                if (command === 'rename_subtitles') {
                    return resolveMediaSourceContext(items).then(function (ctx) {
                        return api.renameSubtitles(ctx.ids, ctx.mediaSourceId);
                    }).catch(function () {});
                }

                if (command === 'set_intro') {
                    return api.setIntro(ids, items);
                }

                if (command === 'clear_intro') {
                    return api.clearIntro(ids);
                }

                if (command === 'search_subhd') {
                    return resolveMediaSourceContext(items).then(function (ctx) {
                        return searchSubhd(items[0], ctx.mediaSourceId);
                    }).catch(function () {});
                }
            }
        };
    }

    (function registerCommandSource(attempt) {
        const maxAttempts = 120;
        Emby.importModule('./modules/common/itemmanager/itemmanager.js')
            .then(itemmanager => {
                if (!itemmanager || typeof itemmanager.registerCommandSource !== 'function') {
                    throw new Error('itemmanager unavailable');
                }

                if (commandSourceState.registered) {
                    return;
                }

                itemmanager.registerCommandSource(buildCommandSource());
                commandSourceState.registered = true;
            })
            .catch(() => {
                if (attempt < maxAttempts) {
                    setTimeout(() => registerCommandSource(attempt + 1), 250);
                }
            });
    })(0);

    return api;
});
