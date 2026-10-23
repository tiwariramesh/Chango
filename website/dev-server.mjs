// Local preview of the website, with the same clean URLs as Vercel (/privacy → privacy.html), the security headers
// from vercel.json, and the feedback function at /api/feedback. No dependencies.
// Usage: node website/dev-server.mjs [port]   → http://localhost:4321
import { createServer } from "node:http";
import { readFile, stat } from "node:fs/promises";
import { extname, join, normalize } from "node:path";
import { fileURLToPath } from "node:url";
import feedback from "./api/feedback.js";

const root = fileURLToPath(new URL(".", import.meta.url));
const port = Number(process.argv[2] ?? 4321);
const types = {
  ".html": "text/html; charset=utf-8", ".css": "text/css; charset=utf-8", ".js": "text/javascript; charset=utf-8",
  ".json": "application/json", ".png": "image/png", ".jpg": "image/jpeg", ".gif": "image/gif", ".svg": "image/svg+xml",
  ".ico": "image/x-icon", ".mp4": "video/mp4", ".webm": "video/webm", ".txt": "text/plain; charset=utf-8", ".xml": "application/xml",
};
const site = JSON.parse(await readFile(join(root, "vercel.json"), "utf8"));
const pageHeaders = Object.fromEntries(site.headers.find((h) => h.source === "/(.*)").headers.map((h) => [h.key, h.value]));
delete pageHeaders["Strict-Transport-Security"]; // localhost is plain http

async function resolve(url) {
  let path = normalize(decodeURIComponent(url.split("?")[0])).replace(/^([\/])+/, "");
  if (path.includes("..") || path.startsWith("api")) return null;
  for (const candidate of [path, `${path}.html`, join(path, "index.html")]) {
    try { const full = join(root, candidate); if ((await stat(full)).isFile()) return full; } catch { }
  }
  return null;
}

createServer(async (req, res) => {
  for (const [k, v] of Object.entries(pageHeaders)) res.setHeader(k, v);
  if ((req.url ?? "").split("?")[0] === "/api/feedback") {
    let raw = "";
    for await (const chunk of req) { raw += chunk; if (raw.length > 100_000) break; }
    req.body = raw;
    return feedback(req, res);
  }
  const file = await resolve(req.url ?? "/");
  if (!file) { res.writeHead(404, { "Content-Type": "text/plain" }); res.end("Not found"); return; }
  const body = await readFile(file);
  const type = types[extname(file)] ?? "application/octet-stream";
  // Videos need byte ranges so browsers can seek and loop.
  const range = req.headers.range?.match(/bytes=(\d*)-(\d*)/);
  if (range) {
    const start = range[1] ? Number(range[1]) : 0, end = range[2] ? Number(range[2]) : body.length - 1;
    res.writeHead(206, { "Content-Type": type, "Content-Range": `bytes ${start}-${end}/${body.length}`, "Accept-Ranges": "bytes", "Content-Length": end - start + 1 });
    res.end(body.subarray(start, end + 1));
    return;
  }
  res.writeHead(200, { "Content-Type": type, "Content-Length": body.length, "Accept-Ranges": "bytes" });
  res.end(body);
}).listen(port, () => console.log(`OrbitDrop website on http://localhost:${port}`));
