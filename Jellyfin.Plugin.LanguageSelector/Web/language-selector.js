(function () {
    'use strict';

    const CONFIG = {
        checkInterval: 500,
        observerThrottleMs: 300
    };

    function apiAvailable() {
        if (typeof ApiClient === 'undefined' || !ApiClient) {
            console.error('[LanguageSelector] ApiClient not available');
            return false;
        }
        return true;
    }

    function apiBase() {
        return (typeof ApiClient !== 'undefined' && ApiClient && ApiClient._serverAddress)
            ? ApiClient._serverAddress
            : '';
    }

    function flagUrl(icon) {
        // Served anonymously with image/svg+xml by the plugin's own controller;
        // ApiClient.getUrl keeps the server address and base path correct.
        if (typeof ApiClient !== 'undefined' && ApiClient && ApiClient.getUrl) {
            return ApiClient.getUrl('LanguageSelector/flags/' + icon);
        }
        return '/LanguageSelector/flags/' + icon;
    }

    const FLAGS = {
        'de': { icon: 'de.svg', label: 'German Audio' },
        'jp': { icon: 'jp.svg', label: 'Japanese Audio' },
        'jp-de': { icon: 'jp-de.svg', label: 'Japanese Audio + German Subtitles' },
        'jp-us': { icon: 'jp-en.svg', label: 'Japanese Audio + English Subtitles' },
        'us': { icon: 'us.svg', label: 'English Audio' }
    };

    const LANG_MAP = {
        'ger': 'de', 'deu': 'de', 'de': 'de',
        'jpn': 'jp', 'ja': 'jp', 'jp': 'jp',
        'eng': 'us', 'en': 'us', 'us': 'us'
    };

    class LanguageSelector {
        constructor() {
            this.currentItemId = null;
            this.observer = null;
            this.throttleTimer = null;
            this.isOnListPage = false;
            this.init();
        }

        init() {
            if (!apiAvailable()) {
                // ApiClient may not be ready yet on very first load; retry shortly.
                setTimeout(() => this.init(), 1000);
                return;
            }
            this.setupPageObserver();
            this.checkCurrentPage();
        }

        setupPageObserver() {
            if (this.observer) {
                this.observer.disconnect();
            }

            this.observer = new MutationObserver(() => {
                if (this.throttleTimer) return;
                this.throttleTimer = setTimeout(() => {
                    this.throttleTimer = null;
                    this.checkCurrentPage();
                }, CONFIG.observerThrottleMs);
            });

            this.observer.observe(document.body, { childList: true, subtree: true });
        }

        checkCurrentPage() {
            if (!apiAvailable()) return;

            const itemId = this.getItemIdFromUrl();

            if (!itemId) {
                if (!this.isOnListPage) {
                    this.isOnListPage = true;
                    this.checkEpisodeList();
                }
                return;
            }

            this.isOnListPage = false;

            // Re-render even for the same id if our flags were wiped by a redraw.
            // Scope the check to the visible page: cached, hidden detail pages can
            // still contain a stale flags container.
            if (itemId === this.currentItemId && this.getVisiblePage().querySelector('.series-language-info')) {
                return;
            }

            this.currentItemId = itemId;
            this.checkItemType();
        }

        getItemIdFromUrl() {
            // Jellyfin 10.10 uses #/details?id=..., older builds #/item?id=...
            const match = window.location.hash.match(/\/(?:details|item)\?id=([a-f0-9-]+)/i);
            return match ? match[1] : null;
        }

        async fetchJson(url) {
            const response = await fetch(url, {
                headers: { 'X-Emby-Token': ApiClient.accessToken() }
            });
            if (!response.ok) return null;
            return response.json();
        }

        async checkItemType() {
            if (!this.currentItemId || !apiAvailable()) return;

            try {
                const item = await this.fetchJson(
                    `${apiBase()}/Users/${ApiClient.getCurrentUserId()}/Items/${this.currentItemId}?fields=MediaStreams,MediaSources`
                );
                if (!item) return;

                switch (item.Type) {
                    case 'Series':
                    case 'Season':
                        this.showSeriesLanguages(item);
                        break;
                    case 'Movie':
                    case 'Episode':
                    case 'Video':
                    case 'MusicVideo':
                        this.showItemLanguages(item);
                        break;
                    default:
                        break;
                }
            } catch (error) {
                console.error('[LanguageSelector] Error checking item type:', error);
            }
        }

        // Single playable item: build clickable options straight from its streams.
        showItemLanguages(item) {
            const options = this.buildOptionsFromItem(item);
            if (options.length > 0) {
                this.renderLanguageInfo(options, true);
            }
        }

        async showSeriesLanguages(item) {
            try {
                const seriesId = item.Type === 'Series' ? item.Id : item.SeriesId;
                if (!seriesId) return;

                const data = await this.fetchJson(
                    `${apiBase()}/Shows/${seriesId}/Episodes?userId=${ApiClient.getCurrentUserId()}&fields=MediaStreams`
                );
                if (!data) return;

                const languages = this.collectLanguagesFromEpisodes(data.Items || []);
                const options = languages.map(code => ({ flagIcon: code }));
                if (options.length > 0) {
                    this.renderLanguageInfo(options, false);
                }
            } catch (error) {
                console.error('[LanguageSelector] Error fetching series languages:', error);
            }
        }

        buildOptionsFromItem(item) {
            if (!item.MediaStreams) return [];

            const audioStreams = item.MediaStreams.filter(s => s.Type === 'Audio');
            const subtitleStreams = item.MediaStreams.filter(s => s.Type === 'Subtitle' && !s.IsForced);

            const seen = new Set();
            const options = [];

            const addOption = (flagIcon, audioIndex, subtitleIndex) => {
                if (!FLAGS[flagIcon] || seen.has(flagIcon)) return;
                seen.add(flagIcon);
                options.push({
                    flagIcon: flagIcon,
                    audioStreamIndex: audioIndex,
                    subtitleStreamIndex: subtitleIndex,
                    displayName: FLAGS[flagIcon].label
                });
            };

            audioStreams.forEach(audio => {
                const audioLang = this.normalizeLanguage(audio.Language);
                if (!audioLang) return;

                addOption(audioLang, audio.Index, -1);

                subtitleStreams.forEach(sub => {
                    const subLang = this.normalizeLanguage(sub.Language);
                    if (subLang) {
                        addOption(`${audioLang}-${subLang}`, audio.Index, sub.Index);
                    }
                });
            });

            return options;
        }

        collectLanguagesFromEpisodes(episodes) {
            const languageSet = new Set();

            episodes.forEach(episode => {
                if (!episode.MediaStreams) return;

                const audioStreams = episode.MediaStreams.filter(s => s.Type === 'Audio');
                const subtitleStreams = episode.MediaStreams.filter(s => s.Type === 'Subtitle');

                audioStreams.forEach(audio => {
                    const audioLang = this.normalizeLanguage(audio.Language);
                    if (audioLang && FLAGS[audioLang]) {
                        languageSet.add(audioLang);
                    }
                    subtitleStreams.forEach(sub => {
                        const subLang = this.normalizeLanguage(sub.Language);
                        if (audioLang && subLang) {
                            const combined = `${audioLang}-${subLang}`;
                            if (FLAGS[combined]) {
                                languageSet.add(combined);
                            }
                        }
                    });
                });
            });

            return Array.from(languageSet);
        }

        normalizeLanguage(lang) {
            if (!lang) return null;
            return LANG_MAP[lang.toLowerCase()] || null;
        }

        renderLanguageInfo(options, clickable) {
            const existingInfo = document.querySelector('.series-language-info');
            if (existingInfo) {
                existingInfo.remove();
            }

            const valid = options.filter(o => FLAGS[o.flagIcon]);
            if (valid.length === 0) return;

            const anchor = this.findDetailAnchor();
            if (!anchor || !anchor.parentElement) return;

            const container = document.createElement('div');
            container.className = 'series-language-info';
            container.style.cssText = 'margin: 1.5em 0; padding: 1em; background: rgba(0,0,0,0.3); border-radius: 8px;';

            const title = document.createElement('div');
            title.textContent = clickable ? 'Play in language:' : 'Available languages:';
            title.style.cssText = 'font-size: 1.1em; margin-bottom: 0.6em; color: #fff;';
            container.appendChild(title);

            const flagGroup = document.createElement('div');
            flagGroup.style.cssText = 'display: flex; gap: 0.6em; flex-wrap: wrap;';

            valid.forEach(option => {
                const flagConfig = FLAGS[option.flagIcon];

                const flagItem = document.createElement(clickable ? 'button' : 'div');
                flagItem.className = 'language-info-flag';
                flagItem.title = flagConfig.label;
                flagItem.style.cssText = 'display: flex; align-items: center; gap: 0.5em; padding: 0.5em 1em; '
                    + 'background: rgba(255,255,255,0.1); border: none; border-radius: 6px; color: #fff;'
                    + (clickable ? ' cursor: pointer;' : '');

                if (clickable) {
                    flagItem.type = 'button';
                    flagItem.addEventListener('click', (e) => {
                        e.preventDefault();
                        e.stopPropagation();
                        this.handleFlagClick(option);
                    });
                    flagItem.addEventListener('mouseenter', () => {
                        flagItem.style.background = 'rgba(255,255,255,0.25)';
                    });
                    flagItem.addEventListener('mouseleave', () => {
                        flagItem.style.background = 'rgba(255,255,255,0.1)';
                    });
                }

                const img = document.createElement('img');
                img.src = flagUrl(flagConfig.icon);
                img.alt = flagConfig.label;
                img.style.cssText = 'width: 32px; height: 24px; border-radius: 4px;';

                const label = document.createElement('span');
                label.textContent = flagConfig.label;
                label.style.cssText = 'font-size: 0.9em;';

                flagItem.appendChild(img);
                flagItem.appendChild(label);
                flagGroup.appendChild(flagItem);
            });

            container.appendChild(flagGroup);
            anchor.parentElement.insertBefore(container, anchor.nextSibling);
        }

        getVisiblePage() {
            const pages = document.querySelectorAll('.itemDetailPage, .detailPage, .page');
            for (const page of pages) {
                if (page.offsetParent !== null) {
                    return page;
                }
            }
            return document;
        }

        // Detail pages for movies/episodes don't always expose .detailLogo, and
        // several cached detail pages can coexist in the DOM. Pick an anchor
        // inside the currently visible detail page.
        findDetailAnchor() {
            const scope = this.getVisiblePage();

            const selectors = [
                '.detailPagePrimaryContainer .detailButtons',
                '.mainDetailButtons',
                '.detailButtons',
                '.detailLogo',
                '.itemName',
                '.nameContainer',
                '.detailPagePrimaryContainer',
                '.detailImageContainer'
            ];

            for (const selector of selectors) {
                const el = scope.querySelector(selector);
                if (el) return el;
            }
            return null;
        }

        async checkEpisodeList() {
            if (!apiAvailable()) return;

            const episodeCards = document.querySelectorAll('.listItem[data-type="Episode"], .listItem[data-isfolder="false"]');
            if (episodeCards.length === 0) return;

            const promises = Array.from(episodeCards).map(async (card) => {
                if (card.querySelector('.episode-language-indicator')) return;

                const itemId = card.getAttribute('data-id');
                if (!itemId) return;

                try {
                    const episode = await this.fetchJson(
                        `${apiBase()}/Users/${ApiClient.getCurrentUserId()}/Items/${itemId}?fields=MediaStreams`
                    );
                    if (!episode) return;

                    const languages = this.collectLanguagesFromEpisodes([episode]);
                    if (languages.length > 0) {
                        this.addEpisodeLanguageIndicator(card, languages);
                    }
                } catch (error) {
                    console.error('[LanguageSelector] Error fetching episode languages:', error);
                }
            });

            await Promise.allSettled(promises);
        }

        addEpisodeLanguageIndicator(card, languages) {
            if (card.querySelector('.episode-language-indicator')) return;

            const cardContent = card.querySelector('.listItemBody') || card;

            const indicator = document.createElement('div');
            indicator.className = 'episode-language-indicator';
            indicator.style.cssText = 'display: flex; gap: 0.3em; margin-top: 0.3em;';

            languages.forEach(langCode => {
                const flagConfig = FLAGS[langCode];
                if (!flagConfig) return;

                const img = document.createElement('img');
                img.src = flagUrl(flagConfig.icon);
                img.alt = flagConfig.label;
                img.title = flagConfig.label;
                img.style.cssText = 'width: 24px; height: 18px; border-radius: 3px; opacity: 0.85;';
                indicator.appendChild(img);
            });

            cardContent.appendChild(indicator);
        }

        async handleFlagClick(option) {
            if (!apiAvailable()) {
                this.showError('API not available');
                return;
            }

            try {
                this.setButtonLoading(true);

                const userId = ApiClient.getCurrentUserId();
                const item = await ApiClient.getItem(userId, this.currentItemId);

                if (!item || !item.MediaSources || item.MediaSources.length === 0) {
                    throw new Error('No media sources found for this item');
                }

                const mediaSource = item.MediaSources[0];
                const userdata = item.UserData || {};
                const resumeTicks = userdata.PlaybackPositionTicks || 0;

                const playOptions = {
                    ids: [this.currentItemId],
                    startPositionTicks: resumeTicks,
                    mediaSourceId: mediaSource.Id,
                    audioStreamIndex: option.audioStreamIndex,
                    subtitleStreamIndex: (option.subtitleStreamIndex !== undefined && option.subtitleStreamIndex !== null)
                        ? option.subtitleStreamIndex : -1
                };

                let started = false;

                // The webpack-bundled playback manager is not exposed globally in
                // modern jellyfin-web, but try it first in case a build provides it.
                const mgr = window.playbackManager;
                if (mgr && typeof mgr.play === 'function') {
                    await mgr.play(playOptions);
                    started = true;
                }

                if (!started) {
                    // Reliable path: remote-control our own session through the
                    // Sessions API — the same mechanism as Jellyfin's "Play On".
                    started = await this.playViaSessionsApi(playOptions);
                }

                if (!started) {
                    this.fallbackPlayback(playOptions);
                }

                this.setButtonLoading(false);
            } catch (error) {
                console.error('[LanguageSelector] Error starting playback:', error);
                this.setButtonLoading(false);
                this.showError('Failed to start playback. Please try again.');
            }
        }

        async playViaSessionsApi(playOptions) {
            try {
                const deviceId = ApiClient.deviceId();
                const sessions = await this.fetchJson(
                    `${apiBase()}/Sessions?deviceId=${encodeURIComponent(deviceId)}`
                );
                if (!sessions || sessions.length === 0) {
                    console.warn('[LanguageSelector] No controllable session found for this device');
                    return false;
                }

                const params = new URLSearchParams();
                params.set('playCommand', 'PlayNow');
                params.set('itemIds', this.currentItemId);
                params.set('audioStreamIndex', playOptions.audioStreamIndex);
                params.set('subtitleStreamIndex', playOptions.subtitleStreamIndex);
                if (playOptions.mediaSourceId) {
                    params.set('mediaSourceId', playOptions.mediaSourceId);
                }
                if (playOptions.startPositionTicks) {
                    params.set('startPositionTicks', playOptions.startPositionTicks);
                }

                const response = await fetch(
                    `${apiBase()}/Sessions/${sessions[0].Id}/Playing?${params.toString()}`,
                    {
                        method: 'POST',
                        headers: { 'X-Emby-Token': ApiClient.accessToken() }
                    }
                );

                if (!response.ok) {
                    console.warn('[LanguageSelector] Play command failed:', response.status);
                    return false;
                }
                return true;
            } catch (error) {
                console.error('[LanguageSelector] Sessions API playback failed:', error);
                return false;
            }
        }

        fallbackPlayback(playOptions) {
            if (!apiAvailable()) return;

            const params = new URLSearchParams();
            params.set('id', this.currentItemId);
            params.set('serverId', ApiClient.serverId());
            if (playOptions.audioStreamIndex !== undefined) {
                params.set('audioStreamIndex', playOptions.audioStreamIndex);
            }
            if (playOptions.subtitleStreamIndex !== undefined && playOptions.subtitleStreamIndex !== -1) {
                params.set('subtitleStreamIndex', playOptions.subtitleStreamIndex);
            }
            window.location.hash = `#/details?${params.toString()}`;
        }

        setButtonLoading(isLoading) {
            document.querySelectorAll('.language-info-flag').forEach(btn => {
                if (typeof btn.disabled === 'boolean') btn.disabled = isLoading;
                btn.style.opacity = isLoading ? '0.6' : '1';
            });
        }

        showError(message) {
            if (window.Dashboard && window.Dashboard.alert) {
                window.Dashboard.alert(message);
            } else {
                console.error('[LanguageSelector]', message);
            }
        }

        destroy() {
            if (this.observer) {
                this.observer.disconnect();
                this.observer = null;
            }
            if (this.throttleTimer) {
                clearTimeout(this.throttleTimer);
                this.throttleTimer = null;
            }
        }
    }

    function bootstrap() {
        if (!window.languageSelector) {
            window.languageSelector = new LanguageSelector();
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', bootstrap);
    } else {
        bootstrap();
    }
})();
