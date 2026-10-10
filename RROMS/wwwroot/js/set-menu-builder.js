// The set menu builder (staff and admin). The whole menu lives in one object, `S`; the screen is drawn from it and posted back as
// a single JSON field that the server checks again. Courses can be dragged or moved with the arrow keys, dishes are chosen from the
// carte in a dialog, and the guest preview and price check redraw as you type.
(function () {
    'use strict';

    var form = document.getElementById('smForm');
    if (!form) return;

    var S = JSON.parse(document.getElementById('smState').textContent);
    var ITEMS = JSON.parse(document.getElementById('smItems').textContent);
    var BY = {};
    ITEMS.forEach(function (i) { BY[i.id] = i; });
    var CUR = form.getAttribute('data-currency') || 'RM';

    var $ = function (id) { return document.getElementById(id); };
    var els = {
        name: $('smName'), price: $('smPrice'), tag: $('smTag'), desc: $('smDesc'),
        courses: $('smCourses'), payload: $('smPayload'), live: $('smLive'),
        pvName: $('pvName'), pvTag: $('pvTag'), pvCourses: $('pvCourses'), pvPrice: $('pvPrice'), pcBody: $('pcBody'),
        pick: $('pickDlg'), pickList: $('pickList'), pickQ: $('pickQ'), pickFor: $('pickFor'), pickAdd: $('pickAdd')
    };

    // ---- small helpers -----------------------------------------------------------------------------------------
    function esc(s) {
        return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
            return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
        });
    }
    function money(n) {
        n = Number(n) || 0;
        var whole = Math.abs(n - Math.round(n)) < 0.005;
        var s = whole ? String(Math.round(n)) : n.toFixed(2);
        return CUR + ' ' + s.replace(/\B(?=(\d{3})+(?!\d)(?:\.|$))/g, ',');
    }
    function parsePrice(text) {
        var n = parseFloat(String(text || '').replace(/,/g, '').replace(/[^\d.]/g, ''));
        return isFinite(n) && n > 0 ? Math.round(n * 100) / 100 : 0;
    }
    var WORDS = ['No', 'One', 'Two', 'Three', 'Four', 'Five', 'Six', 'Seven', 'Eight', 'Nine', 'Ten', 'Eleven', 'Twelve'];
    function say(msg) { if (els.live) { els.live.textContent = ''; setTimeout(function () { els.live.textContent = msg; }, 30); } }

    var ICON_X14 = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" aria-hidden="true" focusable="false"><path d="M5 5l14 14M19 5L5 19"></path></svg>';
    var ICON_X18 = '<svg width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" aria-hidden="true" focusable="false"><path d="M5 5l14 14M19 5L5 19"></path></svg>';
    var ICON_NOPHOTO = '<svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M4 8h3l2-3h6l2 3h3v11H4z"></path><circle cx="12" cy="13" r="3.5"></circle><path d="M3 3l18 18"></path></svg>';
    var ICON_GRIP = '<svg width="14" height="20" viewBox="0 0 14 20" fill="currentColor" aria-hidden="true" focusable="false"><circle cx="4" cy="4" r="1.6"></circle><circle cx="10" cy="4" r="1.6"></circle><circle cx="4" cy="10" r="1.6"></circle><circle cx="10" cy="10" r="1.6"></circle><circle cx="4" cy="16" r="1.6"></circle><circle cx="10" cy="16" r="1.6"></circle></svg>';

    // ---- state ------------------------------------------------------------------------------------------------
    S.courses = S.courses || [];
    S.days = S.days || [];
    S.priceText = S.price ? String(S.price) : '';
    var dirty = false;
    function touch() { dirty = true; }

    function dishCount(c) { return c.dishes.length; }
    function clampPicks(c) {
        var n = dishCount(c);
        if (c.picks > Math.max(1, n)) c.picks = Math.max(1, n);
    }

    // ---- details panel ----------------------------------------------------------------------------------------
    els.name.value = S.name || '';
    els.price.value = S.priceText;
    els.tag.value = S.tagline || '';
    els.desc.value = S.description || '';

    function syncChips() {
        Array.prototype.forEach.call(document.querySelectorAll('[data-day]'), function (b) {
            b.setAttribute('aria-pressed', S.days.indexOf(parseInt(b.getAttribute('data-day'), 10)) >= 0 ? 'true' : 'false');
        });
        var l = document.querySelector('[data-svc="lunch"]'), d = document.querySelector('[data-svc="dinner"]');
        if (l) l.setAttribute('aria-pressed', S.lunch ? 'true' : 'false');
        if (d) d.setAttribute('aria-pressed', S.dinner ? 'true' : 'false');
    }

    els.name.addEventListener('input', function () { S.name = els.name.value; touch(); drawPreview(); });
    els.price.addEventListener('input', function () { S.priceText = els.price.value; S.price = parsePrice(S.priceText); touch(); drawPreview(); });
    els.tag.addEventListener('input', function () { S.tagline = els.tag.value; touch(); drawPreview(); });
    els.desc.addEventListener('input', function () { S.description = els.desc.value; touch(); });
    S.price = parsePrice(S.priceText);

    document.addEventListener('click', function (e) {
        var day = e.target.closest('[data-day]');
        if (day) {
            var v = parseInt(day.getAttribute('data-day'), 10), at = S.days.indexOf(v);
            if (at >= 0) S.days.splice(at, 1); else S.days.push(v);
            syncChips(); touch(); return;
        }
        var svc = e.target.closest('[data-svc]');
        if (svc) {
            var k = svc.getAttribute('data-svc');
            S[k === 'lunch' ? 'lunch' : 'dinner'] = !S[k === 'lunch' ? 'lunch' : 'dinner'];
            syncChips(); touch(); return;
        }
        var common = e.target.closest('[data-common]');
        if (common) { addCourse(common.getAttribute('data-common')); return; }
    });

    // ---- courses ----------------------------------------------------------------------------------------------
    function pickOptions(c, i) {
        var n = dishCount(c), top = Math.min(5, Math.max(n, c.picks, 1)), html = '';
        for (var p = 1; p <= top; p++) {
            html += '<option value="' + p + '"' + (c.picks === p ? ' selected' : '') + '>' + (p === 1 ? '1 dish' : p + ' dishes') + '</option>';
        }
        html += '<option value="0"' + (c.picks === 0 ? ' selected' : '') + '>All dishes</option>';
        return html;
    }

    function dishChip(c, ci, d, di) {
        var it = BY[d.id] || { name: 'A dish that is no longer on the carte', hasPhoto: false, available: false };
        var extra = d.supplement > 0 ? '+ ' + money(d.supplement) : 'Included';
        return '<li class="dchip">' +
            '<span>' + esc(it.name) + '</span>' +
            '<button type="button" class="sup" data-act="sup" data-c="' + ci + '" data-d="' + di + '" aria-label="Extra charge for ' + esc(it.name) + ': ' + (d.supplement > 0 ? esc(money(d.supplement)) : 'none') + '. Change it.">' + esc(extra) + '</button>' +
            (it.available === false ? '<small class="np">Not served now</small>' : '') +
            (it.hasPhoto === false && it.available !== false ? '<small class="np">' + ICON_NOPHOTO + ' No photo</small>' : '') +
            '<button type="button" class="dx" data-act="rm-dish" data-c="' + ci + '" data-d="' + di + '" aria-label="Remove ' + esc(it.name) + ' from ' + esc(c.name || 'this course') + '">' + ICON_X14 + '</button>' +
            '</li>';
    }

    function drawCourses(focusSel) {
        var html = '';
        S.courses.forEach(function (c, i) {
            var label = c.name || 'this course';
            html += '<li class="course" data-i="' + i + '">' +
                '<div class="course-top">' +
                '<button type="button" class="grip" data-act="grip" data-c="' + i + '" aria-label="Move ' + esc(label) + '. Drag, or press the up or down arrow key.">' + ICON_GRIP + '</button>' +
                '<div class="field grow"><label for="cn' + i + '">Course name</label><input class="in" id="cn' + i + '" data-c="' + i + '" data-f="name" type="text" maxlength="40" value="' + esc(c.name) + '"></div>' +
                '<div class="field pick"><label for="cp' + i + '">Guest picks</label><select class="in" id="cp' + i + '" data-c="' + i + '" data-f="picks">' + pickOptions(c, i) + '</select></div>' +
                '<button class="icon-x" type="button" data-act="rm-course" data-c="' + i + '" aria-label="Remove the ' + esc(label) + ' course">' + ICON_X18 + '</button>' +
                '</div>' +
                '<ul class="dishes">' +
                c.dishes.map(function (d, di) { return dishChip(c, i, d, di); }).join('') +
                '<li><button type="button" class="btn btn-line" data-act="add-dish" data-c="' + i + '">Add a dish</button></li>' +
                '</ul></li>';
        });
        els.courses.innerHTML = html || '<li class="course-empty">No courses yet. Add one below, or start from a common one.</li>';
        if (focusSel) { var f = els.courses.querySelector(focusSel); if (f) f.focus(); }
        drawPreview();
    }

    function addCourse(name) {
        S.courses.push({ name: name || '', picks: 1, dishes: [] });
        touch();
        drawCourses('[data-c="' + (S.courses.length - 1) + '"][data-f="name"]');
        say((name || 'A new course') + ' added.');
    }
    $('smAddCourse').addEventListener('click', function () { addCourse(''); });

    // typing a course name or changing the picks: update state and the preview, but do not redraw the list (that would drop focus)
    els.courses.addEventListener('input', function (e) {
        var t = e.target; if (t.getAttribute('data-f') !== 'name') return;
        S.courses[+t.getAttribute('data-c')].name = t.value; touch(); drawPreview();
    });
    els.courses.addEventListener('change', function (e) {
        var t = e.target; var f = t.getAttribute('data-f');
        if (f === 'picks') { S.courses[+t.getAttribute('data-c')].picks = parseInt(t.value, 10); touch(); drawPreview(); }
    });

    els.courses.addEventListener('click', function (e) {
        var b = e.target.closest('[data-act]'); if (!b) return;
        var act = b.getAttribute('data-act'), ci = +b.getAttribute('data-c'), di = +b.getAttribute('data-d');
        if (act === 'rm-course') {
            var c = S.courses[ci];
            if (c.dishes.length && !window.confirm('Remove the ' + (c.name || 'unnamed') + ' course and its ' + c.dishes.length + (c.dishes.length === 1 ? ' dish?' : ' dishes?'))) return;
            S.courses.splice(ci, 1); touch();
            drawCourses(S.courses.length ? '[data-act="rm-course"][data-c="' + Math.min(ci, S.courses.length - 1) + '"]' : '#smAddCourse');
            say('Course removed.');
        } else if (act === 'rm-dish') {
            S.courses[ci].dishes.splice(di, 1); clampPicks(S.courses[ci]); touch();
            drawCourses('[data-act="add-dish"][data-c="' + ci + '"]');
        } else if (act === 'add-dish') {
            openPicker(ci, b);
        } else if (act === 'sup') {
            editSupplement(b, ci, di);
        }
    });

    // ---- extra charge on a dish ------------------------------------------------------------------------------
    function editSupplement(btn, ci, di) {
        var d = S.courses[ci].dishes[di];
        var input = document.createElement('input');
        input.type = 'number'; input.min = '0'; input.step = '1'; input.inputMode = 'decimal';
        input.className = 'in sup-in'; input.value = d.supplement > 0 ? d.supplement : '';
        input.placeholder = '0';
        input.setAttribute('data-c', ci); input.setAttribute('data-d', di);
        input.setAttribute('aria-label', 'Extra charge for ' + ((BY[d.id] || {}).name || 'this dish') + ' in ' + CUR + '. Leave empty for none.');
        btn.replaceWith(input); input.focus(); input.select();
        input.addEventListener('keydown', function (e) {
            if (e.key === 'Enter') { e.preventDefault(); commitSupplement(input, true); }
            else if (e.key === 'Escape') { e.preventDefault(); input.setAttribute('data-cancel', '1'); drawCourses('[data-act="sup"][data-c="' + ci + '"][data-d="' + di + '"]'); }
        });
        input.addEventListener('blur', function () { if (!input.getAttribute('data-cancel') && input.isConnected) commitSupplement(input, false); });
    }
    function commitSupplement(input, refocus) {
        var ci = +input.getAttribute('data-c'), di = +input.getAttribute('data-d');
        var v = parseFloat(input.value); var d = S.courses[ci] && S.courses[ci].dishes[di];
        if (!d) return;
        input.setAttribute('data-cancel', '1');
        d.supplement = isFinite(v) && v > 0 ? Math.round(v * 100) / 100 : 0; touch();
        drawCourses(refocus ? '[data-act="sup"][data-c="' + ci + '"][data-d="' + di + '"]' : null);
    }

    // ---- dish picker -----------------------------------------------------------------------------------------
    var pickFor = -1, pickOpener = null;
    function openPicker(ci, opener) {
        pickFor = ci; pickOpener = opener;
        els.pickFor.textContent = 'For ' + (S.courses[ci].name || 'this course') + '. Tick as many as you like.';
        els.pickQ.value = '';
        drawPickList();
        if (typeof els.pick.showModal === 'function') els.pick.showModal(); else els.pick.setAttribute('open', '');
        els.pickQ.focus();
    }
    function drawPickList() {
        var taken = {}; S.courses[pickFor].dishes.forEach(function (d) { taken[d.id] = true; });
        var q = els.pickQ.value.trim().toLowerCase(), groups = {}, order = [];
        // keep ticks that were made before a search narrowed the list
        var ticked = {}; Array.prototype.forEach.call(els.pickList.querySelectorAll('input:checked'), function (c) { ticked[c.value] = true; });
        ITEMS.forEach(function (it) {
            if (taken[it.id]) return;
            if (q && (it.name + ' ' + it.category).toLowerCase().indexOf(q) < 0 && !ticked[it.id]) return;
            if (!groups[it.category]) { groups[it.category] = []; order.push(it.category); }
            groups[it.category].push(it);
        });
        var html = '';
        order.forEach(function (cat) {
            html += '<fieldset class="pk-cat"><legend>' + esc(cat) + '</legend>';
            groups[cat].forEach(function (it) {
                html += '<label class="pk-row ckb"><input type="checkbox" value="' + it.id + '"' + (ticked[it.id] ? ' checked' : '') + '><i>' +
                    '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="3" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true" focusable="false"><path d="M5 12.5l4.5 4.5L19 7.5"></path></svg></i>' +
                    '<span class="pk-name">' + esc(it.name) + '</span>' +
                    (it.available ? '' : '<small>Not served now</small>') +
                    (it.hasPhoto ? '' : '<small class="np">' + ICON_NOPHOTO + ' No photo</small>') +
                    '<span class="pk-price">' + esc(money(it.price)) + '</span></label>';
            });
            html += '</fieldset>';
        });
        els.pickList.innerHTML = html || '<p class="muted">' + (q ? 'Nothing on the carte matches that.' : 'Every dish is already in this course.') + '</p>';
    }
    els.pickQ.addEventListener('input', drawPickList);
    els.pickAdd.addEventListener('click', function () {
        var ids = Array.prototype.map.call(els.pickList.querySelectorAll('input:checked'), function (c) { return parseInt(c.value, 10); });
        if (ids.length && pickFor >= 0) {
            ids.forEach(function (id) { S.courses[pickFor].dishes.push({ id: id, supplement: 0 }); });
            touch(); say(ids.length + (ids.length === 1 ? ' dish added.' : ' dishes added.'));
        }
        if (typeof els.pick.close === 'function') els.pick.close(); else els.pick.removeAttribute('open');
        drawCourses(pickFor >= 0 ? '[data-act="add-dish"][data-c="' + pickFor + '"]' : null);
    });

    // ---- moving courses: drag the handle, or use the arrow keys on it --------------------------------------------
    function moveCourse(from, to) {
        if (to < 0 || to >= S.courses.length || from === to) return;
        var c = S.courses.splice(from, 1)[0]; S.courses.splice(to, 0, c); touch();
        drawCourses('[data-act="grip"][data-c="' + to + '"]');
        say((c.name || 'Course') + ' is now number ' + (to + 1) + ' of ' + S.courses.length + '.');
    }
    els.courses.addEventListener('keydown', function (e) {
        var g = e.target.closest('[data-act="grip"]'); if (!g) return;
        var i = +g.getAttribute('data-c');
        if (e.key === 'ArrowUp') { e.preventDefault(); moveCourse(i, i - 1); }
        else if (e.key === 'ArrowDown') { e.preventDefault(); moveCourse(i, i + 1); }
    });
    els.courses.addEventListener('pointerdown', function (e) {
        var g = e.target.closest('[data-act="grip"]'); if (!g || e.button > 0) return;
        var li = g.closest('.course'), list = els.courses;
        e.preventDefault();
        try { g.setPointerCapture(e.pointerId); } catch (x) { /* older browsers */ }
        li.classList.add('dragging');
        function move(ev) {
            var y = ev.clientY, sibs = Array.prototype.filter.call(list.children, function (n) { return n !== li; }), next = null;
            for (var k = 0; k < sibs.length; k++) {
                var r = sibs[k].getBoundingClientRect();
                if (y < r.top + r.height / 2) { next = sibs[k]; break; }
            }
            if (next) list.insertBefore(li, next); else list.appendChild(li);
        }
        function up() {
            document.removeEventListener('pointermove', move);
            document.removeEventListener('pointerup', up);
            document.removeEventListener('pointercancel', up);
            li.classList.remove('dragging');
            var order = Array.prototype.map.call(list.children, function (n) { return +n.getAttribute('data-i'); });
            var changed = order.some(function (v, k) { return v !== k; });
            if (changed) {
                S.courses = order.map(function (k) { return S.courses[k]; }); touch();
                drawCourses('[data-act="grip"][data-c="' + order.indexOf(+li.getAttribute('data-i')) + '"]');
                say('Courses reordered.');
            }
        }
        // listen on the document: the pointer leaves the small handle at once, and capture is not available everywhere
        document.addEventListener('pointermove', move);
        document.addEventListener('pointerup', up);
        document.addEventListener('pointercancel', up);
    });

    // ---- the guest preview and the price check ---------------------------------------------------------------------
    function joiner(c) { return c.picks === 0 ? 'and' : 'or'; }
    function drawPreview() {
        els.pvName.textContent = S.name && S.name.trim() ? S.name.trim() : 'Name your set menu';
        var n = S.courses.length;
        els.pvTag.textContent = (S.tagline && S.tagline.trim()) ? S.tagline.trim() :
            (n ? (WORDS[n] || n) + (n === 1 ? ' course' : ' courses') + ', one price' : '');
        var html = '';
        S.courses.forEach(function (c) {
            var spans = c.dishes.map(function (d) {
                var it = BY[d.id] || { name: 'Unavailable dish' };
                return '<span>' + esc(it.name) + (d.supplement > 0 ? ', + ' + esc(money(d.supplement)) : '') + '</span>';
            });
            var dd = spans.length ? spans.join('<em>' + joiner(c) + '</em>') : '<span class="muted">No dishes yet</span>';
            if (c.picks >= 2 && spans.length) dd = '<em class="lead">Choose ' + (WORDS[c.picks] || c.picks).toLowerCase() + '</em>' + dd;
            var pics = c.dishes.slice(0, 2).map(function (d) { return (BY[d.id] || {}).pic || ''; }).join('');
            html += '<div class="mc-row"><dt>' + (c.name && c.name.trim() ? esc(c.name.trim()) : '<span class="muted">Unnamed course</span>') + '</dt><dd>' + dd + '</dd><div class="pair">' + pics + '</div></div>';
        });
        els.pvCourses.innerHTML = html;
        els.pvPrice.textContent = S.price > 0 ? money(S.price) : CUR + ' —';
        drawCheck();
    }

    function drawCheck() {
        var rows = '', sum = 0, any = false;
        S.courses.forEach(function (c) {
            var take = c.picks === 0 ? c.dishes.length : Math.min(c.picks, c.dishes.length);
            c.dishes.slice(0, take).forEach(function (d) {
                var it = BY[d.id]; if (!it) return;
                any = true; sum += it.price;
                rows += '<tr><td>' + esc(it.name) + '<small>' + esc(c.name || 'Course') + '</small></td><td>' + esc(money(it.price)) + '</td></tr>';
            });
        });
        if (!any) { els.pcBody.innerHTML = '<tr><td colspan="2" class="muted">Add dishes to compare the price.</td></tr>'; return; }
        rows += '<tr class="sum"><td>Ordered separately</td><td>' + esc(money(sum)) + '</td></tr>';
        if (S.price > 0) {
            rows += '<tr class="sum"><td>Set price</td><td>' + esc(money(S.price)) + '</td></tr>';
            var diff = sum - S.price;
            if (diff > 0.004) rows += '<tr class="save"><td>Guests save</td><td>' + esc(money(diff)) + ' (' + Math.round(diff / sum * 100) + '%)</td></tr>';
            else if (diff < -0.004) rows += '<tr class="sum"><td>Set price is higher by</td><td>' + esc(money(-diff)) + '</td></tr>';
            else rows += '<tr class="sum"><td>Same as ordering separately</td><td></td></tr>';
        } else {
            rows += '<tr class="sum"><td colspan="2" class="muted">Enter the price per guest to see the saving.</td></tr>';
        }
        els.pcBody.innerHTML = rows;
    }

    // ---- saving ---------------------------------------------------------------------------------------------
    // Enter in a text box must not save the whole menu by accident; the buttons at the top do that.
    form.addEventListener('keydown', function (e) {
        if (e.key === 'Enter' && e.target.tagName === 'INPUT' && e.target.type !== 'submit' && e.target.type !== 'file') e.preventDefault();
    });
    form.addEventListener('submit', function () {
        els.payload.value = JSON.stringify({
            name: (S.name || '').trim(), tagline: (S.tagline || '').trim(), description: (S.description || '').trim(),
            price: S.price || 0, days: S.days, lunch: !!S.lunch, dinner: !!S.dinner,
            courses: S.courses.map(function (c) {
                return { name: (c.name || '').trim(), picks: c.picks, dishes: c.dishes.map(function (d) { return { id: d.id, supplement: d.supplement || 0 }; }) };
            })
        });
        dirty = false;
    });
    window.addEventListener('beforeunload', function (e) { if (dirty) { e.preventDefault(); e.returnValue = ''; } });

    syncChips();
    drawCourses();
})();
