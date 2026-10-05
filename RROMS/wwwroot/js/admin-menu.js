// Menu and sets: fills the dish drawer, shows the wine-only fields, previews the chosen photo and routes "Remove" to its own dialog.
(function () {
    'use strict';
    var $ = function (s, r) { return (r || document).querySelector(s); };
    var $$ = function (s, r) { return Array.prototype.slice.call((r || document).querySelectorAll(s)); };

    var dlg = document.getElementById('dishDlg');
    if (dlg) {
        var cat = $('#dishDlg_cat');
        var price = $('#priceLabel');
        var prev = $('#dishPrev');
        var plate = prev ? prev.innerHTML : ''; // the house plate the page was rendered with
        var file = $('#dishPhoto');
        var fileName = $('#dishPhotoName');
        var removeBox = $('#dishRemoveBox');
        var remove = $('#dishRemove');
        var currency = (price && price.textContent.match(/\((.+?)\)/) || [])[1] || 'RM';

        function isWine() {
            var o = cat && cat.options[cat.selectedIndex];
            return !!o && o.getAttribute('data-wine') === 'true';
        }
        function syncWine() {
            var on = isWine();
            $$('[data-wine-only]', dlg).forEach(function (n) { n.hidden = !on; });
            if (price) price.textContent = (on ? 'Price per bottle (' : 'Price (') + currency + ')';
        }
        function setPicture(url) {
            if (!prev) return;
            if (url) {
                prev.textContent = '';
                var img = document.createElement('img');
                img.src = url; img.alt = ''; img.style.cssText = 'width:100%;height:100%;object-fit:cover;display:block;border-radius:inherit';
                prev.appendChild(img);
            } else prev.innerHTML = plate;
        }
        function setChecks(sel, csv) {
            var have = (csv || '').split(',').map(function (s) { return s.trim().toLowerCase(); }).filter(Boolean);
            $$(sel, dlg).forEach(function (c) { c.checked = have.indexOf(c.value.toLowerCase()) >= 0; });
        }

        if (cat) cat.addEventListener('change', syncWine);

        // runs after blm.js has filled the plain fields
        document.addEventListener('click', function (e) {
            var b = e.target.closest('[data-dialog-open="dishDlg"]');
            if (!b) return;
            setChecks('[data-allergen]', b.getAttribute('data-allergens'));
            setChecks('[data-tag]', b.getAttribute('data-tags'));
            var photo = b.getAttribute('data-photo') || '';
            setPicture(photo);
            if (file) file.value = '';
            if (fileName) fileName.textContent = '';
            if (removeBox) { removeBox.hidden = !photo; var cb = $('input', removeBox); if (cb) cb.checked = false; }
            if (remove) remove.hidden = !b.getAttribute('data-id');
            syncWine();
        });

        if (file) file.addEventListener('change', function () {
            var f = file.files && file.files[0];
            if (!f) { if (fileName) fileName.textContent = ''; return; }
            if (fileName) fileName.textContent = f.name;
            var r = new FileReader();
            r.onload = function () { setPicture(r.result); };
            r.readAsDataURL(f);
        });

        if (remove) remove.addEventListener('click', function () {
            var d = document.getElementById('removeDlg');
            var id = $('#dishDlg_id'), name = $('#dishDlg_name');
            if (!d) return;
            $('#removeDlg_id').value = id ? id.value : '';
            $('#removeDlg_name').textContent = name && name.value ? name.value : 'this item';
            window.blm.openDialog('removeDlg');
        });

        // a drawer reopened after a server-side error keeps the values it was sent
        if (dlg.hasAttribute('data-open-on-load')) {
            var idv = $('#dishDlg_id');
            if (remove && idv && idv.value && idv.value !== '0') remove.hidden = false;
        }
        syncWine();
    }

    // category dialog: a category that already has items cannot become, or stop being, the wine list
    var catDlg = document.getElementById('catDlg');
    if (catDlg) {
        var wine = $('#catDlg_wine'), note = $('#catKindNote');
        document.addEventListener('click', function (e) {
            var b = e.target.closest('[data-dialog-open="catDlg"]');
            if (!b) return;
            var locked = b.getAttribute('data-kind') === 'locked';
            if (wine) wine.disabled = locked;
            if (note) note.hidden = !locked;
            // keep the stored value posting even though a disabled box posts nothing
            var keep = $('#catKeepWine');
            if (keep) keep.remove();
            if (locked && wine && wine.checked) {
                var h = document.createElement('input'); h.type = 'hidden'; h.id = 'catKeepWine'; h.name = wine.name; h.value = 'true';
                wine.parentNode.appendChild(h);
            }
        });
        // wine list and set-menu-only exclude each other
        catDlg.addEventListener('change', function (e) {
            var t = e.target;
            if (t.matches('[data-kind-wine]') && t.checked) { var s = $('[data-kind-set]', catDlg); if (s) s.checked = false; }
            if (t.matches('[data-kind-set]') && t.checked && wine && !wine.disabled) wine.checked = false;
        });
    }
})();
