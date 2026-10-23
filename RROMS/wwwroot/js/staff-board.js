// A board that refreshes itself: <div id="board" data-refresh="20">. It fetches the same page again and swaps in the new board,
// but never while a dialog is open or a button inside the board has focus, so a tap is never taken from under a finger.
(function () {
    'use strict';
    var board = document.getElementById('board');
    if (!board) return;
    var secs = parseInt(board.getAttribute('data-refresh') || '20', 10);
    if (!secs || secs < 5) return;

    function busy() {
        return document.hidden || document.querySelector('dialog[open]') ||
            (document.activeElement && board.contains(document.activeElement) && /^(BUTTON|SELECT|INPUT|A)$/.test(document.activeElement.tagName));
    }
    function refresh() {
        if (busy()) return;
        fetch(location.href, { credentials: 'same-origin', headers: { 'X-Requested-With': 'fetch' } })
            .then(function (r) { return r.ok ? r.text() : null; })
            .then(function (html) {
                if (!html || busy()) return;
                var doc = new DOMParser().parseFromString(html, 'text/html');
                var fresh = doc.getElementById('board');
                if (fresh && fresh.innerHTML !== board.innerHTML) board.innerHTML = fresh.innerHTML;
            })
            .catch(function () { /* offline for a moment: the next tick tries again */ });
    }
    setInterval(refresh, secs * 1000);
})();
