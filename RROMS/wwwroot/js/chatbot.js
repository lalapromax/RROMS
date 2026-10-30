// "Ask the house": the help chat on guest pages. Talks to POST /api/chatbot/ask. Answers are inserted as plain text, never as HTML.
// The server keeps the conversation (so "yes" is read against the last question); a reply may come with one button to a
// page of this site, which is shown under it.
(function () {
    'use strict';

    var root = document.getElementById('cw');
    var fab = document.getElementById('cwFab');
    var panel = document.getElementById('cwPanel');
    var log = document.getElementById('cwLog');
    var form = document.getElementById('cwForm');
    var input = document.getElementById('cwInput');
    var sugg = document.getElementById('cwSugg');
    var closeBtn = document.getElementById('cwClose');
    if (!root || !fab || !panel || !log || !form || !input) return;
    root.hidden = false; // without script the button would do nothing, so it stays hidden until now

    var KEY = 'blm.chat';
    var history = [];
    var sending = false;

    function load() {
        try { return JSON.parse(window.sessionStorage.getItem(KEY) || 'null'); } catch (e) { return null; }
    }
    function save(open) {
        try { window.sessionStorage.setItem(KEY, JSON.stringify({ open: open, log: history.slice(-30) })); } catch (e) { /* private window: the chat just forgets */ }
    }

    function greeting() {
        var h = new Date().getHours();
        var hello = h < 12 ? 'Good morning.' : h < 18 ? 'Good afternoon.' : 'Good evening.';
        return hello + ' I can help with the menu, opening hours, or finding you a table.';
    }

    // Only a page of this site may become a button; anything else is ignored.
    function safeAction(a) {
        return a && typeof a.url === 'string' && a.url.charAt(0) === '/' && a.url.charAt(1) !== '/' && typeof a.label === 'string' ? a : null;
    }

    // Answers carry no label about how they were produced; the footer of the panel says the replies are automatic.
    function bubble(text, who, remember, action) {
        var p = document.createElement('p');
        p.className = 'cw-m ' + (who === 'user' ? 'u' : 'a');
        p.textContent = text;
        log.appendChild(p);
        action = who === 'user' ? null : safeAction(action);
        if (action) {
            var a = document.createElement('a');
            a.className = 'cw-act';
            a.href = action.url;
            a.textContent = action.label;
            log.appendChild(a);
        }
        log.scrollTop = log.scrollHeight;
        if (remember !== false) { history.push(action ? { w: who, t: text, a: action } : { w: who, t: text }); }
        return p;
    }

    function open(focus) {
        panel.hidden = false;
        fab.hidden = true;
        fab.setAttribute('aria-expanded', 'true');
        if (focus !== false) input.focus();
        save(true);
    }
    function close() {
        panel.hidden = true;
        fab.hidden = false;
        fab.setAttribute('aria-expanded', 'false');
        fab.focus();
        save(false);
    }

    fab.addEventListener('click', function () { open(); });
    if (closeBtn) closeBtn.addEventListener('click', close);
    panel.addEventListener('keydown', function (e) { if (e.key === 'Escape') { e.stopPropagation(); close(); } });

    function ask(text) {
        text = (text || '').trim();
        if (!text || sending) return;
        sending = true;
        var fresh = history.length === 0;   // nothing said yet in this visit: the server starts a new conversation
        bubble(text, 'user');
        input.value = '';
        if (sugg) sugg.hidden = true;
        var wait = bubble('One moment.', 'bot', false);
        wait.classList.add('wait');

        fetch('/api/chatbot/ask', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ message: text, reset: fresh })
        })
            .then(function (res) { return res.json(); })
            .then(function (data) {
                wait.remove();
                bubble(data.answer || 'Sorry, I could not work that out. Please ask the team.', 'bot', true, data.action);
            })
            .catch(function () {
                wait.remove();
                bubble('I cannot reach the house just now. Please try again in a moment, or call us.', 'bot', false);
            })
            .then(function () { sending = false; save(!panel.hidden); });
    }

    form.addEventListener('submit', function (e) { e.preventDefault(); ask(input.value); });
    panel.addEventListener('click', function (e) {
        var b = e.target.closest('[data-cw-ask]');
        if (b) ask(b.getAttribute('data-cw-ask'));
    });

    // restore the conversation from earlier in this visit, or start with a greeting
    var saved = load();
    if (saved && saved.log && saved.log.length) {
        saved.log.forEach(function (m) { bubble(m.t, m.w, true, m.a); });
        if (sugg) sugg.hidden = true;
        if (saved.open) open(false);
    } else {
        bubble(greeting(), 'bot', false);
    }
})();
