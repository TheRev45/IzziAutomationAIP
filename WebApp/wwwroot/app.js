'use strict';

// ═══════════════════════════════════════════════════════════
// Constants
// ═══════════════════════════════════════════════════════════

const Q_COLORS = {
    Q1: '#e74c3c', Q2: '#3498db', Q3: '#2ecc71', Q4: '#f39c12', Q5: '#9b59b6'
};

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
    await ctl(state.clock === '08:50:00' ? 'start' : 'resume');
});

document.getElementById('btn-pause').addEventListener('click', () => ctl('pause'));

// ═══════════════════════════════════════════════════════════
// Render pipeline
// ═══════════════════════════════════════════════════════════

let lastState = null;

function renderAll(s) {
    lastState = s;

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

    renderGantt(document.getElementById('gantt-real'), s.ganttBlocks, s.clockMs, s.startMs, s.endMs);
    renderForecastPlaceholder(document.getElementById('gantt-forecast'), s.clockMs, s.startMs, s.endMs);
    renderMachines(s.resources);
    renderQueues(s.queues);
    renderEventLog(s.events);
    renderMetrics(s.metrics);
}

// ═══════════════════════════════════════════════════════════
// Gantt chart (canvas)
// ═══════════════════════════════════════════════════════════

function renderGantt(canvas, blocks, clockMs, startMs, endMs) {
    const dpr  = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    if (rect.width < 10 || rect.height < 10) return;

    canvas.width  = rect.width  * dpr;
    canvas.height = rect.height * dpr;

    const ctx  = canvas.getContext('2d');
    ctx.scale(dpr, dpr);
    const W = rect.width, H = rect.height;

    const MACHINES   = ['M1', 'M2', 'M3'];
    const LEFT       = 36;
    const RIGHT      = 6;
    const TOP        = 4;
    const BOTTOM     = 18;
    const rowH       = (H - TOP - BOTTOM) / MACHINES.length;
    const chartW     = W - LEFT - RIGHT;
    const totalMs    = endMs - startMs;
    const px         = chartW / totalMs;   // pixels per ms

    // Background
    ctx.fillStyle = '#0d1117';
    ctx.fillRect(0, 0, W, H);

    // Row backgrounds
    MACHINES.forEach((_, i) => {
        ctx.fillStyle = i % 2 === 0 ? '#0f131a' : '#0d1117';
        ctx.fillRect(LEFT, TOP + i * rowH, chartW, rowH);
    });

    // Grid lines every 5 minutes
    const gridStep = 5 * 60 * 1000;
    const firstGrid = startMs + (gridStep - (startMs % gridStep)) % gridStep;
    ctx.strokeStyle = '#21262d';
    ctx.lineWidth = 1;
    ctx.font = '9px Consolas';
    ctx.fillStyle = '#4d5565';
    ctx.textAlign = 'center';
    for (let t = firstGrid; t <= endMs; t += gridStep) {
        const x = LEFT + (t - startMs) * px;
        if (x < LEFT || x > W - RIGHT) continue;
        ctx.beginPath(); ctx.moveTo(x, TOP); ctx.lineTo(x, H - BOTTOM); ctx.stroke();
        const d = new Date(t);
        const lbl = d.getUTCHours().toString().padStart(2,'0') + ':' + d.getUTCMinutes().toString().padStart(2,'0');
        ctx.fillStyle = '#4d5565';
        ctx.fillText(lbl, x, H - 4);
    }

    // Machine labels
    ctx.textAlign = 'right';
    ctx.font = 'bold 11px Consolas';
    MACHINES.forEach((m, i) => {
        ctx.fillStyle = '#6e7681';
        ctx.fillText(m, LEFT - 4, TOP + i * rowH + rowH / 2 + 4);
    });

    // Gantt blocks
    (blocks || []).forEach(b => {
        const row = MACHINES.indexOf(b.resource);
        if (row < 0) return;
        const bStart = Math.max(b.startMs, startMs);
        const bEnd   = Math.min(b.endMs ?? clockMs, endMs);
        if (bEnd <= bStart) return;

        const x  = LEFT + (bStart - startMs) * px;
        const bW = (bEnd - bStart) * px;
        const y  = TOP + row * rowH + 2;
        const bH = rowH - 4;

        ctx.fillStyle = b.color;
        ctx.fillRect(x, y, Math.max(bW, 1.5), bH);

        // Queue label when wide enough
        if (bW > 22 && b.queue) {
            ctx.fillStyle = 'rgba(0,0,0,0.65)';
            ctx.font = '9px Consolas';
            ctx.textAlign = 'center';
            ctx.fillText(b.queue, x + bW / 2, y + bH / 2 + 3);
        }
    });

    // Current-time marker
    if (clockMs >= startMs && clockMs <= endMs) {
        const cx = LEFT + (clockMs - startMs) * px;
        ctx.strokeStyle = 'rgba(255,255,255,0.7)';
        ctx.lineWidth = 1.5;
        ctx.setLineDash([5, 3]);
        ctx.beginPath(); ctx.moveTo(cx, TOP); ctx.lineTo(cx, H - BOTTOM); ctx.stroke();
        ctx.setLineDash([]);
    }

    // Border
    ctx.strokeStyle = '#21262d';
    ctx.lineWidth = 1;
    ctx.strokeRect(LEFT, TOP, chartW, H - TOP - BOTTOM);
}

function renderForecastPlaceholder(canvas, clockMs, startMs, endMs) {
    const dpr  = window.devicePixelRatio || 1;
    const rect = canvas.getBoundingClientRect();
    if (rect.width < 10 || rect.height < 10) return;

    canvas.width  = rect.width  * dpr;
    canvas.height = rect.height * dpr;

    const ctx = canvas.getContext('2d');
    ctx.scale(dpr, dpr);
    const W = rect.width, H = rect.height;

    ctx.fillStyle = '#0d1117';
    ctx.fillRect(0, 0, W, H);

    // Subtle grid to hint future structure
    ctx.strokeStyle = '#161b22';
    ctx.lineWidth = 1;
    for (let x = 36; x < W - 6; x += (W - 42) / 8) {
        ctx.beginPath(); ctx.moveTo(x, 4); ctx.lineTo(x, H - 18); ctx.stroke();
    }

    ctx.fillStyle = '#30363d';
    ctx.font = '13px Consolas';
    ctx.textAlign = 'center';
    ctx.fillText('Forecast — coming soon', W / 2, H / 2 - 8);
    ctx.font = '10px Consolas';
    ctx.fillStyle = '#21262d';
    ctx.fillText('Will show IzziForecast projected Gantt', W / 2, H / 2 + 10);

    ctx.strokeStyle = '#21262d';
    ctx.lineWidth = 1;
    ctx.strokeRect(36, 4, W - 42, H - 22);
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

        const dot  = r.queue && Q_COLORS[r.queue]
            ? `<span class="qdot" style="background:${Q_COLORS[r.queue]}"></span>` : '';
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
        const color = Q_COLORS[q.name] || '#555';
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
        const color = Q_COLORS[name] || '#8b949e';
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
