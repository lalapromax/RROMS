// Admin > Collaborations: fills the one add/edit dialog from an entry's data. The server checks every link and picture again on save.
(function () {
    'use strict';
    var dlg = document.getElementById('collabDlg');
    if (!dlg) return;

    function el(name) { return document.getElementById('collabDlg_' + name); }
    function set(name, v) { var e = el(name); if (e) e.value = (v === null || v === undefined) ? '' : v; }

    function fill(d) {
        d = d || {};
        set('id', d.id || 0);
        set('name', d.name);
        set('summary', d.summary);
        set('year', d.year);
        el('ongoing').checked = !!d.isOngoing;
        set('website', d.websiteUrl);
        set('social', d.socialUrl);
        el('file').value = '';
        set('imageurl', d.imageUrl);
        var now = document.getElementById('collabPicNow');
        var img = document.getElementById('collabPicImg');
        if (d.image) { img.src = d.image; now.hidden = false; } else { img.removeAttribute('src'); now.hidden = true; }
        el('removeimage').checked = !!d.removeImage;
        el('published').checked = d.isPublished !== false;
        var title = dlg.querySelector('[data-dialog-title]');
        if (title) title.textContent = d.id ? 'Edit collaboration' : 'Add collaboration';
    }

    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-collab-new]')) {
            e.preventDefault();
            fill(null);
            if (window.blm) window.blm.openDialog('collabDlg');
            return;
        }
        var edit = e.target.closest('[data-collab-edit]');
        if (edit) {
            e.preventDefault();
            var data = null;
            try { data = JSON.parse(edit.getAttribute('data-collab-edit')); } catch (err) { data = null; }
            fill(data);
            if (window.blm) window.blm.openDialog('collabDlg');
        }
    });

    // A picture that cannot be loaded is dropped, so the partner's first letter shows instead.
    Array.prototype.forEach.call(document.querySelectorAll('img[data-collab-img]'), function (img) {
        function drop() { img.remove(); }
        img.addEventListener('error', drop);
        if (img.complete && img.naturalWidth === 0) drop();
    });

    // A save that was refused comes back with what was typed, so nothing has to be entered again.
    var again = document.getElementById('collabReopen');
    if (again) {
        try { fill(JSON.parse(again.textContent)); } catch (err) { fill(null); }
        if (window.blm) window.blm.openDialog('collabDlg');
    } else {
        fill(null);
    }
})();
