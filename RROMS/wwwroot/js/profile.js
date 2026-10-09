// Profile page: pick a photo and it uploads at once (after a size check), and the section list follows the scroll.
(function () {
    'use strict';
    var MAX = 2 * 1024 * 1024;

    // --- photo: check the size before sending; the server checks again (type, signature, size)
    var input = document.getElementById('profilePicture');
    var form = document.getElementById('picForm');
    var err = document.getElementById('picError');
    if (input && form) {
        var manual = form.querySelector('[data-no-js-only]');
        if (manual) manual.hidden = true;
        input.addEventListener('change', function () {
            var file = input.files && input.files[0];
            if (!file) return;
            if (file.size > MAX) {
                if (err) err.textContent = 'That picture is over 2 MB. Please choose a smaller one.';
                input.value = '';
                return;
            }
            if (err) err.textContent = '';
            if (form.requestSubmit) form.requestSubmit(); else form.submit();
        });
    }

    // --- section list: mark the section being read
    var nav = document.querySelector('[data-pf-nav]');
    if (!nav || !('IntersectionObserver' in window)) return;
    var links = Array.prototype.slice.call(nav.querySelectorAll('a[href^="#"]'));
    var map = {};
    links.forEach(function (a) { map[a.getAttribute('href').slice(1)] = a; });
    var sections = links.map(function (a) { return document.getElementById(a.getAttribute('href').slice(1)); }).filter(Boolean);
    function mark(id) {
        links.forEach(function (a) {
            if (a === map[id]) a.setAttribute('aria-current', 'true'); else a.removeAttribute('aria-current');
        });
    }
    var visible = {};
    var io = new IntersectionObserver(function (entries) {
        entries.forEach(function (en) { visible[en.target.id] = en.isIntersecting; });
        for (var i = 0; i < sections.length; i++) {
            if (visible[sections[i].id]) { mark(sections[i].id); return; }
        }
    }, { rootMargin: '-110px 0px -55% 0px' });
    sections.forEach(function (s) { io.observe(s); });
    // the danger zone has no link of its own; leaving it must not leave the last section unmarked
    links.forEach(function (a) { a.addEventListener('click', function () { mark(a.getAttribute('href').slice(1)); }); });
})();
