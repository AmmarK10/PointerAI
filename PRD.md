# PRD: Screen Companion (working title)
**A hotkey-triggered desktop character that helps you find things on screen**

---

## 1. Problem Statement

Users often don't know where a specific option/button lives in an app or website (e.g. "where do I unsubscribe from newsletters in Gmail") and have to hunt through menus themselves or search it up separately. There's no lightweight, always-available, visual guide that points directly at the answer on your own screen.

## 2. Goal

Ship a v1 desktop app (Windows/Mac via Electron) that a solo builder can realistically get to a working demo in 3–6 weeks, using AI-assisted coding. This is scoped intentionally small — no continuous screen watching, manual trigger only.

## 3. Target User

Anyone who wants quick visual help finding a setting/button in an app they're using. Broad consumer utility, similar audience shape to a browser extension, but a desktop app instead. Targeting Windows + macOS via .NET MAUI (see Section 5) — but treat Windows as the primary build/test target first, since the platform-specific overlay/hotkey/capture code is more mature and better documented there; validate Mac behavior as a second pass, not in parallel.

## 4. Core User Flow (V1 scope)

1. Character sprite sits idle in a fixed corner of the screen (small always-on-top window, transparent background).
2. User presses **Hotkey 1** → a chatbox overlay opens near the character.
3. User types a question, e.g. "where do I unsubscribe from newsletters in Gmail."
4. App captures a screenshot of the current screen.
5. Screenshot + question sent to a vision-capable AI model.
6. Model returns: (a) a text answer/instruction, and (b) its best guess at the on-screen region of the target element (see Section 6 — this is the hard part, keep expectations realistic).
7. Character animates moving toward that screen region; a highlight circle/arrow appears near the target.
8. Chatbox shows the text instruction as a reliable fallback regardless of how accurate the pointing is.
9. User presses **Hotkey 2** → character returns to home position, chatbox closes.

**Explicitly OUT of scope for V1:**
- Continuous/always-on screen watching (privacy + cost prohibitive — manual trigger only)
- Multi-step guided workflows (e.g., walking through a 5-step process) — V1 answers one question at a time
- Voice interaction
- Custom/animated character skins — one simple sprite with basic walk animation is enough
- Mobile companion app

## 5. Technical Approach

- **Framework: C# / .NET MAUI.** Switched from WPF per your preference, chosen specifically for Windows + macOS reach. Important caveat to build with eyes open: MAUI gives you a shared UI/app-lifecycle layer, but the three hardest, most load-bearing pieces of this app do **not** come for free cross-platform and will require separate native code per OS regardless of MAUI:
  - **Transparent, borderless, always-on-top overlay windows** — MAUI's windowing model isn't built for this "floating character" use case. Expect to drop into platform-specific code (`Microsoft.Maui.Controls` platform handlers / partial classes) to get true transparency and always-on-top behavior working correctly on both Windows and Mac.
  - **Global hotkeys** — there is no cross-platform hotkey API in .NET. Windows uses the `RegisterHotKey` Win32 API; macOS needs its own Carbon/Cocoa-level equivalent. You will write and maintain two separate hotkey implementations behind a shared interface.
  - **Screenshot capture** — also platform-specific: Windows Graphics Capture API on Windows, `ScreenCaptureKit`/`CGWindowListCreateImage` on macOS. Same pattern: shared interface, two backing implementations.

  MAUI's real benefit here is containing this platform-specific code inside one solution/project structure rather than maintaining two entirely separate codebases — it is not "write once, run anywhere" for this particular app, given how much of it depends on OS-level window/input/capture behavior. Budget real time for the platform-specific layers on both OSes, not just the shared UI.
- **Hotkey listener:** Windows global hotkeys via the `RegisterHotKey` Win32 API (P/Invoke from C#), or a wrapper NuGet package (e.g., `SharpHotkeys`/`NHotkey`) for both hotkeys (open chatbox / return character home).
- **Screenshot capture:** `System.Drawing` or the Windows Graphics Capture API (via `Windows.Graphics.Capture` in newer .NET), triggered only on-demand (after the user sends a question), never continuously.
- **AI model / vision:** Needs a vision-capable model. Verify current vision support and pricing before committing — cross-check GPT-4V, Gemini (strong vision + generous free tier historically), and Grok's vision-specific SKU (older docs call it "Grok 2 Vision," priced separately from text-only Grok models). Don't assume Grok's cheap text pricing applies to vision calls — confirm the actual vision SKU pricing in the xAI console before budgeting.
- **Coordinate grounding (the hard part):** ask the model for both a text description AND a best-guess bounding box/region rather than a single precise pixel. Treat pixel-perfect pointing as a stretch goal, not a v1 requirement. Always show the text instruction in the chatbox as the reliable fallback — the pointing/movement is a nice-to-have layer on top, not the only source of truth.
- **Character animation:** simple 2D sprite, basic idle/walk states. A single static-to-simple-walk-cycle character is enough for v1 — don't over-invest in animation polish before the core loop works.

## 6. Key Risks (go in eyes-open)

1. **Coordinate/region accuracy from vision models is unreliable today.** This is genuinely unsolved at the "pixel perfect" level industry-wide — treat rough-region pointing as the ceiling for v1, and lean on the text answer as the dependable part of the product.
2. **Vision API costs add up faster than text-only chat.** Every question costs a full image + text call. Track real per-query cost early so you're not surprised later.
3. **Screenshot access requires OS permission grants** (especially strict on macOS) — expect real setup friction here, budget time for it.
4. **Privacy perception:** even with manual-only triggering, users may worry about a "screenshot-taking app." Be explicit in your UI/copy about exactly when a screenshot is taken (only after the user asks a question) and that nothing is captured passively.
5. **This is a genuinely harder build than a browser extension.** Desktop app + hotkeys + overlay windows + vision API + animation is more moving parts than anything else discussed in this conversation. Budget more time than feels comfortable.

## 7. Success Metrics (V1)

Not revenue-focused. Track, once you have a working build:
- Does the text-answer portion work reliably (model gives a correct/useful answer to "where is X")?
- Does the rough-region pointing land anywhere close to the right area, even if imprecise?
- Would you personally keep this open and use it unprompted after a week?

## 8. Monetization (V2, not V1)

Not a priority yet — this is a build-to-learn / build-because-you're-excited project first. If it works and you personally keep using it, a paid tier (more queries/day, faster model, custom character) is a reasonable later step. Don't design pricing before you know the core loop is worth using.

## 9. Build Order (suggested)

1. MAUI shell: always-on-top transparent window, basic character sprite sitting in a corner (build and test on Windows first, then validate the same overlay behavior on Mac before assuming parity)
2. Hotkey 1 wired to open a chatbox overlay (no AI yet — just UI plumbing)
3. Wire chatbox to a **text-only** chat call to your chosen model — validate the basic conversation loop works before adding vision
4. Add on-demand screenshot capture, send screenshot + question together to the vision model — test raw accuracy of what the model reports back before building any movement/animation on top of it
5. Only once step 4 output is usably accurate, add character movement toward the reported region + a highlight indicator
6. Hotkey 2 to return character home / close chatbox
7. Polish: idle animation, basic settings (choose model, set hotkeys)

---
*Note: this is intentionally scoped down from the original "always-watching" concept. Continuous screen monitoring, multi-step guidance, and voice are deferred — prove the manual-trigger single-question loop works and is something you'd actually use before expanding scope.*