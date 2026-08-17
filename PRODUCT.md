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
- Bank-statement categorization: **extended to all 100 applicants.** An
  applicant picker was added to the Bank Statement tab (same pattern as the
  Assessment tab's profile picker), and a parameterized generator produces a
  90-day statement for each of the other 99 from their existing hand-set
  profile fields — income pattern (source count, payer consistency, deposit
  regularity, trend), archetype-driven spend intensity, gambling presence for
  risky profiles, dishonour/BNPL/prior-advance transactions, and a
  day-by-day balance walk tuned toward the target overdraft/near-zero/buffer
  buckets. Unlike Samuel's statement (hand-tuned, iterated by eye), these are
  produced algorithmically with no per-applicant tuning — so the
  derived-vs-hand-set comparison is a genuine test, not a rigged one.
  - Building the generator surfaced and fixed several real modelling bugs:
    charging full rent against every casual gig payment instead of a normal
    billing cycle; a mismatched starting balance between the generator's
    internal decisions and how the categorizer reconstructs balance from the
    transaction list alone; a spend cap that couldn't absorb same-day
    windfalls (a second payer or a government payment landing mid-cycle),
    causing balances to snowball upward; and advance-repayment debits landing
    on arbitrary days instead of shortly after a real payday. Each was found
    by tracing a specific applicant's ledger against its target buckets, not
    guessed at — the same "verify, don't assume" approach used on Samuel.
  - Verified via the same categorizer against all 100 hand-set profiles:
    **64% of fields match overall.** Dishonours, prior advances, and other
    active repayments (the count-based fields) match **100%** of the time.
    Income-pattern fields (deposit regularity, source count, income trend,
    payer consistency, gap since last advance) match **60-71%**. The
    balance-timing fields (payday buffer, overdraft days, surplus size/
    consistency) match **28-38%** — these depend on exactly which calendar
    day a dip happens to land relative to a threshold, which is genuinely
    hard to hit with an algorithmic generator (it took hand-tuning to get
    Samuel's statement close on these) and is arguably hard to *hand-guess*
    correctly too. That gap is informative, not a flaw to hide: it's the
    same kind of finding as Samuel's "deposit: irregular" vs. derived
    "consistent" — evidence the categorizer is doing real work against real
    data, not echoing back whatever was assumed when the profile was first
    sketched out.
- Next: use the population-scale comparison to decide whether any hand-set
  profile buckets should be corrected to match what real transactions would
  actually show (the same fix already applied once, for the payroll-
  eligibility rate — see Session Summary §5), and consider whether the
  Assessment tab's scoring should read balance-timing signals continuously
  (e.g. actual overdraft-day count) rather than through hand-picked buckets,
  since those are the fields hardest to bucket correctly either by hand or
  algorithmically.
