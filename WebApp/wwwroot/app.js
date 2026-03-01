'use strict';

// ═══════════════════════════════════════════════════════════
// Dynamic queue colours (populated on first StateUpdate)
// ═══════════════════════════════════════════════════════════

let queueColors = {};

// ═══════════════════════════════════════════════════════════
// SignalR connection
// ═══════════════════════════════════════════════════════════

const conn = new signalR.HubConnectionBuilder()
    .withUrl('/simhub')
    .withAutomaticReconnect()
    .build();

conn.on('StateUpdate', renderAll);

conn.onreconnected(() => console.log('[SignalR] reconnected'));
conn.onclose(() => console.log('[SignalR] connection closed'));

async function startConn() {
    try {
        await conn.start();
        console.log('[SignalR] connected');
    } catch (e) {
        console.error('[SignalR] connection failed, retrying...', e);
        setTimeout(startConn, 3000);
    }
}
startConn();

// ═══════════════════════════════════════════════════════════
// Controls
// ═══════════════════════════════════════════════════════════

let currentSpeed = 60;

async function ctl(action) {
    try {
        if (action === 'start')  await conn.invoke('Start');
        if (action === 'pause')  await conn.invoke('Pause');
        if (action === 'resume') await conn.invoke('Resume');
        if (action === 'reset')  await conn.invoke('Reset');
    } catch (e) { console.error('ctl error', e); }
}

async function setSpd(spd) {
    currentSpeed = spd;
    document.getElementById('speed-label').textContent = `Speed: ${spd >= 600 ? 'MAX' : spd + '×'}`;
    document.querySelectorAll('[data-speed]').forEach(b =>
        b.classList.toggle('active', +b.dataset.speed === spd));
    try { await conn.invoke('SetSpeed', spd); } catch(e) { console.error(e); }
}

// Start/Pause button logic
document.getElementById('btn-start').addEventListener('click', async () => {
    const state = lastState;
    if (!state) { await ctl('start'); return; }
    if (state.isFinished) return;
    if (state.isRunning)  return;
    await ctl(state.clockMs === state.startMs ? 'start' : 'resume');
});

document.getElementById('btn-pause').addEventListener('click', () => ctl('pause'));

// ═══════════════════════════════════════════════════════════
// Render pipeline
// ═══════════════════════════════════════════════════════════

let lastState = null;

function renderAll(s) {
    lastState = s;
    queueColors = s.queueColors || {};

    // Clock
    document.getElementById('clock').textContent = s.clock;

    // Status badge
    const el = document.getElementById('status');
    if      (s.isFinished)  { el.textContent = 'Finished'; el.className = 's-done'; }
    else if (s.isRunning)   { el.textContent = 'Running';  el.className = 's-running'; }
    else                    { el.textContent = 'Paused';   el.className = 's-paused'; }

    // Button states
    document.getElementById('btn-start').disabled = s.isRunning || s.isFinished;
    document.getElementById('btn-pause').disabled = !s.isRunning;

    renderUnifiedGantt(
        document.getElementById('gantt-unified'),
        s.ganttBlocks, s.forecastGanttBlocks || [],
        s.clockMs, s.startMs, s.endMs, s.resources);
    renderMachines(s.resources);
    renderQueues(s.queues);
    renderLegend(s.queues);
    renderEventLog(s.events);
    renderMetrics(s.metrics);
}

// ═══════════════════════════════════════════════════════════
// Unified Gantt — Digital Twin (solid, left of Now)
//               + Izzi Forecast (transparent+dashed, right of Now)
// ═══════════════════════════════════════════════════════════

function renderUnifiedGantt(canvas, realBlocks, forecastBlocks, clockMs, startMs, endMs, resources) {
    const dpr  = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    if (rect.width < 10 || rect.height < 10) return;

    canvas.width  = rect.width  * dpr;
    canvas.height = rect.height * dpr;

    const ctx = canvas.getContext('2d');
    ctx.scale(dpr, dpr);
    const W = rect.width, H = rect.height;

    const MACHINES = (resources || []).map(r => r.name);
    const nRows    = Math.max(MACHINES.length, 1);
    const LEFT     = 36;
    const RIGHT    = 6;
    const TOP      = 20;   // header zone for Now label
    const BOTTOM   = 18;   // time-axis labels
    const chartH   = H - TOP - BOTTOM;
    const rowH     = chartH / nRows;
    const chartW   = W - LEFT - RIGHT;
    const totalMs  = endMs - startMs;
    const px       = chartW / totalMs;

    // ── Background ───────────────────────────────────────────
    ctx.fillStyle = '#0d1117';
    ctx.fillRect(0, 0, W, H);

    // ── Alternating row backgrounds ──────────────────────────
    MACHINES.forEach((_, i) => {
        ctx.fillStyle = i % 2 === 0 ? '#0f131a' : '#0d1117';
        ctx.fillRect(LEFT, TOP + i * rowH, chartW, rowH);
    });

    // ── Future-region tint (right of Now) ────────────────────
    const nowX = LEFT + Math.max(0, Math.min(clockMs - startMs, totalMs)) * px;
    if (nowX < W - RIGHT) {
        ctx.fillStyle = 'rgba(88,166,255,0.035)';
        ctx.fillRect(nowX, TOP, W - RIGHT - nowX, chartH);
    }

    // ── Time grid every 5 minutes ────────────────────────────
    const gridStep  = 5 * 60 * 1000;
    const firstGrid = startMs + (gridStep - (startMs % gridStep)) % gridStep;
    ctx.setLineDash([]);
    for (let t = firstGrid; t <= endMs; t += gridStep) {
        const x = LEFT + (t - startMs) * px;
        if (x < LEFT || x > W - RIGHT) continue;
        ctx.strokeStyle = '#21262d';
        ctx.lineWidth   = 1;
        ctx.beginPath(); ctx.moveTo(x, TOP); ctx.lineTo(x, H - BOTTOM); ctx.stroke();
        const d   = new Date(t);
        const lbl = d.getUTCHours().toString().padStart(2,'0') + ':' +
                    d.getUTCMinutes().toString().padStart(2,'0');
        ctx.fillStyle  = '#4d5565';
        ctx.font       = '9px Consolas';
        ctx.textAlign  = 'center';
        ctx.fillText(lbl, x, H - 4);
    }

    // ── Machine labels ───────────────────────────────────────
    ctx.textAlign = 'right';
    ctx.font      = 'bold 11px Consolas';
    MACHINES.forEach((m, i) => {
        ctx.fillStyle = '#6e7681';
        ctx.fillText(m, LEFT - 4, TOP + i * rowH + rowH / 2 + 4);
    });

    // ── Digital Twin blocks — solid, clipped to left of Now ──
    (realBlocks || []).forEach(b => {
        const row = MACHINES.indexOf(b.resource);
        if (row < 0) return;
        const bStart = Math.max(b.startMs, startMs);
        const bEnd   = Math.min(b.endMs ?? clockMs, clockMs);   // hard-clip at Now
        if (bEnd <= bStart) return;

        const x  = LEFT + (bStart - startMs) * px;
        const bW = (bEnd - bStart) * px;
        const y  = TOP + row * rowH + 2;
        const bH = rowH - 4;

        ctx.fillStyle = b.color;
        ctx.fillRect(x, y, Math.max(bW, 1.5), bH);

        if (bW > 22 && b.queue) {
            ctx.fillStyle = 'rgba(0,0,0,0.65)';
            ctx.font      = '9px Consolas';
            ctx.textAlign = 'center';
            ctx.fillText(b.queue, x + bW / 2, y + bH / 2 + 3);
        }
    });

    // ── Forecast blocks — semi-transparent + dashed border ───
    (forecastBlocks || []).forEach(b => {
        const row = MACHINES.indexOf(b.resource);
        if (row < 0) return;
        const bStart = Math.max(b.startMs, clockMs);   // hard-clip at Now
        const bEnd   = Math.min(b.endMs ?? endMs, endMs);
        if (bEnd <= bStart) return;

        const x  = LEFT + (bStart - startMs) * px;
        const bW = (bEnd - bStart) * px;
        const y  = TOP + row * rowH + 2;
        const bH = rowH - 4;
        const w  = Math.max(bW, 1.5);

        ctx.globalAlpha = 0.42;
        ctx.fillStyle   = b.color;
        ctx.fillRect(x, y, w, bH);
        ctx.globalAlpha = 1.0;

        if (bW > 2) {
            ctx.strokeStyle = b.color;
            ctx.lineWidth   = 1;
            ctx.setLineDash([3, 2]);
            ctx.strokeRect(x + 0.5, y + 0.5, w - 1, bH - 1);
            ctx.setLineDash([]);
        }

        if (bW > 30 && b.queue) {
            ctx.fillStyle = 'rgba(255,255,255,0.55)';
            ctx.font      = '9px Consolas';
            ctx.textAlign = 'center';
            ctx.fillText(b.queue, x + bW / 2, y + bH / 2 + 3);
        }
    });

    // ── Now line — bold yellow vertical ──────────────────────
    if (clockMs >= startMs && clockMs <= endMs) {
        const nx = LEFT + (clockMs - startMs) * px;

        // Vertical line
        ctx.strokeStyle = '#ffe066';
        ctx.lineWidth   = 2;
        ctx.setLineDash([]);
        ctx.beginPath();
        ctx.moveTo(nx, TOP);
        ctx.lineTo(nx, H - BOTTOM);
        ctx.stroke();

        // Small downward triangle at top of line
        ctx.fillStyle = '#ffe066';
        ctx.beginPath();
        ctx.moveTo(nx - 4, TOP);
        ctx.lineTo(nx + 4, TOP);
        ctx.lineTo(nx, TOP + 6);
        ctx.closePath();
        ctx.fill();

        // Time label in a pill above the chart
        const d      = new Date(clockMs);
        const nowStr = d.getUTCHours().toString().padStart(2,'0')    + ':' +
                       d.getUTCMinutes().toString().padStart(2,'0')  + ':' +
                       d.getUTCSeconds().toString().padStart(2,'0');
        ctx.font = 'bold 10px Consolas';
        const tw = ctx.measureText(nowStr).width;
        // Clamp pill so it stays inside chart
        const lx = Math.max(LEFT + tw / 2 + 4, Math.min(nx, W - RIGHT - tw / 2 - 4));
        ctx.fillStyle = 'rgba(13,17,23,0.92)';
        ctx.fillRect(lx - tw / 2 - 4, 2, tw + 8, 14);
        ctx.fillStyle = '#ffe066';
        ctx.textAlign = 'center';
        ctx.fillText(nowStr, lx, 12);
    }

    // ── Chart border ──────────────────────────────────────────
    ctx.strokeStyle = '#21262d';
    ctx.lineWidth   = 1;
    ctx.setLineDash([]);
    ctx.strokeRect(LEFT, TOP, chartW, chartH);
}

// ═══════════════════════════════════════════════════════════
// Right panel — machine states
// ═══════════════════════════════════════════════════════════

function renderMachines(resources) {
    const container = document.getElementById('machines');
    container.innerHTML = (resources || []).map(r => {
        const cls = r.state === 'Working'   ? 'st-working'
                  : r.state === 'Idle'      ? 'st-idle'
                  : r.state === 'LoggedOut' ? 'st-loggedout'
                  : 'st-transit';

        const icon = r.state === 'Working'   ? '▶'
                   : r.state === 'Idle'      ? '◌'
                   : r.state === 'LoggedOut' ? '○'
                   : '…';

        const dot  = r.queueColor
            ? `<span class="qdot" style="background:${r.queueColor}"></span>` : '';
        const user = r.user
            ? `<span style="color:#555;font-size:10px">(${r.user})</span>` : '';

        return `<div class="mcard">
            <div class="mcard-name">${r.name}</div>
            <div class="mcard-state ${cls}">${icon} ${r.state} ${dot}${r.queue || ''} ${user}</div>
        </div>`;
    }).join('');
}

// ── Queue counts ────────────────────────────────────────────

function renderQueues(queues) {
    const container = document.getElementById('queues');
    container.innerHTML = (queues || []).map(q => {
        const color = q.color || '#555';
        return `<div class="qrow">
            <span class="qname">
                <span class="qdot" style="background:${color}"></span>
                ${q.name}
            </span>
            <span>
                <span class="qpending" style="color:${color}">${q.pending}</span>
                <span style="color:#4d5160;font-size:10px"> / ${q.completed}✓</span>
            </span>
        </div>`;
    }).join('');
}

let _legendBuilt = false;

function renderLegend(queues) {
    if (_legendBuilt || !queues || queues.length === 0) return;
    _legendBuilt = true;
    const el = document.getElementById('legend');
    const qEntries = (queues || []).map(q =>
        `<div class="leg"><div class="leg-swatch" style="background:${q.color}"></div>${q.name}</div>`
    ).join('');
    el.innerHTML = qEntries +
        `<div class="leg"><div class="leg-swatch" style="background:#7f8c8d"></div>Login/Out</div>`;
}

// ═══════════════════════════════════════════════════════════
// Event log
// ═══════════════════════════════════════════════════════════

let _lastEvCount = 0;

function renderEventLog(events) {
    if (!events || events.length === _lastEvCount) return;
    _lastEvCount = events.length;

    const el = document.getElementById('event-log');
    el.innerHTML = (events || []).map(e => {
        const cls = e.includes('IZZI')   ? 'ev-izzi'
                  : e.includes('✓')      ? 'ev-done'
                  : e.includes('WAVE')   ? 'ev-wave'
                  : (e.includes('▶') || e.includes('⏸') || e.includes('↺')) ? 'ev-ctrl'
                  : 'ev';
        return `<div class="${cls}">${e}</div>`;
    }).join('');
}

// ═══════════════════════════════════════════════════════════
// Metrics
// ═══════════════════════════════════════════════════════════

function renderMetrics(metrics) {
    if (!metrics) return;

    const tph  = document.getElementById('m-tph');
    const util = document.getElementById('m-util');

    tph.innerHTML = Object.entries(metrics.tasksPerHour || {}).map(([name, val]) => {
        const color = queueColors[name] || '#8b949e';
        return `<div class="mrow">
            <span class="mname"><span class="qdot" style="background:${color}"></span>${name}</span>
            <span class="mval">${val}/h</span>
        </div>`;
    }).join('');

    util.innerHTML = Object.entries(metrics.utilization || {}).map(([name, val]) => {
        const barColor = val > 80 ? '#3fb950' : val > 40 ? '#d29922' : '#8b949e';
        return `<div class="mrow">
            <span class="mname">${name}</span>
            <span class="mval" style="color:${barColor}">${val}%</span>
        </div>`;
    }).join('');
}
