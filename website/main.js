// Chango website: theme, media that follows the theme, and the hands-on desk where visitors try the wheel.
(() => {
  'use strict';
  const root = document.documentElement;
  const reduceMotion = matchMedia('(prefers-reduced-motion: reduce)');
  const systemDark = matchMedia('(prefers-color-scheme: dark)');
  const isDark = () => root.dataset.theme ? root.dataset.theme === 'dark' : systemDark.matches;

  /* ---------- Theme ---------- */

  const themeListeners = [];
  const onTheme = fn => { themeListeners.push(fn); fn(isDark()); };
  const themeChanged = () => themeListeners.forEach(fn => fn(isDark()));
  document.querySelector('.theme-toggle')?.addEventListener('click', () => {
    const next = isDark() ? 'light' : 'dark';
    root.dataset.theme = next;
    try { localStorage.setItem('chango-theme', next); } catch (e) { /* storage unavailable: this visit only */ }
    themeChanged();
  });
  systemDark.addEventListener('change', () => { if (!root.dataset.theme) themeChanged(); });

  // Renders from the app (and the Store badge) come in a light and a dark version.
  onTheme(dark => document.querySelectorAll('img[data-light]').forEach(img => {
    const src = dark ? img.dataset.dark : img.dataset.light;
    if (img.getAttribute('src') !== src) img.src = src;
  }));

  /* ---------- Header and menu ---------- */

  const header = document.querySelector('.site-header');
  const onScroll = () => header.classList.toggle('scrolled', scrollY > 8);
  addEventListener('scroll', onScroll, { passive: true });
  onScroll();

  const menuBtn = document.querySelector('.menu-btn');
  const nav = document.getElementById('nav');
  menuBtn?.addEventListener('click', () => {
    const open = nav.classList.toggle('open');
    menuBtn.setAttribute('aria-expanded', String(open));
  });
  nav?.addEventListener('click', e => {
    if (e.target.closest('a')) { nav.classList.remove('open'); menuBtn.setAttribute('aria-expanded', 'false'); }
  });

  /* ---------- Reveal on scroll ---------- */

  const revealed = document.querySelectorAll('.reveal');
  if ('IntersectionObserver' in window && !reduceMotion.matches) {
    const io = new IntersectionObserver(entries => entries.forEach(en => {
      if (en.isIntersecting) { en.target.classList.add('in'); io.unobserve(en.target); }
    }), { rootMargin: '0px 0px -8% 0px' });
    revealed.forEach(el => io.observe(el));
  } else {
    revealed.forEach(el => el.classList.add('in'));
  }

  /* ---------- Demo clips: the app's own guide scenes, as looping videos ---------- */

  const clips = [...document.querySelectorAll('.clip[data-scene]')];
  const clipSrc = (scene, dark, ext) => `assets/demo/${scene}${dark ? '-dark' : ''}.${ext}`;

  clips.forEach(clip => {
    const scene = clip.dataset.scene;
    const poster = clip.querySelector('img');
    const video = document.createElement('video');
    video.muted = true; video.loop = true; video.playsInline = true; video.preload = 'none';
    video.setAttribute('muted', ''); video.setAttribute('playsinline', '');
    video.setAttribute('aria-label', clip.closest('article, .row')?.querySelector('h3')?.textContent || 'Chango demo');
    const play = document.createElement('button');
    play.className = 'play'; play.type = 'button'; play.setAttribute('aria-label', 'Play the demo');
    play.innerHTML = '<svg><use href="#i-play"/></svg>';
    clip.append(play);
    clip._video = video;

    onTheme(dark => {
      poster.src = clipSrc(scene, dark, 'webp');
      video.poster = poster.src;
      if (video.src) { // theme changed while loaded: swap, and keep playing if it was
        const wasPlaying = !video.paused;
        video.src = clipSrc(scene, dark, 'mp4');
        if (wasPlaying) video.play().catch(() => {});
      }
    });

    const start = () => {
      if (!video.src) { video.src = clipSrc(scene, isDark(), 'mp4'); poster.replaceWith(video); }
      video.play().then(() => clip.classList.remove('paused')).catch(() => clip.classList.add('paused'));
    };
    clip._start = start;
    play.addEventListener('click', () => { clip._userPlay = true; start(); });
    video.addEventListener('click', () => {
      if (video.paused) start(); else { video.pause(); clip.classList.add('paused'); }
    });
    if (reduceMotion.matches) clip.classList.add('paused');
  });

  if ('IntersectionObserver' in window) {
    const vio = new IntersectionObserver(entries => entries.forEach(en => {
      const clip = en.target, video = clip._video;
      if (en.isIntersecting) {
        if (!reduceMotion.matches || clip._userPlay) clip._start();
      } else if (video.src && !video.paused) {
        video.pause();
      }
    }), { rootMargin: '120px 0px' });
    clips.forEach(c => vio.observe(c));
  }

  /* ---------- Copy buttons ---------- */

  document.querySelectorAll('[data-copy]').forEach(btn => btn.addEventListener('click', async () => {
    const text = btn.dataset.copy;
    try { await navigator.clipboard.writeText(text); }
    catch (e) {
      const ta = Object.assign(document.createElement('textarea'), { value: text });
      document.body.append(ta); ta.select();
      try { document.execCommand('copy'); } catch (e2) { /* nothing more to try */ }
      ta.remove();
    }
    const label = btn.textContent;
    btn.textContent = 'Copied';
    setTimeout(() => { btn.textContent = label; }, 1500);
  }));

  /* ---------- Try it: the hands-on desk ---------- */

  const desk = document.getElementById('desk');
  if (!desk) return;
  const sources = [...desk.querySelectorAll('.file.src')];
  const wheelEl = document.getElementById('wheel');
  const hint = document.getElementById('demo-hint');
  const toast = document.getElementById('toast');
  const toastTitle = document.getElementById('toast-title');
  const toastSub = document.getElementById('toast-sub');
  const resetBtn = document.getElementById('demo-reset');
  const coarse = matchMedia('(pointer: coarse)').matches;

  // File-type colours for the badges of new files (tokens in styles.css)
  const TYPE = { PNG: 'image', JPG: 'image', WEBP: 'image', BMP: 'image', TIFF: 'image', GIF: 'image', JFIF: 'image',
    PDF: 'pdf', DOCX: 'word', ODT: 'word', RTF: 'word', TXT: 'text', MD: 'text', HTML: 'text',
    MP4: 'video', WMV: 'video', MOV: 'video', MP3: 'audio', M4A: 'audio', WAV: 'audio', ZIP: 'archive' };

  const made = (name, ext) => ({ file: [name, ext] });
  const say = (title, sub) => ({ say: [title, sub] });
  const convert = (base, exts) => exts.map(x => typeof x === 'string'
    ? { label: x, sub: `Convert to ${x}`, result: made(`${base}.${x.toLowerCase()}`, x) } : x);

  // The wheels each file gets: Convert and Tools depend on the type, Actions are the same for every file.
  function modesFor(src) {
    const base = src.dataset.base, ext = src.dataset.ext, name = `${base}.${ext.toLowerCase()}`;
    const tools = {
      image: [
        { label: 'Edit', sub: 'Open the photo editor', result: say('Opening the photo editor', 'Crop, adjust, mark up, redact') },
        { label: 'Compress', plus: true, sub: 'Make it smaller', result: made(`${base} (compressed).png`, 'PNG') },
        { label: 'Resize', plus: true, sub: 'Change its size', result: made(`${base} (resized).png`, 'PNG') },
        { label: 'Rotate', plus: true, sub: 'Rotate & flip', result: made(`${base} (rotated).png`, 'PNG') },
        { label: 'Grayscale', sub: 'Black and white', result: made(`${base} (grayscale).png`, 'PNG') },
        { label: 'Copy text', sub: 'Read text in the image', result: say(`Copied the text in ${name}`, 'Paste it anywhere') },
      ],
      word: [
        { label: 'File info', sub: 'Pages, words, size', result: say(`${name}: 12 pages`, '3,240 words · 1.4 MB') },
        { label: 'Details', sub: 'Edit title and author', result: say('Opened the details', 'Change the title, author and more') },
        { label: 'Clean', sub: 'Remove metadata', result: made(`${base} (clean).docx`, 'DOCX') },
      ],
      video: [
        { label: 'Trim', sub: 'Cut the start or end', result: say('Opening the trimmer', 'Drag the handles, then save') },
        { label: 'Compress', plus: true, sub: 'Make it smaller', result: made(`${base} (compressed).mp4`, 'MP4') },
        { label: 'Mute', sub: 'Remove the audio', result: made(`${base} (no audio).mov`, 'MOV') },
        { label: 'Snapshot', sub: 'Save a frame', result: made(`${base} (frame).png`, 'PNG') },
        { label: 'Join', sub: 'Join clips', result: say('Pick two or more clips', 'Chango joins them in order') },
      ],
    };
    const conv = {
      image: convert(base, ['JPG', 'WEBP', 'BMP', 'TIFF', 'GIF', 'JFIF', 'PDF']),
      word: convert(base, ['PDF', 'TXT', 'MD', 'HTML', 'RTF', 'ODT']),
      video: convert(base, ['MP4', 'WMV', 'GIF', 'MP3', 'M4A', 'WAV']).concat(
        { label: 'More', plus: true, sub: 'MKV, WebM, AVI…', result: say('More formats', 'MKV, WebM, AVI and more, with free FFmpeg') }),
    };
    return [
      { name: 'Convert', items: conv[src.dataset.kind] },
      { name: 'Tools', items: tools[src.dataset.kind] },
      { name: 'Actions', items: [
        { label: 'Copy path', plus: true, sub: 'To the clipboard', result: say('Copied path', `C:\\Users\\you\\Desktop\\${name}`) },
        { label: 'Show', sub: 'Show in folder', result: { flash: true, say: ['Shown in its folder', `${name} is selected`] } },
        { label: 'Copy to', plus: true, sub: 'Another folder', result: say('Copied to Documents', name) },
        { label: 'Move to', plus: true, sub: 'Another folder', result: say('Moved to Documents', 'In the real app, it would leave the desktop') },
        { label: 'Float', sub: 'Keep it on a shelf', result: say('Floating on the shelf', 'Drag it out whenever you need it') },
        { label: 'Share', sub: 'Windows Share', result: say('Opened Windows Share', 'Send it with any app') },
        { label: 'Archive', plus: true, sub: 'ZIP, 7Z or TAR.GZ', result: made(`${base}.zip`, 'ZIP') },
        { label: 'Terminal', sub: 'Open a terminal here', result: say('Opened Terminal', 'In Desktop') },
        { label: 'Wallpaper', sub: 'Set as wallpaper', result: src.dataset.kind === 'image' ? say('Set as your wallpaper', 'Looking good') : say('Only for pictures', 'Try it with photo.png') },
      ] },
    ];
  }

  const SIZE = 272, R_OUT = 126, R_IN = 56, HUB = 49, INSET = 8;
  const NS = 'http://www.w3.org/2000/svg';
  let modes = [], mode = 0, sel = -1, state = 'idle'; // idle | press | drag | wheel | keys
  let src = null, center = { x: 0, y: 0 }, pointer = { x: 0, y: 0 }, pressAt = null, holdTimer = 0, toastTimer = 0, ghost = null;

  const el = (tag, attrs, parent) => {
    const n = document.createElementNS(NS, tag);
    for (const k in attrs) n.setAttribute(k, attrs[k]);
    parent?.append(n);
    return n;
  };
  const polar = (r, deg) => { const a = (deg - 90) * Math.PI / 180; return [r * Math.cos(a), r * Math.sin(a)]; };

  // A slice as an annular sector, inset so the round stroke brings it back to size with a gap between slices.
  function slicePath(i, n) {
    const step = 360 / n, a0 = i * step - step / 2, a1 = a0 + step;
    const ro = R_OUT - INSET, ri = R_IN + INSET, gap = 7;
    const ao = gap / ro * 180 / Math.PI, ai = gap / ri * 180 / Math.PI;
    const [x1, y1] = polar(ro, a0 + ao), [x2, y2] = polar(ro, a1 - ao);
    const [x3, y3] = polar(ri, a1 - ai), [x4, y4] = polar(ri, a0 + ai);
    const large = step > 180 ? 1 : 0;
    return `M${x1} ${y1}A${ro} ${ro} 0 ${large} 1 ${x2} ${y2}L${x3} ${y3}A${ri} ${ri} 0 ${large} 0 ${x4} ${y4}Z`;
  }

  let slices = [], labels = [], what, sub;
  function buildWheel() {
    const m = modes[mode], n = m.items.length;
    wheelEl.textContent = '';
    const svg = el('svg', { viewBox: `${-SIZE / 2} ${-SIZE / 2} ${SIZE} ${SIZE}`, role: 'presentation' }, wheelEl);
    el('circle', { class: 'disc', r: R_OUT + 8 }, svg);
    el('circle', { class: 'rim', r: R_OUT + 8 }, svg);
    slices = []; labels = [];
    m.items.forEach((it, i) => {
      slices.push(el('path', { class: 'slice', d: slicePath(i, n) }, svg));
      const [x, y] = polar((R_IN + R_OUT) / 2, i * 360 / n);
      const t = el('text', { class: 'label', x, y: it.plus ? y - 5 : y }, svg);
      t.textContent = it.label;
      labels.push(t);
      if (it.plus) { const p = el('text', { class: 'plus', x, y: y + 12 }, svg); p.textContent = '+'; t._plus = p; }
    });
    el('circle', { class: 'hub', r: HUB }, svg);
    el('text', { class: 'mode', y: -17 }, svg).textContent = m.name.toUpperCase();
    what = el('text', { class: 'what', y: 2 }, svg);
    sub = el('text', { class: 'sub', y: 18 }, svg);
    // Mode indicator: a pill for the current wheel, dots for the others (14 + 5 + 5 wide, 4 apart).
    let x = -16;
    modes.forEach((_, i) => {
      const w = i === mode ? 14 : 5;
      el('rect', { class: 'dot' + (i === mode ? ' on' : ''), x, y: 27, width: w, height: 5, rx: 2.5 }, svg);
      x += w + 4;
    });
    select(-1);
  }

  function select(i) {
    sel = i;
    const m = modes[mode];
    slices.forEach((s, k) => s.classList.toggle('on', k === i));
    labels.forEach((t, k) => { t.classList.toggle('on', k === i); t._plus?.classList.toggle('on', k === i); });
    what.textContent = i >= 0 ? m.items[i].label : 'Choose';
    sub.textContent = i >= 0 ? m.items[i].sub : src.querySelector('.name').textContent;
  }

  const rect = () => desk.getBoundingClientRect();
  const local = e => { const r = rect(); return { x: e.clientX - r.left, y: e.clientY - r.top }; };
  const setHint = html => { hint.innerHTML = html; };
  const idleHint = () => setHint(coarse
    ? '<span class="step">Try it</span><b>Drag a file</b> and hold still for a moment.'
    : '<span class="step">Try it</span><b>Drag a file</b> and keep holding.');

  function openWheel(at) {
    clearTimeout(holdTimer);
    const r = rect(), size = wheelEl.offsetWidth || 300, half = size / 2 + 8;
    center = { x: Math.min(Math.max(at.x, half), r.width - half), y: Math.min(Math.max(at.y, half), r.height - half) };
    wheelEl.style.left = center.x + 'px';
    wheelEl.style.top = center.y + 'px';
    buildWheel();
    wheelEl.classList.add('open');
    if (state === 'keys') {
      select(0);
      setHint('<span class="step">Wheel open</span><b>Arrows</b> to choose, <b>Enter</b> to apply, <b>Shift</b> for more.');
    } else {
      state = 'wheel';
      setHint(coarse
        ? '<span class="step">Nice</span><b>Slide onto an option</b> and let go.'
        : '<span class="step">Nice</span><b>Let go on an option.</b> Tap Shift again for Tools and Actions.');
    }
  }
  const closeWheel = () => wheelEl.classList.remove('open');

  function track(p) {
    pointer = p;
    if (ghost) { ghost.style.left = p.x + 'px'; ghost.style.top = p.y + 'px'; }
    if (state !== 'wheel') return;
    const scale = (wheelEl.offsetWidth || 300) / SIZE;
    const dx = (p.x - center.x) / scale, dy = (p.y - center.y) / scale;
    if (Math.hypot(dx, dy) < HUB - 4) { select(-1); return; }
    const n = modes[mode].items.length;
    const ang = (Math.atan2(dy, dx) * 180 / Math.PI + 90 + 360) % 360;
    select(Math.round(ang / (360 / n)) % n);
  }

  function pick(s) { src = s; modes = modesFor(s); mode = 0; } // every gesture starts on Convert, like the app

  function beginDrag(p) {
    state = 'drag';
    src.classList.add('lifted');
    ghost = src.querySelector('.doc').cloneNode(true);
    ghost.classList.add('ghost');
    desk.append(ghost);
    track(p);
    setHint(coarse
      ? '<span class="step">Hold still</span>The wheel opens in a moment…'
      : '<span class="step">Keep holding</span>Now <b>tap Shift</b> <span style="opacity:.7">(or hold still)</span>.');
    armHold();
  }

  // Without a key to tap, holding still opens the wheel.
  function armHold() {
    clearTimeout(holdTimer);
    holdTimer = setTimeout(() => { if (state === 'drag') openWheel(pointer); }, coarse ? 450 : 1100);
  }

  function endDrag(apply) {
    clearTimeout(holdTimer);
    const chosen = apply && state === 'wheel' && sel >= 0 ? modes[mode].items[sel] : null;
    closeWheel();
    ghost?.remove(); ghost = null;
    src?.classList.remove('lifted');
    state = 'idle';
    pressAt = null;
    if (chosen) run(chosen); else idleHint();
  }

  function flash(node) { node.classList.remove('flash'); void node.offsetWidth; node.classList.add('flash'); }

  function run(item) {
    const r = item.result;
    if (r.file) {
      const [name, ext] = r.file;
      const row = src.parentElement;
      let out = [...row.querySelectorAll('.file.out')].find(f => f.dataset.name === name);
      if (out) flash(out);
      else {
        out = document.createElement('div');
        out.className = 'file out fresh';
        out.dataset.name = name;
        out.innerHTML = '<span class="doc"><span class="badge"></span></span><span class="name"></span>';
        out.querySelector('.doc').style.setProperty('--c', `var(--t-${TYPE[ext] || 'text'})`);
        out.querySelector('.badge').textContent = ext;
        out.querySelector('.name').textContent = name;
        row.append(out);
        // Keep the row inside the desk: drop the oldest result when there's no more room
        const room = Math.max(1, Math.floor((desk.clientWidth - row.offsetLeft - 24) / (src.offsetWidth + 4)) - 1);
        const outs = row.querySelectorAll('.file.out');
        if (outs.length > room) outs[0].remove();
      }
      showToast(`Saved ${name}`, 'Right next to the original');
      setHint('<span class="step">Beauty!</span>You made <b></b>. Try another, or tap Shift twice for Actions.');
      hint.querySelector('b').textContent = name;
      resetBtn.classList.add('show');
    } else {
      const [title, subText] = r.say;
      if (r.flash) flash(src);
      showToast(title, subText);
      setHint('<span class="step">Done</span><b></b>. Try another one.');
      hint.querySelector('b').textContent = title;
    }
  }

  function showToast(title, subText) {
    toastTitle.textContent = title;
    toastSub.textContent = subText;
    toast.classList.add('show');
    clearTimeout(toastTimer);
    toastTimer = setTimeout(() => toast.classList.remove('show'), 3200);
  }

  sources.forEach(s => {
    s.addEventListener('pointerdown', e => {
      if (e.button !== 0 || state !== 'idle') return;
      e.preventDefault();
      try { s.setPointerCapture(e.pointerId); } catch (err) { /* synthetic pointer: events still arrive */ }
      pick(s);
      pressAt = local(e);
      state = 'press';
    });
    s.addEventListener('pointermove', e => {
      if (s !== src || state === 'idle' || state === 'keys') return;
      const p = local(e);
      if (state === 'press') {
        if (Math.hypot(p.x - pressAt.x, p.y - pressAt.y) > 5) beginDrag(p);
        return;
      }
      if (state === 'drag' && Math.hypot(p.x - pointer.x, p.y - pointer.y) > 3) armHold();
      track(p);
    });
    const release = e => {
      if (s !== src) return;
      if (state === 'press') { state = 'idle'; pressAt = null; return; }
      if (state === 'drag' || state === 'wheel') endDrag(e.type === 'pointerup');
    };
    s.addEventListener('pointerup', release);
    s.addEventListener('pointercancel', release);
    s.addEventListener('lostpointercapture', () => { if (s === src && (state === 'drag' || state === 'wheel')) endDrag(false); });
    s.addEventListener('click', e => e.preventDefault());
    s.addEventListener('blur', () => { if (s === src && state === 'keys') { closeWheel(); state = 'idle'; idleHint(); } });
  });

  document.addEventListener('keydown', e => {
    if (e.key === 'Shift' && !e.repeat) {
      if (state === 'drag') { e.preventDefault(); openWheel(pointer); return; }
      if (state === 'wheel' || state === 'keys') {
        e.preventDefault();
        mode = (mode + 1) % modes.length;
        buildWheel();
        if (state === 'keys') select(0); else track(pointer);
        return;
      }
    }
    if (e.key === 'Escape' && state !== 'idle') {
      e.preventDefault();
      if (state === 'keys') { closeWheel(); state = 'idle'; idleHint(); } else endDrag(false);
      return;
    }
    if (state === 'keys') {
      const n = modes[mode].items.length;
      if (e.key === 'ArrowRight' || e.key === 'ArrowDown') { e.preventDefault(); select((sel + 1) % n); }
      else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') { e.preventDefault(); select((sel - 1 + n) % n); }
      else if (e.key === 'Tab') { e.preventDefault(); mode = (mode + (e.shiftKey ? modes.length - 1 : 1)) % modes.length; buildWheel(); select(0); }
      else if (e.key === 'Enter' || e.key === ' ') {
        e.preventDefault();
        const chosen = modes[mode].items[sel];
        closeWheel(); state = 'idle';
        if (chosen) run(chosen);
      }
      return;
    }
    const focused = sources.find(s => s === document.activeElement);
    if (focused && (e.key === 'Enter' || e.key === ' ') && state === 'idle') {
      e.preventDefault();
      pick(focused);
      state = 'keys';
      const r = rect();
      openWheel({ x: r.width * (r.width > 640 ? .62 : .5), y: r.height / 2 });
    }
  });

  resetBtn.addEventListener('click', () => {
    desk.querySelectorAll('.file.out').forEach(f => f.remove());
    resetBtn.classList.remove('show');
    idleHint();
    sources[0].focus();
  });

  idleHint();
})();
