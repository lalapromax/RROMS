// The menu page: the à la carte filters and the running total on the open set menu. Without JavaScript every dish is
// shown and the total is the price of the choices ticked when the page loaded.
(function () {
    'use strict';

    // ---------------------------------------------------------------- à la carte filters
    var chips = document.querySelector('[data-chips]');
    if (chips) {
        var dishes = Array.prototype.slice.call(document.querySelectorAll('[data-dish]'));
        var empty = document.querySelector('[data-carte-empty]');
        var count = document.querySelector('[data-carte-count]');
        var note = document.querySelector('[data-suits-note]');
        var state = { cat: null, veg: false, suits: false };

        var pressed = function (el) { return el && el.getAttribute('aria-pressed') === 'true'; };
        var suitsChip = chips.querySelector('[data-filter="suits"]');
        state.suits = pressed(suitsChip);

        function apply() {
            var shown = 0;
            dishes.forEach(function (d) {
                var ok = true;
                if (state.cat && d.getAttribute('data-cat') !== state.cat) ok = false;
                if (state.veg && d.getAttribute('data-veg') !== '1') ok = false;
                if (state.suits && d.getAttribute('data-suits') !== '1') ok = false;
                d.hidden = !ok;
                if (ok) shown++;
            });
            // the chef's picks row disappears when none of its dishes are showing
            var picks = document.querySelector('.picks');
            if (picks) picks.hidden = !picks.querySelector('[data-dish]:not([hidden])');
            if (empty) empty.hidden = shown > 0;
            if (note) note.hidden = !state.suits;
            if (count) count.textContent = shown + (shown === 1 ? ' dish shown' : ' dishes shown');
        }

        chips.addEventListener('click', function (e) {
            var chip = e.target.closest('button[data-filter]');
            if (!chip) return;
            var kind = chip.getAttribute('data-filter');
            if (kind === 'all' || kind === 'cat') {
                state.cat = kind === 'cat' ? chip.getAttribute('data-cat') : null;
                Array.prototype.forEach.call(chips.querySelectorAll('[data-filter="all"],[data-filter="cat"]'), function (c) {
                    c.setAttribute('aria-pressed', c === chip ? 'true' : 'false');
                });
            } else if (kind === 'veg') {
                state.veg = !state.veg;
                chip.setAttribute('aria-pressed', state.veg ? 'true' : 'false');
            } else if (kind === 'suits') {
                state.suits = !state.suits;
                chip.setAttribute('aria-pressed', state.suits ? 'true' : 'false');
            }
            apply();
        });
        apply();
    }

    // ---------------------------------------------------------------- set menu total
    var form = document.querySelector('[data-setform]');
    if (form) {
        var base = parseFloat(form.getAttribute('data-base')) || 0;
        var totalEl = form.querySelector('[data-total]');
        var noteEl = form.querySelector('[data-total-note]');
        var rm = function (n) {
            var whole = Math.abs(n - Math.round(n)) < 0.005;
            return 'RM ' + n.toLocaleString('en-US', { minimumFractionDigits: whole ? 0 : 2, maximumFractionDigits: whole ? 0 : 2 });
        };
        var list = function (names) { return names.length < 2 ? names.join('') : names.slice(0, -1).join(', ') + ' and ' + names[names.length - 1]; };
        var update = function () {
            var supp = 0, names = [];
            Array.prototype.forEach.call(form.querySelectorAll('input[data-supp]:checked'), function (i) {
                var s = parseFloat(i.getAttribute('data-supp')) || 0;
                if (s > 0) { supp += s; names.push(i.getAttribute('data-name')); }
            });
            totalEl.textContent = rm(base + supp);
            noteEl.textContent = supp > 0 ? 'per guest: set price ' + rm(base) + ', plus ' + rm(supp) + ' for ' + list(names) : 'per guest';
        };
        form.addEventListener('change', update);
    }
})();
