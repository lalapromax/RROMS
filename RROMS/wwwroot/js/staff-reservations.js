// Staff > Reservations. Two small things the shared dialog helper does not do: hide the day and time while "seat them now" is ticked,
// and let the edit dialog hand over to the cancel dialog without losing which booking it was about.
(function () {
    'use strict';

    var newDlg = document.getElementById('newDlg');
    if (newDlg) {
        var seat = document.getElementById('newDlg_seat');
        var when = newDlg.querySelector('[data-when]');
        var sync = function () { if (when && seat) when.hidden = seat.checked; };
        if (seat) seat.addEventListener('change', sync);
        // the two "add" buttons say whether the guest is already here
        document.addEventListener('click', function (e) {
            var b = e.target.closest('[data-dialog-open="newDlg"]');
            if (!b || !seat) return;
            seat.checked = b.getAttribute('data-seat') === 'true';
            var h = document.getElementById('newT');
            if (h) h.textContent = seat.checked ? 'Add a walk-in' : 'New reservation';
            sync();
        });
        sync();
    }

    document.addEventListener('click', function (e) {
        var b = e.target.closest('[data-from-edit]');
        if (!b) return;
        var edit = document.getElementById('editDlg');
        var next = document.getElementById(b.getAttribute('data-from-edit'));
        if (!edit || !next) return;
        var id = document.getElementById('editDlg_id').value;
        var name = document.getElementById('editDlg_name').textContent;
        document.getElementById(next.id + '_id').value = id;
        document.getElementById(next.id + '_name').textContent = name;
        var title = next.querySelector('[data-dialog-title]');
        if (title) title.textContent = 'Cancel this booking';
        var action = document.getElementById(next.id + '_action');
        if (action) action.textContent = 'Cancel the booking';
        edit.close();
        window.blm.openDialog(next.id);
    });
})();
