
//   Live view   - tables are coloured from the real reservations (free, free now but booked later, booked, seated,
//                 switched off) and the side panel lists the day's bookings. Refreshes by itself.
//   Edit layout - drag a table onto a dot (mouse, finger or pen) or move it with the arrow keys. The side panel
//                 changes the selected table (number, seats, area, in service) and saves as you go.
(function () {
    'use strict';

    var POLL_MS = 20000;
    var BIG_TABLE = 6; // tables with this many seats or more are drawn as long tables

    document.addEventListener('DOMContentLoaded', function () {
        var board = document.getElementById('fpBoard');
        if (!board) return; // no dining areas yet

        var $ = function (id) { return document.getElementById(id); };
        var cols = parseInt(board.getAttribute('data-cols'), 10) || 10;
        var rows = parseInt(board.getAttribute('data-rows'), 10) || 6;
        var pageUrl = board.getAttribute('data-page-url') || window.location.pathname;

        var areaSelect = $('fpAreaSelect'), dateInput = $('fpDate'), timeInput = $('fpTime'), nowBtn = $('fpNow');
        var modeLive = $('fpModeLive'), modeEdit = $('fpModeEdit');
        var legend = $('fpLegend'), liveLabel = $('fpLive'), pickLine = $('fpPick');
        var tray = $('fpTray'), trayWrap = $('fpTrayWrap'), hint = $('fpHint');
        var bookingList = $('fpBookings'), bookingDay = $('fpBookingsDay');
        var livePanel = $('fpLivePanel'), editPanel = $('fpEditPanel'), none = $('fpNone'), form = $('fpForm');
        var title = $('fpTitle'), sub = $('fpSub'), numberInput = $('fpNumber'), seatsOut = $('fpSeats');
        var seatsDown = $('fpSeatsDown'), seatsUp = $('fpSeatsUp'), areaInput = $('fpArea'), activeInput = $('fpActive');
        var unplaceBtn = $('fpUnplace'), synced = $('fpSynced');
        var scroller = board.parentNode;

        var allTables = [];
        try { allTables = JSON.parse($('fpTablesData').textContent) || []; } catch (e) { allTables = []; }

        var cells = [];            // index = y * cols + x
        var tiles = {};            // tableId -> element (created once, moved between dots)
        var statusById = {};       // tableId -> last status from the server
        var lastBookings = [];
        var statusSeq = 0;         // ignore out-of-date responses
        var followNow = true;      // true until the admin picks a date/time themselves
        var editMode = false;
        var selectedId = null;
        var drag = null;
        var saveTimer = null, saveSeq = 0, savedAt = 0, tickTimer = null;

        // ---------- small helpers ----------
        function pad(n) { return n < 10 ? '0' + n : String(n); }
        function currentAreaId() { return parseInt(areaSelect.value, 10); }
        function tableById(id) { return allTables.find(function (t) { return t.id === id; }); }
        function tileId(tile) { return parseInt(tile.getAttribute('data-table-id'), 10); }
        function antiforgeryToken() {
            var input = document.querySelector('input[name="__RequestVerificationToken"]');
            return input ? input.value : '';
        }
        function isPlaced(t) {
            return t.positionX !== null && t.positionX !== undefined && t.positionY !== null && t.positionY !== undefined;
        }
        function seatsText(n) { return n + (n === 1 ? ' seat' : ' seats'); }
        function areaName(id) {
            var o = areaSelect.querySelector('option[value="' + id + '"]');
            return o ? o.textContent.replace(/ \(hidden\)$/, '') : 'that area';
        }

        function toast(text, isError) {
            var el = document.createElement('div');
            el.className = 'fp-toast' + (isError ? ' error' : '');
            el.setAttribute('role', 'status');
            el.textContent = text;
            document.body.appendChild(el);
            setTimeout(function () { if (el.parentNode) el.parentNode.removeChild(el); }, 4200);
        }

        // ---------- the board ----------
        function buildCells() {
            board.innerHTML = '';
            cells = [];
            for (var y = 0; y < rows; y++) {
                for (var x = 0; x < cols; x++) {
                    var cell = document.createElement('div');
                    cell.className = 'cell';
                    cell.setAttribute('data-x', String(x));
                    cell.setAttribute('data-y', String(y));
                    board.appendChild(cell);
                    cells.push(cell);
                }
            }
        }
        function cellAt(x, y) { return cells[y * cols + x] || null; }

        function makeTile(table) {
            var tile = document.createElement('div');
            tile.setAttribute('data-table-id', String(table.id));
            tile.setAttribute('tabindex', '0');
            tile.setAttribute('role', 'button');

            var num = document.createElement('span');
            num.className = 'tb-n';
            var cap = document.createElement('small');
            cap.className = 'tb-c';
            var state = document.createElement('small');
            state.className = 'tb-s';
            tile.appendChild(num);
            tile.appendChild(cap);
            tile.appendChild(state);

            tile.addEventListener('pointerdown', onPointerDown);
            tile.addEventListener('pointermove', onPointerMove);
            tile.addEventListener('pointerup', function (e) { endDrag(e, false); });
            tile.addEventListener('pointercancel', function (e) { endDrag(e, true); });
            tile.addEventListener('keydown', onTileKey);
            tile.addEventListener('click', function () {
                if (tile.getAttribute('data-ignore-click') === '1') return;
                select(table.id);
            });
            paintTile(tile, table);
            return tile;
        }

        // shape, number, seats and state all come from the table's data and its last status
        function paintTile(tile, table) {
            var shape = table.capacity >= BIG_TABLE ? 'r' : 'c';
            var st = statusById[table.id];
            // a switched-off table is off whatever the last status said; a table just switched back on shows as free until the next refresh
            var live = table.isActive && st && st.state !== 'off' ? st : null;
            var state = !table.isActive ? 'off' : live ? live.state : 'free';
            tile.className = 'tb ' + shape + ' s-' + state +
                (table.id === selectedId ? ' sel' : '') + (tile.classList.contains('dragging') ? ' dragging' : '') + (tile.classList.contains('saving') ? ' saving' : '');
            tile.querySelector('.tb-n').textContent = table.tableNumber;
            tile.querySelector('.tb-c').textContent = seatsText(table.capacity);
            var stateText = !table.isActive ? 'Off' : (live && live.label) || '';
            tile.querySelector('.tb-s').textContent = stateText;
            // in the live view the state line (a time, "Seated", "Off") takes the place of the seat count so the tile stays readable
            tile.classList.toggle('has-state', !!stateText && !board.classList.contains('editing'));
            var detail = !table.isActive ? 'Switched off. Not offered to guests.' : (live && live.detail) || '';
            tile.title = detail;
            tile.setAttribute('aria-label', 'Table ' + table.tableNumber + ', ' + seatsText(table.capacity) + (detail ? ', ' + detail : '') + (isPlaced(table) ? '' : ', not on the plan'));
        }

        function renderTables() {
            var areaId = currentAreaId();
            // Detach everything, then put back only this area's tables. Tiles are reused, so nothing flickers.
            Object.keys(tiles).forEach(function (id) {
                var t = tiles[id];
                if (t.parentNode) t.parentNode.removeChild(t);
            });
            tray.innerHTML = '';

            var unplaced = 0;
            allTables.filter(function (t) { return t.diningAreaId === areaId; })
                .sort(function (a, b) { return a.tableNumber.length - b.tableNumber.length || a.tableNumber.localeCompare(b.tableNumber, undefined, { numeric: true }); })
                .forEach(function (t) {
                    var tile = tiles[t.id];
                    if (!tile) { tile = makeTile(t); tiles[t.id] = tile; } else paintTile(tile, t);
                    var cell = isPlaced(t) ? cellAt(t.positionX, t.positionY) : null;
                    if (cell) cell.appendChild(tile);
                    else { tray.appendChild(tile); unplaced++; }
                });
            if (selectedId !== null) {
                var sel = tableById(selectedId);
                if (!sel || sel.diningAreaId !== areaId) selectedId = null;
            }
            updateHint(unplaced);
            syncSelection();
        }

        function updateHint(unplacedCount) {
            if (typeof unplacedCount !== 'number') unplacedCount = tray.querySelectorAll('.tb').length;
            if (editMode) {
                hint.textContent = 'Drag a table onto a dot, or focus it and press the arrow keys. Drag it back to the tray to take it off the plan.';
            } else if (unplacedCount > 0) {
                hint.textContent = unplacedCount + (unplacedCount === 1 ? ' table is not' : ' tables are not') + ' on the plan yet. Switch to Edit layout to place ' + (unplacedCount === 1 ? 'it' : 'them') + '.';
            } else {
                hint.textContent = 'Select a table for its bookings. The plan refreshes by itself every 20 seconds.';
            }
        }

        // ---------- selection and the side panel ----------
        function select(id) {
            selectedId = (id === selectedId && !editMode) ? null : id;
            savedAt = 0; setSynced('', false);
            syncSelection();
        }

        function syncSelection() { highlight(); fillForm(); }

        // the live panel: what is known about the chosen table, and its bookings marked
        function highlight() {
            Object.keys(tiles).forEach(function (id) {
                tiles[id].classList.toggle('sel', parseInt(id, 10) === selectedId);
            });
            var t = selectedId === null ? null : tableById(selectedId);
            if (t) {
                var st = statusById[t.id];
                var detail = !t.isActive ? 'Switched off. Not offered to guests.' : (st && st.detail) || 'Free for the rest of the day.';
                pickLine.textContent = 'Table ' + t.tableNumber + ' (' + seatsText(t.capacity) + '): ' + detail;
                pickLine.hidden = false;
            } else {
                pickLine.hidden = true;
            }
            Array.prototype.forEach.call(bookingList.children, function (li) {
                li.classList.toggle('on', t !== null && li.getAttribute('data-table-id') === String(t.id));
            });
        }

        // the edit panel
        function fillForm() {
            var t = selectedId === null ? null : tableById(selectedId);
            if (!t) { none.hidden = false; form.hidden = true; return; }
            none.hidden = true; form.hidden = false;
            title.textContent = 'Table ' + t.tableNumber;
            sub.textContent = isPlaced(t)
                ? 'Selected. Drag it, or press the arrow keys to move it one dot.'
                : 'Selected. It is not on the plan yet. Drag it onto a dot.';
            if (document.activeElement !== numberInput) numberInput.value = t.tableNumber;
            seatsOut.textContent = String(t.capacity);
            areaInput.value = String(t.diningAreaId);
            activeInput.checked = !!t.isActive;
            unplaceBtn.hidden = !isPlaced(t);
        }

        // ---------- saving the side panel ----------
        function setSynced(text, isError) {
            synced.textContent = text;
            synced.classList.toggle('err', !!isError);
        }
        function tick() {
            if (!savedAt) return;
            var s = Math.round((Date.now() - savedAt) / 1000);
            if (s < 60) setSynced('Saved ' + (s < 2 ? 'just now' : s + ' s ago'), false);
            else { setSynced('Saved', false); savedAt = 0; clearInterval(tickTimer); tickTimer = null; }
        }

        function queueSave(delay) {
            clearTimeout(saveTimer);
            setSynced('Saving', false);
            saveTimer = setTimeout(saveTable, delay);
        }

        function saveTable() {
            var t = selectedId === null ? null : tableById(selectedId);
            if (!t) return;
            var mine = ++saveSeq;
            var body = {
                id: t.id,
                tableNumber: numberInput.value.trim(),
                diningAreaId: areaInput.value,
                capacity: parseInt(seatsOut.textContent, 10) || t.capacity,
                isActive: activeInput.checked ? 'true' : 'false'
            };
            postForm('UpdateTable', body).then(function (res) {
                if (mine !== saveSeq) return;
                if (!res.success) { setSynced('Not saved. ' + (res.message || 'Try again.'), true); return; }
                var s = res.table;
                t.tableNumber = s.tableNumber; t.capacity = s.capacity; t.isActive = s.isActive;
                t.positionX = s.positionX; t.positionY = s.positionY;
                var moved = !!res.movedArea;
                t.diningAreaId = s.diningAreaId;
                savedAt = Date.now();
                if (!tickTimer) tickTimer = setInterval(tick, 1000);
                tick();
                if (moved) {
                    areaSelect.value = String(s.diningAreaId);
                    renderTables();
                    refreshStatuses();
                    toast('Table ' + s.tableNumber + ' moved to ' + areaName(s.diningAreaId) + '. It is in the tray. Drag it onto that plan.');
                    selectedId = t.id; syncSelection();
                    return;
                }
                var tile = tiles[t.id];
                if (tile) paintTile(tile, t);
                title.textContent = 'Table ' + t.tableNumber;
                highlight();
                refreshStatuses(); // the booking list names tables by number
            }).catch(function () {
                if (mine !== saveSeq) return;
                setSynced('Not saved. Check the connection and try again.', true);
            });
        }

        numberInput.addEventListener('input', function () { queueSave(700); });
        areaInput.addEventListener('change', function () { queueSave(0); });
        activeInput.addEventListener('change', function () { queueSave(0); });
        function stepSeats(d) {
            var n = (parseInt(seatsOut.textContent, 10) || 1) + d;
            if (n < 1 || n > 30) return;
            seatsOut.textContent = String(n);
            queueSave(450);
        }
        seatsDown.addEventListener('click', function () { stepSeats(-1); });
        seatsUp.addEventListener('click', function () { stepSeats(1); });
        $('fpTableForm').addEventListener('submit', function (e) { e.preventDefault(); });
        unplaceBtn.addEventListener('click', function () {
            var tile = selectedId === null ? null : tiles[selectedId];
            if (tile) commitUnplace(tile);
        });

        // ---------- live status (the "is it booked?" part) ----------
        function setInputsToNow() {
            var d = new Date();
            if (document.activeElement !== dateInput) dateInput.value = d.getFullYear() + '-' + pad(d.getMonth() + 1) + '-' + pad(d.getDate());
            if (document.activeElement !== timeInput) timeInput.value = pad(d.getHours()) + ':' + pad(d.getMinutes());
        }

        function statusClassFor(status) {
            return status === 'Seated' ? 'st-seated' : status === 'Pending' ? 'st-pending' : 'st-confirmed';
        }

        function renderBookings(bookings, dayLabel) {
            lastBookings = bookings;
            bookingDay.textContent = dayLabel;
            bookingList.innerHTML = '';
            if (!bookings.length) {
                var empty = document.createElement('li');
                empty.className = 'bk-empty';
                empty.textContent = 'No bookings in this area for that day.';
                bookingList.appendChild(empty);
                return;
            }
            bookings.forEach(function (b) {
                var li = document.createElement('li');
                li.setAttribute('data-table-id', String(b.tableId));
                var left = document.createElement('span');
                var strong = document.createElement('b');
                strong.textContent = 'Table ' + b.table + ' at ' + b.start;
                var small = document.createElement('small');
                // customer names are user-supplied text, so they only ever go in through textContent
                small.textContent = b.customer + ', party of ' + b.party + ' until ' + b.end;
                left.appendChild(strong);
                left.appendChild(small);
                var badge = document.createElement('span');
                badge.className = 'st ' + statusClassFor(b.status);
                badge.textContent = b.status;
                li.appendChild(left);
                li.appendChild(badge);
                bookingList.appendChild(li);
            });
            highlight();
        }

        function refreshStatuses() {
            var areaId = currentAreaId();
            if (!areaId) return;

            if (followNow) setInputsToNow();
            var url = pageUrl + '?handler=FloorStatus&diningAreaId=' + encodeURIComponent(areaId);
            if (!followNow && dateInput.value) url += '&date=' + encodeURIComponent(dateInput.value);
            if (!followNow && timeInput.value) url += '&time=' + encodeURIComponent(timeInput.value);

            var mine = ++statusSeq;
            fetch(url, { credentials: 'same-origin', headers: { 'Accept': 'application/json' } })
                .then(function (r) {
                    if (!r.ok || (r.headers.get('content-type') || '').indexOf('json') === -1) throw new Error('bad response');
                    return r.json();
                })
                .then(function (res) {
                    if (mine !== statusSeq) return; // a newer request has already been sent
                    if (!res.success) { liveLabel.classList.remove('is-live'); liveLabel.textContent = res.message || 'Could not load bookings.'; return; }

                    statusById = {};
                    (res.tables || []).forEach(function (s) { statusById[s.tableId] = s; });
                    Object.keys(tiles).forEach(function (id) {
                        var table = tableById(parseInt(id, 10));
                        if (table) paintTile(tiles[id], table);
                    });
                    renderBookings(res.bookings || [], res.isNow ? 'today' : res.day);

                    var stamp = new Date();
                    liveLabel.classList.toggle('is-live', !!res.isNow);
                    liveLabel.textContent = res.isNow
                        ? 'Live, updated ' + pad(stamp.getHours()) + ':' + pad(stamp.getMinutes()) + ':' + pad(stamp.getSeconds())
                        : 'Showing ' + res.asOf;
                })
                .catch(function () {
                    if (mine !== statusSeq) return;
                    liveLabel.classList.remove('is-live');
                    liveLabel.textContent = 'Cannot reach the server. Showing the last known state.';
                });
        }

        // ---------- saving moves ----------
        function postForm(handler, params) {
            var body = new URLSearchParams();
            Object.keys(params).forEach(function (k) { body.append(k, params[k]); });
            body.append('__RequestVerificationToken', antiforgeryToken());
            return fetch(pageUrl + '?handler=' + handler, {
                method: 'POST',
                credentials: 'same-origin',
                headers: { 'Content-Type': 'application/x-www-form-urlencoded', 'Accept': 'application/json' },
                body: body
            }).then(function (r) {
                if (!r.ok) throw new Error('http ' + r.status);
                return r.json();
            });
        }

        function errText(err, fallback) {
            return err && err.message && err.message.indexOf('http') !== 0 && err.message !== 'bad response' ? err.message : fallback;
        }

        function commitMove(tile, x, y) {
            var id = tileId(tile);
            var table = tableById(id);
            var cell = cellAt(x, y);
            if (!table || !cell) return;
            if (table.positionX === x && table.positionY === y && tile.parentNode === cell) return; // dropped on its own dot

            var previousParent = tile.parentNode;
            var previous = { x: table.positionX, y: table.positionY };
            cell.appendChild(tile);            // move it now so it feels instant
            table.positionX = x; table.positionY = y;
            tile.classList.add('saving');

            postForm('MoveTable', { tableId: id, x: x, y: y }).then(function (res) {
                tile.classList.remove('saving');
                if (!res.success) throw new Error(res.message || 'Could not move that table.');
                paintTile(tile, table);
                updateHint();
                if (id === selectedId) syncSelection();
            }).catch(function (err) {
                // put it back exactly where it was
                tile.classList.remove('saving');
                table.positionX = previous.x; table.positionY = previous.y;
                if (previousParent) previousParent.appendChild(tile);
                toast(errText(err, 'Could not save. The table was put back.'), true);
                updateHint();
            });
        }

        function commitUnplace(tile) {
            var id = tileId(tile);
            var table = tableById(id);
            if (!table || tile.parentNode === tray) return;

            var previousParent = tile.parentNode;
            var previous = { x: table.positionX, y: table.positionY };
            tray.appendChild(tile);
            table.positionX = null; table.positionY = null;
            tile.classList.add('saving');
            updateHint();
            if (id === selectedId) syncSelection();

            postForm('UnplaceTable', { tableId: id }).then(function (res) {
                tile.classList.remove('saving');
                if (!res.success) throw new Error(res.message || 'Could not update that table.');
                paintTile(tile, table);
            }).catch(function (err) {
                tile.classList.remove('saving');
                table.positionX = previous.x; table.positionY = previous.y;
                if (previousParent) previousParent.appendChild(tile);
                toast(errText(err, 'Could not save. The table was put back.'), true);
                updateHint();
                if (id === selectedId) syncSelection();
            });
        }

        // ---------- dragging (Pointer Events: mouse, touch and pen) ----------
        function onPointerDown(e) {
            if (!editMode || drag) return;
            if (e.pointerType === 'mouse' && e.button !== 0) return;
            var tile = e.currentTarget;
            if (tile.classList.contains('saving')) return;
            if (selectedId !== tileId(tile)) { selectedId = tileId(tile); savedAt = 0; setSynced('', false); }
            syncSelection();
            drag = { tile: tile, pointerId: e.pointerId, startX: e.clientX, startY: e.clientY, started: false, ghost: null, target: null };
            try { tile.setPointerCapture(e.pointerId); } catch (err) { /* older browsers: moves still arrive while over the tile */ }
        }

        function startDrag() {
            var rect = drag.tile.getBoundingClientRect();
            drag.offX = drag.startX - rect.left;
            drag.offY = drag.startY - rect.top;
            var ghost = drag.tile.cloneNode(true);
            ghost.classList.add('ghost');
            ghost.classList.remove('sel');
            ghost.removeAttribute('tabindex');
            ghost.removeAttribute('id');
            ghost.setAttribute('aria-hidden', 'true');
            ghost.style.width = rect.width + 'px';
            ghost.style.height = rect.height + 'px';
            document.body.appendChild(ghost);
            drag.ghost = ghost;
            drag.tile.classList.add('dragging');
            drag.tile.setAttribute('data-ignore-click', '1');
            drag.started = true;
        }

        function clearHighlights() {
            board.querySelectorAll('.cell.over, .cell.bad').forEach(function (c) { c.classList.remove('over', 'bad'); });
            trayWrap.classList.remove('over');
        }

        function updateTarget(e) {
            clearHighlights();
            drag.target = null;
            var el = document.elementFromPoint(e.clientX, e.clientY); // the ghost ignores the pointer, so this sees what is underneath
            if (!el || !el.closest) return;

            var cell = el.closest('.cell');
            if (cell && board.contains(cell)) {
                var x = parseInt(cell.getAttribute('data-x'), 10);
                var y = parseInt(cell.getAttribute('data-y'), 10);
                var occupant = cell.querySelector('.tb');
                var blocked = occupant && occupant !== drag.tile;
                cell.classList.add(blocked ? 'bad' : 'over');
                drag.target = { type: 'cell', x: x, y: y, blocked: !!blocked };
                return;
            }
            if (el.closest('#fpTrayWrap')) {
                trayWrap.classList.add('over');
                drag.target = { type: 'tray' };
            }
        }

        function autoScroll(e) {
            var r = scroller.getBoundingClientRect();
            if (e.clientX < r.left + 40) scroller.scrollLeft -= 14;
            else if (e.clientX > r.right - 40) scroller.scrollLeft += 14;
        }

        function onPointerMove(e) {
            if (!drag || e.pointerId !== drag.pointerId) return;
            if (!drag.started) {
                if (Math.abs(e.clientX - drag.startX) + Math.abs(e.clientY - drag.startY) < 6) return;
                startDrag();
            }
            e.preventDefault();
            drag.ghost.style.left = (e.clientX - drag.offX) + 'px';
            drag.ghost.style.top = (e.clientY - drag.offY) + 'px';
            updateTarget(e);
            autoScroll(e);
        }

        function endDrag(e, cancelled) {
            if (!drag || e.pointerId !== drag.pointerId) return;
            var d = drag;
            drag = null;
            try { d.tile.releasePointerCapture(d.pointerId); } catch (err) { /* nothing to release */ }
            if (!d.started) return; // it was a tap, not a drag

            if (d.ghost && d.ghost.parentNode) d.ghost.parentNode.removeChild(d.ghost);
            d.tile.classList.remove('dragging');
            clearHighlights();
            // the click that follows a drag must not count as a tap
            setTimeout(function () { d.tile.removeAttribute('data-ignore-click'); }, 0);
            if (cancelled || !d.target) return;

            if (d.target.type === 'cell') {
                if (d.target.blocked) toast('Another table is already there.', true);
                else commitMove(d.tile, d.target.x, d.target.y);
            } else if (d.target.type === 'tray') {
                commitUnplace(d.tile);
            }
        }

        // ---------- keyboard: arrow keys move the focused table one dot ----------
        function firstFreeCell() {
            for (var i = 0; i < cells.length; i++) if (!cells[i].querySelector('.tb')) return cells[i];
            return null;
        }
        function onTileKey(e) {
            var tile = e.currentTarget;
            var table = tableById(tileId(tile));
            if (!table) return;

            if (e.key === 'Enter' || e.key === ' ') {
                e.preventDefault();
                if (editMode && !isPlaced(table)) {
                    var free = firstFreeCell();
                    if (free) commitMove(tile, parseInt(free.getAttribute('data-x'), 10), parseInt(free.getAttribute('data-y'), 10));
                    else toast('The plan is full.', true);
                    return;
                }
                select(table.id);
                return;
            }
            if (!editMode || !isPlaced(table)) return;

            var dx = 0, dy = 0;
            if (e.key === 'ArrowLeft') dx = -1; else if (e.key === 'ArrowRight') dx = 1;
            else if (e.key === 'ArrowUp') dy = -1; else if (e.key === 'ArrowDown') dy = 1;
            else if (e.key === 'Delete' || e.key === 'Backspace') { e.preventDefault(); commitUnplace(tile); return; }
            else return;

            e.preventDefault();
            var nx = table.positionX + dx, ny = table.positionY + dy;
            if (nx < 0 || nx >= cols || ny < 0 || ny >= rows) return;
            var target = cellAt(nx, ny);
            if (target.querySelector('.tb')) { toast('Another table is already there.', true); return; }
            commitMove(tile, nx, ny);
            tile.focus();
        }

        // ---------- mode, controls, polling ----------
        function setMode(edit) {
            editMode = edit;
            document.body.classList.toggle('fp-edit', edit);
            board.classList.toggle('editing', edit);
            Object.keys(tiles).forEach(function (id) { var tb = tableById(parseInt(id, 10)); if (tb) paintTile(tiles[id], tb); });
            modeLive.setAttribute('aria-pressed', String(!edit));
            modeEdit.setAttribute('aria-pressed', String(edit));
            trayWrap.hidden = !edit;
            legend.hidden = edit;
            livePanel.hidden = edit;
            editPanel.hidden = !edit;
            updateHint();
            syncSelection();
        }

        modeLive.addEventListener('click', function () { setMode(false); });
        modeEdit.addEventListener('click', function () { setMode(true); });
        areaSelect.addEventListener('change', function () { selectedId = null; renderTables(); refreshStatuses(); });
        dateInput.addEventListener('change', function () { followNow = false; refreshStatuses(); });
        timeInput.addEventListener('change', function () { followNow = false; refreshStatuses(); });
        nowBtn.addEventListener('click', function () { followNow = true; refreshStatuses(); });
        document.addEventListener('visibilitychange', function () { if (!document.hidden) refreshStatuses(); });

        buildCells();
        renderTables();
        setInputsToNow();
        setMode(board.getAttribute('data-start-edit') === 'true');
        refreshStatuses();
        setInterval(function () { if (!document.hidden && !drag) refreshStatuses(); }, POLL_MS);
    });
})();
