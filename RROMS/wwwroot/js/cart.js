/* Cart and checkout. Mounts Stripe's own payment form inside the page when "Card" or "Online banking" is chosen, confirms the
   charge, and only then posts the order. Nothing here is trusted: the server asks Stripe again whether the payment succeeded and
   whether it was for the right amount before an order is marked paid.
   Without this file the cart still works; only paying online needs it. */
(function () {
  'use strict';
  var form = document.getElementById('checkoutForm');
  if (!form) return;

  var stripeKey = form.getAttribute('data-stripe-key') || '';
  var placeBtn = document.getElementById('placeOrderBtn');
  var errBox = document.getElementById('paymentElementErrors');
  var elWrap = document.getElementById('payment-element');
  var intentInput = document.getElementById('stripePaymentIntentIdInput');
  var bankNote = form.querySelector('[data-banking-note]');
  var stripe = stripeKey && typeof window.Stripe === 'function' ? window.Stripe(stripeKey) : null;
  var SAVE = 'blm-cart-state';

  var elements = null;
  var paymentEl = null;
  var mountedFor = '';
  var confirmedId = '';
  var loading = 0;

  function field(name) { return form.querySelector('[name="' + name + '"]'); }
  function method() {
    var r = form.querySelector('input[name="Checkout.PaymentMethod"]:checked');
    return r ? r.value : '';
  }
  function isOnline(m) { return m === 'OnlinePayment' || m === 'OnlineBanking'; }
  function signature() {
    var promo = field('Checkout.PromoCode');
    var res = field('Checkout.ReservationId');
    return [method(), promo ? promo.value.trim().toLowerCase() : '', res ? res.value : ''].join('|');
  }
  function showError(text) { if (errBox) errBox.textContent = text || ''; }
  function setBusy(busy, label) {
    if (!placeBtn) return;
    if (busy) { placeBtn.setAttribute('data-label', placeBtn.textContent); placeBtn.textContent = label || 'Please wait…'; }
    else if (placeBtn.getAttribute('data-label')) { placeBtn.textContent = placeBtn.getAttribute('data-label'); }
    placeBtn.disabled = busy || placeBtn.hasAttribute('data-blocked');
  }

  function teardown() {
    loading++;
    if (paymentEl) { try { paymentEl.unmount(); } catch (e) { /* already gone */ } paymentEl = null; }
    elements = null;
    mountedFor = '';
    if (intentInput) intentInput.value = '';
    if (elWrap) { elWrap.hidden = true; elWrap.innerHTML = ''; }
  }

  // Asks the server for a PaymentIntent priced from the real cart, then shows Stripe's form for it.
  function mount() {
    if (!stripe || !elWrap) return;
    var m = method();
    if (!isOnline(m)) { teardown(); showError(''); return; }
    var sig = signature();
    if (sig === mountedFor) return;
    teardown();
    mountedFor = sig;
    var mine = ++loading;
    showError('');
    if (placeBtn) placeBtn.disabled = true;

    fetch(window.location.pathname + '?handler=CreatePaymentIntent', {
      method: 'POST', body: new FormData(form), credentials: 'same-origin', headers: { Accept: 'application/json' }
    })
      .then(function (r) { return r.json(); })
      .then(function (result) {
        if (mine !== loading) return;
        if (!result.success) { mountedFor = ''; showError(result.message || 'We could not start the online payment. Please pay at the restaurant instead.'); return; }
        elements = stripe.elements({ clientSecret: result.clientSecret });
        paymentEl = elements.create('payment');
        elWrap.hidden = false;
        paymentEl.mount('#payment-element');
      })
      .catch(function () { if (mine === loading) { mountedFor = ''; showError('We could not reach the server to start the online payment.'); } })
      .then(function () { if (mine === loading && placeBtn && !placeBtn.hasAttribute('data-blocked')) placeBtn.disabled = false; });
  }

  function remember() {
    try {
      var s = {};
      ['Checkout.Notes', 'Checkout.PromoCode', 'Checkout.ReservationId'].forEach(function (n) { var f = field(n); if (f) s[n] = f.value; });
      s.method = method();
      window.sessionStorage.setItem(SAVE, JSON.stringify(s));
    } catch (e) { /* storage can be blocked; the order then falls back to the page defaults */ }
  }
  function restore() {
    try {
      var raw = window.sessionStorage.getItem(SAVE);
      if (!raw) return;
      var s = JSON.parse(raw);
      ['Checkout.Notes', 'Checkout.PromoCode', 'Checkout.ReservationId'].forEach(function (n) { var f = field(n); if (f && s[n] != null) f.value = s[n]; });
      var r = form.querySelector('input[name="Checkout.PaymentMethod"][value="' + s.method + '"]');
      if (r && !r.disabled) r.checked = true;
      window.sessionStorage.removeItem(SAVE);
    } catch (e) { /* ignore */ }
  }

  function post() {
    if (placeBtn && placeBtn.formAction) form.action = placeBtn.formAction;   // form.submit() does not use the button's handler
    form.submit();
  }

  form.addEventListener('change', function (e) {
    if (e.target && e.target.name === 'Checkout.PaymentMethod') {
      if (bankNote) bankNote.hidden = method() !== 'OnlineBanking';
      mount();
    } else if (e.target && e.target.name === 'Checkout.ReservationId') {
      mount();
    }
  });

  form.addEventListener('submit', function (e) {
    if (e.submitter !== placeBtn) return;           // quantity, remove and apply buttons post as normal
    if (!isOnline(method()) || confirmedId) { setBusy(true, 'Placing your order…'); return; }
    if (!stripe) return;

    e.preventDefault();
    if (!elements) { showError('The payment form is still loading. Please try again in a moment.'); return; }
    showError('');
    setBusy(true, 'Processing payment…');
    remember();

    stripe.confirmPayment({
      elements: elements,
      confirmParams: { return_url: window.location.origin + window.location.pathname },
      redirect: 'if_required'
    }).then(function (result) {
      if (result.error) { showError(result.error.message || 'The payment could not be completed.'); setBusy(false); return; }
      var intent = result.paymentIntent;
      if (!intent || intent.status !== 'succeeded') {
        showError('The payment has not gone through yet (' + (intent ? intent.status : 'unknown') + '). Please try again.');
        setBusy(false);
        return;
      }
      confirmedId = intent.id;
      intentInput.value = intent.id;
      setBusy(true, 'Placing your order…');
      post();
    }).catch(function () { showError('Something went wrong confirming the payment. Please try again.'); setBusy(false); });
  });

  // Coming back from the guest's bank (online banking leaves the page and returns here).
  var params = new URLSearchParams(window.location.search);
  var secret = params.get('payment_intent_client_secret');
  if (stripe && secret && params.get('payment_intent')) {
    restore();
    if (placeBtn) placeBtn.disabled = true;
    stripe.retrievePaymentIntent(secret).then(function (result) {
      var intent = result.paymentIntent;
      if (intent && intent.status === 'succeeded') {
        confirmedId = intent.id;
        intentInput.value = intent.id;
        setBusy(true, 'Placing your order…');
        post();
      } else {
        window.history.replaceState({}, '', window.location.pathname);
        showError('The payment did not go through (' + (intent ? intent.status : 'unknown') + '). You have not been charged. Please try again.');
        if (placeBtn) placeBtn.disabled = false;
        mount();
      }
    }).catch(function () {
      showError('We could not check your payment. Please do not pay again; ask the restaurant to look up your order.');
      if (placeBtn) placeBtn.disabled = false;
    });
    return;
  }

  if (placeBtn && placeBtn.disabled) placeBtn.setAttribute('data-blocked', '');
  window.addEventListener('pageshow', function (ev) { if (ev.persisted) setBusy(false); });
  mount();
})();
