// Opening hours: ticking "Closed" swaps a day's two time boxes for "Closed all day". The times are still posted (the server ignores
// them for a closed day), so unticking brings back what was there.
(function () {
    'use strict';
    document.addEventListener('change', function (e) {
        var box = e.target.closest('[data-closed]');
        if (!box) return;
        var row = box.closest('[data-day-row]');
        if (!row) return;
        var times = row.querySelector('[data-times]');
        var note = row.querySelector('[data-closed-note]');
        if (times) times.hidden = box.checked;
        if (note) note.hidden = !box.checked;
    });
})();
