// Admin > Rewards > Ranking and messages: keep each band's name in step with its cut-off, and keep the guest preview in step with the wording.
// Everything else on the page (dialogs, confirm) is handled by blm.js. The server validates every value again when the form is saved.
(function () {
    'use strict';
    var form = document.getElementById('rankForm');
    if (!form) return;

    form.querySelectorAll('[data-band-cut]').forEach(function (input) {
        var band = input.closest('.band');
        var label = band && band.querySelector('[data-band-label]');
        if (!label) return;
        input.addEventListener('input', function () {
            var n = parseInt(input.value, 10);
            label.textContent = isNaN(n) ? 'Top %' : 'Top ' + n + '%';
        });
    });

    var source = form.querySelector('[data-preview-source]');
    var message = document.getElementById('prevMsg');
    var name = document.getElementById('prevBand');
    if (source && message) {
        source.addEventListener('input', function () { message.textContent = source.value; });
    }
    if (source && name) {
        var cut = source.closest('.band').querySelector('[data-band-cut]');
        if (cut) cut.addEventListener('input', function () {
            var n = parseInt(cut.value, 10);
            name.textContent = isNaN(n) ? 'Top %' : 'Top ' + n + '%';
        });
    }
})();
