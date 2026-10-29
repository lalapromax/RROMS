// Admin > Announcements: fills the one editor from an announcement's data, and runs the "Who should see it?" picker
// (checkboxes for audience selection). The browser only builds the form; the server checks the audience
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
    var summary = document.getElementById('annAudSummary');

    function tierLabel(cb) {
        var l = cb.closest('label');
        return l ? l.textContent.trim() : '';
    }

    function chosenTiers() { return tiers.filter(function (t) { return t.checked; }); }

    function refresh() {
        var isPublic = pub.checked;
        var allCustomersChecked = allC.checked;
        var allStaffChecked = allS.checked;
        var chosenTierCount = chosenTiers().length;

        // When public is checked, uncheck all others
        if (isPublic) {
            allC.checked = false;
            allS.checked = false;
            tiers.forEach(function (t) { t.checked = false; });
        }
        // When all customers is checked, uncheck public, staff, and tiers
        else if (allCustomersChecked) {
            pub.checked = false;
            allS.checked = false;
            tiers.forEach(function (t) { t.checked = false; });
        }
        // When all staff is checked, uncheck public, customers, and tiers
        else if (allStaffChecked) {
            pub.checked = false;
            allC.checked = false;
            tiers.forEach(function (t) { t.checked = false; });
        }
        // When a tier is checked, uncheck public, all customers, all staff, and other tiers
        else if (chosenTierCount > 0) {
            pub.checked = false;
            allC.checked = false;
            allS.checked = false;
            // Only keep the last checked tier, uncheck others
            if (chosenTierCount > 1) {
                var checked = chosenTiers();
                tiers.forEach(function (t) { t.checked = t === checked[checked.length - 1]; });
            }
        }

        var chosen = chosenTiers();
        var parts = [];
        if (pub.checked) parts.push('everyone, including visitors who are not signed in');
        if (allC.checked) parts.push('all customers');
        if (chosen.length) parts.push(chosen.map(tierLabel).join(' and ') + ' customers');
        if (allS.checked) parts.push('all staff');
        summary.classList.remove('is-error');
        summary.textContent = parts.length ? 'This will reach ' + parts.join(', ') + '.' : 'Choose who this is for.';
    }

    function setAudience(a) {
        a = a || {};
        pub.checked = !!a.isPublic;
        allC.checked = !!a.allCustomers;
        allS.checked = !!a.allStaff;
        var ids = (a.tierIds || []).map(String);
        tiers.forEach(function (t) { t.checked = ids.indexOf(t.value) !== -1; });
        refresh();
    }

    root.addEventListener('change', function (e) {
        if (e.target.matches('input[type="checkbox"]')) refresh();
    });

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
        var any = pub.checked || allC.checked || allS.checked || chosenTiers().length > 0;
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
