// The feedback page: switch between a feature request and a bug report, then send it to /api/feedback.
// Everything a visitor types is sent as JSON text and never put into the page as HTML.
(() => {
  "use strict";
  const form = document.getElementById("fb-form");
  if (!form) return;
  const done = document.querySelector(".fb-done");
  const status = form.querySelector(".fb-status");
  const send = form.querySelector('button[type="submit"]');
  const tabs = [...document.querySelectorAll(".fb-switch [role=tab]")];
  const opened = Date.now();

  function show(type) {
    form.elements.type.value = type;
    tabs.forEach((t) => t.setAttribute("aria-selected", String(t.dataset.type === type)));
    form.querySelectorAll("[data-for]").forEach((el) => { el.hidden = el.dataset.for !== type; });
    form.querySelector(".fb-bug").hidden = type !== "bug";
    form.elements.title.placeholder = type === "bug" ? "e.g. CDR to PNG says “That didn't work”" : "e.g. Convert HEIC to JPG";
    status.textContent = "";
    if (location.hash !== `#${type}`) history.replaceState(null, "", `#${type}`);
  }
  tabs.forEach((t) => t.addEventListener("click", () => show(t.dataset.type)));
  show(location.hash === "#bug" ? "bug" : "feature");

  const count = form.querySelector('[data-count="details"]');
  form.elements.details.addEventListener("input", () => { count.textContent = `${form.elements.details.value.length} / 4000`; });

  form.addEventListener("submit", async (e) => {
    e.preventDefault();
    const f = form.elements;
    if (f.title.value.trim().length < 3) { status.textContent = "Please give it a short title."; f.title.focus(); return; }
    if (f.details.value.trim().length < 10) { status.textContent = "Please add a few more details."; f.details.focus(); return; }
    if (f.email.value && !f.email.checkValidity()) { status.textContent = "That email address doesn't look right."; f.email.focus(); return; }

    const body = {
      type: f.type.value, title: f.title.value, details: f.details.value, email: f.email.value,
      steps: f.steps.value, version: f.version.value, windows: f.windows.value, source: f.source.value,
      website: f.website.value, elapsed: Date.now() - opened,
    };
    if (body.type !== "bug") { body.steps = body.version = body.windows = body.source = ""; }

    send.disabled = true;
    status.textContent = "Sending…";
    try {
      const r = await fetch("/api/feedback", { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
      const result = await r.json().catch(() => ({}));
      if (r.ok && result.ok) {
        form.reset();
        count.textContent = "0 / 4000";
        form.hidden = true;
        done.hidden = false;
        done.querySelector("h2").focus?.();
      } else {
        status.textContent = (result.error || "It couldn't be sent just now.") + " You can also open an issue on GitHub.";
      }
    } catch {
      status.textContent = "It couldn't be sent. Check your connection and try again.";
    } finally {
      send.disabled = false;
    }
  });

  document.getElementById("fb-again").addEventListener("click", () => {
    done.hidden = true;
    form.hidden = false;
    status.textContent = "";
    show(form.elements.type.value);
  });
})();
