// OrbitDrop website: interactive wheel previews, keys that light up, and the looping demos.
// The wheels mirror the app: same slices, same order, groups that open when you rest on them, three modes.
(() => {
  "use strict";
  const $ = (s, el = document) => el.querySelector(s);
  const $$ = (s, el = document) => [...el.querySelectorAll(s)];
  const NS = "http://www.w3.org/2000/svg";
  const reduceMotion = matchMedia("(prefers-reduced-motion: reduce)").matches;
  const STORE = "https://apps.microsoft.com/detail/9NJQRMQLL2SZ?mode=direct&cid=website-";

  // ------------------------------------------------------------------ icons (24 × 24, stroked)
  const ICONS = {
    edit: "M4 20h4L19 9l-4-4L4 16v4zM14 6l4 4",
    compress: "M4 14h6v6M20 10h-6V4M14 10l6-6M4 20l6-6",
    resize: "M14 4h6v6M10 20H4v-6M20 4l-7 7M4 20l7-7",
    rotate: "M20 12a8 8 0 1 1-2.3-5.6M20 4v5h-5",
    gray: "M12 3a9 9 0 1 0 0 18zM12 3a9 9 0 0 1 0 18",
    text: "M5 6V4h14v2M12 4v16M9 20h6",
    trim: "M6 9a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM6 21a3 3 0 1 0 0-6 3 3 0 0 0 0 6zM8.1 7.9 20 20M8.1 16.1 20 4",
    snapshot: "M4 8h3l2-3h6l2 3h3v11H4zM12 17a4 4 0 1 0 0-8 4 4 0 0 0 0 8z",
    mute: "M4 9h4l5-4v14l-5-4H4zM17 9l4 6M21 9l-4 6",
    info: "M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18zM12 11v6M12 7.5v.5",
    wave: "M3 12h2M7 8v8M11 5v14M15 8v8M19 10v4M21 12h0",
    channels: "M4 15v-3a8 8 0 0 1 16 0v3M4 15h3v5H4zM17 15h3v5h-3z",
    link: "M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1",
    folder: "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z",
    copy: "M9 9h11v11H9zM5 15H4V4h11v1",
    move: "M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2zM10 13h6M13 10l3 3-3 3",
    pin: "M15 4l5 5-3 1-3 3 1 4-2 1-4-4-5 5M9 11l3-3 1-3",
    share: "M14 5l6 6-6 6M20 11H11a7 7 0 0 0-7 7",
    zip: "M4 7h16v13H4zM3 4h18v3H3zM10 11h4",
    terminal: "M4 5h16v14H4zM8 10l2 2-2 2M12 15h4",
    wallpaper: "M3 5h18v11H3zM8 20h8M12 16v4",
  };
  const icon = (name, size = 22) => {
    const svg = document.createElementNS(NS, "svg");
    svg.setAttribute("viewBox", "0 0 24 24"); svg.setAttribute("width", size); svg.setAttribute("height", size);
    svg.innerHTML = `<path d="${ICONS[name]}" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"/>`;
    return svg;
  };

  // ------------------------------------------------------------------ what each kind of file offers (as in the app)
  const folders = (verb) => ["Documents", "Pictures", "Desktop", "Choose a folder…"].map((f) => ({ label: f, act: verb, dest: f, detail: `${verb === "copy" ? "Copy" : "Move"} to ${f}` }));
  const actions = (withWallpaper) => [
    { label: "Copy path", icon: "link", group: [{ label: "Copy path", act: "path", detail: "Full path to the clipboard" }, { label: "Copy name", act: "name", detail: "Just the file name" }] },
    { label: "Show in folder", icon: "folder", act: "show", detail: "Open File Explorer there" },
    { label: "Copy to", icon: "copy", group: folders("copy") },
    { label: "Move to", icon: "move", group: folders("move") },
    { label: "Float", icon: "pin", act: "float", detail: "Keep it on a shelf" },
    { label: "Share", icon: "share", act: "share", detail: "Nearby Share and apps" },
    { label: "Zip", icon: "zip", act: "zip", detail: "Put it in a ZIP" },
    { label: "Terminal", icon: "terminal", act: "terminal", detail: "Open a terminal here" },
    ...(withWallpaper ? [{ label: "Wallpaper", icon: "wallpaper", act: "wallpaper", detail: "Set as desktop wallpaper" }] : []),
  ];
  const to = (exts, kind) => exts.map((e) => ({ label: e, act: "convert", ext: e.toLowerCase(), detail: kind === "audio-out" ? `Extract audio as ${e}` : `Convert to ${e}`, slow: kind !== "image" }));
  const imageTools = [
    { label: "Edit", icon: "edit", act: "edit", detail: "Crop, adjust, mark up or redact" },
    { label: "Compress", icon: "compress", group: [
      { label: "50%", act: "tool", suffix: "compressed", detail: "Compress to about half the size" },
      { label: "75%", act: "tool", suffix: "compressed", detail: "Compress to about three quarters" },
      { label: "25%", act: "tool", suffix: "compressed", detail: "Compress to about a quarter" },
      { label: "1 MB", act: "tool", suffix: "compressed", detail: "Compress to under 1 MB" },
      { label: "500 KB", act: "tool", suffix: "compressed", detail: "Compress to under 500 KB" },
      { label: "200 KB", act: "tool", suffix: "compressed", detail: "Compress to under 200 KB" }] },
    { label: "Resize", icon: "resize", group: ["50%", "1920 px", "1280 px", "800 px"].map((s) => ({ label: s, act: "tool", suffix: "resized", detail: s.endsWith("%") ? "Half the width and height" : `Fit within ${s}` })) },
    { label: "Rotate & flip", icon: "rotate", group: ["Rotate right", "Rotate left", "Rotate 180°", "Flip horizontal", "Flip vertical"].map((s) => ({ label: s, act: "tool", suffix: s.startsWith("Flip") ? "flipped" : "rotated", detail: s })) },
    { label: "Grayscale", icon: "gray", act: "tool", suffix: "grayscale", detail: "Black and white copy" },
    { label: "Copy text", icon: "text", act: "ocr", detail: "Copy the text in the image" },
  ];
  const KINDS = {
    image: {
      convert: (ext) => to(["JPG", "PNG", "BMP", "TIFF", "GIF", "PDF"].filter((e) => e.toLowerCase() !== ext && !(ext === "jpg" && e === "JPG")), "image"),
      tools: () => imageTools, actions: () => actions(true),
    },
    video: {
      convert: () => [...to(["WMV", "GIF"], "video"), ...to(["MP3", "M4A", "WAV", "WMA", "FLAC"], "audio-out")],
      tools: () => [
        { label: "Trim", icon: "trim", act: "trim", detail: "Keep just the part you choose" },
        { label: "Compress", icon: "compress", group: ["1080p", "720p", "480p", "Half size"].map((s) => ({ label: s, act: "tool", suffix: "compressed", slow: true, detail: s === "Half size" ? "Half the width and height" : `Compress to ${s}` })) },
        { label: "Snapshot", icon: "snapshot", act: "snapshot", detail: "Save the middle frame as PNG" },
        { label: "Remove audio", icon: "mute", act: "tool", suffix: "no audio", slow: true, detail: "A silent copy" },
        { label: "Info", icon: "info", act: "info", detail: "See and edit details" },
      ],
      actions: () => actions(false),
    },
    audio: {
      convert: () => to(["MP3", "M4A", "FLAC", "WMA"], "audio"),
      tools: () => [
        { label: "Trim", icon: "trim", act: "trim", detail: "Keep just the part you choose" },
        { label: "Compress", icon: "compress", group: ["128 kbps", "96 kbps", "64 kbps"].map((s) => ({ label: s, act: "tool", suffix: "compressed", slow: true, detail: `Compress to ${s}` })) },
        { label: "Normalize", icon: "wave", act: "tool", suffix: "normalized", slow: true, detail: "As loud as it can be, no clipping" },
        { label: "Channels", icon: "channels", group: [{ label: "Mono", act: "tool", suffix: "mono", slow: true, detail: "Make it mono" }, { label: "Stereo", act: "tool", suffix: "stereo", slow: true, detail: "Make it stereo" }] },
        { label: "Info", icon: "info", act: "info", detail: "See and edit details" },
      ],
      actions: () => actions(false),
    },
  };
  const MODES = ["convert", "tools", "actions"];
  const kindOf = (name) => /\.(png|jpe?g|gif|bmp|tiff?)$/i.test(name) ? "image" : /\.(mp4|wmv)$/i.test(name) ? "video" : /\.(wav|mp3|m4a|flac|wma)$/i.test(name) ? "audio" : /\.zip$/i.test(name) ? "zip" : "image";
  const extOf = (name) => name.split(".").pop().toLowerCase();
  const baseOf = (name) => name.replace(/\.[^.]+$/, "");
  const menuFor = (name, mode) => {
    const k = kindOf(name);
    if (k === "zip") return mode === "actions" ? actions(false) : [];
    return KINDS[k][mode](extOf(name));
  };

  // ------------------------------------------------------------------ the wheel (SVG, same geometry idea as the app)
  const R = 150, R0 = 64, HUB = 54;
  const polar = (a, r) => [Math.cos(a) * r, Math.sin(a) * r];
  function sector(i, n) {
    const span = (Math.PI * 2) / n, mid = -Math.PI / 2 + i * span, a0 = mid - span / 2, a1 = mid + span / 2;
    const [x0, y0] = polar(a0, R), [x1, y1] = polar(a1, R), [x2, y2] = polar(a1, R0), [x3, y3] = polar(a0, R0);
    const large = span > Math.PI ? 1 : 0;
    if (n === 1) return `M0 ${-R}A${R} ${R} 0 1 1 0 ${R}A${R} ${R} 0 1 1 0 ${-R}zM0 ${-R0}A${R0} ${R0} 0 1 0 0 ${R0}A${R0} ${R0} 0 1 0 0 ${-R0}z`;
    return `M${x0} ${y0}A${R} ${R} 0 ${large} 1 ${x1} ${y1}L${x2} ${y2}A${R0} ${R0} 0 ${large} 0 ${x3} ${y3}z`;
  }

  class Wheel {
    constructor(host, { size = 300, interactive = true } = {}) {
      this.el = document.createElement("div");
      this.el.className = "wheel";
      this.el.style.width = this.el.style.height = `${size}px`;
      this.size = size;
      this.svg = document.createElementNS(NS, "svg");
      this.svg.setAttribute("viewBox", `${-R - 8} ${-R - 8} ${2 * R + 16} ${2 * R + 16}`);
      this.el.append(this.svg);
      this.hub = document.createElement("div");
      this.hub.className = "hub";
      this.hub.innerHTML = `<span class="hub-mode"></span><b class="hub-value"></b><span class="hub-detail"></span><span class="hub-dots"><i></i><i></i><i></i></span>`;
      this.el.append(this.hub);
      host.append(this.el);
      this.items = []; this.index = -1; this.mode = "convert";
      if (interactive) {
        this.el.addEventListener("pointermove", (e) => this.hoverAt(e.clientX, e.clientY));
        this.el.addEventListener("pointerleave", () => this.highlight(-1));
      }
    }
    set(items, mode, value, detail, spin = false) {
      this.items = items; this.mode = mode; this.idleValue = value; this.idleDetail = detail; this.index = -1;
      const n = Math.max(1, items.length);
      this.svg.innerHTML = `<circle class="disc" r="${R + 6}"/>`;
      this.labels = [];
      items.forEach((it, i) => {
        const g = document.createElementNS(NS, "g");
        g.setAttribute("class", "seg");
        g.innerHTML = `<path d="${sector(i, n)}"/>`;
        const span = (Math.PI * 2) / n, mid = -Math.PI / 2 + i * span;
        const [lx, ly] = polar(mid, (R + R0) / 2 + 2);
        const fo = document.createElementNS(NS, "foreignObject");
        fo.setAttribute("x", lx - 46); fo.setAttribute("y", ly - 30); fo.setAttribute("width", 92); fo.setAttribute("height", 60);
        const lab = document.createElement("div");
        lab.className = `seg-label${n > 6 ? " small" : ""}`;
        if (it.icon) lab.append(icon(it.icon, n > 6 ? 18 : 21));
        const t = document.createElement("span"); t.textContent = it.label; lab.append(t);
        if (it.group) { const d = document.createElement("em"); d.textContent = "•••"; lab.append(d); }
        fo.append(lab); g.append(fo);
        this.svg.append(g);
      });
      this.hub.querySelector(".hub-mode").textContent = mode.toUpperCase();
      $$(".hub-dots i", this.hub).forEach((d, k) => d.classList.toggle("on", MODES[k] === mode));
      this.showHub(value, detail);
      if (spin && !reduceMotion) { this.el.classList.remove("spin"); void this.el.offsetWidth; this.el.classList.add("spin"); }
    }
    showHub(value, detail) { this.hub.querySelector(".hub-value").textContent = value; this.hub.querySelector(".hub-detail").textContent = detail; }
    highlight(i) {
      if (i === this.index) return;
      this.index = i;
      $$(".seg", this.svg).forEach((g, k) => g.classList.toggle("on", k === i));
      const it = this.items[i];
      if (it) this.showHub(it.label, it.group ? "Rest for more" : it.detail);
      else this.showHub(this.idleValue, this.idleDetail);
      this.onHighlight?.(it, i);
    }
    /** Which slice is under a screen point: -1 none, -2 the hub. */
    hit(cx, cy) {
      const r = this.el.getBoundingClientRect();
      const k = (2 * R + 16) / r.width;
      const x = (cx - r.left - r.width / 2) * k, y = (cy - r.top - r.height / 2) * k;
      const d = Math.hypot(x, y);
      if (d < R0 - 4) return -2;
      if (d > R + 70 || !this.items.length) return -1;
      const n = this.items.length, span = 360 / n;
      let a = (Math.atan2(y, x) * 180) / Math.PI + 90 + span / 2;
      a = ((a % 360) + 360) % 360;
      return Math.floor(a / span) % n;
    }
    hoverAt(cx, cy) { const i = this.hit(cx, cy); this.highlight(i >= 0 ? i : -1); return i; }
  }

  // ------------------------------------------------------------------ interactive previews (feature tabs and actions)
  const PREVIEW = {
    image: { file: "photo.png", cards: [
      ["Convert", "JPG, PNG, GIF, BMP, TIFF or PDF. Reads WebP, HEIC, AVIF and RAW too."],
      ["Compress", "To half the size, or under 1 MB, 500 KB or 200 KB."],
      ["Edit", "Crop, adjust, mark up and redact. Saves a copy."],
      ["Copy text", "Pull the text out of a screenshot or photo, offline."],
      ["Resize & rotate", "Fit within 1920, 1280 or 800 px. Rotate or flip."],
      ["Clean up", "Remove the location, or all metadata, before you share."],
    ] },
    video: { file: "clip.mp4", cards: [
      ["Convert", "MP4 or WMV, or an animated GIF."],
      ["Just the audio", "MP3, M4A, WAV, WMA or FLAC from any clip."],
      ["Compress", "1080p, 720p, 480p or half size, ready to send."],
      ["Trim", "Keep just the part you want. Play it before you save."],
      ["Snapshot", "Save the middle frame as a picture."],
      ["Join", "Drop several clips at once to make one video."],
    ] },
    audio: { file: "song.wav", cards: [
      ["Convert", "MP3, M4A, FLAC or WMA."],
      ["Compress", "128, 96 or 64 kbps for smaller files."],
      ["Normalize", "Quiet recordings, as loud as they can be. No clipping."],
      ["Trim", "Cut the silence, keep the good part."],
      ["Mono or stereo", "Switch channels in one move."],
      ["Details", "See and edit the artist, album and more."],
    ] },
  };
  $$(".preview").forEach((box) => {
    const mode = box.dataset.mode;
    const host = $(".preview-wheel", box), out = $(".preview-result", box);
    const w = new Wheel(host, { size: +box.dataset.size || 260 });
    w.onHighlight = (it) => { out.textContent = it ? (it.group ? `${it.label} · rest for more` : it.detail) : box.dataset.idle || "Point at an option"; };
    box.wheel = w;
    box.load = (file) => { w.set(menuFor(file, mode), mode, sizeOf(file), file); w.onHighlight(null); };
    box.load(box.dataset.file || "photo.png");
    // A gentle tour of the slices until someone points at it.
    let i = 0, touched = false;
    host.addEventListener("pointerenter", () => { touched = true; });
    host.addEventListener("pointerleave", () => { touched = false; });
    if (!reduceMotion) setInterval(() => { if (!touched && isOnScreen(box)) { w.highlight(i % w.items.length); i++; } }, 1400);
  });
  function sizeOf(name) { return { image: "2.4 MB", video: "18.4 MB", audio: "6.1 MB" }[kindOf(name)] || "2.4 MB"; }
  function isOnScreen(el) { const r = el.getBoundingClientRect(); return r.bottom > 0 && r.top < innerHeight; }

  const tabs = $$(".tabs button"), cards = $("#kind-cards");
  function selectKind(kind) {
    tabs.forEach((t) => { const on = t.dataset.kind === kind; t.setAttribute("aria-selected", on); t.tabIndex = on ? 0 : -1; });
    const p = PREVIEW[kind];
    $$(".kinds-panel .preview").forEach((box) => box.load(p.file));
    cards.innerHTML = p.cards.map(([h, t]) => `<article><h4>${h}</h4><p>${t}</p></article>`).join("");
    cards.classList.remove("in"); void cards.offsetWidth; cards.classList.add("in");
  }
  tabs.forEach((t, k) => {
    t.addEventListener("click", () => selectKind(t.dataset.kind));
    t.addEventListener("keydown", (e) => {
      const d = e.key === "ArrowRight" ? 1 : e.key === "ArrowLeft" ? -1 : 0;
      if (d) { const n = tabs[(k + d + tabs.length) % tabs.length]; n.focus(); selectKind(n.dataset.kind); }
    });
  });
  if (tabs.length) selectKind("image");

  // ------------------------------------------------------------------ keyboard section: the keys light up when pressed
  const keys = { Control: $("#k-ctrl"), Alt: $("#k-alt"), " ": $("#k-space") };
  window.addEventListener("keydown", (e) => {
    keys[e.key]?.classList.add("down");
    if (e.ctrlKey && e.altKey && e.key === " ") $("#combo-ok")?.classList.add("on");
  });
  window.addEventListener("keyup", (e) => keys[e.key]?.classList.remove("down"));
  window.addEventListener("blur", () => Object.values(keys).forEach((k) => k?.classList.remove("down")));

  // ------------------------------------------------------------------ demo videos (light/dark), entrance animations
  const dark = matchMedia("(prefers-color-scheme: dark)");
  const vids = $$("video.demo");
  const pick = () => vids.forEach((v) => { const s = dark.matches ? v.dataset.dark : v.dataset.light; if (v.getAttribute("src") !== s) { v.poster = s.replace(/.mp4$/, ".jpg"); v.src = s; } });
  pick(); dark.addEventListener("change", pick);
  const io = new IntersectionObserver((es) => es.forEach((e) => {
    if (e.target.tagName === "VIDEO") { if (e.isIntersecting && !reduceMotion) e.target.play().catch(() => {}); else e.target.pause(); }
    else if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); }
  }), { threshold: 0.2 });
  vids.forEach((v) => io.observe(v));
  $$(".reveal").forEach((el) => io.observe(el));

  $$("[data-store]").forEach((a) => { a.href = STORE + a.dataset.store; });
  // FAQ: answers glide open and closed (the browser's own instant toggle when motion is reduced).
  $$(".faq details").forEach((d) => {
    const summary = $("summary", d), body = $(".faq-a", d);
    if (!summary || !body || reduceMotion) return;
    let anim = null;
    const ease = "cubic-bezier(.16, 1, .3, 1)";
    summary.addEventListener("click", (e) => {
      e.preventDefault();
      const closing = d.classList.contains("closing");
      // A closed answer can still report its full height, so a fresh open always starts from zero; only an answer
      // caught mid-animation starts from where it is.
      const from = !d.open ? 0 : body.getBoundingClientRect().height;
      anim?.cancel();
      anim = null;
      if (!d.open || closing) {
        d.classList.remove("closing");
        d.open = true;
        anim = body.animate([{ height: `${from}px`, opacity: from ? 1 : 0 }, { height: `${body.scrollHeight}px`, opacity: 1 }], { duration: 460, easing: ease });
        anim.onfinish = () => { anim = null; };
      } else {
        d.classList.add("closing");
        // "forwards" holds it at zero until it's really closed, so the answer never flashes back for a frame.
        const a = body.animate([{ height: `${from}px`, opacity: 1 }, { height: "0px", opacity: 0 }], { duration: 340, easing: ease, fill: "forwards" });
        anim = a;
        a.onfinish = () => { d.open = false; d.classList.remove("closing"); a.cancel(); if (anim === a) anim = null; };
      }
    });
  });

  // Testimonials: a second copy of each row (hidden from screen readers) makes the drift loop seamlessly.
  if (!reduceMotion) $$(".track").forEach((t) => [...t.children].forEach((f) => {
    const c = f.cloneNode(true); c.setAttribute("aria-hidden", "true"); t.appendChild(c);
  }));

  // On Windows, go straight to the Store app's review box; elsewhere the Store web page.
  if (/Windows/.test(navigator.userAgent)) $$("[data-review]").forEach((a) => { a.href = "ms-windows-store://review/?ProductId=9NJQRMQLL2SZ"; });

  // The trailer: one big play button, then the browser's own controls.
  const film = $(".film-frame"), filmVideo = film && $("video", film);
  film && $(".film-play", film).addEventListener("click", () => {
    film.classList.add("started");
    filmVideo.controls = true;
    filmVideo.play().catch(() => {});
  });
})();
