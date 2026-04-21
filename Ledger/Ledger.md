# Building a Double-Entry Ledger for a Digital Broker

This document is a full walkthrough of how we designed and built a double-entry
bookkeeping ledger for a digital broker. It's written for someone new to
accounting concepts — we use one running example (Alice, a customer) and build
up from the simplest idea to a system that handles deposits, withdrawals,
manual review, and failure cases.

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

An account is just a labeled bucket. We track:

- **Assets** — things we have (money at the bank, money at the market)
- **Liabilities** — things we owe (money owed to customers, obligations in flight)

Our chart of accounts has 10 buckets:

| Number | Code                         | Type      | Meaning                                 |
|--------|------------------------------|-----------|-----------------------------------------|
| 110    | `bank.pooling`               | Asset     | Real money at the bank for customers    |
| 120    | `bank.treasury`              | Asset     | Our own money at the bank               |
| 130    | `market`                     | Asset     | Money deployed to market                |
| 150    | `suspense.deposit.inflight`  | Asset     | Deposit notified but not yet settled    |
| 160    | `suspense.treasury_out`      | Asset     | Treasury → Market in flight             |
| 170    | `suspense.treasury_in`       | Asset     | Market → Treasury in flight             |
| 210    | `customer.viban`             | Liability | What we owe customers (per-customer)    |
| 220    | `suspense.withdrawal`        | Liability | Customer withdrawal in flight           |
| 230    | `suspense.deposit.review`    | Liability | Deposit awaiting human review           |
| 240    | `suspense.bounce`            | Liability | Rejected deposit being refunded         |

### Direction — the one rule to memorize

Every row in the journal has a `direction` that is either `+1` (debit) or
`-1` (credit). The rule for choosing it:

|            | goes UP | goes DOWN |
|------------|---------|-----------|
| ASSET      | +1      | -1        |
| LIABILITY  | -1      | +1        |

To book any movement, ask two questions:

1. Is this account an asset or a liability?
2. Is it going up or down because of this event?

Look up the answer in the grid. That's the `direction`.

### Signed amounts

`signed = direction × amount`.

Amounts are always stored as positive numbers. Direction tells us whether the
row adds to the balance or subtracts. When you sum all signed values for an
account, you get that account's current balance.

For **assets**, the raw sum is the real balance.
For **liabilities**, flip the sign: `natural = -SUM(signed)`. This is because
liabilities naturally read negative in the raw math — when we owe money, the
signed sum is negative, but we display it as a positive number ("we owe 100").

### Transactions vs journal entries

Two separate tables:

- `transactions` — one row per business event ("Alice deposited 100 EUR").
  Has a status: `Pending`, `Processing`, `Settled`, `Failed`, `Reversed`.
- `journal_entries` — many rows per transaction, one per account movement.
  Append-only — we never update or delete.

A transaction's status can change over time. Journal entries never change.
If we ever need to undo something, we write *new* journal entries that reverse
the old ones — we don't edit the originals.

---

## Part 2 — Alice's full journey

We'll walk through every scenario we modeled, using one customer (Alice) and
tracking every journal entry and balance.

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
the registry, and books a clean deposit.

**Journal entries created:**

| tx    | account         | dir | amount | signed |
|-------|-----------------|-----|--------|--------|
| TX-D1 | bank.pooling    | +1  | 100.00 | +100   |
| TX-D1 | customer.viban  | -1  | 100.00 | -100   |

Applying the rule:
- `bank.pooling` is an **asset going UP** → direction = +1
- `customer.viban` is a **liability going UP** → direction = -1

**Balance check for TX-D1:** `+100 + (-100) = 0` ✓

**Balances after event 1:**
- `bank.pooling` = 100 EUR (real money we hold)
- `customer.viban` for Alice = 100 EUR owed (flip of -100)

The transaction's status goes straight to `Settled` because a deposit is
observed *after* the money has already physically arrived. There's no
in-flight phase from our perspective.

---

### Event 2 — Alice withdraws 30 EUR (instant SEPA, step 1: Processing)

Alice hits our withdrawal endpoint. We immediately book the journal entries
and instruct the bank to send 30 EUR to her private IBAN. The transaction
starts in `Processing` state.

**Journal entries created:**

| tx     | account              | dir | amount | signed |
|--------|----------------------|-----|--------|--------|
| TX-W1  | customer.viban       | +1  | 30.00  | +30    |
| TX-W1  | suspense.withdrawal  | -1  | 30.00  | -30    |

Applying the rule:
- `customer.viban` is a **liability going DOWN** (we owe less) → direction = +1
- `suspense.withdrawal` is a **liability going UP** (in-flight obligation) → direction = -1

**Why suspense?** The money hasn't left the bank yet — the bank is processing
the SEPA. But we've already deducted it from Alice's available balance. It
needs to live *somewhere* in our books during that window. `suspense.withdrawal`
is that holding room.

**Balances after event 2:**
- `bank.pooling` = 100 EUR (unchanged — money hasn't physically left yet)
- `customer.viban` Alice = 70 EUR owed (Alice sees 70 in her app)
- `suspense.withdrawal` = 30 EUR in flight

---

### Event 3 — Bank confirms the withdrawal (step 2: Settled)

Our polling job reads the bank statement a few seconds later and sees an
outgoing transfer matching TX-W1 (by reference). It books the settlement.

**Journal entries created:**

| tx     | account              | dir | amount | signed |
|--------|----------------------|-----|--------|--------|
| TX-W1  | suspense.withdrawal  | +1  | 30.00  | +30    |
| TX-W1  | bank.pooling         | -1  | 30.00  | -30    |

Applying the rule:
- `suspense.withdrawal` is a **liability going DOWN** (clearing the in-flight) → direction = +1
- `bank.pooling` is an **asset going DOWN** (money physically left) → direction = -1

**Transaction TX-W1 now has 4 rows total.** Sum of all signed values:
`+30 + (-30) + (+30) + (-30) = 0` ✓. The transaction's status becomes `Settled`.

**Balances after event 3:**
- `bank.pooling` = 70 EUR
- `customer.viban` Alice = 70 EUR owed
- `suspense.withdrawal` = 0 (cleared)

---

### Event 4 — A "faulty" withdrawal of 20 EUR (stuck in Processing)

This is our failure-case simulation. Alice hits a faulty endpoint that books
the journal entries but deliberately never tells the bank — simulating a crash
between DB write and bank API call.

**Journal entries created:**

| tx     | account              | dir | amount | signed |
|--------|----------------------|-----|--------|--------|
| TX-W2  | customer.viban       | +1  | 20.00  | +20    |
| TX-W2  | suspense.withdrawal  | -1  | 20.00  | -20    |

The books **still balance** — this is a clean double-entry transaction. But
the bank was never told, so no settlement will ever come. The transaction
sits in `Processing` forever.

**Balances after event 4:**
- `bank.pooling` = 70 EUR (unchanged)
- `customer.viban` Alice = 50 EUR owed
- `suspense.withdrawal` = 20 EUR in flight (stuck!)

**Who catches this?** Our **Suspense Aging Monitor** job runs every 15
seconds. For `suspense.withdrawal`, the SLA for Instant SEPA is 30 seconds.
After 30 seconds of TX-W2 sitting there, the monitor logs a `STUCK IN SUSPENSE`
warning. Ops would then investigate and either:
- Resolve by submitting the withdrawal to the bank manually, or
- Cancel by writing a reversal transaction that moves the 20 EUR from suspense
  back to Alice's viban.

**Important distinction between two monitoring jobs:**
- **Reconciliation job** — checks if the math balances. It *passes* here because
  the books are internally consistent.
- **Suspense aging monitor** — checks if anything's been stuck too long. It
  *flags* here because the process has stalled.

The faulty withdrawal is a *process* error, not a *bookkeeping* error.
Different jobs catch different kinds of problems.

---

### Event 5 — Alice deposits 150 EUR but name doesn't match (review path)

Let's assume TX-W2 got cancelled by ops, so Alice is back to 70 EUR owed.

Someone sends 150 EUR to Alice's vIBAN, but the name on the incoming transfer
is "Alicia Müller" — close to "Alice Muller" but not exact. Our deposit
processing flags this for manual review.

**Where does the money go?** It physically arrived at `bank.pooling`, so that
must be recorded. But we can't credit Alice's vIBAN — we're not 100% sure it's
her. So the credit side goes to `suspense.deposit.review`.

**Journal entries created:**

| tx     | account                    | dir | amount | signed |
|--------|----------------------------|-----|--------|--------|
| TX-D2  | bank.pooling               | +1  | 150.00 | +150   |
| TX-D2  | suspense.deposit.review    | -1  | 150.00 | -150   |

Applying the rule:
- `bank.pooling` is an **asset going UP** → direction = +1
- `suspense.deposit.review` is a **liability going UP** (we owe 150 to
  *someone*, identity TBD) → direction = -1

The transaction has `CustomerId = null` (we're honest about not knowing),
`Status = Processing`, and `ReviewReason = NameMismatch`.

**Balances after event 5:**
- `bank.pooling` = 220 EUR (70 + 150)
- `customer.viban` Alice = 70 EUR owed (unchanged — she sees no change)
- `suspense.deposit.review` = 150 EUR unassigned

**Important:** Alice does NOT see this money. The 150 EUR is sitting in
review, waiting for a human decision. The **Suspense Aging Monitor** watches
`suspense.deposit.review` too, with a 4-hour SLA. After 4 hours with no
resolution, it flags the transaction.

---

### Branch A — Reviewer approves (confirms it IS Alice)

Operations staff confirm the name variant is indeed Alice, and approve.

**Journal entries created:**

| tx     | account                    | dir | amount | signed |
|--------|----------------------------|-----|--------|--------|
| TX-D2  | suspense.deposit.review    | +1  | 150.00 | +150   |
| TX-D2  | customer.viban             | -1  | 150.00 | -150   |

The review suspense is cleared. Alice is now credited. Same transaction TX-D2,
now with 4 rows total, status `Settled`, and `CustomerId` set to Alice's ID.
`ReviewedBy` and `ReviewedAt` are set.

**Balances after Branch A:**
- `bank.pooling` = 220 EUR
- `customer.viban` Alice = 220 EUR owed (70 + 150)
- `suspense.deposit.review` = 0

---

### Branch B — Reviewer rejects (refund sender)

Alternatively, the reviewer decides this isn't Alice and instructs a bounce —
sending the money back to the original sender.

**This is a two-step outbound**, just like a withdrawal. The same pattern:
book to suspense immediately, settle when the bank confirms.

**Step B1 — Book the bounce instruction:**

| tx     | account                    | dir | amount | signed |
|--------|----------------------------|-----|--------|--------|
| TX-D2  | suspense.deposit.review    | +1  | 150.00 | +150   |
| TX-D2  | suspense.bounce            | -1  | 150.00 | -150   |

Review suspense clears. Bounce suspense now holds 150. Money hasn't physically
left the bank yet.

We call `bank.SubmitBounce(...)` to instruct the bank to send it back.

**Step B2 — Bank confirms bounce sent (polling job picks it up):**

| tx     | account          | dir | amount | signed |
|--------|------------------|-----|--------|--------|
| TX-D2  | suspense.bounce  | +1  | 150.00 | +150   |
| TX-D2  | bank.pooling     | -1  | 150.00 | -150   |

Bounce suspense clears. Pooling goes down. Transaction status → `Failed`.

**Balances after Branch B (fully settled bounce):**
- `bank.pooling` = 70 EUR (back to starting)
- `customer.viban` Alice = 70 EUR owed (never changed throughout)
- All suspense accounts = 0

TX-D2 now has **6 journal entries** across 3 state transitions — each step
added 2 rows. The books remained consistent at every step.

---

### Why this design is powerful

Notice a few properties that fall out for free:

1. **Books always balance.** Every transaction's journal entries sum to zero.
2. **Suspense accounts make failures visible.** Any non-zero suspense balance
   is pending work. Query for it.
3. **Append-only means the journal is the audit log.** No separate history
   table needed.
4. **Every failure mode is modeled.** Stuck withdrawal, name mismatch, bounce,
   successful settlement — all use the same primitives.
5. **New business cases = new suspense buckets.** The math doesn't change.

---

## Part 3 — A quick tour of the code

The implementation is a single ASP.NET Core Web API project with a folder
structure like this:

```
Ledger/
├── Banking/                    - mock bank + statement model
│   ├── BankStatementEntry.cs
│   ├── IMockBank.cs
│   └── MockBank.cs
├── Controllers/
│   ├── ReviewController.cs     - approve/reject endpoints
│   ├── TestingController.cs    - inject fake statements
│   └── WithdrawalsController.cs
├── Customers/                  - in-memory customer registry
│   ├── Customer.cs
│   ├── CustomerRegistry.cs
│   └── ICustomerRegistry.cs
├── Domain/                     - entities + enums
│   ├── Account.cs
│   ├── AccountNumbers.cs       - well-known account numbers
│   ├── AccountType.cs
│   ├── JournalEntry.cs
│   ├── ReviewReason.cs
│   ├── SepaType.cs
│   ├── Transaction.cs
│   ├── TransactionStatus.cs
│   └── TransactionType.cs
├── Infrastructure/             - EF Core setup
│   ├── Configurations/
│   │   ├── AccountConfiguration.cs
│   │   ├── JournalEntryConfiguration.cs
│   │   └── TransactionConfiguration.cs
│   ├── ChartOfAccountsSeeder.cs
│   └── LedgerDbContext.cs
├── Jobs/                       - background services
│   ├── BankStatementPollingJob.cs
│   ├── ReconciliationJob.cs
│   └── SuspenseAgingMonitor.cs
├── Ledger/
│   └── JournalEntryFactory.cs  - all debit/credit logic
└── Program.cs
```

### The moving pieces

**Domain**
: Plain classes for `Account`, `Transaction`, `JournalEntry`, and the enums.
Transactions carry extra fields for the review path: `CounterpartyIban`,
`CounterpartyName`, `ReviewReason`, `ReviewedAt`, `ReviewedBy`.

**Infrastructure**
: `LedgerDbContext` is the EF Core context. Entity configurations live in
`Configurations/` — one file per entity. The `ChartOfAccountsSeeder` runs
on startup and ensures all 10 accounts exist.

**Banking**
: `MockBank` is a singleton that holds an in-memory list of
`BankStatementEntry` rows. Our code writes to it when we submit a withdrawal
or bounce. A testing endpoint writes to it to simulate incoming deposits.
The polling job reads from it.

**Customers**
: `CustomerRegistry` is a singleton holding a list of customers. Seeded with
Alice for testing. The polling job uses it to match incoming deposits by IBAN.

**JournalEntryFactory**
: The single source of truth for debit/credit logic. Each method produces the
journal entries for one state transition (e.g. `WithdrawalToProcessing`,
`ReviewApproved`, `BounceSettled`). Controllers and jobs call these methods
rather than constructing entries manually — this keeps accounting rules in
one place.

**Controllers**
: `WithdrawalsController` creates withdrawal transactions (real and faulty).
`ReviewController` handles approve/reject of deposits in review.
`TestingController` injects fake statement entries so we can trigger any
scenario from HTTP.

**Jobs (background services)**
: Three of them, each with a single job:
- `BankStatementPollingJob` reads unprocessed statement entries every 5
seconds and either settles an outgoing transfer (withdrawal or bounce)
or creates a new deposit (clean or review path).
- `SuspenseAgingMonitor` checks every 15 seconds for anything that's been
in a suspense account too long, with a per-account SLA.
- `ReconciliationJob` runs every minute and verifies the core invariant:
`pooling + deposit.inflight = customer + withdrawal + review + bounce`.

### The flows end to end

- **Clean deposit**: testing endpoint → MockBank statement → polling job
  matches by IBAN → creates Transaction (Settled) + 2 journal entries.
- **Review deposit**: testing endpoint with `forceReview` → MockBank → polling
  job routes to review path → creates Transaction (Processing, no customer) +
  2 entries to `suspense.deposit.review`.
- **Withdrawal**: POST `/api/withdrawals` → Transaction + 2 entries
  (Processing) → submit to MockBank → polling job later sees outgoing entry →
  adds 2 more entries (Settled).
- **Faulty withdrawal**: POST `/api/withdrawals/faulty` → Transaction +
  2 entries, but MockBank never told → stays Processing → SuspenseAgingMonitor
  flags after SLA.
- **Approve review**: POST `/api/reviews/{id}/approve` → 2 more entries
  (review → viban), Settled.
- **Reject review**: POST `/api/reviews/{id}/reject` → 2 entries
  (review → bounce) + submit to MockBank → polling job later sees outgoing →
  2 more entries (bounce → pooling), Failed.

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
        WHEN a.type = 'Asset'     THEN  COALESCE(SUM(je.direction * je.amount), 0)
        WHEN a.type = 'Liability' THEN -COALESCE(SUM(je.direction * je.amount), 0)
    END AS natural_balance
FROM ledger.accounts a
LEFT JOIN ledger.journal_entries je ON je.account_number = a.number
GROUP BY a.number, a.code, a.type
ORDER BY a.number;
```

**How to read:**
- `signed_balance` is the raw sum (debits minus credits).
- `natural_balance` is the human-friendly number: for assets, it equals the
  raw sum; for liabilities, it's flipped so it reads positive when we owe money.

**What "good" looks like:** assets are positive, liabilities are positive
(after flipping), suspense accounts are zero when nothing's in flight.

---

### Query 2 — The core reconciliation invariant

**Purpose:** "Do the books add up overall?"

```sql
SELECT
    SUM(CASE WHEN account_number = 110 THEN direction * amount ELSE 0 END) AS pooling,
    -SUM(CASE WHEN account_number = 210 THEN direction * amount ELSE 0 END) AS customer_owed,
    -SUM(CASE WHEN account_number = 220 THEN direction * amount ELSE 0 END) AS suspense_withdrawal,
    -SUM(CASE WHEN account_number = 230 THEN direction * amount ELSE 0 END) AS suspense_review,
    -SUM(CASE WHEN account_number = 240 THEN direction * amount ELSE 0 END) AS suspense_bounce,
    SUM(CASE WHEN account_number = 150 THEN direction * amount ELSE 0 END) AS suspense_deposit_inflight,
    (SUM(CASE WHEN account_number = 110 THEN direction * amount ELSE 0 END)
     + SUM(CASE WHEN account_number = 150 THEN direction * amount ELSE 0 END)
     + SUM(CASE WHEN account_number = 210 THEN direction * amount ELSE 0 END)
     + SUM(CASE WHEN account_number = 220 THEN direction * amount ELSE 0 END)
     + SUM(CASE WHEN account_number = 230 THEN direction * amount ELSE 0 END)
     + SUM(CASE WHEN account_number = 240 THEN direction * amount ELSE 0 END)
    ) AS drift
FROM ledger.journal_entries;
```

**How to read:** `drift` should always be **exactly 0**. If it's not, the
invariant has broken and there's a bug in the code creating unbalanced
transactions.

Why does adding everything together work? Because liabilities sum to negative
numbers in raw form, and assets sum to positive. When books balance, they
cancel out to zero.

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
signed amounts must sum to zero (that's literally what double-entry means).
If any row comes back, that transaction was booked incorrectly.

Use this after any suspicious activity or as part of a nightly check.

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

**How to read:** normal processing transactions clear within seconds
(Instant SEPA) or hours (Standard). Anything older is either:
- Stuck — bank never confirmed (withdrawal or bounce)
- Pending review — waiting for a human decision (deposit with `review_reason`)

Cross-reference with the SuspenseAgingMonitor logs.

---

### Query 5 — Full audit trail for one transaction

**Purpose:** "Walk me through everything that happened to this transaction."

```sql
SELECT
    je.id,
    je.posted_at,
    a.code AS account,
    je.direction,
    je.amount,
    je.direction * je.amount AS signed
FROM ledger.journal_entries je
JOIN ledger.accounts a ON a.number = je.account_number
WHERE je.transaction_id = '<tx id>'
ORDER BY je.posted_at, je.id;
```

**How to read:** rows appear in chronological order. You can literally read
the story top-to-bottom. The gaps between `posted_at` timestamps tell you
how long each external step took (e.g. how long the bank took to confirm).

Reference `transactions.created_at` and `updated_at` to see when the business
transaction's status changed.

---

### Query 6 — Point-in-time balance (time machine)

**Purpose:** "What was Alice's balance on Monday 9 AM?"

```sql
SELECT -SUM(direction * amount) AS balance_at_point_in_time
FROM ledger.journal_entries
WHERE account_number = 210
  AND customer_id = '11111111-1111-1111-1111-111111111111'
  AND posted_at <= '2026-04-15 09:00:00+00';
```

**How to read:** this is the same as Query 1 but with a timestamp filter.
Because journal entries are append-only, we can reconstruct the balance of
any account at any moment in history.

The flip (`-SUM`) is because `customer.viban` is a liability.

---

### Query 7 — Running balance for a customer

**Purpose:** "Show Alice's balance changing over time."

```sql
SELECT
    je.posted_at,
    t.type,
    t.status,
    je.direction,
    je.amount,
    -SUM(je.direction * je.amount) OVER (
        ORDER BY je.posted_at, je.id
    ) AS running_balance
FROM ledger.journal_entries je
JOIN ledger.transactions t ON t.id = je.transaction_id
WHERE je.account_number = 210
  AND je.customer_id = '11111111-1111-1111-1111-111111111111'
ORDER BY je.posted_at, je.id;
```

**How to read:** each row shows a transaction's journal entry for Alice's
vIBAN, and the `running_balance` column is her balance after that entry was
posted. This is essentially the "transactions" view you'd show a customer in
an app.

`SUM(...) OVER (ORDER BY ...)` is a **window function** — it computes a
cumulative sum as the query scans through rows.

---

### Query 8 — End-of-day balance snapshots

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
       ON je.account_number = 210
      AND je.customer_id = '11111111-1111-1111-1111-111111111111'
      AND je.posted_at < d.day + INTERVAL '1 day'
GROUP BY d.day
ORDER BY d.day;
```

**How to read:** `generate_series` creates one row per day in the range.
For each day, we sum everything posted up to end-of-that-day. Useful for
charts, monthly statements, or regulatory reports.

---

### Query 9 — Customer transaction statement

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
WHERE t.customer_id = '11111111-1111-1111-1111-111111111111'
  AND t.created_at >= '2026-04-01'
  AND t.created_at <  '2026-05-01'
ORDER BY t.created_at;
```

**How to read:** unlike queries against `journal_entries`, this uses the
`transactions` table because we want one row per business event. For the
full accounting detail, join to `journal_entries`.

---

### Query 10 — Time-in-state analytics

**Purpose:** "How fast do our withdrawals settle? Which reviews dragged on?"

Average settlement time for withdrawals:
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

Longest-sitting reviews:
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

**How to read:** `EXTRACT(EPOCH FROM ...)` converts an interval to seconds
so we can average it. Useful for SLA reporting and operational dashboards.

---

### Query 11 — Period delta

**Purpose:** "What changed in each account between two timestamps?"

```sql
SELECT
    a.code,
    SUM(je.direction * je.amount) AS change_over_period
FROM ledger.journal_entries je
JOIN ledger.accounts a ON a.number = je.account_number
WHERE je.posted_at >  '2026-04-10 23:59:59+00'
  AND je.posted_at <= '2026-04-13 23:59:59+00'
GROUP BY a.code
HAVING SUM(je.direction * je.amount) <> 0
ORDER BY a.code;
```

**How to read:** filter to a time window and group by account. The result
tells you how each account moved during that window. Useful for
"what happened over the weekend" type questions.

---

## Key takeaways

- **Direction is just a number (+1 or -1).** Signed amount is
  `direction × amount`. Balance is the sum of signed amounts. Everything
  flows from that.

- **Every transaction sums to zero.** If it doesn't, the books are broken.

- **Suspense accounts are diagnostic tools.** Non-zero suspense = pending
  work. Each bucket has a clear, single reason to exist.

- **Append-only journal = free audit log.** Time-travel queries fall out
  for free. Never update or delete a row.

- **Two kinds of anomaly checks:**
    - Reconciliation = "do the books balance?" (math check)
    - Aging monitors = "has anything been stuck too long?" (process check)

- **New business scenarios add new suspense buckets, not new math.** The
  primitives handle everything.