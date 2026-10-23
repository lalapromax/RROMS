/* Review form. Everything here is a convenience on top of a form that works without it: the server checks every score,
   tag, photo and comment again (Services/ReviewService.cs).
     - "Nothing, all good" clears the other problem chips (and ticking one of them clears it)
     - photo previews, with the type, size and count checked before upload; a photo can be taken out again
     - "Save for later" keeps what was typed on this device (not the photos) and puts it back next time */
(function () {
  'use strict';
  var form = document.getElementById('reviewForm');
  if (!form) return;

  var MAX = parseInt(form.getAttribute('data-max-photos'), 10) || 3;
  var MAX_BYTES = 3 * 1024 * 1024;
  var ALLOWED = ['image/jpeg', 'image/png', 'image/webp'];
  var editing = form.getAttribute('data-editing') === 'true';
  var KEY = 'blm-review-draft:' + (form.getAttribute('data-draft-key') || 'none');

  function $(sel, root) { return (root || form).querySelector(sel); }
  function $$(sel, root) { return Array.prototype.slice.call((root || form).querySelectorAll(sel)); }

  // ---- "Nothing, all good" ----
  var none = $('[data-none-of-the-above]');
  var tags = $$('input[name="Input.IssueTags"]');
  if (none) {
    none.addEventListener('change', function () { if (none.checked) tags.forEach(function (t) { t.checked = false; }); });
    tags.forEach(function (t) { t.addEventListener('change', function () { if (t.checked) none.checked = false; }); });
  }

  // ---- photos ----
  var input = document.getElementById('reviewImages');
  var msg = document.getElementById('photoMsg');
  var wrap = document.getElementById('photos');
  var chosen = [];                      // File objects the guest has picked
  var urls = [];

  function say(text) { if (msg) msg.textContent = text ? ' ' + text : ''; }
  function keptCount() { return $$('[data-kept] input[type="checkbox"]:checked').length; }

  function syncInput() {
    if (!input || typeof DataTransfer === 'undefined') return;
    var dt = new DataTransfer();
    chosen.forEach(function (f) { dt.items.add(f); });
    input.files = dt.files;
  }

  function render() {
    if (!wrap) return;
    urls.forEach(function (u) { URL.revokeObjectURL(u); });
    urls = [];
    $$('[data-preview]', wrap).forEach(function (n) { n.remove(); });
    var slots = $$('[data-slot]', wrap);
    chosen.forEach(function (file, i) {
      var u = URL.createObjectURL(file); urls.push(u);
      var div = document.createElement('div');
      div.className = 'photo has'; div.setAttribute('data-preview', '');
      var img = document.createElement('img'); img.src = u; img.alt = 'Photo you added: ' + file.name;
      var rm = document.createElement('button'); rm.type = 'button'; rm.className = 'photo-rm'; rm.setAttribute('aria-label', 'Take out ' + file.name);
      rm.textContent = '×';
      rm.addEventListener('click', function () { chosen.splice(i, 1); syncInput(); render(); say(''); });
      div.appendChild(img); div.appendChild(rm);
      var slot = slots[i];
      if (slot) wrap.insertBefore(div, slot); else wrap.appendChild(div);
    });
    slots.forEach(function (s, i) { s.hidden = i < chosen.length; });
  }

  if (input) {
    input.addEventListener('change', function () {
      var picked = Array.prototype.slice.call(input.files || []);
      var room = MAX - keptCount() - chosen.length;
      var problems = [];
      picked.forEach(function (f) {
        if (ALLOWED.indexOf(f.type) < 0) { problems.push(f.name + ' is not a JPG, PNG or WebP picture.'); return; }
        if (f.size > MAX_BYTES) { problems.push(f.name + ' is over 3 MB.'); return; }
        if (room <= 0) { problems.push('Only ' + MAX + ' photos fit on a review.'); return; }
        chosen.push(f); room--;
      });
      syncInput(); render();
      say(problems.join(' '));
    });
    $$('[data-kept] input[type="checkbox"]').forEach(function (c) {
      c.addEventListener('change', function () {
        // taking a kept photo off frees a place for a new one
        var over = keptCount() + chosen.length - MAX;
        if (over > 0) { chosen = chosen.slice(0, chosen.length - over); syncInput(); render(); }
      });
    });
  }

  // ---- save for later (this device only) ----
  var saveBtn = document.getElementById('saveLater');
  var note = document.getElementById('draftNote');

  function collect() {
    var d = { radios: {}, tags: [], comments: '', show: true };
    $$('input[type="radio"]:checked').forEach(function (r) { d.radios[r.name] = r.value; });
    tags.forEach(function (t) { if (t.checked) d.tags.push(t.value); });
    var c = $('[name="Input.Comments"]'); d.comments = c ? c.value : '';
    var s = $('input[type="checkbox"][name="Input.ShowFirstName"]'); d.show = s ? s.checked : true;
    return d;
  }
  function pristine() {
    var c = $('[name="Input.Comments"]');
    return !$('input[type="radio"]:checked') && !$('input[name="Input.IssueTags"]:checked') && !(c && c.value.trim());
  }
  function apply(d) {
    Object.keys(d.radios || {}).forEach(function (name) {
      var r = $('input[type="radio"][name="' + name + '"][value="' + d.radios[name] + '"]'); if (r) r.checked = true;
    });
    tags.forEach(function (t) { t.checked = (d.tags || []).indexOf(t.value) >= 0; });
    var c = $('[name="Input.Comments"]'); if (c && d.comments) c.value = d.comments;
    var s = $('input[type="checkbox"][name="Input.ShowFirstName"]'); if (s) s.checked = d.show !== false;
  }

  if (saveBtn && !editing) {
    var canStore = true;
    try { window.localStorage.getItem(KEY); } catch (e) { canStore = false; }
    if (canStore) {
      saveBtn.hidden = false;
      try {
        var raw = window.localStorage.getItem(KEY);
        if (raw && pristine()) { apply(JSON.parse(raw)); if (note) note.textContent = 'Your saved answers are back. Photos are not saved, so add them again if you like.'; }
      } catch (e) { /* a broken draft is simply ignored */ }
      saveBtn.addEventListener('click', function () {
        try { window.localStorage.setItem(KEY, JSON.stringify(collect())); if (note) note.textContent = 'Saved on this device. Come back to this page whenever you are ready. Photos are not saved.'; }
        catch (e) { if (note) note.textContent = 'This browser would not let us save it.'; }
      });
    }
  }

  // ---- sending ----
  form.addEventListener('submit', function () {
    var send = document.getElementById('sendReview');
    if (send) { send.disabled = true; send.textContent = editing ? 'Saving…' : 'Sending…'; }
    try { window.localStorage.removeItem(KEY); } catch (e) { /* ignore */ }
  });
  window.addEventListener('pageshow', function (ev) {
    if (!ev.persisted) return;
    var send = document.getElementById('sendReview');
    if (send) { send.disabled = false; send.textContent = editing ? 'Save changes' : 'Send my review'; }
  });
})();
