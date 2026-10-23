// Shared by the inner pages (updates, feedback): blocks marked .reveal rise in as they scroll into view.
// motion.js handles the headings, cursor and smooth scrolling.
(() => {
  "use strict";
  const els = document.querySelectorAll(".reveal");
  if (!("IntersectionObserver" in window)) { els.forEach((el) => el.classList.add("in")); return; }
  const io = new IntersectionObserver((es) => es.forEach((e) => {
    if (e.isIntersecting) { e.target.classList.add("in"); io.unobserve(e.target); }
  }), { threshold: 0.12 });
  els.forEach((el) => io.observe(el));
})();
