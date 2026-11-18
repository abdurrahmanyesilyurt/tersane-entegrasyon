// Mock ERP demo screen: reads /mock-admin/state every 3 seconds. Plain DOM APIs, text only (no innerHTML with data).
(function () {
    'use strict';
    const $ = (id) => document.getElementById(id);
    const fmt = (n) => Number(n).toLocaleString('tr-TR', { maximumFractionDigits: 3 });
    const time = (s) => new Date(s).toLocaleTimeString('tr-TR');

    function row(cells) {
        const tr = document.createElement('tr');
        for (const c of cells) {
            const td = document.createElement('td');
            if (c instanceof Node) td.appendChild(c); else td.textContent = c.text ?? c;
            if (c.cls) td.className = c.cls;
            tr.appendChild(td);
        }
        return tr;
    }

    function badge(text, cls) {
        const span = document.createElement('span');
        span.className = 'st ' + (cls || text);
        span.textContent = text;
        return span;
    }

    function button(text, onClick) {
        const b = document.createElement('button');
        b.type = 'button';
        b.textContent = text;
        b.addEventListener('click', onClick);
        return b;
    }

    async function post(url, method = 'POST', body) {
        await fetch(url, { method, headers: body ? { 'Content-Type': 'application/json' } : {}, body: body ? JSON.stringify(body) : undefined });
        await refresh();
    }

    async function refresh() {
        const state = await (await fetch('/mock-admin/state')).json();

        $('outage').checked = state.chaos.outage;
        $('rate').value = state.chaos.failureRate;
        $('rateOut').textContent = '%' + Math.round(state.chaos.failureRate * 100);
        $('latency').value = String(state.chaos.latencyMs);

        $('reqs').replaceChildren(...state.requisitions.map((r) => row([
            { text: r.requisitionNo, cls: 'mono' }, { text: r.externalRef, cls: 'mono' }, r.projectId ?? '—', r.lines.length,
            badge(r.objstate), time(r.changedAt),
            ['Released', 'Authorized'].includes(r.objstate)
                ? button('İlerlet →', () => post(`/mock-admin/requisitions/${encodeURIComponent(r.requisitionNo)}/advance`))
                : '',
        ])));
        if (state.requisitions.length === 0) $('reqs').replaceChildren(row([{ text: 'Henüz talep yok.' }]));

        $('stock').replaceChildren(...state.stock.map((s) => row([
            { text: s.partNo, cls: 'mono' }, s.locationNo, { text: fmt(s.qtyOnhand), cls: 'n' }, { text: fmt(s.qtyReserved), cls: 'n' },
        ])));

        $('parts').replaceChildren(...state.parts.map((p) => row([
            { text: p.partNo + ' — ' + p.description }, p.unitMeas,
            badge(p.partStatus === 'A' ? 'Aktif' : 'Kapalı', p.partStatus === 'A' ? 'Authorized' : 'Cancelled'),
            button(p.partStatus === 'A' ? 'Kapat' : 'Aç', () => post(`/mock-admin/parts/${encodeURIComponent(p.partNo)}/blocked/${p.partStatus === 'A'}`)),
        ])));
    }

    const chaos = () => post('/mock-admin/chaos', 'PUT', {
        outage: $('outage').checked, failureRate: Number($('rate').value), latencyMs: Number($('latency').value),
    });
    $('outage').addEventListener('change', chaos);
    $('latency').addEventListener('change', chaos);
    $('rate').addEventListener('input', () => { $('rateOut').textContent = '%' + Math.round($('rate').value * 100); });
    $('rate').addEventListener('change', chaos);
    $('revoke').addEventListener('click', () => post('/mock-admin/tokens/revoke'));

    refresh();
    setInterval(refresh, 3000);
})();
