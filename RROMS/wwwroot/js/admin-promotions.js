// Admin > Promotions: fills the one add/edit dialog from a promotion's data, and shows only the parts of the form that apply
// (a percentage or an amount, which dish or category, which guests). The server validates everything again on save.
(function () {
    'use strict';
    var dlg = document.getElementById('promoDlg');
    if (!dlg) return;

    function el(name) { return document.getElementById('promoDlg_' + name); }
    function pad(n) { return n < 10 ? '0' + n : '' + n; }
    function dateIn(days) {
        var d = new Date(); d.setDate(d.getDate() + days);
        return d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
    }
    function setValue(name, v) { var e = el(name); if (e) e.value = (v === null || v === undefined) ? '' : v; }
    function setRadio(field, v) {
        dlg.querySelectorAll('input[name="' + field + '"]').forEach(function (r) { r.checked = (r.value === v); });
    }
    function getRadio(field) {
        var r = dlg.querySelector('input[name="' + field + '"]:checked');
        return r ? r.value : '';
    }
    function show(id, on) { var e = document.getElementById(id); if (e) e.hidden = !on; }

    // Show only what applies to the choices made so far.
    function sync() {
        var kind = getRadio('Input.Kind') || 'percent';
        var scope = getRadio('Input.Scope') || 'storewide';
        var eligibility = getRadio('Input.Eligibility') || 'all';
        show('kindPercent', kind === 'percent');
        show('kindAmount', kind === 'amount');
        show('scopeCategory', scope === 'category');
        show('scopeItem', scope === 'item');
        show('eligEmails', eligibility === 'specific');
        show('eligTiers', eligibility === 'tier');

        // Telling every guest only suits a new, open promotion that applies by itself.
        var isNew = !(parseInt(el('id').value, 10) > 0);
        var open = eligibility === 'all' && !el('code').value.trim();
        var notify = el('notify');
        var row = document.getElementById('notifyRow');
        if (row) row.hidden = !isNew;
        notify.disabled = !open;
        if (!open) notify.checked = false;
        show('notifyNote', isNew && !open);
    }

    function fill(d) {
        d = d || {};
        setValue('id', d.id || 0);
        setValue('title', d.title);
        setValue('code', d.code);
        setValue('description', d.description);
        setRadio('Input.Kind', d.kind || 'percent');
        setValue('percent', d.percent);
        setValue('amount', d.amount);
        setValue('start', d.start || dateIn(0));
        setValue('end', d.end || dateIn(7));
        setRadio('Input.Scope', d.scope || 'storewide');
        setValue('category', d.category);
        setValue('item', d.item);
        setRadio('Input.Eligibility', d.eligibility || 'all');
        setValue('emails', d.emails);
        var tiers = (d.tiers || []).map(String);
        dlg.querySelectorAll('[data-tier]').forEach(function (c) { c.checked = tiers.indexOf(c.value) >= 0; });
        for (var i = 0; i < 7; i++) {
            var c = el('day' + i);
            if (c) c.checked = (d.days || []).indexOf(i) >= 0;
        }
        setValue('min', d.min);
        setValue('total', d.total);
        setValue('perguest', d.perGuest);
        el('active').checked = d.active !== false;
        el('notify').checked = !!d.notify;
        var title = dlg.querySelector('[data-dialog-title]');
        if (title) title.textContent = d.id ? 'Edit promotion' : 'New promotion';
        sync();
    }

    document.addEventListener('click', function (e) {
        var make = e.target.closest('[data-promo-new]');
        if (make) {
            e.preventDefault();
            fill(null);
            if (window.blm) window.blm.openDialog('promoDlg');
            return;
        }
        var edit = e.target.closest('[data-promo-edit]');
        if (edit) {
            e.preventDefault();
            var data = null;
            try { data = JSON.parse(edit.getAttribute('data-promo-edit')); } catch (err) { data = null; }
            fill(data);
            if (window.blm) window.blm.openDialog('promoDlg');
        }
    });

    dlg.addEventListener('change', sync);
    dlg.addEventListener('input', function (e) { if (e.target === el('code')) sync(); });

    // A save that was refused comes back with what was typed, so nothing has to be entered again.
    var again = document.getElementById('promoReopen');
    if (again) {
        try { fill(JSON.parse(again.textContent)); } catch (err) { fill(null); }
    } else {
        fill(null);
    }
})();
