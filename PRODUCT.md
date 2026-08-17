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

  **4. Upload a real bank statement (PDF) — rebuilt after testing against
  an actual statement.** The first version (one line = one transaction,
  single signed amount) missed income entirely on a real ANZ statement the
  user supplied for testing. Root cause, found by reading the actual PDF
  content rather than guessing: a single transaction commonly wraps across
  up to three physical lines (date+description, a card/reference
  continuation line, then the amount) — that alone was enough to drop most
  transactions into "unparsed." On top of that, statements typically use
  separate Withdrawals / Deposits / Balance columns rather than one signed
  amount, and that distinction is column *position*, which plain linear
  text extraction throws away.
  - Text extraction now preserves each text item's X position. Rows
    between one date-starting line and the next are grouped into a single
    record regardless of how many lines it wraps across (capped at 3
    continuation lines, and explicitly stopped by "Totals at end of page" /
    "Balance brought forward" / legend text, so page-footer totals don't
    get swallowed into the last transaction on the page).
  - Amount columns are identified by clustering X positions across the
    whole statement: three clusters → Withdrawals / Deposits / Balance
    (left to right, matching the header order every AU/NZ statement in
    testing has used); two → Amount / Balance; fewer than that → falls
    back to the old sign-guessing heuristic (DR/CR suffix, default debit)
    with a visible warning in the UI to check every row's sign.
  - Added a small legend of standard-ish AU/NZ transaction-type codes
    (DC/BP/AP/DD/AT/EP/VT/CQ/ED/FX/IP/IF/IA — e.g. DC = Direct Credit, AT =
    Automatic Teller Machine) used as a sign/category fallback *only* when
    no merchant keyword matches — never overriding a specific merchant.
  - If every reviewed row carries a real extracted balance, that's used
    directly instead of recomputing a running sum from $0 — correct for a
    revolving-credit/overdraft-style account that starts already drawn
    (the ANZ test statement was exactly this: a home-loan redraw facility
    with an "OD" balance throughout, which the user confirmed should be
    treated as representative rather than set aside as an edge case).
  - Categorization refinements, decided with the user against real
    transaction descriptions rather than assumed: a casino's own bars and
    food outlets (e.g. "SkyCity Flare Bar", "SkyCity Food Republic")
    categorize by what was actually bought (alcohol/eating-out, via new
    generic `\bBAR\b` / `\bFOOD\b` / bakery/kitchen/sushi keywords), while
    cashier/cage draws and casino-located ATM withdrawals ("SKYCITY CASHIER
    MAIN", "ACU SCC1004 Sky City...") count as gambling directly — a
    real-money distinction a generic "contains casino name" rule would
    have missed either direction.
  - Re-verified against a hand-built simulation of the real statement's
    layout (can't execute pdf.js itself in this sandboxed session — no
    network access to the CDN — so validated the extraction logic against
    realistic row/column data instead of a live render): income is now
    detected, casino cashier/ATM draws land in gambling, casino food/bar
    outlets land in eating-out/alcohol, ordinary (non-casino) ATM
    withdrawals fall back to cash_withdrawal via the type-code legend, and
    "Opening balance" and page-footer totals are correctly excluded rather
    than mis-parsed as transactions. **Still needs a real end-to-end run in
    an actual browser with internet access** to confirm the pdf.js
    extraction itself behaves as assumed — the row/column logic was
    validated, the live PDF→text step wasn't.
  **5. Merchant-level categorization walked through line by line against
  the real ANZ statement**, resolved with the user rather than guessed:
  - **Three new categories** added: Vape/Tobacco (flagged like alcohol/
    gambling — a discretionary, habit-forming spend worth its own line, not
    buried in generic retail), Pharmacy/Health, Travel/Accommodation.
  - **Mortgage repayments** ("LOAN PAYMENT" to the applicant's own home-loan
    sub-accounts) count as a housing cost, grouped with Rent — not as
    existing-debt use, since it's the cost of keeping the roof over their
    head, not debt-stacking.
  - **Council/regional rates** group with Bills & Utilities. **Loan
    interest** groups with bank/account fees (both are the cost of holding
    the account, not spending).
  - **"Hotel"/"Motel" reads as accommodation** here — flagged in the code
    that in AU/NZ this word often just means a pub, so re-check per
    statement rather than assume.
  - Op-shop-style purchases at community/charity orgs (St John, social
    services, surf life saving clubs) count as ordinary retail spending,
    not donations — confirmed with the user rather than assumed either way.
  - Merchant keyword coverage went NZ-aware in the process (New World, Four
    Square, Pak'nSave for groceries; AA Insurance; Mercury Energy) since
    the only real statement tested so far was a New Zealand account — the
    product's actual applicants are Australian, so this is incidental
    breadth, not a deliberate NZ expansion.
  - One thing intentionally left unresolved rather than guessed: "BP M D
    CHATTERTON BILL PAYMENT" (a bill payment where the payee is literally
    the account holder's own name) is left uncategorized since it's unclear
    whether it's a transfer to another of their own accounts or something
    else. Also unresolved: a long tail of small, ambiguously-named local
    merchants (cafes, takeaway spots) that don't match any keyword and fall
    through to "other" — true of any keyword-based categorizer against
    small independent businesses, not something to chase merchant-by-
    merchant.

  **6. Income detection now merges an employer that posts under more than
  one description.** The user confirmed "Spida Machin SM2012 Ltd" and
  "SM2012 Limited Salary" are the same employer — the smaller, irregular
  "Spida Machin" credits are expense reimbursements, not a second wage. Two
  fixes, both general (not hardcoded to this one payer name):
  - **Same-payer merging**: wage credits are still grouped by exact
    description first, then groups sharing a reference token (a
    company/payroll code like "SM2012", extracted via
    `/\b[A-Z]{1,6}\d{2,6}\b/`) get merged via union-find. Before this fix,
    the applicant would have read as `source: multiple` (two employers)
    instead of `single` (one employer, one of its payment types just uses
    different description text). Coincidental token matches between two
    genuinely different payers are a real (if rare) false-merge risk — the
    alternative of never merging was the worse, more common failure, since
    it's exactly what broke this real applicant's read.
  - **Reimbursement exclusion**: within a merged payer's credits, a
    "primary" cluster (amounts within 0.4x–2.5x of the group's median) is
    used for the actual income amount/frequency/trend figures; smaller or
    larger one-off credits from the same payer are recognized as belonging
    to that employer (for `source`/`payerConsistency`) but excluded from
    the salary calculation itself, so they no longer dilute or distort it.
  - Verified: a synthetic version of this applicant's pattern (salary
    credits + interleaved reimbursement credits, same reference token) now
    reads `source: single`, `payerConsistency: same`, and an income figure
    matching the salary cluster only. Re-ran the full 100-applicant
    validation afterward — 58.4% overall match, unchanged from before this
    fix, confirming it's additive (none of the generated statements happen
    to have this multi-description-per-employer pattern, so nothing
    regressed).

  **7. Self-transfers to the account holder's own name.** "BP M D
  CHATTERTON BILL PAYMENT" — a bill payment where the payee is literally
  the account holder's own name — turned out to be a transfer to their
  savings account, confirmed by the user, who called it a cash withdrawal
  (money leaving the spending account, not third-party spend). Built as a
  general detector, not hardcoded to this name: `detectAccountHolderName`
  reads the statement's own "Account name" field (a fairly standard label),
  falling back to an addressee-style line near the top of the document;
  `isSelfTransferDescription` then checks whether a debit's payee text
  substantially overlaps that name (surname plus at least one other token,
  to avoid a coincidental partial match). Only applies to debits, and only
  as a categorization fallback under the keyword rules — never overrides a
  specific merchant match. Surfaced transparently in the PDF review screen
  ("Account holder detected as ... — a debit paid out to that name is
  treated as ...") rather than applied silently. Verified against a
  synthetic reproduction (correctly flags the self-payment, correctly
  leaves an unrelated same-type "BP sapori BILL PAYMENT" alone) and
  re-ran the full 100-applicant validation (58.4%, unchanged — no
  simulated applicant has this pattern, so nothing regressed).
- Next: use the population-scale comparison to decide whether any hand-set
  profile buckets should be corrected to match what real transactions would
  actually show (the same fix already applied once, for the payroll-
  eligibility rate — see Session Summary §5); decide the fate of the
  overdraft dial now that it's structurally always "0"; and get a couple of
  real (anonymized/redacted) Australian bank statement PDFs to test the
  upload parser against actual local bank formats.

  **8. The remaining unrecognized merchants, resolved one by one against
  the real statement.** Ran every distinct merchant description through
  the categorizer as it stood, found 26 still landing in "other" (the
  user's own count of "39" was almost certainly line-item occurrences —
  several of these repeat), and went through them individually rather than
  guessing silently. Also caught and fixed one bug while building the
  list: "The Warehous" (truncated, missing the final E — the same
  fixed-length card-description truncation seen earlier with "Google
  Claud") wasn't matching the "THE WAREHOUSE" keyword; now matches with or
  without the trailing E.
  - Most matched the user's own instinct ("most small purchases are
    obviously eating out or alcohol") — added as keywords: eating out
    (Little Honey, Buns N Rolls, Yumbunmee, Bench Noodle, The Chilli
    House, Scotts Epicurean, Mexico Hamilton, An An, Greedy Cat, Coffix,
    Saint Alice, Buster Crabb, Slice Slice Baby, Stallions Cambridge);
    groceries (Wyllie Road [Superette], Vege Heaven, D & N Mart); retail
    (Fade Away Barber Shop, Hudsons airport newsstand).
  - Four corrected specific guesses, confirmed against local knowledge the
    categorizer couldn't have had from the text alone: "The Whitianga
    Hotel" is a pub → alcohol, added as a specific exception checked
    *before* the generic `\bHOTEL\b` → travel/accommodation rule (the same
    pattern already used for casino bars vs. the generic gambling
    catch-all). "sapori BILL PAYMENT" is a wine order despite the unusual
    payment method → alcohol. "INVERCARGILL AIRPORT" is airport food, not
    a travel/accommodation charge → eating out. "GLASSHOUSE" is a discount
    retailer (like a Reject Shop), not a cafe → retail/shopping, overriding
    my eating-out-shaped guess.
  - One left genuinely unresolved: "KS" — no signal in the description at
    all, and the user didn't know either. Stays in "other".
  - Verified all 26 (now 25 resolved + 1 intentionally left as "other")
    against the categorizer directly, and re-ran the full 100-applicant
    validation afterward (58.4%, unchanged — these are all real-world
    merchant names that don't appear in the simulated data, so nothing
    regressed).

  **9. Two more fixes from a second look at the "other" bucket** — one a
  real bug, one a deliberate false-positive the user caught by name:
  - **Self-transfer detector was too loose.** "lmkb chatterton whitianga 4
    poplar st" — a real payment to a tradesperson (builder/electrician,
    "lmkb") — was misread as a self-transfer, because the payer's own name
    and address were used as the *payment reference* (a common bill-payment
    convention) and the detector was doing a substring search anywhere in
    the description, including single-letter initial tokens like "M" that
    match almost any text by coincidence. Fixed by requiring the account
    holder's name to *lead* the description (checked against a window sized
    to the name, using whole-token equality, not substring search) — true
    self-payments like "M D CHATTERTON BILL PAYMENT" still match, reference
    notes tacked onto a real third-party payment no longer do. The user
    confirmed "lmkb" is a genuine renovation-related payment and asked for
    it to stay "other" rather than be recategorized further — done.
  - **Small unrecognized debits now default to Eating Out.** Once bills,
    rent, loans, and the other keyword-covered categories are accounted
    for, what's actually left over in "other" on a real statement is
    overwhelmingly everyday food spend — confirmed against what was
    genuinely still sitting there, not assumed in advance. Any debit under
    $80 that doesn't match a merchant keyword or type-code hint now falls
    back to Eating Out instead of "other". Won't always be right (small
    transport/parking/vending charges exist too), but is right far more
    often than leaving it uncategorized. Purely a categorized-ledger/
    display change — none of the scoring-relevant derived fields
    (buffer/overdraft/dishonours/otherRepayments/etc.) key off "other" or
    "eating_out" specifically, so this has zero effect on the Assessment
    tab's inputs.
  - Verified both against the exact "lmkb" vs. genuine self-payment pair,
    and re-ran the full 100-applicant validation (58.4%, unchanged, as
    expected since the small-amount fallback doesn't touch any scored
    field).

  **10. Simplified the everyday-spend taxonomy; redefined "Other".** The
  user's read after two rounds of merchant-by-merchant precision tuning:
  splitting groceries/eating out/alcohol/retail-shopping into four separate
  buckets was more precision than the product needs — none of it is
  scoring-relevant, and it was pulling attention away from the transactions
  that actually matter for an approve/decline call. Instruction: "we dont
  need to be exact about eating out alcohol grocery etc. its all just
  retail purchaes. all we want in other are big questionable outgoings.
  whether theyre regualr or one off."
  - Merged all four `SPEND_CATEGORY_RULES` entries (groceries, eating out,
    alcohol, retail/shopping) into one `retail_purchases` category —
    "Retail Purchases". Their individual keyword regexes are untouched
    (still separate rule entries for maintainability), only the output
    `category`/`label` changed, so every merchant already resolved in step
    8 (New World, Woolworths, McDonald's, BWS, Kmart, The Warehouse, the
    Whitianga Hotel, etc.) still matches — just lands in one bucket instead
    of four.
  - Updated `CATEGORY_LABELS` and `CATEGORY_ORDER` to drop the four old
    IDs and use `retail_purchases` once.
  - Redefined the unrecognized-small-debit fallback (step 9) to match: it
    now targets `retail_purchases` instead of `eating_out`, and the amount
    ceiling was raised from $80 to $150 — because "Other" is no longer a
    generic catch-all for anything unmatched, it's specifically meant to
    flag the big/questionable outgoings worth a human's attention (one-off
    *or* recurring), so routine spend needs a wider net before it's swept
    into ordinary retail rather than left sitting in "Other" unexamined.
  - Confirmed via grep that none of the scoring derivers
    (`deriveIncomeProfile`, `deriveAccountConduct`, `deriveExistingCreditUse`,
    `deriveSurplus`) reference any of the four old category IDs or
    `retail_purchases` — this is purely a categorized-ledger display
    simplification, zero effect on Assessment-tab inputs.
  - Verified: `node --check` on the extracted script, full 100-applicant
    validation (58.4%, unchanged as expected), and the Playwright
    categorized-ledger smoke test (group count dropped from the prior run
    as the four categories collapsed into one, balances still never go
    negative, no bad ATM amounts, PDF upload UI still present).

  **11. Vape/tobacco folded into Retail Purchases; new Cash Flow panel
  (regular income, ATM in vs. out, totals, a graph over time).** Two asks
  in one message: "vape is retail purchase" (finish the taxonomy
  simplification — vape/tobacco was the one flagged category left over
  from before the "everything routine is retail purchases" call), and "now
  categorise income. i need to see regular salary wage. the atm deposits
  compared to atm withdrawls (the might cross each other out) and totals
  of money in and money out and a graph over time showing both."
  - `vape_tobacco` removed from `SPEND_CATEGORY_RULES`/`CATEGORY_STYLE`/
    `CATEGORY_LABELS`/`CATEGORY_ORDER` the same way groceries/eating-out/
    alcohol/retail-shopping were — same keywords, output category changed
    to `retail_purchases`. It's no longer flagged red as a risk category on
    the ledger.
  - **New `cash_deposit` category**, checked on the credit side of
    `categorizeTransaction` (income detection only ever looked at debits
    for `SPEND_CATEGORY_RULES`, so this needed its own check in the
    `amount > 0` branch, not a rules-array entry). Catches ATM/branch/
    counter cash deposits and — importantly — excludes them from
    `income_credit`, so they no longer get treated as verified wage income
    by `deriveIncomeProfile`. This matches how a real lender would treat
    it: a named payer is a verifiable income source, a cash deposit isn't.
    Checking the actual simulated data turned up 36 "CASH DEPOSIT" credits
    across several "casual"-archetype applicants (used by the generator as
    a stand-in for cash-in-hand income) — previously these were counted
    toward `income_credit` and could form a fake "recurring payer" purely
    from repeated `$CASH DEPOSIT` descriptions; now they're correctly kept
    out of the income calculation instead. Re-ran the 100-applicant
    validation after this and the result was unchanged (58.4%, identical
    per-field breakdown) — the affected applicants' derived fields didn't
    flip, so this is a genuine accuracy fix with no measured downside.
  - **New "Cash flow" panel** on both the simulated Bank Statement tab and
    the real-PDF-upload result section (`computeCashFlow` +
    `cashFlowSummaryHTML` + `renderCashFlowChart`, shared by both call
    sites the same way `categorizedLedgerHTML` already was):
    - *Regular income*: the primary recurring payer's name, amount per
      pay, frequency, and payer/timing consistency — pulled straight from
      `deriveIncomeProfile` instead of only showing up buried in the
      hand-set-vs-derived compare table. Other recurring payers (if any)
      listed underneath.
    - *ATM/cash deposits vs. withdrawals*: totals side by side plus the
      net, with an explicit note that cash cycled in and back out can
      offset itself rather than reading as two separate events — directly
      addressing "the might cross each other out."
    - *Total money in vs. money out*: sum of every credit vs. every debit
      over the statement, plus net.
    - *Graph over time*: reused the existing `drawLineChart` SVG utility
      (already powering the multi-year simulation charts) rather than
      pulling in a charting library — transactions bucketed into weeks,
      plotting "Money in" (green) against "Money out" (rust) per week
      across the statement period.
  - Verified with `node --check`, a direct unit test of `categorizeTransaction`
    against `VAPE SHOP WHITIANGA` (→ retail_purchases), `ATM DEPOSIT BRANCH`
    / `CASH DEPOSIT` (→ cash_deposit), and `ATM WITHDRAWAL CBA` (→ still
    cash_withdrawal, unaffected), the 100-applicant validation (58.4%,
    unchanged), and a Playwright check across three applicants confirming
    the chart renders with the correct two-line legend and the summary
    panel's numbers populate (including one applicant with real "Cash
    Deposit" rows in its ledger, confirmed excluded from the income figure
    and correctly reflected in the ATM deposits total instead).

  **12. "PBKA" recognized as an ATM cash deposit.** On a real statement, an
  ATM cash deposit doesn't necessarily contain the word "deposit" at all —
  ANZ NZ's own transaction-type prefix for one is "PBKA", which the
  `CASH_DEPOSIT_RE` keyword list from step 11 had no way to catch. The user
  confirmed directly: "all the PBKA... deposits are atm cash deposits."
  Added `\bPBKA` to the regex. Verified with a direct unit test
  (`categorizeTransaction("PBKA123456 DEPOSIT", 200)` → `cash_deposit`,
  and confirmed a real wage payer like "SPIDA MACHIN SM2012 LTD" still
  correctly resolves to `income_credit`, unaffected) and the 100-applicant
  validation (58.4%, unchanged — no simulated applicant's data contains
  "PBKA", this is specific to the real statement).

  **13. Gambling withdrawals folded into the cash-out total.** Ask: "show
  gambling withdrawls alongside and totaled with atm withdrawls affecting
  net cash movement." The reasoning: money spent at a casino cashier/cage,
  TAB, pokies venue, or betting app is cash leaving the account the same
  way an ATM withdrawal is — it shouldn't be able to hide inside "spend by
  category" while the Cash Flow panel's "net cash movement" figure only
  accounts for ATM withdrawals.
  - `computeCashFlow` now also totals `gambling`-category debits
    (`gamblingWithdrawalTotal`) and exposes a combined `cashOutTotal`
    (ATM/cash withdrawals + gambling withdrawals).
  - `cashFlowSummaryHTML`'s "ATM / cash deposits vs. withdrawals" section
    now shows ATM withdrawals and gambling withdrawals as separate stat
    cards *alongside* a combined "Total cash out (ATM + gambling)" card,
    and "Net cash movement" is computed against that combined total
    (deposits − (ATM withdrawals + gambling withdrawals)) instead of ATM
    withdrawals alone.
  - Verified against applicant 5 (Olivia Adams, two BET365 transactions
    totaling $112 in the ledger): ATM withdrawals $560 + gambling $112 =
    total cash out $672, matching exactly, with net cash movement showing
    -$672 given $0 in cash deposits for that applicant. `node --check`
    passed and the 100-applicant validation stayed at 58.4% (unchanged, as
    expected — this only changes what's displayed in the Cash Flow panel,
    not any scoring-relevant derived field).

  **14. "04 BILL PAYMENT" recognized as a cash deposit; fixed a real
  self-transfer misclassification bug found while testing it.** User:
  "04 bill payment is a cash deposit, not income" — another real
  transaction-type prefix (like "PBKA" in step 12) added to
  `CASH_DEPOSIT_RE`, scoped to the credit side only so it can't collide
  with genuine debit-side bill payments.
  - While unit-testing this against the earlier-resolved "BP M D
    CHATTERTON BILL PAYMENT" self-transfer example (step 9), found that it
    was actually still being shown as **Transport**, not **Cash
    Withdrawal** as confirmed back then — a real bug, not a hypothetical.
    Cause: `categorizeTransaction` always checks `SPEND_CATEGORY_RULES`
    (the merchant-keyword rules) before ever consulting `categoryHint`,
    and the transport rule's `\bBP\b` keyword (for BP fuel stations) also
    matches the bank's own "BP" = Bill Payment transaction-type prefix —
    so the self-transfer detector's verdict was silently getting
    overridden by a coincidental keyword collision every time.
  - The underlying issue: `categoryHint` was one flat string used for two
    very different kinds of signal — a soft, often-wrong bank type-code
    guess (correctly checked *after* keywords, e.g. "DD" → bills_utilities)
    and the self-transfer detector's verdict, which is a definitive,
    structural match (the account holder's own name literally leading the
    description) that deserves to win over a keyword collision. Fixed by
    giving self-transfer its own sentinel (`categoryHint = "self_transfer"`
    instead of directly "cash_withdrawal") and checking it in
    `categorizeTransaction` *before* the keyword-rules loop, while leaving
    every other (soft) categoryHint use checked after, unchanged.
  - Verified directly: `categorizeTransaction("BP M D CHATTERTON BILL
    PAYMENT", -300, "self_transfer")` now correctly returns
    `cash_withdrawal`; a genuine `"BP FUEL"` debit with no hint still
    correctly returns `transport` (the fix doesn't touch real fuel-station
    transactions, only the specific collision); `"04 BILL PAYMENT"` credit
    returns `cash_deposit`; a real wage payer is unaffected. `node --check`
    passed, the 100-applicant validation stayed at 58.4% (unchanged — no
    simulated applicant has this exact collision), and the Playwright
    smoke test showed no new errors.

  **15. Self-transfer detection extended to credits.** User: "m d
  chatterton bill payment $3122 is a cash deposit (not income) make this
  change happen." Step 9 only ever checked self-transfer on the debit side
  (money leaving to the holder's own name → cash withdrawal); the same
  structural signal on the credit side — "M D CHATTERTON BILL PAYMENT" as
  a *credit* — means the holder's own money is coming back in, not a real
  payer paying them, so it can't count as verified income either.
  - `isSelfTransferDescription` is purely text-based (checks whether the
    description leads with the account holder's own name) and never
    actually depended on transaction direction — the `amount < 0` guard in
    `recordToTransaction` was the only thing limiting it to debits. Removed
    that guard so the `"self_transfer"` categoryHint gets set for both
    directions.
  - `categorizeTransaction`'s credit branch now checks
    `categoryHint === "self_transfer"` first (mirroring the debit-side
    check added in step 14) and returns `cash_deposit` — checked ahead of
    `CASH_DEPOSIT_RE` and the income regexes, same priority reasoning as
    before: a structural name-match is definitive, not a soft guess.
  - Verified directly: `categorizeTransaction("M D CHATTERTON BILL
    PAYMENT", 3122, "self_transfer")` → `cash_deposit`; the debit-side
    self-transfer and plain "BP FUEL" cases from step 14 still resolve
    correctly (untouched); a real wage payer still resolves to
    `income_credit`. `node --check` passed, the 100-applicant validation
    stayed at 58.4% (unchanged — self-transfer detection only runs in the
    real-PDF-upload pipeline, not on the simulated applicants), and the
    Playwright smoke test showed no new errors.
