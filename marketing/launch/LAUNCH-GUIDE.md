# OrbitDrop launch guide

Where to post, what to post, and when. Every post below is ready to copy. Each one has its own Store link, so Partner Center can show which post brought the downloads.

**The one-line pitch:** OrbitDrop lets you convert, compress and share files without opening an app: drag a file, tap Shift, let go. Free on the Microsoft Store.

**New since the first draft:** PDF tools (merge, split, compress, rotate) and VS Code support. Mention them: "merge PDFs without uploading them anywhere" is a strong hook on its own.

---

## 0. Before you post (once, about 30 minutes)

- [ ] **Website live** on Vercel (see `website/README.md`). Put the address in the Store listing's **Website** field and in the GitHub **About** box.
- [ ] **GitHub repo tidy:** README badge (done), topics (`windows`, `productivity`, `file-converter`, `wpf`, `radial-menu`), and a social preview image. In GitHub → Settings → Social preview, upload `images/og-1200x630.png`.
- [ ] **Turn on GitHub Discussions** (Settings → Features), so questions don't all become issues.
- [ ] **Make the X/Twitter account look ready:** profile picture `../../src/OrbitDrop.UI/Assets/orbitdrop-512.png`, header `images/x-header-1500x500.png`, and a bio such as: *Convert files without opening an app. Drag, tap Shift, let go. Free for Windows.*
- [ ] **Plan to be online for the first 3 hours after each post.** Replying fast to every comment matters more than anything else on this list.

### Your tracking links

Use the link for each place you post. The `cid=` part lets Partner Center → Insights → Acquisitions show where downloads came from.

| Where | Link |
|---|---|
| Reddit | `https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=reddit` |
| Hacker News | `https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=hn` |
| Product Hunt | `https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=producthunt` |
| X / Twitter | `https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=x` |
| LinkedIn | `https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=linkedin` |
| YouTube / TikTok / Instagram | `https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=shorts` |
| News sites / email | `https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=press` |
| AlternativeTo | `https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=alternativeto` |

> When your website is live, you can link to it instead in places where links to stores look salesy (Hacker News especially). The website's buttons carry their own `cid=website-…` tags.

---

## 1. The plan (two weeks)

Don't post everywhere on the same day. Spread it out so you can reply to everyone and learn from each round.

| Day | Where | Why then |
|---|---|---|
| Day 1 (Tue or Wed) | **r/Windows11** + X thread | The core audience. Learn which questions come up. |
| Day 2 | **Show HN** (Hacker News) | Post between 8 and 10 am US Eastern. Open source and offline go down well here. |
| Day 3 | **r/software**, **r/opensource** | Different crowds, same post with small tweaks. |
| Day 4 | **r/vscode** | OrbitDrop now works from VS Code's sidebar: a niche, very engaged crowd. |
| Day 4 | **YouTube Shorts, TikTok, Instagram Reels** | Post all three vertical clips over the week, one per day. |
| Day 5 | **AlternativeTo** + **LinkedIn** | Slow-burn discovery; people search "Tangerine alternative". |
| Week 2, Tue | **Product Hunt** | Goes live at 12:01 am US Pacific. Ask friends to look that day, but never ask for upvotes. |
| Week 2 | **Email Windows news sites** | Use the reactions and numbers from week 1 as proof. |
| Week 2 | **r/productivity**, **r/SideProject**, **r/windows** | The second wave, once you know which wording works. |

---

## 2. The posts

### Reddit: r/Windows11 (Day 1)

**Read each subreddit's rules first.** Many allow self-made tools only on certain days or with a flair. Post as yourself ("I made…"), stay humble, and reply to everything.

**Attach:** `gifs/guide-hero.gif`, or upload `videos/OrbitDrop-Square-Convert.mp4` as a video post (video gets more views on Reddit).

**Title:**
```
I made a free app that converts files without opening anything: drag a file, tap Shift, let go
```
**Text** (as the post body or your first comment):
```
Hi all! I got tired of opening an app, exporting, picking a format and finding a folder just to turn one PNG into a JPG, so I built OrbitDrop.

How it works: start dragging any file in File Explorer, tap Shift, and a little wheel pops up under your cursor. Let go on what you want, and the new file appears right next to the original. The original is never changed.

Tap Shift again for Tools (compress to under 1 MB, resize, rotate, a small photo editor, copy text out of an image) and once more for Actions (copy/move to, share, zip). There's also Ctrl+Alt+Space if you'd rather not drag.

- Free, no account, no ads, no tracking
- Everything runs offline on your PC
- Open source (MIT): https://github.com/i3sam/OrbitDrop
- Microsoft Store: https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=reddit

It's inspired by Tangerine on the Mac. I'd love feedback, especially on what's missing from the wheel!
```

### Reddit: r/vscode (Day 4)

**Attach:** `videos/OrbitDrop-Kinetic-16x9.mp4`, or a short screen recording of you dragging a file out of VS Code's sidebar and tapping Shift (that's the most convincing thing you can post here).

**Title:**
```
I made a free tool: drag a file from VS Code's Explorer, tap Shift, and convert/compress/zip it without leaving the editor
```
**Text:**
```
OrbitDrop is a small Windows app: start dragging any file (VS Code's Explorer works too), tap Shift, and a wheel opens under your cursor. Let go on PNG, PDF, MP3, "compress to 1 MB", "zip", "merge PDFs"... and the result lands next to the original.

No dragging? Select files in VS Code's Explorer and press Ctrl+Alt+Space.

Free, offline, open source (MIT): https://github.com/i3sam/OrbitDrop
Microsoft Store: https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=reddit
Works in Cursor and VSCodium too. Feedback very welcome!
```

### Reddit: r/software and r/opensource (Day 3)

Same post, with one change. For **r/opensource**, lead with the open-source part:
```
Title: OrbitDrop: an open-source (MIT) Windows tool to convert, compress and share files with one drag and a tap of Shift
```
and put the GitHub link first.

### Hacker News: Show HN (Day 2)

Link to the **GitHub repo** (or your website), not the Store. HN readers like seeing the code.

**Title** (HN allows 80 characters):
```
Show HN: OrbitDrop – Drag a file, tap Shift, convert it (Windows, open source)
```
**URL:** `https://github.com/i3sam/OrbitDrop`

**First comment** (post it right away):
```
Hi HN, I built OrbitDrop because converting a file on Windows usually means opening an app, exporting, picking a format and finding a folder.

With OrbitDrop you start dragging a file, tap Shift, and a radial menu appears under the cursor; let go on JPG/PNG/PDF/MP3/etc. and the result is written next to the original (never overwritten). Tapping Shift again switches to Tools (compress to a target size, resize, OCR, a small editor) and Actions (copy/move/zip/share).

Some technical notes:
- WPF on .NET 10. Detecting "the user is dragging files and tapped Shift" from outside the drag source was the hard part: a low-level mouse hook plus watching for the shell's drag image window turned out to be the most reliable signal.
- All processing is local: WIC for images (so any codec Windows has works), Media Foundation for audio/video, Windows.Media.Ocr for text. No network calls in the Store build.
- It's in the Microsoft Store (free) and on GitHub releases.

Inspired by Tangerine for Mac. Happy to answer anything, and I'd love to hear what would make it useful for you.
```
> Before posting, check that the technical notes still match the code. If any detail is off, cut it: HN readers will notice.

### Product Hunt (Week 2, Tuesday)

Create the product at <https://www.producthunt.com/posts/new>.

| Field | Value |
|---|---|
| Name | OrbitDrop |
| Tagline (60 characters) | `Convert files without opening an app. Drag, tap Shift, done.` |
| Link | `https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=producthunt` |
| Topics | Productivity, Windows, Open Source, Design Tools |
| Thumbnail | `producthunt/thumbnail-240.png` |
| Gallery | `producthunt/gallery-1-convert.png` … `gallery-6-customize.png` (in order) |
| Video | Upload `videos/OrbitDrop-Trailer-1080p.mp4` to YouTube (unlisted is fine), then paste the YouTube link |
| Pricing | Free |

**Description:**
```
OrbitDrop is a tiny Windows app that puts a wheel of actions right under your cursor. Drag any file, tap Shift, and let go on what you want: JPG, PNG, PDF, MP3, compress, resize, zip, share… The result lands next to the original, which is never changed.

It's free, open source, works offline, and needs no account.
```
**Maker comment** (post it as soon as you go live):
```
Hey Product Hunt! 👋 I'm Mohammed, and I made OrbitDrop.

On the Mac there's a lovely app called Tangerine that lets you convert files by dragging them onto a little wheel. I wanted that on Windows, so I built it.

What you can do from one wheel:
• Convert images, PDFs, video and audio
• Compress photos to a size (under 1 MB, 500 KB…), resize, rotate, edit
• Copy text out of an image
• Copy/move to folders, zip, share, float files on a shelf

Everything runs on your PC: no uploads, no account, no tracking. And it's open source.

I'd love to know: what would you add to the wheel?
```

### X / Twitter thread (Day 1)

**Tweet 1** (attach `videos/OrbitDrop-Square-Convert.mp4`):
```
I made a free Windows app that converts files without opening anything.

Drag a file. Tap Shift. Let go. ✨

The new file lands right next to the original.
```
**Tweet 2** (attach `images/post-3-modes-1080.png`):
```
Tap Shift again for Tools: compress to under 1 MB, resize, rotate, a little photo editor, copy text out of images.

Once more for Actions: copy, move, zip, share.
```
**Tweet 3** (attach `images/post-2-original-1080.png`):
```
Everything happens on your PC.
No uploads. No account. No tracking.
Your original file is never touched.
```
**Tweet 4:**
```
It's free on the Microsoft Store and open source on GitHub 👇
https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=x
https://github.com/i3sam/OrbitDrop
```

### LinkedIn (Day 5)

**Attach:** `images/portrait-1-files-1080x1350.png`, or `videos/OrbitDrop-Vertical-Convert.mp4`.
```
I just shipped my first app on the Microsoft Store 🎉

OrbitDrop turns "convert this file" from a nine-click chore into one move: drag a file, tap Shift, let go. The result appears right next to the original.

It can also compress photos to a target size, resize, edit, copy text out of images, trim video, pull the audio out of a clip, zip and share, all from a small wheel under your cursor.

It's free, open source, and works completely offline. Building it taught me a lot about Windows internals, design, and shipping something real end to end.

Try it: https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=linkedin
Code: https://github.com/i3sam/OrbitDrop

#Windows #Productivity #OpenSource #IndieDev
```

### YouTube Shorts, TikTok, Instagram Reels (Days 4–10)

Post one clip per day. The captions are already burned in, so they work with the sound off.

| Day | File | Caption |
|---|---|---|
| 1 | `videos/OrbitDrop-Vertical-Convert.mp4` | `Convert any file in one move on Windows 🤯 Drag, tap Shift, let go. Free app: OrbitDrop` |
| 2 | `videos/OrbitDrop-Vertical-Tools.mp4` | `Tap Shift again… and again 👀 Compress, resize, zip, or pull the audio out of a video` |
| 3 | `videos/OrbitDrop-Vertical-Private.mp4` | `A file converter that never uploads your files 🔒 Free on the Microsoft Store` |

**Hashtags:** `#windows11 #productivity #techtips #pctips #windowstips #freeapps`

**Link:** put `https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=shorts` in your bio, or in the description on YouTube.

The full trailer, `videos/OrbitDrop-Trailer-1080p.mp4`, can go on YouTube as a normal video titled **"OrbitDrop: convert files without opening an app (Windows)"**.

### AlternativeTo (Day 5)

1. Sign up at <https://alternativeto.net> and choose **Add application**.
2. Fill it in:
   - **Name:** OrbitDrop
   - **Platforms:** Windows
   - **License:** Free, Open Source
   - **Description:** the Product Hunt description above
   - **Images:** the gallery images
3. Then open **Tangerine**'s page on AlternativeTo and suggest OrbitDrop as an alternative for Windows. People switching from Mac search exactly this.

### Windows news sites (Week 2)

Small, polished Windows tools get written up regularly. Send a short, personal email.

| Site | How to reach them |
|---|---|
| Neowin | "Contact" or "Submit a tip" form on neowin.net |
| gHacks | ghacks.net contact page (they love small utilities) |
| Windows Central | tips form on windowscentral.com |
| XDA Developers | tips form on xda-developers.com |
| MakeUseOf, How-To Geek | contact pages (good for "best free Windows apps" lists) |
| Softpedia, MajorGeeks | software submission forms (download directories) |

**Subject:** `New free Windows tool: convert files by dragging and tapping Shift`

**Email:**
```
Hi [name],

I'm Mohammed, an independent developer. I just released OrbitDrop, a free and open-source Windows app that converts and manages files with one gesture: start dragging a file, tap Shift, and a small wheel appears under the cursor. Let go on JPG, PDF, MP3, "compress to under 1 MB", "zip" and so on, and the result lands next to the original.

It runs entirely offline (no uploads, no account), is on the Microsoft Store, and its code is on GitHub. It's inspired by Tangerine on the Mac, which has no Windows equivalent.

• Store: https://apps.microsoft.com/detail/9NJQRMQLL2SZ?cid=press
• One-minute film: [YouTube link]
• Screenshots and logos: [link to a shared folder, or the website]

Happy to answer any questions. Thanks for your time!

Mohammed Essam
```

---

## 3. Replies you'll need (copy and adapt)

**"Is this a keylogger? Why does it watch the keyboard?"**
> Fair question! It watches only the mouse buttons and the Shift key, so it can notice "dragging a file + tap Shift". It never records or sends anything, and it makes no network requests in the Store version. The code is open source if you want to check: https://github.com/i3sam/OrbitDrop

**"Windows says it's unsafe / SmartScreen warning."**
> That's the GitHub download, which isn't code-signed yet. The Microsoft Store version is signed by Microsoft and installs without any warning.

**"Mac / Linux version?"**
> On the Mac, Tangerine does this beautifully. Linux isn't planned for now, but the code is open if someone wants to try!

**"Can it do X?"**
> Not yet, but I'm collecting ideas. Could you open a quick issue so I don't lose it? https://github.com/i3sam/OrbitDrop/issues

**"It didn't work for me."**
> Sorry about that! Could you tell me your Windows version, and what you dragged from where? The logs (Settings → Advanced → Logs) help a lot. I'll fix it.

---

## 4. What's in this folder

| File | Size | Use it for |
|---|---|---|
| `images/og-1200x630.png` | 1200 × 630 | Link previews, GitHub social preview, Facebook |
| `images/post-1-convert-1080.png` | 1080 × 1080 | X, Instagram, Facebook, Reddit image post |
| `images/post-2-original-1080.png` | 1080 × 1080 | "Your original never changes" |
| `images/post-3-modes-1080.png` | 1080 × 1080 | "Three wheels. One key." |
| `images/post-4-store-1080.png` | 1080 × 1080 | Simple "it's out!" announcement |
| `images/portrait-1-files-1080x1350.png` | 1080 × 1350 | Instagram and LinkedIn feed (portrait gets more space) |
| `images/portrait-2-dark-1080x1350.png` | 1080 × 1350 | Dark-mode lovers |
| `images/x-header-1500x500.png` | 1500 × 500 | X/Twitter profile header |
| `videos/OrbitDrop-Kinetic-16x9.mp4` | 33 s, 16:9 | **Best trailer.** YouTube, Product Hunt, Store trailer, website, Reddit, X |
| `videos/OrbitDrop-Kinetic-Laptop-9x16.mp4` | 33 s, 9:16 | **Best vertical.** TikTok, Reels, Shorts (the "filmed on a laptop" look) |
| `videos/OrbitDrop-Trailer-1080p.mp4` | 70 s, 16:9 | The longer, narrated film (AI voice: check the ElevenLabs licence first) |
| `videos/OrbitDrop-Square-Convert.mp4` | 15 s, 1:1 | X, LinkedIn, Reddit |
| `videos/OrbitDrop-Vertical-*.mp4` | 13–16 s, 9:16 | Shorts, TikTok, Reels, Stories |
| `gifs/guide-*.gif` | 600 × 320 | Reddit, GitHub, forums, anywhere video won't play |
| `producthunt/thumbnail-240.png` | 240 × 240 | Product Hunt thumbnail |
| `producthunt/gallery-*.png` | 1270 × 760 | Product Hunt gallery, AlternativeTo |

**Remaking any of it:** the images and clips come from `../launch-film/src/social.tsx`. Run `cd marketing/launch-film`, then `node scripts/social.mjs`.

---

## 5. Measuring it

- **Partner Center → Insights → Acquisitions:** installs per day, and per `cid` (which post worked).
- **Partner Center → Insights → Usage:** people actually using it.
- **Partner Center → Ratings and reviews:** reply to every review, good or bad, within a day.
- **GitHub → Insights → Traffic:** visitors and referrers to the repo.

After two weeks, look at which post brought the most installs, and do more of that.
