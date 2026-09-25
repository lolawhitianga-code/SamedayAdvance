# Same Day Advance — 60-Second Investor Video
## Script, storyboard & production notes

Prepared for handoff to a voice service (ElevenLabs, Descript) and a video
editor. Everything below is ready to use as-is.

---

## 1. Concept & tone

A tight, confident fintech-style investor teaser — the "here's our latest
iPhone" energy the founder asked for: clean, minimal, product-first, a
few hard numbers landing right when the voiceover says them. Not
hard-sell; more "we built something real, here's the proof."

**Voice**: Australian female, warm but confident — think a calm product-launch
narrator, not a hype-reel shouter. Measured pace, clear enunciation on numbers.

**Visual style**: Dark background (matches the actual product's console —
charcoal/near-black with gold accent, consistent with the Assessment tab's
existing palette), clean sans-serif/monospace type, subtle motion (fades and
slides, no gimmicky transitions), real screenshots from the working product
wherever possible instead of stock photography.

**Total runtime**: ~61 seconds. **Word count**: ~150 words (~2.5 words/sec,
comfortable pace for clear AU delivery — do not rush it to fit; trim visuals
instead if it's running long).

---

## 2. Full voiceover script (word-for-word, timed)

> **[0:01–0:04]**
> Same Day Advance, because your next payday shouldn't decide your today.
>
> **[0:05–0:14]**
> We're a mobile-first wage advance platform, giving everyday Australians
> fair, fast access to money they've already earned, before payday, and
> without the fees of a payday loan.
>
> **[0:15–0:31]**
> Here's how it works. Someone applies from their phone, and we securely
> read their real bank transaction data. Our own risk-scoring engine, built
> in-house, checks income stability, account conduct, existing debt and
> spare cash flow, and returns a decision in seconds. Approved funds land
> the same day.
>
> **[0:32–0:36]**
> No spreadsheets, no guesswork. Just data-driven lending, done responsibly.
>
> **[0:37–0:55]**
> We've modelled three growth scenarios over three years, on the same
> twenty-one-thousand-dollar starting pool. Conservative: two hundred and
> sixty-four thousand dollars profit. Our base case: six hundred and
> ninety-nine thousand. And our high-growth case: nearly one point two
> million dollars; currently capped by capital, not by demand.
>
> **[0:55–0:59]**
> Same Day Advance. Smarter lending, real returns. Let's talk.

**Word count check**: 150 words (61s including the end card; ~59s of speech at a natural, unhurried pace).

**Delivery notes** (learned building the rendered version): read each block
as one continuous passage rather than line by line, and let the voice flow
through the commas at "from their phone, and…", "spare cash flow, and…" and
"smarter lending, real returns". The one deliberate beat inside a sentence
is after "…million dollars" — land the number, pause briefly, then the
"capped by capital" aside. Keep comma pauses around 0.3s and sentence
pauses under half a second.

---

## 3. Shot-by-shot storyboard

| # | Time | Visual | On-screen text | Audio/SFX cue |
|---|------|--------|-----------------|----------------|
| 1 | 0:00–0:05 | Black screen → logo/wordmark fades up center, subtle gold particle/line animation | **SAME DAY ADVANCE** | Music: soft synth swell starts here. A single low "arrival" tone on the wordmark landing. |
| 2 | 0:05–0:15 | Cut to phone mockup: a person's hand holding a phone, the applicant-flow UI (already built) visible and scrolling naturally | small caption: *"Fair, fast, before payday."* | Music continues, steady mid-tempo underneath |
| 3 | 0:15–0:28 | Screen recording / clean mockup of the actual product: applicant flow → bank data connecting (a loading/checkmark animation) → Assessment tab decision card appearing with a green "Approved" stamp | on-screen labels appear in sync with VO: "Bank data" → "Risk score" → "Decision in seconds" | A light "tick" SFX exactly as "decision in seconds" is spoken; music lifts slightly |
| 4 | 0:28–0:34 | Quick cut: categorized ledger view from the Bank Statement tab (colour-coded category badges scrolling), then the Assessment tab's score breakdown (four weighted categories) | *"No guesswork. Just data."* | Music holds steady, slightly sparser to let the line land |
| 5 | 0:34–0:47 | The Multi-Year Simulation chart animates in — three overlaid lines (or three sequential bars) for Low / Medium / High, each scenario's profit figure counting up as it's named | **LOW: $264K** → **MEDIUM: $699K** → **HIGH: $1.19M+** (each appears as its number is spoken) | Music builds here — this is the emotional peak of the piece. A soft "count-up" tick sound under each number if the editor can sync it |
| 6 | 0:47–0:55 | Hold on the High scenario bar/line, then a small caption slides in under it | *"Capital-constrained, not demand-constrained."* | Music sustains |
| 7 | 0:55–1:00 | Cut to black, wordmark returns with tagline beneath, fades to a simple contact/CTA card | **SAME DAY ADVANCE**<br>*Smarter lending. Real returns.*<br>[contact / website placeholder] | Music resolves to a clean final chord/stop, timed to land as VO says "Let's talk." |

---

## 4. The three scenarios — source data

Pulled directly from the product's own Multi-Year Simulation engine (not
invented figures) — same $21,000 starting capital in all three, over a
3-year run, varying only the growth curve and expected default rates:

| | Low | Medium (base case) | High |
|---|---|---|---|
| Peak applicants/day | 15 | 30 | 50 |
| Days to reach peak | 270 | 180 | 120 |
| Default rate (strong / average / risky applicants) | 0.4% / 0.6% / 0.9% | 0.2% / 0.3% / 0.4% | 0.15% / 0.2% / 0.25% |
| Repeat-customer rate | 30% | 50% | 65% |
| **Total applicants (3yr)** | 14,677 | 30,338 | 52,005 |
| **Approvals (3yr)** | 10,321 | 16,749 | 20,270 |
| **Total profit after tax (3yr)** | **$264,123** | **$699,352** | **$1,189,968** |

Note for the deck/appendix (not spoken in the 60-second cut): the High
scenario shows 27,580 applications that couldn't be processed due to
insufficient available funds — i.e. growth outpaced the $21k capital pool.
That's a genuine finding from the model, not a script exaggeration, and
it's a legitimate case for raising more capital: demand exceeds what the
current pool can fund.

These are model outputs from stated assumptions (fee %, default rates,
tax treatment, advertising/overhead costs as configured in the console),
not audited financials or guarantees — worth a one-line disclaimer on
screen or in an accompanying deck if this goes to real investors.

---

## 5. Production notes for whoever assembles this

- **Voice**: ElevenLabs or Descript Overdub, Australian female voice preset
  (e.g. ElevenLabs' "Charlotte" or similar AU-accented voice — audition a
  couple, pick for warmth over "corporate narrator" stiffness). Feed it the
  script in Section 2 verbatim, including the timing brackets as pacing
  guides (don't read them aloud).
- **Music**: royalty-free corporate/fintech bed, moderate tempo (100–115
  BPM), builds gently from 0:34 to the final scenario reveal, resolves
  cleanly by 1:00. Epidemic Sound / Artlist "tech optimism" or "corporate
  uplift" categories are a good starting search term.
- **Visuals**: reuse real product screens wherever possible — the
  Assessment tab's decision card, the Bank Statement tab's categorized
  ledger, and the Multi-Year Simulation tab's chart are all already built
  and screen-recordable directly from the console. This makes the video
  more credible to investors than stock UI mockups.
- **Captions**: burn in the on-screen text from the storyboard table even
  though there's a voiceover — many investors will first watch muted.
