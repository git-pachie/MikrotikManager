(() => {
    const panel = document.querySelector('[data-traffic-url]');
    if (!panel) return;

    const pane = panel.closest('[role="tabpanel"]');
    const canvas = panel.querySelector('canvas');
    const graph = canvas.getContext('2d');
    const status = panel.querySelector('[role="status"]');
    const download = panel.querySelector('[data-download]');
    const upload = panel.querySelector('[data-upload]');
    const windowMs = 5 * 60 * 1000;
    const pollIntervalMs = 1000;
    const points = [];
    const messages = {
        offline: 'Offline. Waiting for a PPPoE connection.',
        unavailable: 'Traffic data unavailable. Retrying…',
        no_account: 'This customer has no provisioned PPPoE account.',
        mock: 'Live traffic requires a RouterOS connection.'
    };

    let previous;
    let timer;
    let request;
    let stopped = false;
    let segment = 0;

    function setStatus(message) {
        if (status.textContent !== message) status.textContent = message;
    }

    function draw() {
        const width = canvas.clientWidth;
        const height = canvas.clientHeight;
        if (!graph || !width || !height) return;

        const scale = window.devicePixelRatio || 1;
        canvas.width = Math.round(width * scale);
        canvas.height = Math.round(height * scale);
        graph.setTransform(scale, 0, 0, scale, 0, 0);
        const style = getComputedStyle(panel);
        const first = performance.now() - windowMs;
        while (points.length && points[0].at < first) points.shift();

        const peak = Math.max(0.1, ...points.map(point => Math.max(point.down, point.up)));
        const tickTarget = peak / 5;
        const magnitude = 10 ** Math.floor(Math.log10(tickTarget));
        const step = ([1, 2, 2.5, 5].find(value => value * magnitude >= tickTarget) ?? 10) * magnitude;
        const limit = 5 * step;
        const left = 3;
        const top = 9;
        const bottom = height - 25;
        graph.font = '11px Inter, system-ui, sans-serif';
        const tickLabels = Array.from({ length: 6 }, (_, i) =>
            (i * step).toLocaleString(undefined, { maximumFractionDigits: 3 }) + ' Mbps');
        const right = width - Math.ceil(Math.max(...tickLabels.map(label => graph.measureText(label).width))) - 10;
        if (right <= left) return;
        const y = value => bottom - (bottom - top) * value / limit;
        const x = at => left + (right - left) * (at - first) / windowMs;

        graph.fillStyle = style.getPropertyValue('--muted');
        graph.strokeStyle = style.getPropertyValue('--border');
        graph.textAlign = 'left';
        for (const [i, label] of tickLabels.entries()) {
            const line = y(i * step);
            graph.beginPath();
            graph.moveTo(left, line);
            graph.lineTo(right, line);
            graph.stroke();
            graph.fillText(label, right + 7, line + 4);
        }
        const minuteStep = right - left < 200 ? 5 : 1;
        for (let minute = 0; minute <= 5; minute += minuteStep) {
            graph.textAlign = minute === 0 ? 'left' : minute === 5 ? 'right' : 'center';
            graph.fillText(minute === 5 ? 'Now' : '−' + (5 - minute) + 'm', left + (right - left) * minute / 5, height - 5);
        }

        function plot(key, color, dash) {
            graph.strokeStyle = graph.fillStyle = style.getPropertyValue(color);
            graph.lineWidth = 1.5;
            graph.lineJoin = 'round';
            graph.setLineDash(dash);
            graph.beginPath();
            let priorSegment;
            for (const point of points) {
                const position = x(point.at);
                const rate = y(point[key]);
                if (point.segment === priorSegment) graph.lineTo(position, rate);
                else graph.moveTo(position, rate);
                priorSegment = point.segment;
            }
            graph.stroke();
            const latest = points.at(-1);
            if (latest?.segment === segment) {
                graph.beginPath();
                graph.arc(x(latest.at), y(latest[key]), 1.75, 0, 2 * Math.PI);
                graph.fill();
            }
        }

        plot('down', '--traffic-download', []);
        plot('up', '--traffic-upload', [4, 3]);
    }

    function showGap(message) {
        if (previous) segment++;
        previous = undefined;
        download.textContent = upload.textContent = '—';
        setStatus(message);
        draw();
    }

    function showSample(sample) {
        const counters = sample.counters;
        const time = Date.parse(sample.sampledAtUtc);
        if (!counters?.connectionKey || !Number.isFinite(time)
            || !Number.isFinite(counters.downloadBytes) || !Number.isFinite(counters.uploadBytes)
            || counters.downloadBytes < 0 || counters.uploadBytes < 0) {
            showGap(messages.unavailable);
            return;
        }

        const elapsed = previous ? (time - previous.time) / 1000 : 0;
        if (elapsed > 0 && elapsed <= 15 && previous.counters.connectionKey === counters.connectionKey
            && counters.downloadBytes >= previous.counters.downloadBytes && counters.uploadBytes >= previous.counters.uploadBytes) {
            const down = (counters.downloadBytes - previous.counters.downloadBytes) * 8 / elapsed / 1e6;
            const up = (counters.uploadBytes - previous.counters.uploadBytes) * 8 / elapsed / 1e6;
            const rate = value => value > 0 && value < 0.01 ? '<0.01 Mbps' : value.toFixed(2) + ' Mbps';
            points.push({ at: performance.now(), down, up, segment });
            download.textContent = rate(down);
            upload.textContent = rate(up);
            setStatus('Online. Updating every second.');
            draw();
        } else {
            showGap('Online. Measuring traffic…');
        }
        previous = { time, counters };
    }

    const visible = () => !pane.hidden && !document.hidden;

    async function poll() {
        timer = undefined;
        if (!visible()) return;
        const startedAt = performance.now();
        const current = request = new AbortController();
        let interval = pollIntervalMs;
        try {
            const response = await fetch(panel.dataset.trafficUrl, { cache: 'no-store', signal: current.signal });
            if (current.signal.aborted) return;
            if (response.redirected || [401, 403, 404].includes(response.status)) {
                stopped = true;
                showGap('Traffic is unavailable. Reopen Customer Details to try again.');
                return;
            }
            if (!response.ok) throw new Error();
            const sample = await response.json();
            if (current.signal.aborted) return;

            if (sample.state === 'online') {
                showSample(sample);
            } else {
                if (sample.state === 'unavailable') interval = 6000;
                stopped = sample.state === 'mock' || sample.state === 'no_account';
                showGap(messages[sample.state] ?? messages.unavailable);
            }
        } catch {
            interval = 6000;
            if (!current.signal.aborted) showGap(messages.unavailable);
        } finally {
            if (request === current) {
                request = undefined;
                if (visible() && !stopped) {
                    const delay = Math.max(0, interval - (performance.now() - startedAt));
                    timer = window.setTimeout(poll, delay);
                }
            }
        }
    }

    function pause() {
        clearTimeout(timer);
        timer = undefined;
        request?.abort();
        request = undefined;
        if (previous) segment++;
        previous = undefined;
    }

    function sync() {
        if (!visible()) {
            pause();
            return;
        }
        draw();
        if (!request && timer === undefined && !stopped) {
            download.textContent = upload.textContent = '—';
            setStatus('Checking the PPPoE connection…');
            void poll();
        }
    }

    new MutationObserver(sync).observe(pane, { attributes: true, attributeFilter: ['hidden'] });
    new MutationObserver(draw).observe(document.documentElement, { attributes: true, attributeFilter: ['data-bs-theme'] });
    new ResizeObserver(draw).observe(canvas);
    document.addEventListener('visibilitychange', sync);
    window.addEventListener('pagehide', pause);
    window.addEventListener('pageshow', sync);
    sync();
})();
