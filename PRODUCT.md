# Same Day Advance — product & engineering notes

Australian payday/wage-advance product, mobile-browser-first. This file exists so
design decisions survive between chat sessions (sessions don't share memory —
this doc is the persistent record).

## Product shape

- Applicant picks an advance amount and a payday date on a calendar, then goes
  through an ID check (phone/email) and a bank-connection step (Talefin/Tailfin)
  for account verification.
- New applicants are capped at $100. Repeat customers unlock a 50% cap increase
  per 3 on-time repayments, up to $500 (console currently models advances up to
  $800 — reconcile with the $500 cap when tuning `s-maxAdvance`).
- Positioned as a wage advance, not a loan. `console/sameday-advance-console.html`
  hard-gates every assessment against National Credit Code s6(1) exemption caps
  (term ≤ 62 days, fee ≤ 5%, interest ≤ 24% p.a.) — push past either and the
  compliance banner turns red. This is a product-design constraint, not legal
  advice; get the "not a loan" classification confirmed by a lawyer before launch.

## Files

- `console/applicant-flow.html` — applicant-facing mobile UI mockup (amount
  slider, fee/interest receipt, payday calendar, employment questions, Talefin
  handoff stub, decline screen).
- `console/sameday-advance-console.html` — internal Assessment & Simulation
  Console (single HTML file, no build step):
  - **Assessment tab**: weighted risk-scoring engine across four categories
    (income stability, account conduct, existing credit use, surplus buffer),
    a payroll-eligibility gate (same employer + consistent deposits, to filter
    out sole-trader/invoice income), and the s6(1) compliance gate.
  - **Simulation tab**: multi-year applicant ramp-up, capital/funds-outstanding
    modelling, company tax, Tailfin/advertising/overhead costs, VIP repeat
    customers, and blocking of doomed reapplications.
  - **Session Summary tab**: running log of what's been built and why.
  - **Bank Statement tab** (added — see below): raw transaction ledger +
    categorization engine for one applicant, feeding the Assessment tab.
  - 100 simulated applicant profiles (`SIMULATED_PROFILES`), each with
    hand-set categorical fields (buffer, dishonours, overdraft, deposit
    regularity, etc.) used to drive the scorer directly.

## Transaction categorization framework

The console's scorer currently consumes *pre-bucketed* categorical fields
(e.g. `dishonours: "0"`, `deposit: "irregular"`) — someone (or something) has
to derive those buckets from an actual bank statement. That derivation is
the categorization layer, and the design is:

### 1. Income detection (before anything else)
- Group credits by normalized payer name (strip reference numbers/dates).
- A payer qualifies as recurring income if it appears ≥2 times with amounts
  within ~15% of each other and a consistent gap (weekly/fortnightly/monthly).
- Exclude self-transfers and one-off refunds.
- Flag Centrelink/government credits and gig-platform payouts (Uber, DoorDash,
  Airtasker, etc.) as separate income types, not "salary."
- Derives: `source` (single/multiple/casual), `payerConsistency`
  (same/mostly/different), `deposit` (consistent/mostly/irregular — from gap
  variability), `trend` (rising/flat/declining — first half vs second half of
  window), `incomeAmount`/`incomeFrequency`.

### 2. Outgoing categorization
Keyword/merchant-pattern matching into a small, decision-relevant taxonomy:
rent/housing, bills & utilities, insurance, groceries, eating out, alcohol,
transport, subscriptions, retail/shopping, cash withdrawals, gambling,
BNPL/other-loan repayments, bank fees (dishonours/overdraft), and — critically
— repayments to us or other payday-style lenders (existing-credit-use signal).

### 3. Turn categories into decision variables
- Account conduct: overdraft days, dishonour count, near-zero-balance
  frequency, post-payday buffer (money left 3 days after each payday).
- Existing credit use: other active BNPL/loan repayments, count and recency
  of prior advances (ours specifically, for the escalating-cap rule).
- Surplus buffer: net cash flow per pay cycle, and whether it's consistently
  positive or dips negative.

### 4. Rules first, ML later
Auto-decline on: no verifiable recurring income, active gambling pattern,
2+ other payday-style products in the last 90 days, recent dishonours above
threshold. Auto-approve on: clean income match + healthy scores + (for repeat
customers) on-time history supporting the requested tier. Everything else →
manual review. Keep it rules-based and auditable before reaching for a model —
declines need to be explainable.

## Status

- Applicant flow UI and Assessment/Simulation console: built (imported this
  session, not authored fresh).
- Bank-statement categorization: **built** for one applicant (Samuel Reid,
  `SIMULATED_PROFILES[0]`) as a proof of the pipeline — a new "Bank Statement"
  tab in the console. Includes:
  - A hand-tuned but realistic fake 90-day transaction ledger (102 txns:
    two wage payers on an irregular cycle, rent, bills, groceries, eating
    out, alcohol, transport, cash withdrawals, one recurring Afterpay
    repayment, no gambling, no dishonours).
  - A merchant-keyword categorizer (`SPEND_CATEGORY_RULES`) and an
    income-detection pass (`deriveIncomeProfile`) that groups credits by
    normalized payer, tests recurrence/regularity/trend.
  - Account-conduct (`deriveAccountConduct`), existing-credit-use
    (`deriveExistingCreditUse`), and surplus (`deriveSurplus`) derivers that
    turn the categorized ledger into the exact same bucketed fields the
    scorer already consumes (`buffer`, `dishonours`, `overdraft`, `nearZero`,
    `deposit`, `source`, `trend`, `payerConsistency`, `priorAdvances`,
    `otherRepayments`, `gapSince`, `surplusSize`, `surplusConsistency`,
    `incomeAmount`, `incomeFrequency`, `daysUntilNextPayday`).
  - A side-by-side comparison against the originally hand-set profile
    values — 12 of 16 fields matched exactly on first run; the 4 that
    differed (deposit regularity, surplus size, income amount, days to next
    payday) are genuinely informative gaps between a guessed profile and
    what the transactions actually show. That comparison is the point: it's
    evidence the derivation is doing real work, not just echoing the guess.
  - A "Load derived inputs into Assessment tab" button that runs the
    applicant through the real scorer end-to-end (verified in a headless
    browser: 102 ledger rows render, comparison table populates, and the
    Assessment tab produces a stamped decision from the derived inputs).
- Bank-statement categorization: **extended to all 100 applicants**, then
  corrected to match real transaction-account behaviour, plus two new
  scrutiny views. In order:

  **1. All 100 applicants.** An applicant picker was added to the Bank
  Statement tab (same pattern as the Assessment tab's profile picker), and a
  parameterized generator produces a 90-day statement for every applicant
  (including Samuel — now regenerated through the same engine rather than
  hand-tuned separately, for consistency) from their existing hand-set
  profile fields — income pattern, archetype-driven spend intensity,
  gambling presence for risky profiles, dishonour/BNPL/prior-advance
  transactions. Produced algorithmically with no per-applicant tuning, so
  the derived-vs-hand-set comparison is a genuine test, not a rigged one.
  Building it surfaced and fixed several real modelling bugs along the way
  (rent charged against every casual gig payment instead of a normal billing
  cycle; a starting-balance mismatch between generation and categorization;
  a spend cap too tight to absorb same-day income windfalls; advance
  repayments landing on arbitrary days instead of after a real payday) —
  each found by tracing a specific applicant's ledger, not guessed at.

  **2. Real account behaviour: balances can't go negative.** A standard
  Australian transaction account has no overdraft facility — a card/EFTPOS
  purchase just declines at the point of sale if funds are short, and a
  scheduled direct debit (rent, a bill, a BNPL instalment, an advance
  repayment) gets *dishonoured* instead, with a dishonour fee, rather than
  going through into negative territory. The generator now enforces this as
  a hard constraint for every applicant: every debit is capped at the
  available balance, and any bill-type debit that can't be covered is
  skipped and replaced with a dishonour fee (if there's enough left to cover
  the fee — otherwise nothing happens at all that day). Cash withdrawals
  (ATM and EFTPOS alike) are now always $20 multiples, capped by whatever
  balance is available. Verified: **zero negative-balance instances and
  zero non-$20 cash withdrawals across all 100 applicants.**
  - Structural consequence, called out explicitly in the UI rather than
    hidden: **"Overdraft days" now always derives to "0"**, since it's
    describing something that's literally impossible on a no-overdraft
    account. That field only still matches applicants whose hand-set target
    was already "0" — worth deciding whether the Assessment tab's overdraft
    dial should be retired or repointed at something that can actually
    happen (declined-transaction frequency, or dishonour count, are the
    closer real-world signals now).
  - Dishonours, prior advances, and payday buffer are no longer hand-dialed
    to a target count either — they're genuine emergent consequences of the
    cash-flow simulation now (a bill either bounces or it doesn't), so they
    won't reliably land on whatever bucket was originally guessed.
  - Re-verified against all 100 hand-set profiles after these corrections:
    **58% of fields match overall** (down from 64% before the fix, which is
    expected and correct — some of that 64% came from mechanics, like
    driving balances negative on purpose, that couldn't happen on a real
    account). "Other active repayments" still matches 100%. Income-pattern
    fields match 59-71%. Near-zero balance frequency matches 78% (up from
    63% — removing negative excursions made the near-zero band easier to
    land in cleanly). Dishonours/prior-advances/buffer/surplus match 27-43%,
    now for the more defensible reason that they're honest simulation
    outcomes, not dial-in targets.

  **3. Categorized ledger.** Every applicant's transactions are now also
  shown grouped by category (all income together, all rent together, all
  eating-out together, etc.), each group with a running subtotal and count,
  alongside the existing chronological ledger.

  **4. Upload a real bank statement (PDF).** Client-side only — nothing is
  uploaded anywhere. Uses pdf.js (loaded from a CDN) to extract positioned
  text from the PDF and reconstruct it into lines, then a heuristic parser
  looks for a leading date and a trailing dollar amount on each line to
  identify candidate transactions. This is explicitly a best-effort, generic
  parser, not a bank-specific integration — statement layouts vary too much
  (separate debit/credit columns, DR/CR suffixes, thousands separators) to
  get right for every bank from text alone, and the sign of an amount is
  often ambiguous once column position is lost, so it defaults new amounts
  to debits and asks the user to flip any that are really credits. Every
  extracted row is shown in an editable review table (checkbox to
  exclude, editable date/description/amount) before anything is run through
  the categorizer, plus a raw-text view of lines that didn't parse. Once
  confirmed, the same `deriveApplicantFromStatement` pipeline, categorized
  ledger, and "load into Assessment" flow used for the simulated applicants
  runs on the reviewed rows.
- Next: use the population-scale comparison to decide whether any hand-set
  profile buckets should be corrected to match what real transactions would
  actually show (the same fix already applied once, for the payroll-
  eligibility rate — see Session Summary §5); decide the fate of the
  overdraft dial now that it's structurally always "0"; and get a couple of
  real (anonymized/redacted) bank statement PDFs to test the upload parser
  against actual bank formats rather than synthetic sample lines.
