// Admin > Announcements: fills the one editor from an announcement's data, and runs the "Who should see it?" picker
// (quick choices, boxes, and a search for named people). The browser only builds the form; the server checks the audience
// again and decides what is allowed. See Services/AnnouncementService.cs.
(function () {
    'use strict';
    var dlg = document.getElementById('annDlg');
    var form = document.getElementById('annForm');
    if (!dlg || !form) return;

    function el(name) { return document.getElementById('annDlg_' + name); }
    function set(name, v) { var e = el(name); if (e) e.value = (v === null || v === undefined) ? '' : v; }
    function box(name, on) { var e = el(name); if (e) e.checked = !!on; }

    // ---- who should see it ----------------------------------------------------------------------------------------
    var root = document.getElementById('annAud');
    var pub = el('public'), allC = el('all'), allS = el('staff');
    var tiers = Array.prototype.slice.call(root.querySelectorAll('[data-aud-tier]'));
    var search = el('search'), searchTier = el('searchtier');
    var results = document.getElementById('annResults');
    var peopleBox = document.getElementById('annPeople');
    var summary = document.getElementById('annAudSummary');
    var presets = Array.prototype.slice.call(root.querySelectorAll('[data-preset]'));
    var people = {};      // id -> name
    var timer = null;

    function tierLabel(cb) {
        var l = cb.closest('label');
        return l ? l.textContent.trim() : '';
    }

    function renderPeople() {
        peopleBox.textContent = '';
        Object.keys(people).forEach(function (id) {
            var chip = document.createElement('span');
            chip.appendChild(document.createTextNode(people[id] + ' '));
            var x = document.createElement('button');
            x.type = 'button';
            x.className = 'ppl-x';
            x.setAttribute('aria-label', 'Remove ' + people[id]);
            x.textContent = '×';
            x.addEventListener('click', function () { delete people[id]; renderPeople(); refresh(); });
            var h = document.createElement('input');
            h.type = 'hidden'; h.name = 'F.UserIds'; h.value = id;
            chip.appendChild(x); chip.appendChild(h);
            peopleBox.appendChild(chip);
        });
    }

    function chosenTiers() { return tiers.filter(function (t) { return t.checked; }); }
    function peopleCount() { return Object.keys(people).length; }

    function refresh() {
        var isPublic = pub.checked;
        // Public means nobody is singled out, so the other boxes switch off. All customers already includes every tier.
        [allC, allS].concat(tiers).forEach(function (c) { c.disabled = isPublic; if (isPublic) c.checked = false; });
        if (!isPublic && allC.checked) tiers.forEach(function (t) { t.checked = false; t.disabled = true; });
        search.disabled = isPublic; searchTier.disabled = isPublic;
        if (isPublic && peopleCount()) { people = {}; renderPeople(); }

        var chosen = chosenTiers();
        var n = peopleCount();
        presets.forEach(function (b) {
            var p = b.getAttribute('data-preset');
            var on = false;
            if (p === 'public') on = isPublic;
            else if (!isPublic && n === 0) {
                if (p === 'customers') on = allC.checked && !allS.checked;
                else if (p === 'staff') on = allS.checked && !allC.checked && chosen.length === 0;
                else if (p === 'tier') on = !allC.checked && !allS.checked && chosen.length === 1 && chosen[0].value === b.getAttribute('data-tier');
            }
            b.classList.toggle('on', on);
            b.setAttribute('aria-pressed', on ? 'true' : 'false');
        });

        var parts = [];
        if (isPublic) parts.push('everyone, including visitors who are not signed in');
        if (allC.checked) parts.push('all customers');
        if (chosen.length) parts.push(chosen.map(tierLabel).join(' and ') + ' customers');
        if (allS.checked) parts.push('all staff');
        if (n) parts.push(n + ' named ' + (n === 1 ? 'person' : 'people'));
        summary.classList.remove('is-error');
        summary.textContent = parts.length ? 'This will reach ' + parts.join(', ') + '.' : 'Choose who this is for.';
    }

    function setAudience(a) {
        a = a || {};
        pub.checked = !!a.isPublic;
        allC.checked = !!a.allCustomers;
        allS.checked = !!a.allStaff;
        var ids = (a.tierIds || []).map(String);
        tiers.forEach(function (t) { t.disabled = false; t.checked = ids.indexOf(t.value) !== -1; });
        people = {};
        (a.people || []).forEach(function (p) { people[p.id] = p.name; });
        renderPeople();
        results.hidden = true; results.textContent = '';
        search.value = ''; searchTier.value = '';
        refresh();
    }

    presets.forEach(function (b) {
        b.addEventListener('click', function () {
            var p = b.getAttribute('data-preset');
            var a = { isPublic: false, allCustomers: false, allStaff: false, tierIds: [], people: [] };
            if (p === 'public') a.isPublic = true;
            if (p === 'customers') a.allCustomers = true;
            if (p === 'staff') a.allStaff = true;
            if (p === 'tier') a.tierIds = [b.getAttribute('data-tier')];
            setAudience(a);
        });
    });

    root.addEventListener('change', function (e) {
        if (e.target.matches('input[type="checkbox"]')) refresh();
        if (e.target === searchTier && search.value.trim()) runSearch();
    });

    function runSearch() {
        var url = root.getAttribute('data-search-url');
        var term = search.value.trim();
        if (!url || (!term && !searchTier.value)) { results.hidden = true; return; }
        var full = url + (url.indexOf('?') === -1 ? '?' : '&') + 'handler=People&q=' + encodeURIComponent(term) +
            (searchTier.value ? '&tierId=' + encodeURIComponent(searchTier.value) : '');
        fetch(full, { headers: { 'Accept': 'application/json' }, credentials: 'same-origin' })
            .then(function (r) { if (!r.ok) throw new Error('search'); return r.json(); })
            .then(function (list) {
                results.textContent = '';
                if (!list.length) {
                    var none = document.createElement('li');
                    none.className = 'aud-none'; none.textContent = 'No one found.';
                    results.appendChild(none);
                }
                list.forEach(function (p) {
                    var li = document.createElement('li');
                    var b = document.createElement('button');
                    b.type = 'button';
                    var name = document.createElement('b'); name.textContent = p.name;
                    var meta = document.createElement('small');
                    meta.textContent = p.email + ' (' + (p.tierName || p.role) + ')';
                    b.appendChild(name); b.appendChild(meta);
                    b.addEventListener('click', function () {
                        people[p.id] = p.name;
                        renderPeople(); refresh();
                        results.hidden = true; search.value = '';
                        search.focus();
                    });
                    li.appendChild(b); results.appendChild(li);
                });
                results.hidden = false;
            })
            .catch(function () {
                results.textContent = '';
                var bad = document.createElement('li');
                bad.className = 'aud-none'; bad.textContent = 'The search did not work. Try again in a moment.';
                results.appendChild(bad); results.hidden = false;
            });
    }
    search.addEventListener('input', function () { clearTimeout(timer); timer = setTimeout(runSearch, 250); });
    search.addEventListener('keydown', function (e) { if (e.key === 'Enter') { e.preventDefault(); clearTimeout(timer); runSearch(); } });

    // ---- the rest of the editor -----------------------------------------------------------------------------------
    var publishBtn = document.getElementById('annPublish');
    var draftBtn = form.querySelector('button[value="draft"]');
    var notifyRow = document.getElementById('annNotify');
    var live = document.getElementById('annDlg_live');
    var publishAt = el('publishat');

    function sync() {
        var isLive = live.value === 'true';
        var later = false;
        if (publishAt.value) { var t = new Date(publishAt.value); later = !isNaN(t) && t.getTime() > Date.now(); }
        publishBtn.textContent = isLive ? 'Save changes' : (later ? 'Schedule' : 'Publish');
        // Saving as a draft would take a live one down; the list has Unpublish for that.
        if (draftBtn) draftBtn.hidden = isLive;
        // A notice goes out once, when something first goes live. For something already live there is nothing to send.
        if (notifyRow) notifyRow.hidden = isLive;
        Array.prototype.forEach.call(form.querySelectorAll('[data-count-for]'), function (c) {
            var src = document.getElementById(c.getAttribute('data-count-for'));
            if (!src) return;
            var max = parseInt(c.getAttribute('data-max'), 10) || src.maxLength;
            c.textContent = src.value.length + ' of ' + max + ' characters';
        });
    }
    form.addEventListener('input', sync);
    form.addEventListener('change', sync);

    function fill(d) {
        d = d || {};
        set('id', d.id || 0);
        set('title', d.title);
        set('content', d.content);
        el('image').value = '';
        var pic = document.getElementById('annPic');
        var img = document.getElementById('annPicImg');
        if (d.image) { img.src = d.image; pic.hidden = false; } else { img.removeAttribute('src'); pic.hidden = true; }
        box('removeimage', d.removeImage);
        set('publishat', d.publishAt);
        set('expiresat', d.expiresAt);
        box('pinned', d.pinned);
        set('btntext', d.buttonText);
        set('btnurl', d.buttonUrl);
        live.value = d.live ? 'true' : 'false';
        box('notify', d.id ? !!d.notify : true);
        setAudience(d.aud || { allCustomers: true });
        var title = dlg.querySelector('[data-dialog-title]');
        if (title) title.textContent = d.id ? 'Edit announcement' : 'New announcement';
        sync();
    }

    // Somebody has to be chosen before anything is saved; the server checks again.
    form.addEventListener('submit', function (e) {
        var any = pub.checked || allC.checked || allS.checked || chosenTiers().length > 0 || peopleCount() > 0;
        if (!any) {
            e.preventDefault();
            summary.textContent = 'Choose who this is for before saving.';
            summary.classList.add('is-error');
            root.scrollIntoView({ block: 'nearest' });
        }
    });

    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-ann-new]')) {
            e.preventDefault();
            fill(null);
            if (window.blm) window.blm.openDialog('annDlg');
            return;
        }
        var edit = e.target.closest('[data-ann-edit]');
        if (edit) {
            e.preventDefault();
            var data = null;
            try { data = JSON.parse(edit.getAttribute('data-ann-edit')); } catch (err) { data = null; }
            fill(data);
            if (window.blm) window.blm.openDialog('annDlg');
        }
    });

    // A save that was refused comes back with what was typed, so nothing has to be entered again.
    var again = document.getElementById('annReopen');
    if (again) {
        try { fill(JSON.parse(again.textContent)); } catch (err) { fill(null); }
        if (window.blm) window.blm.openDialog('annDlg');
    } else {
        fill(null);
    }
})();
