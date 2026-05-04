# Building a Double-Entry Ledger for a Digital Broker

This document is a full walkthrough of how we designed and built a double-entry
bookkeeping ledger for a digital broker. It's written for someone new to
accounting concepts — we use one running example (Alice, a customer) and build
up from the simplest idea to a system that handles deposits, withdrawals,
manual review, failure cases, and a complete chart of accounts driven by
named accounting events.

---

## Part 1 — The concepts, explained from scratch

### Why double-entry?

When money moves, it never appears or disappears — it moves *from* somewhere
*to* somewhere else. A double-entry ledger records both sides of every movement,
so the books always tell a complete story.

Every transaction, no matter how complex, follows one rule:

> **The sum of everything that moves equals zero.**

If 100 EUR arrives at our bank, then *somewhere else* in our books, 100 EUR
worth of obligation must have appeared — we now owe that money to someone.

### Accounts are buckets

An account is a labeled bucket. Accounts come in five categories, which
together make up the **accounting equation**:

```
Assets = Liabilities + Equity + Revenue - Expenses
```

In raw signed-amount terms, that becomes:

```
SUM(direction * amount) across ALL journal entries = 0
```

This is the universal invariant that keeps the system honest.

The five categories:

- **Assets** — things we have (money at the bank, money at the market)
- **Liabilities** — things we owe (money owed to customers, in-flight obligations)
- **Equity** — owner's stake in the business (paid-in capital, retained earnings)
- **Revenue** — money the business earns (fees, interest income)
- **Expenses** — money the business spends (bank fees, operational costs)

### The chart of accounts (4-digit, hierarchical)

We use the standard accounting numbering convention. Top digit identifies the
category, allowing reports to filter by range (e.g. "show me all 4xxx" → all
revenue). The chart has two levels — leaf accounts and rollup parents.

Rollup parents (`is_postable = false`) are structural — they exist only so
queries can sum their children. Real bookings only go to leaves.

```
1xxx  Assets
   1100  Cash & Bank                       (rollup)
      1110  bank.pooling                   (asset)
      1120  bank.treasury                  (asset)
   1150  Suspense (Assets)                 (rollup)
      1151  suspense.deposit.inflight      (asset)
      1160  suspense.treasury_out          (asset)
      1170  suspense.treasury_in           (asset)
   1300  Investments                       (rollup)
      1310  market                         (asset)

2xxx  Liabilities
   2100  Customer Obligations              (rollup)
      2110  customer.viban                 (liability)
   2200  Suspense (Liabilities)            (rollup)
      2210  suspense.withdrawal            (liability)
      2220  suspense.deposit.review        (liability)
      2230  suspense.bounce                (liability)

3xxx  Equity
   3100  retained_earnings                 (equity)
   3200  paid_in_capital                   (equity)

4xxx  Revenue
   4100  fee_income.deposit                (revenue)
   4200  fee_income.withdrawal             (revenue)
   4300  interest_income                   (revenue)

5xxx  Expenses
   5100  bank_fees                         (expense)
   5200  operational_expenses              (expense)
```

Equity, Revenue, and Expense accounts are provisioned but unused at this stage.
They become active when we introduce fees, treasury operations, and similar
business events.

### Direction — the rule to memorize

Every row in the journal has a `direction` that is either `+1` (debit) or
`-1` (credit). The rule, extended to all five account types:

|              | goes UP | goes DOWN |
|--------------|---------|-----------|
| ASSET        | +1      | -1        |
| LIABILITY    | -1      | +1        |
| EQUITY       | -1      | +1        |
| REVENUE      | -1      | +1        |
| EXPENSE      | +1      | -1        |

Liabilities, Equity, and Revenue all behave the same way. Assets and Expenses
behave the same way. To book any movement, ask:

1. Which type is this account?
2. Is it going up or down because of this event?

Look up the answer in the grid. That's the `direction`.

### Signed amounts

`signed = direction × amount`.

Amounts are always stored as positive numbers. Direction tells us whether the
row adds to the balance or subtracts. When you sum all signed values for an
account, you get that account's current balance.

For **assets and expenses**, the raw sum is the natural balance.
For **liabilities, equity, and revenue**, flip the sign: `natural = -SUM(signed)`.

This sign convention is what makes the accounting equation collapse into one
universal check: sum every signed amount in the journal, and it must be zero.

### Three core tables

The ledger has these tables that work together:

- **`accounts`** — the chart of accounts. Static reference data, seeded once.
- **`transactions`** — one row per business event ("Alice deposited 100 EUR").
  Has a status: `Pending`, `Processing`, `Settled`, `Failed`, `Reversed`.
- **`accounting_events`** — one row per "something accountable happened on this
  transaction" (deposit detected, withdrawal initiated, review approved, etc.).
  Append-only.
- **`journal_entries`** — many rows per event, one per account movement.
  Append-only.

The relationship:

```
   transaction (1) ──┬── (n) accounting_events
                     │            │
                     │            └── (n) journal_entries
                     │
                     └── (n) journal_entries (also reachable directly)
```

A transaction has **events** that document its accounting story, and **entries**
that are the bookkeeping rows. Every entry references both the transaction
and the event that produced it. The transaction's `status` is mutable; everything
else is append-only.

If we ever need to undo something, we write **new** journal entries (and a new
event) that reverse the originals. We never edit history.

### Accounting events — the named transitions

An event is a named, well-defined business moment that has a fixed accounting
meaning. The seven events we currently model:

| Event                                  | What happens                                                    |
|----------------------------------------|-----------------------------------------------------------------|
| `deposit.detected.clean_match`         | Money arrived, IBAN matches a known customer → credit them      |
| `deposit.detected.requires_review`     | Money arrived, identity unclear → park in review suspense       |
| `deposit.review.approved`              | Ops approved → release from review to the customer's vIBAN      |
| `withdrawal.initiated`                 | Customer asked to withdraw → debit them, park in suspense       |
| `withdrawal.settled`                   | Bank confirmed sent → clear suspense, debit pooling             |
| `bounce.initiated`                     | Ops rejected a review → move from review suspense to bounce     |
| `bounce.settled`                       | Bank confirmed bounce sent → clear bounce suspense, debit pool  |

Each event has a fixed mapping to journal entries (a "posting rule"). The
mapping is stored in code as structured data — a dictionary of templates.
This separation between *event* (what happened) and *entries* (how it was
booked) is what makes the system maintainable: you can browse the rule book
in one place and see the entire accounting model at a glance.

### The posting engine

The component that converts events into entries is the `PostingEngine`. When
the application code raises an event on a transaction, the engine:

1. Looks up the entry templates for that event type
2. Creates one `JournalEntry` per template
3. Copies `customer_id` from the transaction onto entries that need it (e.g.
   anything touching `customer.viban`)
4. Attaches both the event and the new entries to the transaction
5. Returns — leaving persistence to the caller

Controllers and background jobs no longer construct journal entries directly.
They build a `Transaction`, raise events via the engine, and save changes.
Accounting logic lives in exactly one place.

---

## Part 2 — Alice's full journey

We'll walk through every scenario we modeled, using one customer (Alice) and
tracking every event, journal entry, and balance.

### Alice's info (in our in-memory customer registry)

```
id:   11111111-1111-1111-1111-111111111111
name: Alice Muller
iban: DE89370400440532013000
```

Starting balances: all zero.

---

### Event 1 — Alice deposits 100 EUR (clean, instant SEPA)

Alice transfers 100 EUR from her private IBAN to her vIBAN. The bank statement
shows the incoming transfer. Our polling job reads it, finds Alice by IBAN in
the registry, and raises `deposit.detected.clean_match` on a new transaction.

**Event raised:** `deposit.detected.clean_match`

**Journal entries produced by the rule:**

| tx    | account              | dir | amount | signed |
|-------|----------------------|-----|--------|--------|
| TX-D1 | bank.pooling (1110)  | +1  | 100.00 | +100   |
| TX-D1 | customer.viban (2110)| -1  | 100.00 | -100   |

Applying the rule:
- `bank.pooling` is an **asset going UP** → direction = +1
- `customer.viban` is a **liability going UP** → direction = -1

**Balance check for TX-D1:** `+100 + (-100) = 0` ✓

**Balances after event 1:**
- `bank.pooling` = 100 EUR
- `customer.viban` for Alice = 100 EUR owed

The transaction's status goes straight to `Settled` because a deposit is
observed *after* the money has already physically arrived.

---

### Event 2 — Alice withdraws 30 EUR (instant SEPA, step 1: Processing)

Alice hits our withdrawal endpoint. We immediately raise the initiation event
and instruct the bank to send 30 EUR to her private IBAN.

**Event raised:** `withdrawal.initiated`

**Journal entries:**

| tx    | account                    | dir | amount | signed |
|-------|----------------------------|-----|--------|--------|
| TX-W1 | customer.viban (2110)      | +1  | 30.00  | +30    |
| TX-W1 | suspense.withdrawal (2210) | -1  | 30.00  | -30    |

Applying the rule:
- `customer.viban` is a **liability going DOWN** → direction = +1
- `suspense.withdrawal` is a **liability going UP** → direction = -1

**Why suspense?** The money hasn't left the bank yet. But we've deducted it
from Alice's available balance. It needs to live somewhere on our books during
that window. `suspense.withdrawal` is that holding room.

**Balances after event 2:**
- `bank.pooling` = 100 EUR (unchanged)
- `customer.viban` Alice = 70 EUR owed (Alice sees 70 in her app)
- `suspense.withdrawal` = 30 EUR in flight

---

### Event 3 — Bank confirms the withdrawal (step 2: Settled)

The polling job reads the bank statement seconds later, sees an outgoing transfer
matching TX-W1, and raises the settlement event.

**Event raised:** `withdrawal.settled`

**Journal entries:**

| tx    | account                    | dir | amount | signed |
|-------|----------------------------|-----|--------|--------|
| TX-W1 | suspense.withdrawal (2210) | +1  | 30.00  | +30    |
| TX-W1 | bank.pooling (1110)        | -1  | 30.00  | -30    |

**Transaction TX-W1 now has 2 events and 4 journal entries.** Sum of all signed:
`+30 + (-30) + (+30) + (-30) = 0` ✓.

**Balances after event 3:**
- `bank.pooling` = 70 EUR
- `customer.viban` Alice = 70 EUR owed
- `suspense.withdrawal` = 0

---

### Event 4 — A "faulty" withdrawal of 20 EUR (stuck in Processing)

Failure-case simulation. Alice hits a faulty endpoint that raises `withdrawal.initiated`
but deliberately never tells the bank — simulating a crash between DB write and
bank API call.

**Event raised:** `withdrawal.initiated`. No subsequent settlement event will arrive.

**Journal entries:**

| tx    | account                    | dir | amount | signed |
|-------|----------------------------|-----|--------|--------|
| TX-W2 | customer.viban (2110)      | +1  | 20.00  | +20    |
| TX-W2 | suspense.withdrawal (2210) | -1  | 20.00  | -20    |

The books **still balance** — the transaction sums to zero. But no settlement
event will ever follow. The transaction sits in `Processing` forever.

**Balances after event 4:**
- `bank.pooling` = 70 EUR
- `customer.viban` Alice = 50 EUR owed
- `suspense.withdrawal` = 20 EUR in flight (stuck!)

**Who catches this?** Our **Suspense Aging Monitor** job runs every 15 seconds.
For `suspense.withdrawal`, the SLA for Instant SEPA is 30 seconds. After 30
seconds, the monitor logs a `STUCK IN SUSPENSE` warning. Ops would investigate.

**Two monitoring jobs catch different failure modes:**
- **Reconciliation job** — checks if the math balances (the accounting equation).
  *Passes* here because every transaction is internally balanced.
- **Suspense Aging Monitor** — checks if anything's been stuck in suspense too
  long. *Flags* here because the process has stalled.

The faulty withdrawal is a *process* error, not a *bookkeeping* error.

---

### Event 5 — Alice deposits 150 EUR but name doesn't match (review path)

Assume TX-W2 was cancelled by ops, so Alice is back to 70 EUR owed.

Someone sends 150 EUR to Alice's vIBAN, but the name on the incoming SEPA is
"Alicia Müller" — not exact. Our deposit processing flags this for manual review.

**Event raised:** `deposit.detected.requires_review`

**Journal entries:**

| tx    | account                       | dir | amount | signed |
|-------|-------------------------------|-----|--------|--------|
| TX-D2 | bank.pooling (1110)           | +1  | 150.00 | +150   |
| TX-D2 | suspense.deposit.review (2220)| -1  | 150.00 | -150   |

The transaction has `customer_id = null` (we're honest about not knowing),
`status = Processing`, and `review_reason = NameMismatch`.

**Balances after event 5:**
- `bank.pooling` = 220 EUR (70 + 150)
- `customer.viban` Alice = 70 EUR owed (unchanged)
- `suspense.deposit.review` = 150 EUR unassigned

**Important:** Alice does NOT see this money. The 150 EUR is sitting in review,
waiting for a human decision. The Suspense Aging Monitor watches
`suspense.deposit.review` with a 4-hour SLA.

---

### Branch A — Reviewer approves (confirms it IS Alice)

Operations confirm the name variant is indeed Alice and approve.

**Event raised:** `deposit.review.approved`

**Journal entries:**

| tx    | account                       | dir | amount | signed |
|-------|-------------------------------|-----|--------|--------|
| TX-D2 | suspense.deposit.review (2220)| +1  | 150.00 | +150   |
| TX-D2 | customer.viban (2110)         | -1  | 150.00 | -150   |

The review suspense clears. Alice is credited. Same TX-D2, now with 2 events
and 4 journal entries. Status `Settled`. `customer_id` is set, along with
`reviewed_at` and `reviewed_by`.

**Balances after Branch A:**
- `bank.pooling` = 220 EUR
- `customer.viban` Alice = 220 EUR owed
- `suspense.deposit.review` = 0

---

### Branch B — Reviewer rejects (refund sender)

Alternatively, the reviewer rejects and instructs a bounce.

This is a two-step outbound, just like a withdrawal. The review suspense empties
into a bounce suspense, then the bank confirms the refund.

**Step B1 — Bounce instruction**

**Event raised:** `bounce.initiated`

**Journal entries:**

| tx    | account                       | dir | amount | signed |
|-------|-------------------------------|-----|--------|--------|
| TX-D2 | suspense.deposit.review (2220)| +1  | 150.00 | +150   |
| TX-D2 | suspense.bounce (2230)        | -1  | 150.00 | -150   |

**Step B2 — Bank confirms bounce sent**

**Event raised:** `bounce.settled`

**Journal entries:**

| tx    | account                | dir | amount | signed |
|-------|------------------------|-----|--------|--------|
| TX-D2 | suspense.bounce (2230) | +1  | 150.00 | +150   |
| TX-D2 | bank.pooling (1110)    | -1  | 150.00 | -150   |

Bounce suspense clears. Pooling goes down. Status → `Failed`.

**Balances after Branch B:**
- `bank.pooling` = 70 EUR (back to start)
- `customer.viban` Alice = 70 EUR owed (never changed)
- All suspense accounts = 0

TX-D2 ends up with **3 events and 6 journal entries** across the rejection path.

---

### Why this design is powerful

1. **Books always balance.** The accounting equation collapses to one check:
   `SUM(direction * amount)` across all entries equals zero.
2. **Suspense accounts make failures visible.** Any non-zero suspense balance
   is pending work.
3. **Append-only journal = free audit log.** Time-travel queries fall out
   for free.
4. **Events tell the business story.** Looking at `accounting_events` for a
   transaction tells you what happened in human terms; `journal_entries`
   tells you what was booked.
5. **All accounting rules live in one place** (`PostingRules`). New events
   are a single dictionary entry plus the templates.
6. **New business cases add new events and possibly new accounts**, but the
   math doesn't change.

---

## Part 3 — A quick tour of the code

The implementation is a single ASP.NET Core Web API project:

```
Ledger/
├── Banking/                        - mock bank + statement model
│   ├── BankStatementEntry.cs
│   ├── IMockBank.cs
│   └── MockBank.cs
├── Controllers/
│   ├── ReviewController.cs         - approve/reject endpoints
│   ├── TestingController.cs        - inject fake statements
│   └── WithdrawalsController.cs    - real and faulty withdrawals
├── Customers/                      - in-memory customer registry
│   ├── Customer.cs
│   ├── CustomerRegistry.cs
│   └── ICustomerRegistry.cs
├── Domain/                         - entities + enums
│   ├── Account.cs                  - now with parent + is_postable
│   ├── AccountingEvent.cs          - NEW: events table
│   ├── AccountNumbers.cs           - well-known leaf account constants
│   ├── AccountType.cs              - now 5 categories
│   ├── EventTypes.cs               - NEW: event name constants
│   ├── JournalEntry.cs             - now references EventId
│   ├── ReviewReason.cs
│   ├── SepaType.cs
│   ├── Transaction.cs              - now has Events collection
│   ├── TransactionStatus.cs
│   └── TransactionType.cs
├── Infrastructure/                 - EF Core setup
│   ├── Configurations/
│   │   ├── AccountConfiguration.cs
│   │   ├── AccountingEventConfiguration.cs   - NEW
│   │   ├── JournalEntryConfiguration.cs
│   │   └── TransactionConfiguration.cs
│   ├── ChartOfAccountsSeeder.cs    - now seeds full 4-digit chart
│   └── LedgerDbContext.cs
├── Jobs/                           - background services
│   ├── BankStatementPollingJob.cs  - now raises events
│   ├── ReconciliationJob.cs        - simplified: SUM(signed) = 0
│   └── SuspenseAgingMonitor.cs
├── Posting/                        - NEW: events → entries
│   ├── PostingEngine.cs
│   └── PostingRules.cs             - the rule book
└── Program.cs
```

### The moving pieces

**Domain**
: Plain classes for `Account`, `Transaction`, `AccountingEvent`, and `JournalEntry`.
Constants for accounts (`AccountNumbers`) and event names (`EventTypes`) ensure
no string typos.

**Posting**
: The new heart of the system. `PostingRules` is a static dictionary mapping
event types to entry templates. `PostingEngine.RaiseEvent(tx, eventType)` reads
the dictionary and produces the entries.

**Infrastructure**
: `LedgerDbContext` exposes four `DbSet`s (Accounts, Transactions, Events,
JournalEntries). Configurations are one file per entity. The seeder populates
the full 4-digit chart of accounts with hierarchy.

**Banking**
: Unchanged from before — `MockBank` is the in-memory simulation of the bank.

**Customers**
: Unchanged — in-memory registry seeded with Alice.

**Controllers**
: Now thin. They build a Transaction, call `PostingEngine.RaiseEvent`, save.
They no longer construct journal entries directly.

**Jobs**
:
- `BankStatementPollingJob` — every 5 seconds: matches incoming statements
  to existing transactions (raising settlement events) or creates new deposits
  (raising detection events).
- `SuspenseAgingMonitor` — every 15 seconds: flags transactions with non-zero
  suspense balance older than the bucket's SLA.
- `ReconciliationJob` — every minute: checks `SUM(direction * amount) = 0`
  across all entries (the accounting equation).

### The flows end to end

- **Clean deposit**: testing endpoint → MockBank statement → polling job
  matches by IBAN → raises `deposit.detected.clean_match` → 1 event, 2 entries,
  status Settled.
- **Review deposit**: testing endpoint with `forceReview` → polling job sees no
  match (or forced review) → raises `deposit.detected.requires_review` →
  1 event, 2 entries, status Processing, customer_id null.
- **Withdrawal**: POST `/api/withdrawals` → raises `withdrawal.initiated` →
  1 event, 2 entries → submit to MockBank → polling job later raises
  `withdrawal.settled` → 2nd event, 2 more entries, status Settled.
- **Faulty withdrawal**: POST `/api/withdrawals/faulty` → raises
  `withdrawal.initiated` but never tells MockBank → stuck → flagged by
  Suspense Aging Monitor.
- **Approve review**: POST `/api/reviews/{id}/approve` → raises
  `deposit.review.approved` → 2nd event, 2 more entries, status Settled.
- **Reject review**: POST `/api/reviews/{id}/reject` → raises `bounce.initiated`
  → 2nd event, 2 more entries → submit to MockBank → polling job raises
  `bounce.settled` → 3rd event, 2 more entries, status Failed.

---

## Part 4 — The queries to inspect what's happening

All queries assume the schema is `ledger`.

### Query 1 — Current balances across all accounts

**Purpose:** "What does every bucket hold right now?"

```sql
SELECT
    a.number,
    a.code,
    a.type,
    COALESCE(SUM(je.direction * je.amount), 0) AS signed_balance,
    CASE
        WHEN a.type IN ('Asset', 'Expense')               THEN  COALESCE(SUM(je.direction * je.amount), 0)
        WHEN a.type IN ('Liability', 'Equity', 'Revenue') THEN -COALESCE(SUM(je.direction * je.amount), 0)
    END AS natural_balance
FROM ledger.accounts a
LEFT JOIN ledger.journal_entries je ON je.account_number = a.number
WHERE a.is_postable = true
GROUP BY a.number, a.code, a.type
ORDER BY a.number;
```

**How to read:**
- Filter `is_postable = true` to skip rollup parents (they never have entries).
- `signed_balance` is the raw sum.
- `natural_balance` flips the sign for liabilities, equity, and revenue so they
  read positive in their natural direction.

---

### Query 2 — The accounting equation

**Purpose:** "Do the books add up overall?"

The full accounting equation reduces to one beautifully simple check:

```sql
SELECT SUM(direction * amount) AS drift
FROM ledger.journal_entries;
```

**How to read:** `drift` should always be **exactly 0**. Because asset balances
are positive in raw form and the rest are negative, when books are consistent
they cancel out to zero. If drift isn't zero, the universal invariant is broken.

If you want a breakdown by category to debug a non-zero drift:

```sql
SELECT
    a.type,
    SUM(je.direction * je.amount) AS signed_total
FROM ledger.journal_entries je
JOIN ledger.accounts a ON a.number = je.account_number
GROUP BY a.type
ORDER BY a.type;
```

The grand total of these signed totals must be zero.

---

### Query 3 — Per-transaction balance check

**Purpose:** "Are any transactions broken?"

```sql
SELECT transaction_id, SUM(direction * amount) AS imbalance
FROM ledger.journal_entries
GROUP BY transaction_id
HAVING SUM(direction * amount) <> 0;
```

**How to read:** this should return **zero rows**. Every transaction's
signed amounts must sum to zero. A non-empty result means a bug created an
unbalanced transaction.

---

### Query 4 — Transactions currently in Processing

**Purpose:** "What hasn't settled yet, and how old is it?"

```sql
SELECT id, type, sepa_type, amount, review_reason, created_at,
       NOW() - created_at AS age
FROM ledger.transactions
WHERE status = 'Processing'
ORDER BY created_at;
```

**How to read:** normal Processing transactions clear within seconds (Instant
SEPA), hours (Standard), or pending review (4-hour SLA). Anything older is
either stuck or genuinely awaiting a human decision.

---

### Query 5 — Full audit trail for one transaction (with events)

**Purpose:** "Walk me through everything that happened to this transaction —
the business events AND the bookkeeping rows they produced."

```sql
SELECT
    ae.occurred_at,
    ae.event_type,
    a.code      AS account,
    je.direction,
    je.amount,
    je.direction * je.amount AS signed,
    je.customer_id
FROM ledger.accounting_events ae
JOIN ledger.journal_entries je ON je.event_id = ae.id
JOIN ledger.accounts a         ON a.number    = je.account_number
WHERE ae.transaction_id = '<tx id>'
ORDER BY ae.occurred_at, je.id;
```

**How to read:** rows are grouped by event (chronologically) and within each
event you see the entries. The story reads top-to-bottom: "At 10:30 the
withdrawal was initiated, customer debited, suspense credited. At 10:30:12
the settlement event fired, suspense cleared, pooling debited."

This is the new go-to audit query — it's much richer than just looking at
journal entries because the event names tell you *why* each pair of entries
exists.

---

### Query 6 — All events on a transaction

**Purpose:** "What events have been raised on this transaction?"

```sql
SELECT id, event_type, occurred_at, payload
FROM ledger.accounting_events
WHERE transaction_id = '<tx id>'
ORDER BY occurred_at;
```

**How to read:** returns the timeline of business events. The `payload` JSONB
column contains the context recorded at the moment of the event (counterparty
info, reviewer name, etc.).

---

### Query 7 — Event distribution

**Purpose:** "What types of events have we processed today?"

```sql
SELECT
    event_type,
    COUNT(*) AS count,
    MIN(occurred_at) AS first_seen,
    MAX(occurred_at) AS last_seen
FROM ledger.accounting_events
WHERE occurred_at >= CURRENT_DATE
GROUP BY event_type
ORDER BY count DESC;
```

**How to read:** an operational dashboard query. Spikes in
`deposit.detected.requires_review` mean ops has a backlog. Spikes in
`bounce.initiated` mean lots of rejected deposits — investigate why.

---

### Query 8 — Point-in-time balance (time machine)

**Purpose:** "What was Alice's balance on Monday 9 AM?"

```sql
SELECT -SUM(direction * amount) AS balance_at_point_in_time
FROM ledger.journal_entries
WHERE account_number = 2110
  AND customer_id = '11111111-1111-1111-1111-111111111111'
  AND posted_at <= '2026-04-15 09:00:00+00';
```

**How to read:** same as Query 1 but with a timestamp filter. Because journal
entries are append-only, we can reconstruct the balance at any past moment.
The flip (`-SUM`) is because `customer.viban` is a liability.

---

### Query 9 — Running balance for a customer

**Purpose:** "Show Alice's balance changing over time."

```sql
SELECT
    je.posted_at,
    ae.event_type,
    t.status,
    je.direction,
    je.amount,
    -SUM(je.direction * je.amount) OVER (
        ORDER BY je.posted_at, je.id
    ) AS running_balance
FROM ledger.journal_entries je
JOIN ledger.transactions t      ON t.id  = je.transaction_id
JOIN ledger.accounting_events ae ON ae.id = je.event_id
WHERE je.account_number = 2110
  AND je.customer_id   = '11111111-1111-1111-1111-111111111111'
ORDER BY je.posted_at, je.id;
```

**How to read:** each row shows a journal entry on Alice's vIBAN, the event
that produced it, and the running balance after that entry. `SUM(...) OVER (...)`
is a window function that computes the cumulative sum.

---

### Query 10 — End-of-day balance snapshots

**Purpose:** "Show Alice's end-of-day balance for every day in April."

```sql
WITH days AS (
    SELECT generate_series(
        DATE '2026-04-01',
        DATE '2026-04-30',
        INTERVAL '1 day'
    )::date AS day
)
SELECT
    d.day,
    COALESCE(-SUM(je.direction * je.amount), 0) AS balance_eod
FROM days d
LEFT JOIN ledger.journal_entries je
       ON je.account_number = 2110
      AND je.customer_id   = '11111111-1111-1111-1111-111111111111'
      AND je.posted_at < d.day + INTERVAL '1 day'
GROUP BY d.day
ORDER BY d.day;
```

**How to read:** `generate_series` creates one row per day. For each, sum
everything posted up to end-of-day. Useful for charts and monthly statements.

---

### Query 11 — Customer transaction statement

**Purpose:** "Every transaction on Alice's account in a date range."

```sql
SELECT
    t.id,
    t.type,
    t.status,
    t.amount,
    t.counterparty_name,
    t.created_at,
    t.updated_at
FROM ledger.transactions t
WHERE t.customer_id  = '11111111-1111-1111-1111-111111111111'
  AND t.created_at  >= '2026-04-01'
  AND t.created_at  <  '2026-05-01'
ORDER BY t.created_at;
```

**How to read:** uses the `transactions` table for one row per business event.
Join to `accounting_events` if you want the full event story per transaction.

---

### Query 12 — Time-in-state analytics

**Purpose:** "How fast do our withdrawals settle? Which reviews dragged on?"

Average withdrawal settlement time:
```sql
SELECT
    AVG(EXTRACT(EPOCH FROM (updated_at - created_at))) AS avg_seconds_to_settle,
    MIN(updated_at - created_at) AS fastest,
    MAX(updated_at - created_at) AS slowest,
    COUNT(*) AS settled_count
FROM ledger.transactions
WHERE type = 'Withdrawal'
  AND status = 'Settled'
  AND created_at >= NOW() - INTERVAL '30 days';
```

Longest reviews:
```sql
SELECT id, amount, review_reason, counterparty_name,
       created_at, reviewed_at,
       reviewed_at - created_at AS time_in_review
FROM ledger.transactions
WHERE review_reason IS NOT NULL
  AND reviewed_at IS NOT NULL
ORDER BY time_in_review DESC
LIMIT 20;
```

---

### Query 13 — Period delta

**Purpose:** "What changed in each account between two timestamps?"

```sql
SELECT
    a.code,
    a.type,
    SUM(je.direction * je.amount) AS change_over_period
FROM ledger.journal_entries je
JOIN ledger.accounts a ON a.number = je.account_number
WHERE je.posted_at >  '2026-04-10 23:59:59+00'
  AND je.posted_at <= '2026-04-13 23:59:59+00'
GROUP BY a.code, a.type
HAVING SUM(je.direction * je.amount) <> 0
ORDER BY a.code;
```

**How to read:** filter to a window, group by account. Useful for "what
happened over the weekend" type questions.

---

### Query 14 — Stuck suspense (process-error detector)

**Purpose:** "What's been sitting in suspense too long?"

```sql
WITH suspense_aging AS (
    SELECT
        je.transaction_id,
        a.code AS suspense_account,
        SUM(je.direction * je.amount) AS net_in_suspense,
        MIN(je.posted_at)              AS oldest_entry,
        NOW() - MIN(je.posted_at)      AS age
    FROM ledger.journal_entries je
    JOIN ledger.accounts a ON a.number = je.account_number
    WHERE a.code LIKE 'suspense.%'
    GROUP BY je.transaction_id, a.code
    HAVING SUM(je.direction * je.amount) <> 0
)
SELECT *
FROM suspense_aging
ORDER BY age DESC;
```

**How to read:** any transaction with a non-zero balance in any suspense
account is in-flight. The `age` column tells you how long. Cross-reference
with the SuspenseAgingMonitor SLAs (30s for withdrawal, 4h for review,
5min for bounce).

---

### Query 15 — Account hierarchy rollup

**Purpose:** "Show me totals at every level of the chart."

```sql
WITH RECURSIVE account_tree AS (
    SELECT number, parent_number, number AS root_number
    FROM ledger.accounts
    WHERE parent_number IS NULL

    UNION ALL

    SELECT a.number, a.parent_number, t.root_number
    FROM ledger.accounts a
    JOIN account_tree t ON a.parent_number = t.number
)
SELECT
    parent.number      AS parent_number,
    parent.code        AS parent_code,
    parent.name        AS parent_name,
    COALESCE(SUM(je.direction * je.amount), 0) AS signed_total
FROM ledger.accounts parent
JOIN account_tree     descendant ON descendant.root_number = parent.number
LEFT JOIN ledger.journal_entries je ON je.account_number = descendant.number
WHERE parent.is_postable = false
GROUP BY parent.number, parent.code, parent.name
ORDER BY parent.number;
```

**How to read:** sums all leaf entries under each rollup parent. So
`rollup.cash_and_bank` shows the combined balance of `bank.pooling` plus
`bank.treasury`. Good for top-level financial reports.

---

### Query 16 — Balance Sheet (categorical totals)

**Purpose:** "Summary by major category — the building blocks of a balance sheet."

```sql
SELECT
    a.type,
    COUNT(DISTINCT a.number) FILTER (WHERE je.id IS NOT NULL) AS accounts_with_activity,
    COALESCE(SUM(je.direction * je.amount), 0) AS signed_total,
    CASE
        WHEN a.type IN ('Asset', 'Expense')               THEN  COALESCE(SUM(je.direction * je.amount), 0)
        WHEN a.type IN ('Liability', 'Equity', 'Revenue') THEN -COALESCE(SUM(je.direction * je.amount), 0)
    END AS natural_total
FROM ledger.accounts a
LEFT JOIN ledger.journal_entries je ON je.account_number = a.number
WHERE a.is_postable = true
GROUP BY a.type
ORDER BY a.type;
```

**How to read:** one row per category. Sum of `signed_total` across all rows
must be zero (the accounting equation). The `natural_total` column is what
you'd put on a balance sheet — assets and expenses positive, others flipped.

---

### Query 17 — Per-customer balances summary

**Purpose:** "What does every customer's vIBAN hold?"

```sql
SELECT
    je.customer_id,
    -SUM(je.direction * je.amount) AS balance_owed
FROM ledger.journal_entries je
WHERE je.account_number = 2110
  AND je.customer_id IS NOT NULL
GROUP BY je.customer_id
HAVING -SUM(je.direction * je.amount) <> 0
ORDER BY balance_owed DESC;
```

**How to read:** lists every customer with a non-zero vIBAN balance. If a
customer's `customer_id` is missing on a row, they won't appear here — that's
the data integrity bug we caught in the "5 EUR mystery" scenario. The total
of this query plus any unattributed (`customer_id IS NULL`) rows should
equal the total `customer.viban` balance from Query 1.

---

### Query 18 — Events without entries (sanity check)

**Purpose:** "Did any event fail to produce its entries?"

```sql
SELECT ae.id, ae.event_type, ae.occurred_at, ae.transaction_id
FROM ledger.accounting_events ae
LEFT JOIN ledger.journal_entries je ON je.event_id = ae.id
WHERE je.id IS NULL;
```

**How to read:** should return **zero rows**. Every event must produce at
least one journal entry. A non-empty result indicates a bug in the posting
engine or a partial commit.

---

## Key takeaways

- **Direction is just a number (+1 or -1).** Signed amount is `direction × amount`.
  Balance is the sum of signed amounts. Everything flows from that.

- **Every transaction sums to zero.** If it doesn't, the books are broken.

- **The accounting equation reduces to one check:**
  `SUM(direction * amount)` across the whole journal equals zero.

- **Events carry the business meaning; entries carry the bookkeeping.**
  Together they tell the full story.

- **Posting rules live in one place** (the `PostingRules` dictionary). Every
  event type maps to a fixed set of entry templates.

- **Suspense accounts are diagnostic tools.** Non-zero suspense = pending work.

- **Append-only journal = free audit log.** Time-travel queries fall out
  for free.

- **Two kinds of anomaly checks:**
  - Reconciliation = "do the books balance?" (math)
  - Aging monitors = "has anything been stuck too long?" (process)

- **New business scenarios add new events and possibly accounts.** The math
  and the architecture don't change.