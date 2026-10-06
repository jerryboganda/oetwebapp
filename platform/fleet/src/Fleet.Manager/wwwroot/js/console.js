/*
 * Owner console behaviour. One small file, served from 'self' (the CSP has no inline script and no 'unsafe-eval').
 *
 *  1. Live regions: an element with data-live-url is re-fetched (a server-rendered HTML fragment) when the manager's event stream says
 *     something changed, and on a timer as a fallback. The browser never builds markup from data: it only swaps in HTML that the
 *     server already encoded.
 *  2. Key files are read in the browser into the key box. Nothing is uploaded on its own and nothing is stored in the browser.
 *  3. Secret fields are emptied when the page is left, so a back-forward cache cannot bring a key or a passphrase back.
 *  4. A submit button is disabled once its form is submitted, so a double click cannot use a one-time authenticator code twice.
 */
(function () {
  'use strict';

  var MIN_SECONDS = 5;
  var DEBOUNCE_MS = 400;
  var MAX_KEY_BYTES = 16 * 1024;

  var liveChip = document.getElementById('live-state');
  var sessionEnded = false;
  var source = null;
  var regions = [];

  function setChip(text, kind) {
    if (!liveChip) {
      return;
    }
    liveChip.textContent = text;
    liveChip.className = 'chip ' + kind;
  }

  // ---- 2. key files -----------------------------------------------------------------------------

  document.addEventListener('change', function (event) {
    var input = event.target;
    if (!(input instanceof HTMLInputElement) || input.type !== 'file' || !input.dataset.fillTarget) {
      return;
    }
    var target = document.getElementById(input.dataset.fillTarget);
    var file = input.files && input.files[0];
    input.setCustomValidity('');
    if (!target || !file) {
      return;
    }
    if (file.size > MAX_KEY_BYTES) {
      input.value = '';
      input.setCustomValidity('That file is too large to be an SSH key (16 KiB at most).');
      input.reportValidity();
      return;
    }
    var reader = new FileReader();
    reader.onload = function () {
      target.value = String(reader.result || '');
      input.value = '';
    };
    reader.onerror = function () {
      input.value = '';
      input.setCustomValidity('The file could not be read.');
      input.reportValidity();
    };
    reader.readAsText(file);
  });

  // ---- 3. secrets leave the page with it ---------------------------------------------------------

  function clearSecrets() {
    var fields = document.querySelectorAll('[data-secret]');
    for (var i = 0; i < fields.length; i++) {
      if (fields[i].value) {
        fields[i].value = '';
      }
    }
  }

  window.addEventListener('pagehide', clearSecrets);
  window.addEventListener('pageshow', function (event) {
    if (event.persisted) {
      clearSecrets();
    }
    var buttons = document.querySelectorAll('button[type="submit"]');
    for (var i = 0; i < buttons.length; i++) {
      buttons[i].disabled = false;
    }
  });

  // ---- 4. one submit per code ----------------------------------------------------------------------

  document.addEventListener('submit', function (event) {
    var form = event.target;
    if (!(form instanceof HTMLFormElement)) {
      return;
    }
    var buttons = form.querySelectorAll('button[type="submit"]');
    // After the browser has taken the submitter's value and formaction: disabling it earlier would drop them.
    window.setTimeout(function () {
      for (var i = 0; i < buttons.length; i++) {
        buttons[i].disabled = true;
      }
    }, 0);
  });

  // ---- 1. live regions -------------------------------------------------------------------------------

  function isDirty() {
    var fields = document.querySelectorAll('input:not([type="hidden"]):not([type="file"]):not([type="radio"]):not([type="checkbox"]), textarea');
    for (var i = 0; i < fields.length; i++) {
      if (fields[i].value && fields[i].defaultValue !== fields[i].value) {
        return true;
      }
    }
    return false;
  }

  function showStale() {
    if (document.getElementById('stale-banner')) {
      return;
    }
    var banner = document.createElement('p');
    banner.id = 'stale-banner';
    banner.className = 'notice warn';
    banner.setAttribute('role', 'status');
    banner.textContent = 'This page has changed on the server. ';
    var link = document.createElement('a');
    link.href = window.location.href;
    link.textContent = 'Reload it';
    banner.appendChild(link);
    banner.appendChild(document.createTextNode(' when you have finished typing.'));
    var main = document.getElementById('main');
    if (main) {
      main.insertBefore(banner, main.firstChild);
    }
  }

  function endSession() {
    if (sessionEnded) {
      return;
    }
    sessionEnded = true;
    if (source) {
      source.close();
    }
    setChip('Live updates: off', 'muted');
    var banner = document.createElement('p');
    banner.className = 'notice warn';
    banner.setAttribute('role', 'alert');
    banner.textContent = 'Your session has ended. ';
    var link = document.createElement('a');
    link.href = '/Login';
    link.textContent = 'Sign in again';
    banner.appendChild(link);
    var main = document.getElementById('main');
    if (main) {
      main.insertBefore(banner, main.firstChild);
    }
  }

  function openStates(root) {
    var details = root.querySelectorAll('details');
    var states = [];
    for (var i = 0; i < details.length; i++) {
      states.push(details[i].open);
    }
    return states;
  }

  function applyOpenStates(root, states) {
    var details = root.querySelectorAll('details');
    for (var i = 0; i < details.length && i < states.length; i++) {
      details[i].open = states[i];
    }
  }

  function apply(region, html) {
    var template = document.createElement('template');
    template.innerHTML = html;
    var next = template.content.querySelector('[data-live-state]');
    var current = region.querySelector('[data-live-state]');
    if (region.dataset.reloadOnChange === 'true' && current && next && current.dataset.liveState !== next.dataset.liveState) {
      // What you can do on this page depends on this state: reload, unless that would throw away something you are typing.
      if (isDirty()) {
        showStale();
      } else {
        window.location.reload();
      }
      return;
    }
    var states = openStates(region);
    region.replaceChildren(template.content);
    applyOpenStates(region, states);
  }

  function refresh(region) {
    if (sessionEnded || document.visibilityState === 'hidden') {
      return;
    }
    // Do not swap content under a keyboard or screen-reader user who is inside it.
    if (region.contains(document.activeElement) && document.activeElement !== document.body) {
      return;
    }
    fetch(region.dataset.liveUrl, { credentials: 'same-origin', headers: { Accept: 'text/html' }, cache: 'no-store' })
      .then(function (response) {
        if (response.redirected || response.status === 401 || response.status === 403) {
          endSession();
          return null;
        }
        return response.ok ? response.text() : null;
      })
      .then(function (html) {
        if (html !== null && !sessionEnded) {
          apply(region, html);
        }
      })
      .catch(function () {
        // A failed refresh keeps what is on screen; the next tick tries again.
      });
  }

  function schedule(region) {
    if (region.liveTimer) {
      window.clearTimeout(region.liveTimer);
    }
    region.liveTimer = window.setTimeout(function () {
      region.liveTimer = 0;
      refresh(region);
    }, DEBOUNCE_MS);
  }

  function startLive() {
    regions = Array.prototype.slice.call(document.querySelectorAll('[data-live-url]'));
    if (regions.length === 0) {
      return;
    }

    regions.forEach(function (region) {
      var seconds = parseInt(region.dataset.liveEvery || '15', 10);
      if (!(seconds >= MIN_SECONDS)) {
        seconds = 15;
      }
      window.setInterval(function () {
        refresh(region);
      }, seconds * 1000);
    });

    if (typeof window.EventSource !== 'function') {
      setChip('Live updates: timer', 'muted');
      return;
    }

    source = new window.EventSource('/api/v1/events', { withCredentials: true });
    source.onopen = function () {
      setChip('Live updates: on', 'ok');
    };
    source.onerror = function () {
      // The browser reconnects by itself; a closed stream (for example a signed-out session) falls back to the timers.
      setChip(source.readyState === 2 ? 'Live updates: timer' : 'Live updates: reconnecting', source.readyState === 2 ? 'muted' : 'warn');
    };

    var types = {};
    regions.forEach(function (region) {
      (region.dataset.liveEvents || '').split(',').forEach(function (name) {
        var type = name.trim();
        if (type) {
          types[type] = true;
        }
      });
    });
    Object.keys(types).forEach(function (type) {
      source.addEventListener(type, function () {
        regions.forEach(function (region) {
          if ((',' + (region.dataset.liveEvents || '') + ',').indexOf(',' + type + ',') !== -1) {
            schedule(region);
          }
        });
      });
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', startLive);
  } else {
    startLive();
  }
})();
