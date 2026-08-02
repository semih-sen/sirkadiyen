// Sirkadiyen prototip — paylaşılan durum yönlendirme ve etkileşim yardımcıları.
// Gerçek üretim kodu değildir; yalnızca ?state= parametresine göre önceden
// tanımlı varyantları göstermek ve form/tablo etkileşimlerini simüle etmek içindir.
(function () {
  "use strict";

  function qs(key, fallback) {
    var v = new URLSearchParams(location.search).get(key);
    return v === null ? fallback : v;
  }
  window.SRK = window.SRK || {};
  SRK.qs = qs;

  // --- Durum bazlı görünürlük: [data-state-panel="X,Y"] yalnızca state X ya da Y ise görünür.
  SRK.applyStatePanels = function (paramName, defaultState) {
    var state = qs(paramName, defaultState);
    document.querySelectorAll("[data-state-panel]").forEach(function (el) {
      var list = el.getAttribute("data-state-panel").split(",").map(function (s) { return s.trim(); });
      el.hidden = list.indexOf(state) === -1;
    });
    document.querySelectorAll("[data-state-label]").forEach(function (el) {
      el.textContent = el.getAttribute("data-state-label-" + state) || el.textContent;
    });
    return state;
  };

  // --- Basit modal aç/kapat + odak tuzağı.
  SRK.bindModal = function (openSelector, modalId) {
    var modal = document.getElementById(modalId);
    if (!modal) return;
    document.querySelectorAll(openSelector).forEach(function (btn) {
      btn.addEventListener("click", function () {
        modal.hidden = false;
        var firstField = modal.querySelector("input, button, select, textarea");
        if (firstField) firstField.focus();
        document.addEventListener("keydown", escHandler);
      });
    });
    modal.querySelectorAll("[data-close-modal]").forEach(function (btn) {
      btn.addEventListener("click", function () { closeModal(); });
    });
    modal.addEventListener("click", function (e) { if (e.target === modal) closeModal(); });
    function closeModal() { modal.hidden = true; document.removeEventListener("keydown", escHandler); }
    function escHandler(e) { if (e.key === "Escape") closeModal(); }
  };

  // --- Güçlü onay: kullanıcı beklenen dizgeyi yazana kadar onay düğmesi pasif.
  SRK.bindStrongConfirm = function (inputId, expectedText, buttonId) {
    var input = document.getElementById(inputId);
    var button = document.getElementById(buttonId);
    if (!input || !button) return;
    function check() {
      var ok = input.value.trim() === String(expectedText).trim();
      button.disabled = !ok;
      button.setAttribute("aria-disabled", String(!ok));
    }
    input.addEventListener("input", check);
    check();
  };

  // --- Tablo: metin arama + kolon filtreleri (select) + sıralama.
  SRK.bindTable = function (tableId, opts) {
    opts = opts || {};
    var table = document.getElementById(tableId);
    if (!table) return;
    var rows = Array.prototype.slice.call(table.querySelectorAll("tbody tr"));

    function applyFilters() {
      var searchInput = opts.searchInputId ? document.getElementById(opts.searchInputId) : null;
      var term = searchInput ? searchInput.value.trim().toLowerCase() : "";
      var filterEls = (opts.filterSelectIds || []).map(function (id) { return document.getElementById(id); }).filter(Boolean);
      rows.forEach(function (row) {
        var text = row.textContent.toLowerCase();
        var matchesSearch = !term || text.indexOf(term) !== -1;
        var matchesFilters = filterEls.every(function (sel) {
          if (!sel.value || sel.value === "all") return true;
          return row.getAttribute("data-" + sel.dataset.filterKey) === sel.value;
        });
        row.hidden = !(matchesSearch && matchesFilters);
      });
      var emptyRow = table.querySelector("[data-empty-row]");
      if (emptyRow) emptyRow.hidden = rows.some(function (r) { return !r.hidden; });
    }

    if (opts.searchInputId) {
      var s = document.getElementById(opts.searchInputId);
      if (s) s.addEventListener("input", applyFilters);
    }
    (opts.filterSelectIds || []).forEach(function (id) {
      var el = document.getElementById(id);
      if (el) el.addEventListener("change", applyFilters);
    });
    applyFilters();
  };

  // --- Detay çekmecesi (drawer) açma: tıklanan satırdaki data-* alanlarını doldurur.
  SRK.bindDrawer = function (rowSelector, drawerId, mapFn) {
    var drawer = document.getElementById(drawerId);
    if (!drawer) return;
    document.querySelectorAll(rowSelector).forEach(function (row) {
      row.addEventListener("click", function () {
        mapFn(row, drawer);
        drawer.classList.add("is-open");
        drawer.hidden = false;
      });
    });
    drawer.querySelectorAll("[data-close-drawer]").forEach(function (btn) {
      btn.addEventListener("click", function () { drawer.hidden = true; drawer.classList.remove("is-open"); });
    });
  };

  // --- License input: otomatik büyük harf + tire ekleme (SRK-XXXXX-XXXXX).
  SRK.bindLicenseInput = function (inputId) {
    var input = document.getElementById(inputId);
    if (!input) return;
    input.addEventListener("input", function () {
      var raw = input.value.toUpperCase().replace(/[^A-Z0-9]/g, "");
      var withoutPrefix = raw.replace(/^SRK/, "");
      var body = withoutPrefix.slice(0, 10);
      var formatted = "SRK";
      if (body.length > 0) formatted += "-" + body.slice(0, 5);
      if (body.length > 5) formatted += "-" + body.slice(5, 10);
      input.value = formatted;
    });
  };

  // --- Senkronizasyon ilerleme simülasyonu: aşamaları sırayla "active" -> "done" yapar.
  SRK.simulateSync = function (opts) {
    var stages = document.querySelectorAll(opts.stageSelector);
    var percentEl = document.getElementById(opts.percentId);
    var counters = opts.counters || {};
    var i = 0;
    var pct = 0;
    if (!stages.length) return;
    var timer = setInterval(function () {
      if (i > 0) stages[i - 1].setAttribute("data-status", "done");
      if (i >= stages.length) { clearInterval(timer); if (percentEl) percentEl.textContent = "100%"; return; }
      stages[i].setAttribute("data-status", "active");
      pct = Math.min(96, Math.round(((i + 1) / stages.length) * 100));
      if (percentEl) percentEl.textContent = pct + "%";
      Object.keys(counters).forEach(function (key) {
        var el = document.getElementById(counters[key].id);
        if (el) el.textContent = Math.min(counters[key].target, Math.round((counters[key].target * (i + 1)) / stages.length));
      });
      i++;
    }, opts.interval || 1400);
  };

  // --- Dependent select (üst grup -> alt grup).
  SRK.bindDependentSelect = function (parentId, childWrapId) {
    var parent = document.getElementById(parentId);
    var childWrap = document.getElementById(childWrapId);
    if (!parent || !childWrap) return;
    function sync() {
      childWrap.hidden = !parent.value;
      var childSelect = childWrap.querySelector("select");
      if (childSelect) childSelect.disabled = !parent.value;
    }
    parent.addEventListener("change", sync);
    sync();
  };

  // --- Basit form doğrulama demosu: submit anında zorunlu alanları kontrol eder.
  SRK.bindFormDemo = function (formId, opts) {
    opts = opts || {};
    var form = document.getElementById(formId);
    if (!form) return;
    form.addEventListener("submit", function (e) {
      e.preventDefault();
      var ok = true;
      form.querySelectorAll("[required]").forEach(function (field) {
        var invalid = !field.value || !field.value.trim();
        field.setAttribute("aria-invalid", String(invalid));
        var err = form.querySelector('[data-error-for="' + field.id + '"]');
        if (err) err.hidden = !invalid;
        if (invalid) ok = false;
      });
      var successPanel = opts.successId ? document.getElementById(opts.successId) : null;
      var errorPanel = opts.serviceErrorId ? document.getElementById(opts.serviceErrorId) : null;
      if (errorPanel) errorPanel.hidden = true;
      if (!ok) return;
      if (opts.simulateServiceError && qs("state") === "servis-hatasi") {
        if (errorPanel) errorPanel.hidden = false;
        return;
      }
      form.hidden = true;
      if (successPanel) successPanel.hidden = false;
    });
  };

  // --- Admin sidebar: tüm admin-*.html sayfalarının paylaştığı ortak navigasyon.
  var ADMIN_NAV = [
    { key: "dashboard", label: "Genel bakış", href: "admin-dashboard.html", icon: "▦" },
    { key: "finance", label: "Finans", href: "admin-finance.html", icon: "₺" },
    { key: "users", label: "Kullanıcılar", href: "admin-users.html", icon: "◍" },
    { key: "bulk-event", label: "Toplu etkinlik", href: "admin-bulk-event.html", icon: "▤" },
    { key: "user-warning", label: "Kullanıcı uyarısı", href: "admin-user-warning.html", icon: "◭" },
    { key: "sources", label: "Kaynaklar & senkron", href: "admin-sources.html", icon: "⇄" },
    { key: "server", label: "Sunucu", href: "admin-server.html", icon: "▣" },
    { key: "access-logs", label: "Erişim kayıtları", href: "admin-access-logs.html", icon: "☰" }
  ];

  SRK.mountAdminShell = function (activeKey, opts) {
    opts = opts || {};
    var freeze = qs("freeze", "0") === "1";
    var freezeBanner = document.getElementById("freeze-banner");
    if (freezeBanner) freezeBanner.hidden = !freeze;

    var mount = document.getElementById("admin-sidebar-mount");
    if (mount) {
      var groupsHtml = '<a class="brand" href="admin-dashboard.html"><span class="brand__mark">S</span> Sirkadiyen Yönetim</a>';
      groupsHtml += '<div class="admin-nav-group-label">Operasyon</div><ul class="admin-nav">';
      ADMIN_NAV.slice(0, 2).forEach(function (item) { groupsHtml += navItem(item, activeKey); });
      groupsHtml += '</ul><div class="admin-nav-group-label">Kullanıcı işlemleri</div><ul class="admin-nav">';
      ADMIN_NAV.slice(2, 5).forEach(function (item) { groupsHtml += navItem(item, activeKey); });
      groupsHtml += '</ul><div class="admin-nav-group-label">Altyapı</div><ul class="admin-nav">';
      ADMIN_NAV.slice(5).forEach(function (item) { groupsHtml += navItem(item, activeKey); });
      groupsHtml += '</ul>';
      mount.innerHTML = groupsHtml;
    }

    var topbarMount = document.getElementById("admin-topbar-mount");
    if (topbarMount) {
      topbarMount.innerHTML =
        '<div class="cluster" style="gap:10px;">' +
        '<span class="env-chip">🧪 Prototip ortamı</span>' +
        (freeze ? '<span class="badge badge-warning">Dondurulmuş</span>' : '<span class="badge badge-success">Çalışıyor</span>') +
        '</div>' +
        '<div class="cluster" style="gap:14px;">' +
        '<span class="muted" style="font-size:13px;">Operatör: ' + (opts.operator || "S. Şen (SuperAdmin)") + '</span>' +
        '<a class="btn btn-tertiary btn-sm" href="landing.html">Öğrenci tarafına dön</a>' +
        '</div>';
    }
  };

  function navItem(item, activeKey) {
    var current = item.key === activeKey ? ' aria-current="page"' : "";
    return '<li><a href="' + item.href + '"' + current + '><span class="nav-icon">' + item.icon + '</span>' + item.label + '</a></li>';
  }

  document.addEventListener("DOMContentLoaded", function () {
    document.querySelectorAll("[data-year]").forEach(function (el) { el.textContent = new Date().getFullYear(); });
  });
})();
