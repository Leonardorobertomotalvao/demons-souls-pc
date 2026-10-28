/* Demon's Souls PC — site oficial (JS mínimo: menu, reveal, lightbox) */
(function () {
  "use strict";

  /* ---- Menu móvel acessível ---- */
  var btn = document.querySelector(".menu-btn");
  var nav = document.querySelector(".site-nav");
  function closeMenu() {
    if (!btn || !nav) return;
    btn.setAttribute("aria-expanded", "false");
    nav.classList.remove("open");
  }
  if (btn && nav) {
    btn.addEventListener("click", function () {
      var open = btn.getAttribute("aria-expanded") === "true";
      btn.setAttribute("aria-expanded", String(!open));
      nav.classList.toggle("open", !open);
    });
    document.addEventListener("keydown", function (e) {
      if (e.key === "Escape" && btn.getAttribute("aria-expanded") === "true") {
        closeMenu();
        btn.focus();
      }
    });
    nav.addEventListener("click", function (e) {
      if (e.target.closest("a") && window.matchMedia("(max-width: 1260px)").matches) closeMenu();
    });
  }

  /* ---- Reveal suave entre seções (jornada por Boletaria) ---- */
  var revealables = Array.prototype.slice.call(document.querySelectorAll(".reveal"));
  if ("IntersectionObserver" in window) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (en) {
        if (en.isIntersecting) { en.target.classList.add("in"); io.unobserve(en.target); }
      });
    }, { threshold: 0.12, rootMargin: "0px 0px -8% 0px" });
    revealables.forEach(function (el) { io.observe(el); });
  } else {
    revealables.forEach(function (el) { el.classList.add("in"); });
  }

  /* ---- Galeria: visualização ampliada com <dialog> ---- */
  var dlg = document.getElementById("lightbox");
  var lbImg = document.getElementById("lb-img");
  var lbCap = document.getElementById("lb-cap");
  var closeBtn = document.getElementById("lb-close");
  if (dlg && lbImg && lbCap) {
    Array.prototype.forEach.call(document.querySelectorAll("[data-lightbox]"), function (item) {
      item.addEventListener("click", function () {
        var img = item.querySelector("img");
        if (!img) return;
        lbImg.src = img.getAttribute("src");
        lbImg.alt = img.getAttribute("alt") || "";
        lbCap.textContent = img.getAttribute("data-cap") || img.getAttribute("alt") || "";
        if (typeof dlg.showModal === "function") dlg.showModal();
      });
    });
    if (closeBtn) closeBtn.addEventListener("click", function () { dlg.close(); });
    dlg.addEventListener("click", function (e) {
      if (e.target === dlg) dlg.close(); /* clique no backdrop fecha */
    });
  }
})();
