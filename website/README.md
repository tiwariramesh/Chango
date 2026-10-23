# OrbitDrop website

A static site (plain HTML, CSS and a little JavaScript, no build step, no dependencies) plus one serverless function
for the feedback form.

| File | What |
|---|---|
| `index.html` | The home page, including the FAQ (its structured data for Google sits in the `ld+json` block at the top) |
| `updates.html` | Every release, newest first (`/updates`) |
| `feedback.html`, `feedback.js` | Request a feature / report a bug (`/feedback`) |
| `api/feedback.js` | The form's server side: checks a report and emails it privately to the developer through Resend |
| `privacy.html` | The privacy policy (`/privacy`) |
| `styles.css` | Look: clean and white, dark mode follows the system |
| `app.js` | The home page's interactive wheels, tabs, keys, demos, testimonials and FAQ animation |
| `motion.js`, `page.js` | Smooth scrolling, custom cursor, magnetic buttons, headings; `page.js` reveals blocks on inner pages |
| `vendor/lenis-1.1.20.min.js` | Smooth scrolling (Lenis, MIT, see `vendor/lenis-LICENSE.txt`), served from here, not a CDN |
| `vercel.json` | Clean URLs, caching and the security headers (Content-Security-Policy and friends) |
| `dev-server.mjs` | Local preview with the same headers and the feedback function (not deployed; see `.vercelignore`) |
| `tests/feedback.test.mjs` | Tests for the feedback function: `node --test website/tests/feedback.test.mjs` |

## Preview locally

```powershell
node website/dev-server.mjs        # → http://localhost:4321
```

## The feedback form: switch it on (once)

Reports are emailed to you through [Resend](https://resend.com) (free: 3,000 emails a month). Nothing is stored on
the website, and the address they go to is never in this repository.

1. Sign up at resend.com **with the email address you want reports sent to**.
2. In Resend, open **API Keys → Create API key** (permission: *Sending access*). Copy it.
3. In Vercel, open the project → **Settings → Environment Variables** and add, for *Production*:
   - `RESEND_API_KEY` = the key from step 2
   - `FEEDBACK_TO` = the same email address as in step 1
4. **Deployments → ⋯ → Redeploy** (environment variables apply to new deployments).

Until then the form says "The form isn't set up yet" and points people to GitHub. Resend's built-in sender
(`onboarding@resend.dev`) can only send to your own Resend address, which is exactly what's wanted. To send from your
own domain instead, verify it in Resend and add `FEEDBACK_FROM`, e.g. `OrbitDrop <feedback@essam.biz>`.

## Put it online with Vercel (free)

1. Sign in at <https://vercel.com> with your GitHub account.
2. **Add New → Project**, then pick the `i3sam/OrbitDrop` repository.
3. Set **Root Directory** to `website`. Leave the framework as **Other**, with no build command and no output directory.
4. Click **Deploy**. Every push to `main` redeploys.
