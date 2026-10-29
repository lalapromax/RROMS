// Behaviour shared by every page of Brasserie La Mélancolie. No framework: menus, dialogs, drawers, flash messages,
// password show/hide, in-page tabs and small helpers. Everything is opt-in through data- attributes so a page that
// does not use a feature pays nothing for it.
(function () {
    'use strict';

    var $ = function (sel, root) { return (root || document).querySelector(sel); };
    var $$ = function (sel, root) { return Array.prototype.slice.call((root || document).querySelectorAll(sel)); };

    // ---------------------------------------------------------------- popover menus (account menu, "More")
    // <div data-menu><button data-menu-toggle aria-expanded="false" aria-controls="x">..</button><div id="x" hidden>..</div></div>
    function closeMenu(wrap, returnFocus) {
        var btn = $('[data-menu-toggle]', wrap);
        var panel = btn && document.getElementById(btn.getAttribute('aria-controls'));
        if (!panel || panel.hidden) return;
        panel.hidden = true;
        btn.setAttribute('aria-expanded', 'false');
        if (returnFocus) btn.focus();
    }
    function openMenu(wrap) {
        $$('[data-menu]').forEach(function (w) { if (w !== wrap) closeMenu(w); });
        var btn = $('[data-menu-toggle]', wrap);
        var panel = btn && document.getElementById(btn.getAttribute('aria-controls'));
        if (!panel) return;
        panel.hidden = false;
        btn.setAttribute('aria-expanded', 'true');
        var first = $('a,button', panel);
        if (first && document.activeElement === btn && window.__lastKeyboard) first.focus();
    }
    document.addEventListener('keydown', function () { window.__lastKeyboard = true; }, true);
    document.addEventListener('mousedown', function () { window.__lastKeyboard = false; }, true);

    document.addEventListener('click', function (e) {
        var toggle = e.target.closest('[data-menu-toggle]');
        if (toggle) {
            var wrap = toggle.closest('[data-menu]');
            var open = toggle.getAttribute('aria-expanded') === 'true';
            open ? closeMenu(wrap) : openMenu(wrap);
            return;
        }
        $$('[data-menu]').forEach(function (w) { if (!w.contains(e.target)) closeMenu(w); });
    });
    document.addEventListener('keydown', function (e) {
        if (e.key !== 'Escape') return;
        $$('[data-menu]').forEach(function (w) { closeMenu(w, true); });
    });
    document.addEventListener('focusin', function (e) {
        $$('[data-menu]').forEach(function (w) { if (!w.contains(e.target)) closeMenu(w); });
    });

    // ---------------------------------------------------------------- dialogs, drawers and the phone menu sheet
    function openDialog(dlg) {
        if (!dlg) return;
        if (typeof dlg.showModal === 'function') { if (!dlg.open) dlg.showModal(); }
        else dlg.setAttribute('open', '');
    }
    function closeDialog(dlg) {
        if (!dlg) return;
        if (typeof dlg.close === 'function') dlg.close(); else dlg.removeAttribute('open');
    }
    function shouldOpenOnLoad(dlg) {
        return !!dlg && dlg.getAttribute('data-open-on-load') === 'true';
    }
    window.blm = window.blm || {};
    window.blm.openDialog = function (id) { openDialog(document.getElementById(id)); };
    window.blm.closeDialog = function (id) { closeDialog(document.getElementById(id)); };

    document.addEventListener('click', function (e) {
        var opener = e.target.closest('[data-dialog-open]');
        if (opener) {
            e.preventDefault();
            var dlg = document.getElementById(opener.getAttribute('data-dialog-open'));
            fillFromTrigger(opener, dlg);
            openDialog(dlg);
            return;
        }
        var closer = e.target.closest('[data-dialog-close]');
        if (closer) {
            var host = closer.closest('dialog');
            if (closer.tagName !== 'A') e.preventDefault();
            closeDialog(host);
            return;
        }
        if (e.target.tagName === 'DIALOG' && e.target.hasAttribute('data-light-dismiss') === false && e.target.classList.contains('sheet')) {
            closeDialog(e.target);
        }
        if (e.target.tagName === 'DIALOG' && e.target.hasAttribute('data-light-dismiss')) closeDialog(e.target);
    });

    document.addEventListener('DOMContentLoaded', function () {
        $$('dialog[data-open-on-load]').forEach(function (dlg) {
            if (shouldOpenOnLoad(dlg)) openDialog(dlg);
        });
    });

    // Copy data-* from a trigger into matching fields: <button data-dialog-open="editX" data-fields="id,name" data-id=".." data-name="..">
    // fills #editX_id and #editX_name. A missing attribute clears the field so a previous record never lingers.
    function fillFromTrigger(btn, dlg) {
        var fields = (btn.getAttribute('data-fields') || '').split(',').map(function (s) { return s.trim(); }).filter(Boolean);
        if (!fields.length || !dlg) return;
        fields.forEach(function (f) {
            var target = document.getElementById(dlg.id + '_' + f);
            if (!target) return;
            var raw = btn.getAttribute('data-' + f);
            var value = raw === null ? '' : raw;
            if (target.type === 'checkbox') target.checked = value === 'true' || value === 'True';
            else if (target.type === 'radio') { $$('input[name="' + target.name + '"]', dlg).forEach(function (r) { r.checked = (r.value === value); }); }
            else if (target.tagName === 'SELECT' || target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') target.value = value;
            else target.textContent = value;
        });
        var title = btn.getAttribute('data-title');
        var h = $('[data-dialog-title]', dlg);
        if (title && h) h.textContent = title;
    }

    // ---------------------------------------------------------------- "Other" chip that reveals a text field
    document.addEventListener('change', function (e) {
        var t = e.target.closest('[data-other-toggle]');
        if (!t) return;
        var box = document.getElementById(t.getAttribute('data-other-toggle'));
        if (!box) return;
        box.hidden = !t.checked;
        if (t.checked) { var f = box.querySelector('input'); if (f) f.focus(); }
    });

    // ---------------------------------------------------------------- flash messages
    document.addEventListener('click', function (e) {
        var b = e.target.closest('[data-dismiss]');
        if (!b) return;
        var f = b.closest('.flash');
        if (f) f.remove();
    });

    // ---------------------------------------------------------------- password show / hide
    document.addEventListener('click', function (e) {
        var b = e.target.closest('[data-toggle-password]');
        if (!b) return;
        e.preventDefault();
        var input = document.getElementById(b.getAttribute('data-toggle-password'));
        if (!input) return;
        var show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        b.textContent = show ? 'Hide' : 'Show';
        b.setAttribute('aria-pressed', show ? 'true' : 'false');
    });

    // ---------------------------------------------------------------- in-page tabs: <div role="tablist" data-tabs> <a href="#panel-id"> ...
    function selectTab(list, link) {
        $$('a[role="tab"]', list).forEach(function (a) {
            var on = a === link;
            if (on) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
            a.setAttribute('aria-selected', on ? 'true' : 'false');
            var panel = document.getElementById((a.getAttribute('href') || '').slice(1));
            if (panel) panel.hidden = !on;
        });
    }
    document.addEventListener('click', function (e) {
        var a = e.target.closest('[data-tabs] a[role="tab"]');
        if (!a) return;
        var list = a.closest('[data-tabs]');
        if (!list.hasAttribute('data-tabs-switch')) return;
        e.preventDefault();
        selectTab(list, a);
        if (history.replaceState) history.replaceState(null, '', a.getAttribute('href'));
    });
    document.addEventListener('DOMContentLoaded', function () {
        $$('[data-tabs][data-tabs-switch]').forEach(function (list) {
            var wanted = location.hash && $('a[role="tab"][href="' + location.hash + '"]', list);
            selectTab(list, wanted || $('a[role="tab"][aria-current]', list) || $('a[role="tab"]', list));
        });
    });

    // ---------------------------------------------------------------- the Menu page's three sections in the top bar
    document.addEventListener('DOMContentLoaded', function () {
        var links = $$('header.nav .nav-links a[href*="/Menu"]');
        if (!links.length || !/\/Menu/i.test(location.pathname)) return;
        function mark() {
            var h = location.hash || '#carte';
            links.forEach(function (a) {
                var frag = (a.getAttribute('href') || '').split('#')[1];
                if (('#' + frag) === h) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
            });
        }
        mark();
        window.addEventListener('hashchange', mark);
    });

    // ---------------------------------------------------------------- auto-dismiss the green "done" message
    document.addEventListener('DOMContentLoaded', function () {
        $$('.flash:not(.bad)').forEach(function (f) { setTimeout(function () { if (f.parentNode) f.remove(); }, 7000); });
        $$('dialog[data-open-on-load]').forEach(function (dlg) {
            if (shouldOpenOnLoad(dlg)) openDialog(dlg);
        });
    });

    // ---------------------------------------------------------------- small helpers
    // <select data-autosubmit> and <input type="checkbox" data-autosubmit> submit their form when changed
    document.addEventListener('change', function (e) {
        var el = e.target.closest('[data-autosubmit]');
        if (el && el.form) el.form.submit();
    });
    // <form data-confirm="Are you sure?"> asks before posting (used only where a page has no dialog of its own)
    document.addEventListener('submit', function (e) {
        var f = e.target;
        var msg = f.getAttribute && f.getAttribute('data-confirm');
        if (msg && !window.confirm(msg)) e.preventDefault();
    });
    // <form data-autosubmit> saves as soon as a switch is flipped; its [data-no-js-only] save button is then not needed
    document.addEventListener('change', function (e) {
        var f = e.target.closest && e.target.closest('form[data-autosubmit]');
        if (f) { if (f.requestSubmit) f.requestSubmit(); else f.submit(); }
    });
    $$('form[data-autosubmit] [data-no-js-only]').forEach(function (el) { el.hidden = true; });
    // <button data-print> prints the page (the receipt page has a print layout that shows only the receipt)



    // ---------------------------------------------------------------- "Other" chip that reveals a text field
    document.addEventListener('change', function (e) {
        var t = e.target.closest('[data-other-toggle]');
        if (!t) return;
        var box = document.getElementById(t.getAttribute('data-other-toggle'));
        if (!box) return;
        box.hidden = !t.checked;
        if (t.checked) { var f = box.querySelector('input'); if (f) f.focus(); }
    });

    // ---------------------------------------------------------------- flash messages
    document.addEventListener('click', function (e) {
        var b = e.target.closest('[data-dismiss]');
        if (!b) return;
        var f = b.closest('.flash');
        if (f) f.remove();
    });

    // ---------------------------------------------------------------- password show / hide
    document.addEventListener('click', function (e) {
        var b = e.target.closest('[data-toggle-password]');
        if (!b) return;
        e.preventDefault();
        var input = document.getElementById(b.getAttribute('data-toggle-password'));
        if (!input) return;
        var show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        b.textContent = show ? 'Hide' : 'Show';
        b.setAttribute('aria-pressed', show ? 'true' : 'false');
    });

    // ---------------------------------------------------------------- in-page tabs: <div role="tablist" data-tabs> <a href="#panel-id"> ...
    function selectTab(list, link) {
        $$('a[role="tab"]', list).forEach(function (a) {
            var on = a === link;
            if (on) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
            a.setAttribute('aria-selected', on ? 'true' : 'false');
            var panel = document.getElementById((a.getAttribute('href') || '').slice(1));
            if (panel) panel.hidden = !on;
        });
    }
    document.addEventListener('click', function (e) {
        var a = e.target.closest('[data-tabs] a[role="tab"]');
        if (!a) return;
        var list = a.closest('[data-tabs]');
        if (!list.hasAttribute('data-tabs-switch')) return;
        e.preventDefault();
        selectTab(list, a);
        if (history.replaceState) history.replaceState(null, '', a.getAttribute('href'));
    });
    document.addEventListener('DOMContentLoaded', function () {
        $$('[data-tabs][data-tabs-switch]').forEach(function (list) {
            var wanted = location.hash && $('a[role="tab"][href="' + location.hash + '"]', list);
            selectTab(list, wanted || $('a[role="tab"][aria-current]', list) || $('a[role="tab"]', list));
        });
    });

    // ---------------------------------------------------------------- the Menu page's three sections in the top bar
    document.addEventListener('DOMContentLoaded', function () {
        var links = $$('header.nav .nav-links a[href*="/Menu"]');
        if (!links.length || !/\/Menu/i.test(location.pathname)) return;
        function mark() {
            var h = location.hash || '#carte';
            links.forEach(function (a) {
                var frag = (a.getAttribute('href') || '').split('#')[1];
                if (('#' + frag) === h) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
            });
        }
        mark();
        window.addEventListener('hashchange', mark);
    });

    // ---------------------------------------------------------------- auto-dismiss the green "done" message
    document.addEventListener('DOMContentLoaded', function () {
        $$('.flash:not(.bad)').forEach(function (f) { setTimeout(function () { if (f.parentNode) f.remove(); }, 7000); });
        $$('dialog[data-open-on-load]').forEach(function (dlg) {
            if (shouldOpenOnLoad(dlg)) openDialog(dlg);
        });
    });

    // ---------------------------------------------------------------- small helpers
    // <select data-autosubmit> and <input type="checkbox" data-autosubmit> submit their form when changed
    document.addEventListener('change', function (e) {
        var el = e.target.closest('[data-autosubmit]');
        if (el && el.form) el.form.submit();
    });
    // <form data-confirm="Are you sure?"> asks before posting (used only where a page has no dialog of its own)
    document.addEventListener('submit', function (e) {
        var f = e.target;
        var msg = f.getAttribute && f.getAttribute('data-confirm');
        if (msg && !window.confirm(msg)) e.preventDefault();
    });
    // <form data-autosubmit> saves as soon as a switch is flipped; its [data-no-js-only] save button is then not needed
    document.addEventListener('change', function (e) {
        var f = e.target.closest && e.target.closest('form[data-autosubmit]');
        if (f) { if (f.requestSubmit) f.requestSubmit(); else f.submit(); }
    });
    $$('form[data-autosubmit] [data-no-js-only]').forEach(function (el) { el.hidden = true; });
    // <button data-print> prints the page (the receipt page has a print layout that shows only the receipt)



    document.addEventListener('click', function (e) {
        var opener = e.target.closest('[data-dialog-open]');
        if (opener) {
            e.preventDefault();
            var dlg = document.getElementById(opener.getAttribute('data-dialog-open'));
            fillFromTrigger(opener, dlg);
            openDialog(dlg);
            return;
        }
        var closer = e.target.closest('[data-dialog-close]');
        if (closer) {
            var host = closer.closest('dialog');
            // links inside the phone sheet should still navigate
            if (closer.tagName !== 'A') e.preventDefault();
            closeDialog(host);
            return;
        }
        // a click on the dim backdrop (the dialog element itself) closes it
        if (e.target.tagName === 'DIALOG' && e.target.hasAttribute('data-light-dismiss') === false && e.target.classList.contains('sheet')) {
            closeDialog(e.target);
        }
        if (e.target.tagName === 'DIALOG' && e.target.hasAttribute('data-light-dismiss')) closeDialog(e.target);
    });

    // Only auto-open dialogs whose explicit value is "true". A blank or "false" value must not trigger on page refresh.
    $$('dialog[data-open-on-load]').forEach(function (dlg) {
        if (dlg.getAttribute('data-open-on-load') === 'true') openDialog(dlg);
    });

    // Copy data-* from a trigger into matching fields: <button data-dialog-open="editX" data-fields="id,name" data-id=".." data-name="..">
    // fills #editX_id and #editX_name. A missing attribute clears the field so a previous record never lingers.
    function fillFromTrigger(btn, dlg) {
        var fields = (btn.getAttribute('data-fields') || '').split(',').map(function (s) { return s.trim(); }).filter(Boolean);
        if (!fields.length || !dlg) return;
        fields.forEach(function (f) {
            var target = document.getElementById(dlg.id + '_' + f);
            if (!target) return;
            var raw = btn.getAttribute('data-' + f);
            var value = raw === null ? '' : raw;
            if (target.type === 'checkbox') target.checked = value === 'true' || value === 'True';
            else if (target.type === 'radio') { $$('input[name="' + target.name + '"]', dlg).forEach(function (r) { r.checked = (r.value === value); }); }
            else if (target.tagName === 'SELECT' || target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') target.value = value;
            else target.textContent = value;
        });
        var title = btn.getAttribute('data-title');
        var h = $('[data-dialog-title]', dlg);
        if (title && h) h.textContent = title;
    }

    // ---------------------------------------------------------------- "Other" chip that reveals a text field
    document.addEventListener('change', function (e) {
        var t = e.target.closest('[data-other-toggle]');
        if (!t) return;
        var box = document.getElementById(t.getAttribute('data-other-toggle'));
        if (!box) return;
        box.hidden = !t.checked;
        if (t.checked) { var f = box.querySelector('input'); if (f) f.focus(); }
    });

    // ---------------------------------------------------------------- flash messages
    document.addEventListener('click', function (e) {
        var b = e.target.closest('[data-dismiss]');
        if (!b) return;
        var f = b.closest('.flash');
        if (f) f.remove();
    });

    // ---------------------------------------------------------------- password show / hide
    document.addEventListener('click', function (e) {
        var b = e.target.closest('[data-toggle-password]');
        if (!b) return;
        e.preventDefault();
        var input = document.getElementById(b.getAttribute('data-toggle-password'));
        if (!input) return;
        var show = input.type === 'password';
        input.type = show ? 'text' : 'password';
        b.textContent = show ? 'Hide' : 'Show';
        b.setAttribute('aria-pressed', show ? 'true' : 'false');
    });

    // ---------------------------------------------------------------- in-page tabs: <div role="tablist" data-tabs> <a href="#panel-id"> ...
    function selectTab(list, link) {
        $$('a[role="tab"]', list).forEach(function (a) {
            var on = a === link;
            if (on) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
            a.setAttribute('aria-selected', on ? 'true' : 'false');
            var panel = document.getElementById((a.getAttribute('href') || '').slice(1));
            if (panel) panel.hidden = !on;
        });
    }
    document.addEventListener('click', function (e) {
        var a = e.target.closest('[data-tabs] a[role="tab"]');
        if (!a) return;
        var list = a.closest('[data-tabs]');
        if (!list.hasAttribute('data-tabs-switch')) return;
        e.preventDefault();
        selectTab(list, a);
        if (history.replaceState) history.replaceState(null, '', a.getAttribute('href'));
    });
    document.addEventListener('DOMContentLoaded', function () {
        $$('[data-tabs][data-tabs-switch]').forEach(function (list) {
            var wanted = location.hash && $('a[role="tab"][href="' + location.hash + '"]', list);
            selectTab(list, wanted || $('a[role="tab"][aria-current]', list) || $('a[role="tab"]', list));
        });
    });

    // ---------------------------------------------------------------- the Menu page's three sections in the top bar
    document.addEventListener('DOMContentLoaded', function () {
        var links = $$('header.nav .nav-links a[href*="/Menu"]');
        if (!links.length || !/\/Menu/i.test(location.pathname)) return;
        function mark() {
            var h = location.hash || '#carte';
            links.forEach(function (a) {
                var frag = (a.getAttribute('href') || '').split('#')[1];
                if (('#' + frag) === h) a.setAttribute('aria-current', 'page'); else a.removeAttribute('aria-current');
            });
        }
        mark();
        window.addEventListener('hashchange', mark);
    });

    // ---------------------------------------------------------------- auto-dismiss the green "done" message
    document.addEventListener('DOMContentLoaded', function () {
        $$('.flash:not(.bad)').forEach(function (f) { setTimeout(function () { if (f.parentNode) f.remove(); }, 7000); });
    });

    // ---------------------------------------------------------------- small helpers
    // <select data-autosubmit> and <input type="checkbox" data-autosubmit> submit their form when changed
    document.addEventListener('change', function (e) {
        var el = e.target.closest('[data-autosubmit]');
        if (el && el.form) el.form.submit();
    });
    // <form data-confirm="Are you sure?"> asks before posting (used only where a page has no dialog of its own)
    document.addEventListener('submit', function (e) {
        var f = e.target;
        var msg = f.getAttribute && f.getAttribute('data-confirm');
        if (msg && !window.confirm(msg)) e.preventDefault();
    });
    // <form data-autosubmit> saves as soon as a switch is flipped; its [data-no-js-only] save button is then not needed
    document.addEventListener('change', function (e) {
        var f = e.target.closest && e.target.closest('form[data-autosubmit]');
        if (f) { if (f.requestSubmit) f.requestSubmit(); else f.submit(); }
    });
    $$('form[data-autosubmit] [data-no-js-only]').forEach(function (el) { el.hidden = true; });
    // <button data-print> prints the page (the receipt page has a print layout that shows only the receipt)
    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-print]')) { e.preventDefault(); window.print(); }
    });
    // buttons that act as radio groups of chips: <div data-chip-group="single"> <button aria-pressed>
    document.addEventListener('click', function (e) {
        var chip = e.target.closest('[data-chip-group] [aria-pressed]');
        if (!chip) return;
        var group = chip.closest('[data-chip-group]');
        if (group.getAttribute('data-chip-group') === 'single') {
            $$('[aria-pressed]', group).forEach(function (c) { c.setAttribute('aria-pressed', c === chip ? 'true' : 'false'); });
        } else {
            chip.setAttribute('aria-pressed', chip.getAttribute('aria-pressed') === 'true' ? 'false' : 'true');
        }
        group.dispatchEvent(new CustomEvent('chipchange', { bubbles: true, detail: { chip: chip } }));
    });
})();
