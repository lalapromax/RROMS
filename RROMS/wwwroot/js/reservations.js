/* My reservations: the "Change your booking" dialog. When it opens (blm.js has already filled the date, guests and notes),
   this fills the time list with the times still free for that day and party, keeping the guest's current time available. */
(function () {
  'use strict';
  var dlg = document.getElementById('changeDlg');
  if (!dlg) return;

  var dateSel = document.getElementById('changeDlg_date');
  var timeSel = document.getElementById('changeDlg_time');
  var partySel = document.getElementById('changeDlg_party');
  var hint = document.getElementById('changeDlg_hint');
  var url = dlg.getAttribute('data-slots-url');
  var current = { date: '', time: '' };
  var token = 0;

  function listTimes(times) {
    if (times.length === 1) return times[0];
    return times.slice(0, -1).join(', ') + ' and ' + times[times.length - 1];
  }

  function load(wantedTime) {
    var d = dateSel.value;
    var p = partySel.value;
    if (!d || !p) return;
    var mine = ++token;
    timeSel.disabled = true;
    hint.textContent = 'Checking the times…';
    fetch(url + '&date=' + encodeURIComponent(d) + '&party=' + encodeURIComponent(p), { headers: { Accept: 'application/json' }, credentials: 'same-origin' })
      .then(function (r) { if (!r.ok) throw new Error('slots'); return r.json(); })
      .then(function (data) {
        if (mine !== token) return;
        var free = data.slots.filter(function (s) { return !s.full; }).map(function (s) { return s.time; });
        var full = data.slots.filter(function (s) { return s.full; }).map(function (s) { return s.time; });
        // The booking's own table shows as taken in the list, so its current time is always offered for the same day.
        if (d === current.date && current.time && free.indexOf(current.time) < 0) {
          free.push(current.time);
          full = full.filter(function (t) { return t !== current.time; });
        }
        free.sort();
        timeSel.innerHTML = '';
        free.forEach(function (t) {
          var o = document.createElement('option');
          o.value = t; o.textContent = t;
          timeSel.appendChild(o);
        });
        if (wantedTime && free.indexOf(wantedTime) >= 0) timeSel.value = wantedTime;
        timeSel.disabled = free.length === 0;
        if (data.closed) hint.textContent = 'We are closed that day. Please choose another.';
        else if (!free.length) hint.textContent = 'No tables are free that day for this many guests.';
        else if (full.length) hint.textContent = listTimes(full) + (full.length === 1 ? ' is' : ' are') + ' full.';
        else hint.textContent = '';
      })
      .catch(function () {
        if (mine !== token) return;
        hint.textContent = 'We could not load the times. Please close this and try again.';
        timeSel.disabled = true;
      });
  }

  // Runs after blm.js has copied the booking's details into the fields.
  document.addEventListener('click', function (e) {
    var opener = e.target.closest('[data-dialog-open="changeDlg"]');
    if (!opener) return;
    current = { date: opener.getAttribute('data-date') || '', time: opener.getAttribute('data-time') || '' };
    load(current.time);
  });

  dateSel.addEventListener('change', function () { load(timeSel.value); });
  partySel.addEventListener('change', function () { load(timeSel.value); });
})();
