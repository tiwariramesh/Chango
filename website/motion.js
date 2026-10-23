// OrbitDrop website motion: smooth scrolling (Lenis), a custom cursor, magnetic buttons, cards that tilt and light up
// under the pointer, headings that rise in word by word, a showcase that breathes in as it scrolls into view, and a
// floating glass header that steps aside while you read. Everything is off with "reduce motion"; touch keeps the
// native scrolling and cursor.
(() => {
  "use strict";
  const $ = (s, el = document) => el.querySelector(s);
  const $$ = (s, el = document) => [...el.querySelectorAll(s)];
  const reduce = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const fine = matchMedia("(hover: hover) and (pointer: fine)").matches;
  const lerp = (a, b, k) => a + (b - a) * k;
  const clamp = (v, a = 0, b = 1) => Math.min(b, Math.max(a, v));
  const root = document.documentElement;
  if (!reduce) root.classList.add("motion"); // turns on the entrance states in styles.css

  // ------------------------------------------------------------------ headings: split into words that rise in
  $$(".split").forEach((el) => {
    const words = [];
    const walk = (node) => {
      for (const child of [...node.childNodes]) {
        if (child.nodeType === Node.TEXT_NODE) {
          const frag = document.createDocumentFragment();
          child.textContent.split(/(\s+)/).forEach((part) => {
            if (!part) return;
            if (/^\s+$/.test(part)) { frag.append(part); return; }
            const outer = document.createElement("span"), inner = document.createElement("span");
            outer.className = "w"; inner.className = "wi"; inner.textContent = part;
            outer.append(inner); frag.append(outer); words.push(inner);
          });
          child.replaceWith(frag);
        } else if (child.nodeType === Node.ELEMENT_NODE && child.tagName !== "BR") walk(child);
      }
    };
    walk(el);
    words.forEach((w, i) => w.style.setProperty("--i", i));
  });

  const show = new IntersectionObserver((entries) => entries.forEach((e) => {
    if (!e.isIntersecting) return;
    e.target.classList.add("in");
    show.unobserve(e.target);
  }), { threshold: 0.2, rootMargin: "0px 0px -8% 0px" });
  $$(".split").forEach((el) => show.observe(el));

  // Grids reveal their items one after another.
  $$(".cards, .points, .bento, .ticks, .format-list, .faq").forEach((grid) =>
    [...grid.children].forEach((child, i) => child.style.setProperty("--stagger", `${Math.min(i, 8) * 70}ms`)));

  // Tabs: a dark pill slides to the selected kind.
  const tabs = $(".tabs");
  if (tabs) {
    const thumb = document.createElement("span");
    thumb.className = "tab-thumb"; thumb.setAttribute("aria-hidden", "true");
    tabs.prepend(thumb); tabs.classList.add("has-thumb");
    const place = () => {
      const sel = tabs.querySelector('[aria-selected="true"]');
      if (!sel) return;
      thumb.style.width = `${sel.offsetWidth}px`; thumb.style.height = `${sel.offsetHeight}px`;
      thumb.style.translate = `${sel.offsetLeft}px ${sel.offsetTop}px`;
    };
    place();
    new MutationObserver(place).observe(tabs, { attributes: true, subtree: true, attributeFilter: ["aria-selected"] });
    addEventListener("resize", place);
    document.fonts?.ready.then(place);
  }

  // The hero plays in on load.
  requestAnimationFrame(() => root.classList.add("loaded"));

  if (reduce) { $$(".split").forEach((el) => el.classList.add("in")); return; }

  // ------------------------------------------------------------------ smooth scrolling
  let lenis = null;
  if (fine && window.Lenis) {
    lenis = new window.Lenis({ duration: 1.15, easing: (t) => 1 - Math.pow(1 - t, 4), smoothWheel: true });
    const raf = (time) => { lenis.raf(time); requestAnimationFrame(raf); };
    requestAnimationFrame(raf);
    $$('a[href^="#"]').forEach((a) => a.addEventListener("click", (e) => {
      const target = a.getAttribute("href") === "#" ? null : $(a.getAttribute("href"));
      if (!target) return;
      e.preventDefault();
      lenis.scrollTo(target, { offset: -96 });
    }));
  }

  // ------------------------------------------------------------------ floating header: compact once you scroll, always visible
  const nav = $("#nav");
  const onScroll = (y) => {
    if (nav) {
      nav.classList.toggle("scrolled", y > 24);
      // The header stays on screen all the time, so the download button is always one click away.
    }
    breathe();
  };
  if (lenis) lenis.on("scroll", ({ scroll }) => onScroll(scroll));
  else addEventListener("scroll", () => onScroll(scrollY), { passive: true });

  // ------------------------------------------------------------------ the showcase breathes in as it enters
  const showcase = $(".showcase");
  function breathe() {
    if (!showcase) return;
    const r = showcase.getBoundingClientRect();
    const k = clamp((innerHeight - r.top) / (innerHeight * 0.75)); // 0 when it appears, 1 once well in view
    const e = 1 - Math.pow(1 - k, 3);
    showcase.style.setProperty("--breathe", e.toFixed(4));
  }
  breathe();

  if (!fine) return;

  // ------------------------------------------------------------------ cursor
  // Pages without the cursor markup get it here, so the native cursor is never hidden with nothing in its place.
  if (!$(".cursor")) document.body.insertAdjacentHTML("beforeend",
    '<div class="cursor" aria-hidden="true"><div class="cursor-ring"><span class="cursor-label"></span></div><div class="cursor-dot"></div></div>');
  const cursor = $(".cursor"), ring = $(".cursor-ring"), dot = $(".cursor-dot"), label = $(".cursor-label");
  root.classList.add("has-cursor");
  let mx = innerWidth / 2, my = innerHeight / 2, rx = mx, ry = my, seen = false;
  addEventListener("pointermove", (e) => {
    mx = e.clientX; my = e.clientY;
    if (!seen) { seen = true; rx = mx; ry = my; cursor.classList.add("on"); }
  }, { passive: true });
  document.addEventListener("pointerleave", () => cursor.classList.remove("on"));
  document.addEventListener("pointerenter", () => seen && cursor.classList.add("on"));
  addEventListener("pointerdown", () => cursor.classList.add("down"));
  addEventListener("pointerup", () => cursor.classList.remove("down"));

  // What the ring does over different things: grow over links and buttons, tighten over the wheels, label media.
  const modes = [
    [".preview-wheel", "precise", ""],
    [".film-play", "media", "Play"],
    ["a, button, summary, [role=tab], kbd, label", "link", ""],
    [".tile, .cards article, .points article, .showcase", "card", ""],
  ];
  document.addEventListener("pointerover", (e) => {
    let mode = "", text = "";
    for (const [sel, m, t] of modes) { if (e.target.closest(sel)) { mode = m; text = t; break; } }
    cursor.dataset.mode = mode;
    label.textContent = text;
  });

  // ------------------------------------------------------------------ magnetic buttons
  const magnets = $$("[data-magnetic]").map((el) => ({ el, x: 0, y: 0, tx: 0, ty: 0 }));
  magnets.forEach((m) => {
    m.el.addEventListener("pointermove", (e) => {
      const r = m.el.getBoundingClientRect();
      m.tx = (e.clientX - (r.left + r.width / 2)) * 0.3;
      m.ty = (e.clientY - (r.top + r.height / 2)) * 0.35;
    });
    m.el.addEventListener("pointerleave", () => { m.tx = 0; m.ty = 0; });
  });

  // ------------------------------------------------------------------ cards: tilt and a soft light that follows the pointer
  const cards = $$(".tile, .cards article, .points article, .pair, .actions-grid, .format-list .row, .faq details").map((el) => {
    el.classList.add("alive");
    const c = { el, rx: 0, ry: 0, trx: 0, try_: 0, s: 1, ts: 1 };
    const tilt = !el.matches(".format-list .row, .faq details, .actions-grid, .pair");
    el.addEventListener("pointermove", (e) => {
      const r = el.getBoundingClientRect();
      const px = (e.clientX - r.left) / r.width, py = (e.clientY - r.top) / r.height;
      el.style.setProperty("--mx", `${(px * 100).toFixed(1)}%`);
      el.style.setProperty("--my", `${(py * 100).toFixed(1)}%`);
      if (tilt) { c.trx = (0.5 - py) * 5; c.try_ = (px - 0.5) * 6; }
      c.ts = tilt ? 1.015 : 1;
    });
    el.addEventListener("pointerleave", () => { c.trx = 0; c.try_ = 0; c.ts = 1; });
    return c;
  });

  // The hero glow follows the pointer a little.
  const glow = $(".hero-glow");
  let gx = 0.5, gy = 0.3;

  // ------------------------------------------------------------------ one frame loop for everything that eases
  (function frame() {
    rx = lerp(rx, mx, 0.18); ry = lerp(ry, my, 0.18);
    dot.style.transform = `translate3d(${mx}px, ${my}px, 0)`;
    ring.style.transform = `translate3d(${rx}px, ${ry}px, 0)`;
    for (const m of magnets) {
      m.x = lerp(m.x, m.tx, 0.16); m.y = lerp(m.y, m.ty, 0.16);
      if (Math.abs(m.x) > 0.05 || Math.abs(m.y) > 0.05 || m.tx || m.ty) m.el.style.translate = `${m.x.toFixed(2)}px ${m.y.toFixed(2)}px`;
      else if (m.el.style.translate) m.el.style.translate = "";
    }
    for (const c of cards) {
      c.rx = lerp(c.rx, c.trx, 0.1); c.ry = lerp(c.ry, c.try_, 0.1); c.s = lerp(c.s, c.ts, 0.12);
      if (Math.abs(c.rx) > 0.01 || Math.abs(c.ry) > 0.01 || Math.abs(c.s - 1) > 0.0005)
        c.el.style.transform = `perspective(1100px) rotateX(${c.rx.toFixed(3)}deg) rotateY(${c.ry.toFixed(3)}deg) scale(${c.s.toFixed(4)})`;
      else if (c.el.style.transform) c.el.style.transform = "";
    }
    if (glow) {
      gx = lerp(gx, mx / innerWidth, 0.04); gy = lerp(gy, my / innerHeight, 0.04);
      glow.style.transform = `translate3d(${((gx - 0.5) * 80).toFixed(1)}px, ${((gy - 0.3) * 50).toFixed(1)}px, 0)`;
    }
    requestAnimationFrame(frame);
  })();
})();
