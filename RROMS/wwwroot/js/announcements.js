// The announcement editor (staff and admin): picks who it is for, shows the schedule field only when "Schedule" is chosen,
// counts the message, and words the main button for what it will do. The dialog itself is opened and filled by blm.js.
(function () {
    'use strict';
    var form = document.getElementById('annForm');
    if (!form) return;

    var publish = document.getElementById('annPublish');
    var whenBox = document.getElementById('annWhen');
    var whenInput = document.getElementById('annDlg_publishat');
    var notify = document.getElementById('annNotify');
    var live = document.getElementById('annDlg_live');

    function when() {
        var r = form.querySelector('input[name="F.When"]:checked');
        return r ? r.value : 'now';
    }

    function sync() {
        var later = when() === 'later';
        if (whenBox) whenBox.hidden = !later;
        if (whenInput) whenInput.required = later;
        var isLive = live && live.value === 'true';
        if (publish) publish.textContent = isLive && !later ? 'Save changes' : (later ? 'Schedule' : 'Publish');
        // A notice is sent once, when something first goes live. For something already live there is nothing to send.
        if (notify) notify.hidden = isLive;
        Array.prototype.forEach.call(form.querySelectorAll('[data-count-for]'), function (el) {
            var src = document.getElementById(el.getAttribute('data-count-for'));
            if (!src) return;
            var max = parseInt(el.getAttribute('data-max'), 10) || src.maxLength;
            el.textContent = src.value.length + ' of ' + max + ' characters';
        });
    }

    // "All customers" and single tiers exclude each other: all customers already includes every tier.
    form.addEventListener('change', function (e) {
        var t = e.target;
        if (t.matches('[data-aud-all]') && t.checked) {
            Array.prototype.forEach.call(form.querySelectorAll('[data-aud-tier]'), function (c) { c.checked = false; });
        } else if (t.matches('[data-aud-tier]') && t.checked) {
            var all = form.querySelector('[data-aud-all]');
            if (all) all.checked = false;
        }
        if (t.name === 'F.When' && t.value === 'later' && whenInput && !whenInput.value) {
            var d = new Date(Date.now() + 24 * 3600 * 1000);
            d.setMinutes(0, 0, 0);
            var p = function (n) { return (n < 10 ? '0' : '') + n; };
            whenInput.value = d.getFullYear() + '-' + p(d.getMonth() + 1) + '-' + p(d.getDate()) + 'T' + p(d.getHours()) + ':00';
        }
        sync();
    });
    form.addEventListener('input', sync);

    // blm.js fills the dialog on the same click; run after it.
    document.addEventListener('click', function (e) {
        if (e.target.closest('[data-dialog-open="annDlg"]')) setTimeout(sync, 0);
    });
    document.addEventListener('DOMContentLoaded', sync);
    sync();
})();
