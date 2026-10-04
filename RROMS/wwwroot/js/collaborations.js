/* Home page: Collaborations strip.
   1. Logo images that fail to load are removed so the initial letter underneath shows instead.
   2. A social post (Instagram reel/post or YouTube video) is NOT loaded with the page. It loads only when the
      visitor presses play, so no third-party site sees them (or slows the page) unless they ask for it.
      If it can't load - a blocker, a network problem, a site that refuses to be embedded - the visitor is
      told and the plain "Open on ..." link is always there as the way out.
   The embed address comes from the server, which rebuilt it from a parsed post id; it is checked again here. */
(function () {
    'use strict';

    var ALLOWED_HOSTS = ['www.instagram.com', 'www.youtube-nocookie.com'];
    var LOAD_TIMEOUT_MS = 10000;

    function isAllowedEmbed(src) {
        try {
            var url = new URL(src, window.location.href);
            return url.protocol === 'https:' && ALLOWED_HOSTS.indexOf(url.hostname) !== -1;
        } catch (e) {
            return false;
        }
    }

    function showFallback(frame, article) {
        var iframe = frame.querySelector('iframe');
        if (iframe) iframe.remove();

        var button = frame.querySelector('.collab-embed-play');
        if (button) button.hidden = true;

        var note = article.querySelector('.collab-embed-note');
        if (note) note.hidden = false;
    }

    function loadEmbed(button) {
        var frame = button.closest('[data-embed-frame]');
        var article = button.closest('.collab-social');
        var src = button.getAttribute('data-embed-src');
        if (!frame || !article) return;

        if (!isAllowedEmbed(src) || navigator.onLine === false) {
            showFallback(frame, article);
            return;
        }

        var iframe = document.createElement('iframe');
        iframe.src = src;
        iframe.title = button.getAttribute('data-embed-title') || 'Embedded video';
        iframe.loading = 'lazy';
        iframe.referrerPolicy = 'strict-origin-when-cross-origin';
        iframe.setAttribute('allow', 'autoplay; encrypted-media; picture-in-picture; fullscreen');
        iframe.setAttribute('allowfullscreen', '');
        // The embedded page may run its own scripts and open the post in a new tab, but it can't navigate this page.
        iframe.setAttribute('sandbox', 'allow-scripts allow-same-origin allow-popups allow-popups-to-escape-sandbox allow-presentation');

        var loaded = false;
        var timer = window.setTimeout(function () {
            if (!loaded) showFallback(frame, article);
        }, LOAD_TIMEOUT_MS);

        iframe.addEventListener('load', function () {
            loaded = true;
            window.clearTimeout(timer);
        });
        iframe.addEventListener('error', function () {
            window.clearTimeout(timer);
            showFallback(frame, article);
        });

        button.hidden = true;
        frame.appendChild(iframe);
        iframe.focus();
    }

    document.addEventListener('DOMContentLoaded', function () {
        document.querySelectorAll('img[data-collab-img]').forEach(function (img) {
            function drop() { img.remove(); }
            img.addEventListener('error', drop);
            // An image that already failed before this script ran.
            if (img.complete && img.naturalWidth === 0) drop();
        });

        document.querySelectorAll('.collab-embed-play').forEach(function (button) {
            button.addEventListener('click', function () { loadEmbed(button); });
        });
    });
})();
