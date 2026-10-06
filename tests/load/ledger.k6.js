import http from 'k6/http';
import exec from 'k6/execution';
import { check, sleep } from 'k6';
import { Counter, Rate } from 'k6/metrics';

const SCENARIOS = ['smoke', 'write-spread', 'write-hot-account', 'balance-read', 'statement-read', 'mixed'];

const SCENARIO = __ENV.SCENARIO || 'smoke';
const BASE_URL = (__ENV.BASE_URL || 'http://localhost:8080').replace(/\/$/, '');
const TOKEN = __ENV.TOKEN || '';
const LOAD_SCALE = Number(__ENV.LOAD_SCALE || '1');
const RAMP_SECONDS = Number(__ENV.RAMP_SECONDS || '120');
const HOLD_SECONDS = Number(__ENV.HOLD_SECONDS || '300');
const COOLDOWN_SECONDS = Number(__ENV.COOLDOWN_SECONDS || '30');
const HOT_ACCOUNT_RATE = Number(__ENV.HOT_ACCOUNT_RATE || '100');
const LATENCY_FACTOR = Number(__ENV.LATENCY_FACTOR || '1') > 0 ? Number(__ENV.LATENCY_FACTOR || '1') : 1;
const WORKER_URL = (__ENV.WORKER_URL || '').replace(/\/$/, '');
const K6_API_URL = __ENV.K6_API_URL || 'http://localhost:6565';
const BALANCE_BATCH_SIZE = 50;
const OUTBOX_DRAIN_SECONDS = Number(__ENV.OUTBOX_DRAIN_SECONDS || '60');
const OUTBOX_LAG_OK_RATE = 0.99;
const ASOF_LAG_SECONDS = Number(__ENV.ASOF_LAG_SECONDS || '5');
const ASOF_RATIO = Number(__ENV.ASOF_RATIO || '0.05');
const REPLAY_RATIO = Number(__ENV.REPLAY_RATIO || '0.01');
const VERIFIED_SAMPLE = Number(__ENV.VERIFIED_SAMPLE || '20');
const STATEMENT_ACCOUNT_COUNT = Number(__ENV.STATEMENT_ACCOUNTS || '10');
const STATEMENT_ENTRIES = Number(__ENV.STATEMENT_ENTRIES || '250');
const STATEMENT_PAGE_SIZE = 100;

const WRITE_PEAK_RATE = Math.max(1, Math.round(2000 * LOAD_SCALE));
const READ_PEAK_RATE = Math.max(1, Math.round(10000 * LOAD_SCALE));
const STATEMENT_PEAK_RATE = Math.max(1, Math.round(500 * LOAD_SCALE));
const SPREAD_ACCOUNT_COUNT = Number(__ENV.ACCOUNT_COUNT || Math.max(10, Math.round(10000 * LOAD_SCALE)));
const SPREAD_INITIAL_BALANCE = '100000.00';
const HOT_INITIAL_BALANCE = '1000000.00';
const ZERO_GUID = '00000000-0000-0000-0000-000000000000';
const PROVISION_BATCH_SIZE = 50;
const TOTAL_SECONDS = RAMP_SECONDS + Math.ceil(RAMP_SECONDS / 2) + HOLD_SECONDS + COOLDOWN_SECONDS;

const WRITE_P99_MS = 150 * LATENCY_FACTOR;
const READ_P99_MS = 50 * LATENCY_FACTOR;
const STATEMENT_P99_MS = 200 * LATENCY_FACTOR;
const MAX_FAILED_RATE = 0.0005;

const invariantViolations = new Counter('ledger_invariant_violations');
const idempotentReplays = new Counter('ledger_idempotent_replays');
const creditedCents = new Counter('ledger_credited_cents');
const debitedCents = new Counter('ledger_debited_cents');
const outboxLagOk = new Rate('ledger_outbox_lag_ok');

if (!SCENARIOS.includes(SCENARIO)) {
    throw new Error(`SCENARIO must be one of ${SCENARIOS.join(', ')}`);
}

if (!TOKEN) {
    throw new Error('TOKEN is required: sign an RS256 token with scopes ledger.read and ledger.write using dev-keys/private.pem and pass it as LEDGER_TOKEN');
}

http.setResponseCallback(http.expectedStatuses(200, 201));

function includes(name) {
    if (name === 'write-hot-account' && HOT_ACCOUNT_RATE <= 0) {
        return false;
    }

    if (SCENARIO === name) {
        return true;
    }

    return SCENARIO === 'mixed' && name !== 'smoke';
}

if (SCENARIO === 'write-hot-account' && HOT_ACCOUNT_RATE <= 0) {
    throw new Error('HOT_ACCOUNT_RATE must be positive for the write-hot-account scenario');
}

function probesOutboxLag() {
    return WORKER_URL !== '' && SCENARIO !== 'smoke';
}

function arrivalStages(peak) {
    return [
        { target: Math.ceil(peak / 2), duration: `${RAMP_SECONDS}s` },
        { target: peak, duration: `${Math.ceil(RAMP_SECONDS / 2)}s` },
        { target: peak, duration: `${HOLD_SECONDS}s` },
        { target: 0, duration: `${COOLDOWN_SECONDS}s` },
    ];
}

function rampingScenario(execName, peak) {
    return {
        executor: 'ramping-arrival-rate',
        exec: execName,
        startRate: Math.max(1, Math.ceil(peak / 20)),
        timeUnit: '1s',
        preAllocatedVUs: Math.max(20, Math.ceil(peak * 0.1)),
        maxVUs: Math.max(50, peak),
        stages: arrivalStages(peak),
    };
}

function buildScenarios() {
    const scenarios = {};

    if (includes('smoke')) {
        scenarios.smoke = {
            executor: 'constant-arrival-rate',
            exec: 'smoke',
            rate: 2,
            timeUnit: '1s',
            duration: '30s',
            preAllocatedVUs: 10,
            maxVUs: 50,
        };
    }

    if (includes('write-spread')) {
        scenarios.write_spread = rampingScenario('writeSpread', WRITE_PEAK_RATE);
    }

    if (includes('write-hot-account')) {
        scenarios.write_hot_account = {
            executor: 'constant-arrival-rate',
            exec: 'writeHotAccount',
            rate: HOT_ACCOUNT_RATE,
            timeUnit: '1s',
            duration: `${SCENARIO === 'mixed' ? TOTAL_SECONDS : HOLD_SECONDS}s`,
            preAllocatedVUs: Math.max(10, Math.ceil(HOT_ACCOUNT_RATE * 0.5)),
            maxVUs: Math.max(50, HOT_ACCOUNT_RATE * 4),
        };
    }

    if (includes('balance-read')) {
        scenarios.balance_read = rampingScenario('balanceRead', READ_PEAK_RATE);
    }

    if (includes('statement-read')) {
        scenarios.statement_read = rampingScenario('statementRead', STATEMENT_PEAK_RATE);
    }

    if (probesOutboxLag()) {
        scenarios.outbox_lag = {
            executor: 'constant-arrival-rate',
            exec: 'outboxLag',
            rate: 1,
            timeUnit: '1s',
            duration: `${SCENARIO === 'mixed' || includes('write-spread') ? TOTAL_SECONDS : HOLD_SECONDS}s`,
            preAllocatedVUs: 2,
            maxVUs: 5,
        };
    }

    return scenarios;
}

function buildThresholds() {
    const thresholds = {
        checks: ['rate>0.999'],
        ledger_invariant_violations: ['count==0'],
        dropped_iterations: ['count==0'],
    };

    if (includes('smoke')) {
        thresholds.checks = ['rate==1'];
        thresholds['http_req_duration{operation:write,scenario:smoke}'] = [`p(99)<${WRITE_P99_MS}`];
        thresholds['http_req_duration{operation:balance,scenario:smoke}'] = [`p(99)<${READ_P99_MS}`];
        thresholds['http_req_failed{scenario:smoke}'] = ['rate==0'];
    }

    if (includes('write-spread')) {
        thresholds['http_req_duration{scenario:write_spread}'] = [`p(99)<${WRITE_P99_MS}`];
        thresholds['http_req_failed{scenario:write_spread}'] = [`rate<${MAX_FAILED_RATE}`];
    }

    if (includes('write-hot-account')) {
        thresholds['http_req_duration{scenario:write_hot_account}'] = [`p(99)<${WRITE_P99_MS}`];
        thresholds['http_req_failed{scenario:write_hot_account}'] = [`rate<${MAX_FAILED_RATE}`];
    }

    if (includes('balance-read')) {
        thresholds['http_req_duration{scenario:balance_read}'] = [`p(99)<${READ_P99_MS}`];
        thresholds['http_req_failed{scenario:balance_read}'] = [`rate<${MAX_FAILED_RATE}`];
    }

    if (includes('statement-read')) {
        thresholds['http_req_duration{scenario:statement_read}'] = [`p(99)<${STATEMENT_P99_MS}`];
        thresholds['http_req_failed{scenario:statement_read}'] = [`rate<${MAX_FAILED_RATE}`];
    }

    if (probesOutboxLag()) {
        thresholds.ledger_outbox_lag_ok = [`rate>${OUTBOX_LAG_OK_RATE}`];
    }

    return thresholds;
}

export const options = {
    scenarios: buildScenarios(),
    thresholds: buildThresholds(),
    setupTimeout: '15m',
    teardownTimeout: '15m',
    summaryTrendStats: ['avg', 'min', 'med', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

function authHeaders(extra) {
    return Object.assign({ Authorization: `Bearer ${TOKEN}`, 'Content-Type': 'application/json' }, extra || {});
}

function checkDigit(digits) {
    let sum = 0;
    for (let i = 0; i < digits.length; i++) {
        sum += digits[i] * (digits.length + 1 - i);
    }

    const remainder = (sum * 10) % 11;
    return remainder === 10 ? 0 : remainder;
}

function validCpf(seed) {
    const base = String((seed * 2654435761 + 12345) % 1000000000).padStart(9, '0');
    const digits = base.split('').map(Number);
    if (digits.every((digit) => digit === digits[0])) {
        digits[8] = (digits[8] + 1) % 10;
    }

    digits.push(checkDigit(digits));
    digits.push(checkDigit(digits));
    return digits.join('');
}

function toCents(amount) {
    return Math.round(Number(amount) * 100);
}

function randomAmount() {
    return (1 + Math.floor(Math.random() * 4900) / 100).toFixed(2);
}

function accountRequest(seed) {
    return {
        method: 'POST',
        url: `${BASE_URL}/v1/accounts`,
        body: JSON.stringify({ holderDocument: validCpf(seed), currency: 'BRL', overdraftLimit: '0.00' }),
        params: { headers: authHeaders(), tags: { operation: 'account', name: 'POST /v1/accounts' } },
    };
}

function entryRequest(accountId, type, amount, key, label) {
    return {
        method: 'POST',
        url: `${BASE_URL}/v1/accounts/${accountId}/entries`,
        body: JSON.stringify({ type, amount, currency: 'BRL', description: `k6 ${label}`, reference: key }),
        params: {
            headers: authHeaders({ 'Idempotency-Key': key }),
            tags: { operation: 'write', name: 'POST /v1/accounts/{id}/entries' },
        },
    };
}

function postEntry(accountId, type, amount, key, label, extraParams) {
    const request = entryRequest(accountId, type, amount, key, label);
    return http.post(request.url, request.body, Object.assign({}, request.params, extraParams || {}));
}

function provisionAccounts(count, initialBalance, runId, label, seedOffset) {
    const accountIds = [];

    for (let offset = 0; offset < count; offset += PROVISION_BATCH_SIZE) {
        const size = Math.min(PROVISION_BATCH_SIZE, count - offset);
        const creations = [];
        for (let i = 0; i < size; i++) {
            creations.push(accountRequest(seedOffset + offset + i));
        }

        const created = http.batch(creations);
        const batchIds = created.map((response) => {
            if (response.status !== 201) {
                throw new Error(`account provisioning failed with status ${response.status}: ${response.body}`);
            }

            return response.json('accountId');
        });

        const fundings = batchIds.map((accountId) =>
            entryRequest(accountId, 'CREDIT', initialBalance, `${runId}-${label}-fund-${accountId}`, `${label} funding`));

        http.batch(fundings).forEach((response) => {
            if (response.status !== 201) {
                throw new Error(`account funding failed with status ${response.status}: ${response.body}`);
            }
        });

        batchIds.forEach((accountId) => accountIds.push(accountId));
    }

    return accountIds;
}

function pickAccount(data) {
    return data.accounts[Math.floor(Math.random() * data.accounts.length)];
}

function countAccepted(type, amount) {
    if (type === 'CREDIT') {
        creditedCents.add(toCents(amount));
    } else {
        debitedCents.add(toCents(amount));
    }
}

function recordViolation(message) {
    invariantViolations.add(1);
    console.error(`invariant violated: ${message}`);
}

function readStatement(accountId) {
    const entries = [];
    let cursor = null;

    do {
        const suffix = cursor ? `&cursor=${encodeURIComponent(cursor)}` : '';
        const response = http.get(`${BASE_URL}/v1/accounts/${accountId}/entries?limit=200${suffix}`, {
            headers: authHeaders(),
            tags: { operation: 'verify', name: 'GET /v1/accounts/{id}/entries' },
        });

        if (response.status !== 200) {
            recordViolation(`statement of ${accountId} answered ${response.status}`);
            return null;
        }

        const page = response.json();
        page.items.forEach((item) => entries.push(item));
        cursor = page.nextCursor;
    } while (cursor);

    return entries;
}

function verifyAccount(accountId) {
    const entries = readStatement(accountId);
    if (entries === null) {
        return;
    }

    let running = 0;
    for (let i = entries.length - 1; i >= 0; i--) {
        const entry = entries[i];
        const expectedVersion = entries.length - i;
        const signed = entry.type === 'CREDIT' ? toCents(entry.amount) : -toCents(entry.amount);
        running += signed;

        if (entry.accountVersion !== expectedVersion) {
            recordViolation(`account ${accountId} version ${entry.accountVersion} found where ${expectedVersion} was expected`);
            return;
        }

        if (toCents(entry.balanceAfter) !== running) {
            recordViolation(`account ${accountId} version ${entry.accountVersion} has balanceAfter ${entry.balanceAfter}, chain says ${running / 100}`);
            return;
        }
    }

    const balance = http.get(`${BASE_URL}/v1/accounts/${accountId}/balance`, {
        headers: authHeaders(),
        tags: { operation: 'verify', name: 'GET /v1/accounts/{id}/balance' },
    });

    if (balance.status !== 200 || toCents(balance.json('balance')) !== running) {
        recordViolation(`account ${accountId} balance differs from the sum of its entries (${running / 100})`);
    }
}

function provisionStatementAccounts(runId) {
    const accountIds = provisionAccounts(STATEMENT_ACCOUNT_COUNT, SPREAD_INITIAL_BALANCE, runId, 'statement', 800000000);

    for (let sequence = 1; sequence < STATEMENT_ENTRIES; sequence++) {
        const requests = accountIds.map((accountId) =>
            entryRequest(accountId, 'CREDIT', '1.00', `${runId}-statement-${sequence}-${accountId}`, 'statement seeding'));

        http.batch(requests).forEach((response) => {
            if (response.status !== 201) {
                throw new Error(`statement seeding failed with status ${response.status}: ${response.body}`);
            }
        });
    }

    return accountIds;
}

export function setup() {
    const runId = Date.now().toString(36);
    const data = { runId, accounts: [], hotAccount: null, statementAccounts: [], fundedCents: 0 };

    const needsSpreadAccounts = includes('write-spread') || includes('balance-read');
    if (needsSpreadAccounts) {
        data.accounts = provisionAccounts(SPREAD_ACCOUNT_COUNT, SPREAD_INITIAL_BALANCE, runId, 'spread', 0);
    }

    if (includes('write-hot-account')) {
        data.hotAccount = provisionAccounts(1, HOT_INITIAL_BALANCE, runId, 'hot', 900000000)[0];
    }

    if (includes('write-spread')) {
        data.fundedCents += data.accounts.length * toCents(SPREAD_INITIAL_BALANCE);
    }

    if (data.hotAccount) {
        data.fundedCents += toCents(HOT_INITIAL_BALANCE);
    }

    if (includes('statement-read')) {
        data.statementAccounts = provisionStatementAccounts(runId);
    }

    return data;
}

export function writeSpread(data) {
    const accountId = pickAccount(data);
    const type = Math.random() < 0.55 ? 'CREDIT' : 'DEBIT';
    const amount = randomAmount();
    const key = `${data.runId}-spread-${exec.scenario.iterationInTest}`;

    const response = postEntry(accountId, type, amount, key, 'write-spread');
    const accepted = check(response, { 'spread entry accepted': (r) => r.status === 201 });

    if (accepted) {
        countAccepted(type, amount);
    }

    if (accepted && Math.random() < REPLAY_RATIO) {
        const replay = postEntry(accountId, type, amount, key, 'write-spread');
        idempotentReplays.add(1);
        check(replay, {
            'replay answers 201': (r) => r.status === 201,
            'replay is flagged': (r) => r.headers['Idempotent-Replayed'] === 'true',
            'replay returns the original entry': (r) => r.json('entryId') === response.json('entryId'),
        });
    }
}

export function writeHotAccount(data) {
    const iteration = exec.scenario.iterationInTest;
    const credit = iteration % 2 === 0;
    const key = `${data.runId}-hot-${iteration}`;

    const type = credit ? 'CREDIT' : 'DEBIT';
    const amount = credit ? '2.00' : '1.00';

    const response = postEntry(data.hotAccount, type, amount, key, 'write-hot-account');
    const accepted = check(response, { 'hot account entry accepted': (r) => r.status === 201 });

    if (accepted) {
        countAccepted(type, amount);
    }
}

export function outboxLag() {
    const response = http.get(`${WORKER_URL}/health/ready`, {
        responseCallback: http.expectedStatuses(200, 503),
        tags: { operation: 'lag', name: 'GET worker /health/ready' },
    });

    outboxLagOk.add(response.status === 200 && response.json('status') === 'Healthy');
}

export function balanceRead(data) {
    const accountId = pickAccount(data);
    let url = `${BASE_URL}/v1/accounts/${accountId}/balance`;
    let name = 'GET /v1/accounts/{id}/balance';

    if (Math.random() < ASOF_RATIO) {
        const asOf = new Date(Date.now() - ASOF_LAG_SECONDS * 1000).toISOString();
        url = `${url}?asOf=${encodeURIComponent(asOf)}`;
        name = 'GET /v1/accounts/{id}/balance?asOf';
    }

    const response = http.get(url, { headers: authHeaders(), tags: { operation: 'balance', name } });
    check(response, { 'balance answered 200': (r) => r.status === 200 });
}

export function statementRead(data) {
    const accountId = data.statementAccounts[Math.floor(Math.random() * data.statementAccounts.length)];
    const tags = { operation: 'statement', name: 'GET /v1/accounts/{id}/entries' };

    const first = http.get(`${BASE_URL}/v1/accounts/${accountId}/entries?limit=${STATEMENT_PAGE_SIZE}`, {
        headers: authHeaders(),
        tags,
    });
    const firstOk = check(first, {
        'first statement page answered 200': (r) => r.status === 200,
        'first statement page is full': (r) => r.json('items').length === STATEMENT_PAGE_SIZE,
    });

    if (!firstOk) {
        return;
    }

    const second = http.get(
        `${BASE_URL}/v1/accounts/${accountId}/entries?limit=${STATEMENT_PAGE_SIZE}&cursor=${encodeURIComponent(first.json('nextCursor'))}`,
        { headers: authHeaders(), tags });
    check(second, { 'second statement page answered 200': (r) => r.status === 200 });
}

export function smoke(data) {
    const journey = `${data.runId}-smoke-${exec.scenario.iterationInTest}`;
    const document = validCpf(500000000 + exec.scenario.iterationInTest);

    const anonymous = http.get(`${BASE_URL}/v1/accounts/${ZERO_GUID}/balance`, {
        responseCallback: http.expectedStatuses(401),
        tags: { operation: 'auth', name: 'GET /v1/accounts/{id}/balance without token' },
    });
    check(anonymous, { 'request without token is rejected': (r) => r.status === 401 });

    const created = http.post(
        `${BASE_URL}/v1/accounts`,
        JSON.stringify({ holderDocument: document, currency: 'BRL', overdraftLimit: '0.00' }),
        { headers: authHeaders(), tags: { operation: 'account', name: 'POST /v1/accounts' } });
    const accountCreated = check(created, {
        'account created': (r) => r.status === 201,
        'document is masked in the answer': (r) => typeof r.json('holderDocumentMasked') === 'string' && r.json('holderDocumentMasked') !== document,
    });
    if (!accountCreated) {
        return;
    }

    const accountId = created.json('accountId');

    const credit = postEntry(accountId, 'CREDIT', '100.00', `${journey}-credit`, 'smoke');
    check(credit, {
        'credit accepted': (r) => r.status === 201,
        'credit balanceAfter is 100.00': (r) => r.json('balanceAfter') === '100.00',
    });

    const debitKey = `${journey}-debit`;
    const debit = postEntry(accountId, 'DEBIT', '30.00', debitKey, 'smoke');
    check(debit, {
        'debit accepted': (r) => r.status === 201,
        'debit balanceAfter is 70.00': (r) => r.json('balanceAfter') === '70.00',
    });

    const replay = postEntry(accountId, 'DEBIT', '30.00', debitKey, 'smoke');
    check(replay, {
        'replay answers 201': (r) => r.status === 201,
        'replay is flagged': (r) => r.headers['Idempotent-Replayed'] === 'true',
        'replay returns the original entry': (r) => r.json('entryId') === debit.json('entryId'),
    });

    const reused = postEntry(accountId, 'DEBIT', '31.00', debitKey, 'smoke', { responseCallback: http.expectedStatuses(422) });
    check(reused, {
        'same key with another body is rejected': (r) => r.status === 422,
        'rejection code is IDEMPOTENCY_KEY_REUSED': (r) => r.json('code') === 'IDEMPOTENCY_KEY_REUSED',
    });

    const overdraw = postEntry(accountId, 'DEBIT', '1000.00', `${journey}-overdraw`, 'smoke', { responseCallback: http.expectedStatuses(422) });
    check(overdraw, {
        'overdraw is rejected': (r) => r.status === 422,
        'rejection code is INSUFFICIENT_FUNDS': (r) => r.json('code') === 'INSUFFICIENT_FUNDS',
    });

    const balance = http.get(`${BASE_URL}/v1/accounts/${accountId}/balance`, {
        headers: authHeaders(),
        tags: { operation: 'balance', name: 'GET /v1/accounts/{id}/balance' },
    });
    check(balance, {
        'balance answered 200': (r) => r.status === 200,
        'balance is 70.00': (r) => r.json('balance') === '70.00',
    });

    const atDebit = http.get(`${BASE_URL}/v1/accounts/${accountId}/balance?asOf=${encodeURIComponent(debit.json('recordedAt'))}`, {
        headers: authHeaders(),
        tags: { operation: 'balance', name: 'GET /v1/accounts/{id}/balance?asOf' },
    });
    check(atDebit, {
        'historical balance at the debit answered 200': (r) => r.status === 200,
        'historical balance at the debit is 70.00': (r) => r.json('balance') === '70.00',
    });

    const atCredit = http.get(`${BASE_URL}/v1/accounts/${accountId}/balance?asOf=${encodeURIComponent(credit.json('recordedAt'))}`, {
        headers: authHeaders(),
        tags: { operation: 'balance', name: 'GET /v1/accounts/{id}/balance?asOf' },
    });
    check(atCredit, {
        'historical balance at the credit answered 200': (r) => r.status === 200,
        'historical balance at the credit is 100.00': (r) => r.json('balance') === '100.00',
    });

    const firstPage = http.get(`${BASE_URL}/v1/accounts/${accountId}/entries?limit=1`, {
        headers: authHeaders(),
        tags: { operation: 'statement', name: 'GET /v1/accounts/{id}/entries' },
    });
    check(firstPage, {
        'first statement page answered 200': (r) => r.status === 200,
        'newest entry comes first': (r) => r.json('items.0.entryId') === debit.json('entryId'),
        'a next cursor is offered': (r) => typeof r.json('nextCursor') === 'string',
    });

    const secondPage = http.get(`${BASE_URL}/v1/accounts/${accountId}/entries?limit=1&cursor=${encodeURIComponent(firstPage.json('nextCursor'))}`, {
        headers: authHeaders(),
        tags: { operation: 'statement', name: 'GET /v1/accounts/{id}/entries' },
    });
    check(secondPage, {
        'second statement page answered 200': (r) => r.status === 200,
        'older entry comes next': (r) => r.json('items.0.entryId') === credit.json('entryId'),
    });

    const reversal = http.post(
        `${BASE_URL}/v1/accounts/${accountId}/entries/${debit.json('entryId')}/reversals`,
        JSON.stringify({ description: 'k6 smoke reversal' }),
        {
            headers: authHeaders({ 'Idempotency-Key': `${journey}-reversal` }),
            tags: { operation: 'write', name: 'POST /v1/accounts/{id}/entries/{id}/reversals' },
        });
    check(reversal, {
        'reversal accepted': (r) => r.status === 201,
        'reversal credits the amount back': (r) => r.json('type') === 'CREDIT' && r.json('amount') === '30.00',
        'reversal restores 100.00': (r) => r.json('balanceAfter') === '100.00',
        'reversal points to the original': (r) => r.json('reversesEntryId') === debit.json('entryId'),
    });

    const secondReversal = http.post(
        `${BASE_URL}/v1/accounts/${accountId}/entries/${debit.json('entryId')}/reversals`,
        JSON.stringify({}),
        {
            headers: authHeaders({ 'Idempotency-Key': `${journey}-reversal-again` }),
            responseCallback: http.expectedStatuses(409),
            tags: { operation: 'write', name: 'POST /v1/accounts/{id}/entries/{id}/reversals' },
        });
    check(secondReversal, {
        'second reversal is rejected': (r) => r.status === 409,
        'rejection code is ENTRY_ALREADY_REVERSED': (r) => r.json('code') === 'ENTRY_ALREADY_REVERSED',
    });

    verifyAccount(accountId);
}

function readCounter(name) {
    const response = http.get(`${K6_API_URL}/v1/metrics/${name}`, {
        responseCallback: http.expectedStatuses(200),
        tags: { operation: 'verify', name: 'k6 metrics' },
    });

    if (response.status !== 200) {
        recordViolation(`the k6 metrics API answered ${response.status} for ${name}`);
        return null;
    }

    return response.json('data.attributes.sample.count');
}

function sumBalances(accountIds) {
    let total = 0;

    for (let offset = 0; offset < accountIds.length; offset += BALANCE_BATCH_SIZE) {
        const requests = accountIds.slice(offset, offset + BALANCE_BATCH_SIZE).map((accountId) => ({
            method: 'GET',
            url: `${BASE_URL}/v1/accounts/${accountId}/balance`,
            params: { headers: authHeaders(), tags: { operation: 'verify', name: 'GET /v1/accounts/{id}/balance' } },
        }));

        for (const response of http.batch(requests)) {
            if (response.status !== 200) {
                recordViolation(`a balance read for the conservation check answered ${response.status}`);
                return null;
            }

            total += toCents(response.json('balance'));
        }
    }

    return total;
}

function verifyConservation(data) {
    const writable = (includes('write-spread') ? data.accounts : []).concat(data.hotAccount ? [data.hotAccount] : []);

    if (writable.length === 0) {
        return;
    }

    sleep(3);

    const credited = readCounter('ledger_credited_cents');
    const debited = readCounter('ledger_debited_cents');
    const actual = sumBalances(writable);

    if (credited === null || debited === null || actual === null) {
        return;
    }

    const expected = data.fundedCents + credited - debited;

    if (actual !== expected) {
        recordViolation(`the balances add up to ${actual / 100} and the funding plus accepted credits minus accepted debits is ${expected / 100}`);
    } else {
        console.log(`conservation holds: ${writable.length} accounts add up to ${actual / 100}`);
    }
}

function verifyOutboxDrained() {
    if (!probesOutboxLag()) {
        return;
    }

    const deadline = Date.now() + OUTBOX_DRAIN_SECONDS * 1000;

    while (Date.now() < deadline) {
        const response = http.get(`${WORKER_URL}/health/ready`, {
            responseCallback: http.expectedStatuses(200, 503),
            tags: { operation: 'lag', name: 'GET worker /health/ready' },
        });

        if (response.status === 200 && response.json('status') === 'Healthy') {
            return;
        }

        sleep(2);
    }

    recordViolation(`the worker still reported an outbox lag above the budget ${OUTBOX_DRAIN_SECONDS} seconds after the load ended`);
}

export function teardown(data) {
    verifyConservation(data);
    verifyOutboxDrained();

    if (data.hotAccount) {
        verifyAccount(data.hotAccount);
    }

    const sample = Math.min(VERIFIED_SAMPLE, data.accounts.length);
    for (let i = 0; i < sample; i++) {
        verifyAccount(data.accounts[Math.floor((i * data.accounts.length) / sample)]);
    }

    data.statementAccounts.slice(0, 3).forEach((accountId) => verifyAccount(accountId));
}
