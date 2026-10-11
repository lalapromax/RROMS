/* Booking page: refreshes the start times when the party, day or seating area changes, and keeps the printed summary in step.
   Everything also works without this file: the month arrows and the "Show the times" button post the form and redraw the page. */
(function () {
  'use strict';
  var form = document.querySelector('[data-book]');
  if (!form) return;

  var slotBox = document.getElementById('slotBox');
  var note = document.getElementById('slotNote');
  var bigNote = form.querySelector('[data-big-note]');
  var confirmBtn = document.getElementById('confirmBtn');
  var waitBtn = document.getElementById('waitBtn');
  var fine = document.getElementById('fineText');
  var url = form.getAttribute('data-slots-url');
  var cutoff = 4;
  var big = 7;
  var request = 0;

  var days = ['Sunday', 'Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday'];
  var months = ['January', 'February', 'March', 'April', 'May', 'June', 'July', 'August', 'September', 'October', 'November', 'December'];

  function val(name) {
    var el = form.querySelector('input[name="' + name + '"]:checked');
    return el ? el.value : '';
  }
  function out(key, text) {
    var el = form.querySelector('[data-out="' + key + '"]');
    if (el) el.textContent = text;
  }
  function row(key, show) {
    form.querySelectorAll('[data-row="' + key + '"]').forEach(function (el) { el.hidden = !show; });
  }
  function esc(s) {
    return String(s).replace(/[&<>"]/g, function (c) { return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c]; });
  }

  function party() { return parseInt(val('Form.PartySize'), 10) || 2; }

  function syncSummary() {
    var d = val('Form.Date');
    if (d) {
      var p = d.split('-');
      var dt = new Date(+p[0], +p[1] - 1, +p[2]);
      out('date', days[dt.getDay()] + ' ' + dt.getDate() + ' ' + months[dt.getMonth()]);
    } else out('date', 'Not chosen');
    var t = val('Form.Time');
    out('time', t || 'Not chosen');
    out('party', String(party()));
    var occ = val('Form.Occasion');
    out('occasion', occ); row('occasion', !!occ);
    var setEl = form.querySelector('input[data-sum="set"]:checked');
    var setName = setEl ? setEl.getAttribute('data-label') : '';
    out('set', setName); row('set', !!setName);
    if (bigNote) bigNote.hidden = party() < big;
    syncButtons();
  }

  function syncButtons() {
    var el = form.querySelector('input[name="Form.Time"]:checked');
    var full = !!el && el.getAttribute('data-full') === '1';
    confirmBtn.hidden = full;
    waitBtn.hidden = !full;
    fine.textContent = full
      ? 'We will message you if a table opens. Nothing is charged.'
      : 'You can change or cancel up to ' + cutoff + ' hours before.';
    if (note) {
      var anyFull = !!form.querySelector('input[name="Form.Time"][data-full="1"]');
      note.hidden = !anyFull;
      note.textContent = 'Dashed times are full for ' + party() + '. Choose one to join the waiting list and we will message you if a table opens.';
    }
  }

  function slotLabel(s, keep) {
    return '<label><input type="radio" name="Form.Time" value="' + esc(s.time) + '" data-full="' + (s.full ? '1' : '0') + '"' +
      (keep === s.time ? ' checked' : '') + ' /><span' + (s.full ? ' class="full"' : '') + '>' + esc(s.time) + '</span></label>';
  }

  function render(data, keep) {
    var html;
    if (data.closed) html = '<p class="slots-empty">We are closed that day. Please choose another.</p>';
    else if (!data.slots.length) html = '<p class="slots-empty">There are no more tables today. Please choose another day.</p>';
    else {
      var lunch = data.slots.filter(function (s) { return s.time < '17:00'; });
      var dinner = data.slots.filter(function (s) { return s.time >= '17:00'; });
      var both = lunch.length && dinner.length;
      html = '';
      if (lunch.length) html += (both ? '<p class="slots-h">Lunch</p>' : '') + '<div class="slots">' + lunch.map(function (s) { return slotLabel(s, keep); }).join('') + '</div>';
      if (dinner.length) html += (both ? '<p class="slots-h">Dinner</p>' : '') + '<div class="slots">' + dinner.map(function (s) { return slotLabel(s, keep); }).join('') + '</div>';
    }
    slotBox.innerHTML = html;
    syncSummary();
  }

  function refreshSlots() {
    var d = val('Form.Date');
    if (!d) return;
    var area = val('Form.DiningAreaId');
    var keep = val('Form.Time');
    var mine = ++request;
    slotBox.setAttribute('aria-busy', 'true');
    fetch(url + '&date=' + encodeURIComponent(d) + '&party=' + party() + (area ? '&area=' + encodeURIComponent(area) : ''), { headers: { Accept: 'application/json' }, credentials: 'same-origin' })
      .then(function (r) { if (!r.ok) throw new Error('slots'); return r.json(); })
      .then(function (data) { if (mine === request) render(data, keep); })
      .catch(function () {
        if (mine === request) slotBox.innerHTML = '<p class="slots-empty">We could not load the times. Please try again, or use the button below.</p>';
      })
      .then(function () { slotBox.removeAttribute('aria-busy'); });
  }

  form.addEventListener('change', function (e) {
    var n = e.target && e.target.name;
    if (n === 'Form.PartySize' || n === 'Form.Date' || n === 'Form.DiningAreaId') refreshSlots();
    syncSummary();
  });

  // A slot has to be chosen before booking; say so next to the times instead of after a round trip.
  form.addEventListener('submit', function (e) {
    var sub = e.submitter;
    if (!sub || sub.hasAttribute('formnovalidate') && sub.id !== 'waitBtn') return;
    if (!val('Form.Time')) {
      e.preventDefault();
      var msg = slotBox.querySelector('.slots-need');
      if (!msg) { msg = document.createElement('p'); msg.className = 'slots-empty slots-need'; msg.setAttribute('role', 'alert'); slotBox.appendChild(msg); }
      msg.textContent = 'Please choose a time first.';
      var first = slotBox.querySelector('input');
      if (first) first.focus();
      slotBox.scrollIntoView({ behavior: 'smooth', block: 'center' });
      return;
    }
    // Stop a second tap from sending the booking twice.
    window.setTimeout(function () { sub.disabled = true; }, 0);
  });

  // Coming back with the browser's back button must not leave the booking button switched off.
  window.addEventListener('pageshow', function () {
    form.querySelectorAll('button[type=submit]:not(.mnav)').forEach(function (b) { b.disabled = false; });
  });

  syncSummary();
})();
