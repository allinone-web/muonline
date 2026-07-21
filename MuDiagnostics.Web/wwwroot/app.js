(() => {
  'use strict';

  const COLORS = {
    accent: '#6fe7c8', blue: '#5fa8ff', danger: '#ff6f7d', violet: '#a989ff',
    warning: '#ffcb6b', steel: '#b0c4d8', grid: 'rgba(112,137,168,.12)', text: '#718198'
  };
  const state = {
    snapshots: [], events: [], latest: null, status: null,
    paused: false, rangeSeconds: 300, renderQueued: false, source: null,
    analysis: null, importedAnalysis: false, analysisTimer: null,
    demo: new URLSearchParams(location.search).get('demo') === '1'
  };
  const $ = id => document.getElementById(id);
  const number = (v, digits = 0) => Number.isFinite(Number(v)) ? Number(v).toLocaleString('pl-PL', { maximumFractionDigits: digits, minimumFractionDigits: digits }) : '—';
  const ms = v => `${number(v, 1)} ms`;
  const percent = v => `${number(v, 1)}%`;
  const statusClass = (ok, warning = false) => ok ? 'good' : warning ? 'warn' : 'bad';
  const snapshotOf = envelope => envelope && envelope.snapshot;

  async function init() {
    bindControls();
    bindNavigation();
    window.addEventListener('resize', scheduleRender);

    if (state.demo) {
      seedDemoHistory();
      state.analysis = buildDemoAnalysis();
      if (new URLSearchParams(location.search).get('static') !== '1')
        startDemo();
      scheduleRender();
      return;
    }

    await loadInitialData();
    await reloadAnalysis();
    connectLiveStream();
    state.analysisTimer = setInterval(() => {
      if (!state.paused && !state.importedAnalysis)
        reloadAnalysis();
    }, 2500);
    scheduleRender();
  }

  function bindControls() {
    $('range-select').addEventListener('change', async event => {
      state.rangeSeconds = Number(event.target.value);
      $('export-button').href = `/api/export.csv?seconds=${state.rangeSeconds}`;
      $('report-button').href = `/api/report.md?seconds=${state.rangeSeconds}`;
      state.importedAnalysis = false;
      $('import-button').classList.remove('import-active');
      $('import-button').textContent = 'Analizuj CSV';
      await Promise.all([reloadHistory(), reloadAnalysis()]);
    });
    $('pause-button').addEventListener('click', () => {
      state.paused = !state.paused;
      $('pause-button').textContent = state.paused ? 'Wznów' : 'Wstrzymaj';
    });
    $('reset-button').addEventListener('click', async () => {
      await fetch('/api/session/reset', { method: 'POST' }).catch(() => null);
      state.snapshots = [];
      state.events = [];
      state.latest = null;
      state.analysis = null;
      state.importedAnalysis = false;
      $('import-button').classList.remove('import-active');
      $('import-button').textContent = 'Analizuj CSV';
      await reloadAnalysis();
      scheduleRender();
    });

    $('import-button').addEventListener('click', () => $('import-file').click());
    $('import-file').addEventListener('change', async event => {
      const file = event.target.files?.[0];
      if (!file) return;
      const form = new FormData();
      form.append('file', file);
      $('import-button').textContent = 'Analizuję…';
      try {
        const response = await fetch('/api/analyze.csv', { method: 'POST', body: form });
        if (!response.ok) throw new Error(await response.text());
        state.analysis = await response.json();
        state.importedAnalysis = true;
        $('import-button').classList.add('import-active');
        $('import-button').textContent = 'CSV aktywny';
        scheduleRender();
        document.querySelector('#analysis')?.scrollIntoView({ behavior: 'smooth' });
      } catch (error) {
        console.warn('Could not analyze CSV', error);
        $('import-button').textContent = 'Błąd CSV';
        setTimeout(() => { $('import-button').textContent = 'Analizuj CSV'; }, 1800);
      } finally {
        event.target.value = '';
      }
    });
  }

  function bindNavigation() {
    const links = [...document.querySelectorAll('.nav-link')];
    const sections = links.map(link => document.querySelector(link.getAttribute('href'))).filter(Boolean);
    const observer = new IntersectionObserver(entries => {
      const visible = entries.filter(x => x.isIntersecting).sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0];
      if (!visible) return;
      links.forEach(link => link.classList.toggle('active', link.getAttribute('href') === `#${visible.target.id}`));
    }, { rootMargin: '-20% 0px -70% 0px', threshold: [0, .1, .5] });
    sections.forEach(section => observer.observe(section));
  }

  async function loadInitialData() {
    try {
      const [statusResponse, historyResponse, eventsResponse] = await Promise.all([
        fetch('/api/status'), fetch(`/api/history?seconds=${state.rangeSeconds}`), fetch('/api/events?limit=300')
      ]);
      if (statusResponse.ok) state.status = await statusResponse.json();
      if (historyResponse.ok) state.snapshots = await historyResponse.json();
      if (eventsResponse.ok) state.events = await eventsResponse.json();
      state.latest = state.status?.latest || state.snapshots.at(-1) || null;
      trimState();
    } catch (error) {
      console.warn('Diagnostics API is not available', error);
    }
  }

  async function reloadHistory() {
    try {
      const response = await fetch(`/api/history?seconds=${state.rangeSeconds}`);
      if (response.ok) state.snapshots = await response.json();
      trimState();
      scheduleRender();
    } catch (error) {
      console.warn('Could not reload history', error);
    }
  }


  async function reloadAnalysis() {
    if (state.demo) {
      state.analysis = buildDemoAnalysis();
      scheduleRender();
      return;
    }
    if (state.importedAnalysis) return;
    try {
      const response = await fetch(`/api/analysis?seconds=${state.rangeSeconds}`);
      if (response.ok) {
        state.analysis = await response.json();
        scheduleRender();
      }
    } catch (error) {
      console.warn('Could not load analysis', error);
    }
  }

  function connectLiveStream() {
    if (state.source) state.source.close();
    state.source = new EventSource('/api/live');
    state.source.onmessage = event => {
      if (state.paused) return;
      try {
        const envelope = JSON.parse(event.data);
        if (envelope.kind === 'snapshot' && envelope.snapshot) {
          state.snapshots.push(envelope);
          state.latest = envelope;
        } else if (envelope.kind === 'event' && envelope.event) {
          state.events.push(envelope);
        } else if (envelope.kind === 'hello') {
          state.status = { ...(state.status || {}), pipeConnected: true, client: envelope.client, activeSessionId: envelope.sessionId };
        }
        trimState();
        scheduleRender();
      } catch (error) {
        console.warn('Invalid live telemetry message', error);
      }
    };
    state.source.onerror = () => {
      if (state.status) state.status.pipeConnected = false;
      scheduleRender();
    };
    state.source.onopen = async () => {
      try {
        const response = await fetch('/api/status');
        if (response.ok) state.status = await response.json();
      } catch { /* retry is handled by EventSource */ }
      scheduleRender();
    };
  }

  function trimState() {
    const cutoff = Date.now() - state.rangeSeconds * 1000;
    state.snapshots = state.snapshots.filter(x => Date.parse(x.timestampUtc) >= cutoff && x.snapshot);
    if (state.snapshots.length > 10000) state.snapshots = state.snapshots.slice(-10000);
    if (state.events.length > 1000) state.events = state.events.slice(-1000);
  }

  function scheduleRender() {
    if (state.renderQueued) return;
    state.renderQueued = true;
    requestAnimationFrame(() => {
      state.renderQueued = false;
      render();
    });
  }

  function render() {
    renderConnection();
    const latest = snapshotOf(state.latest || state.snapshots.at(-1));
    renderOverview(latest);
    renderMetrics(latest);
    renderCharts();
    renderAnalysis();
    renderEvents();
  }

  function renderConnection() {
    const connected = Boolean(state.status?.pipeConnected) || Boolean(state.latest && Date.now() - Date.parse(state.latest.timestampUtc) < 3000);
    $('sidebar-dot').className = `status-dot ${connected ? 'online' : 'offline'}`;
    $('sidebar-status').textContent = connected ? 'Klient połączony' : 'Oczekiwanie na klienta';
    const session = state.status?.activeSessionId || state.latest?.sessionId;
    $('sidebar-session').textContent = session ? `session ${session.slice(0, 10)}` : 'brak sesji';
    const latest = snapshotOf(state.latest);
    if (latest) {
      const world = latest.session.worldIndex == null ? '' : ` · World ${latest.session.worldIndex}`;
      $('context-line').textContent = `${latest.session.scene}${world} · ${new Date(state.latest.timestampUtc).toLocaleTimeString('pl-PL')} · ${state.snapshots.length} próbek`;
    } else {
      $('context-line').textContent = state.demo ? 'Tryb demonstracyjny dashboardu.' : 'Uruchom klienta gry, aby rozpocząć pomiary.';
    }
  }

  function fullFrameP50(frame) { return frame.wallP50Ms > 0 ? frame.wallP50Ms : frame.p50Ms; }
  function isFrameActive(frame) { return frame.isActive !== false; }
  function fullFrameP95(frame) { return frame.wallP95Ms > 0 ? frame.wallP95Ms : frame.p95Ms; }
  function fullFrameP99(frame) { return frame.wallP99Ms > 0 ? frame.wallP99Ms : frame.p99Ms; }

  function renderOverview(s) {
    if (!s) {
      ['kpi-fps','kpi-p95','kpi-p99','kpi-draws','kpi-visible','kpi-memory'].forEach(id => $(id).textContent = '—');
      applyHealth(null);
      return;
    }
    $('kpi-fps').textContent = number(s.frame.fps);
    $('kpi-fps-note').textContent = `UPS ${number(s.frame.ups)} · update ${number(s.frame.updateMs, 1)} ms`;
    $('kpi-p95').textContent = ms(fullFrameP95(s.frame));
    $('kpi-p99').textContent = ms(fullFrameP99(s.frame));
    $('kpi-draws').textContent = number(s.rendering.estimatedDrawCalls);
    $('kpi-visible').textContent = number(s.world.visibleObjects);
    $('kpi-culling-note').textContent = `culling ${number(s.world.cullMs, 2)} ms · ${s.world.cullWasRebuild ? 'rebuild' : 'cached'}`;
    $('kpi-memory').textContent = `${number(s.runtime.workingSetMb)} MB`;
    $('kpi-cpu-note').textContent = `CPU ${number(s.runtime.processCpuPercent, 1)}% · heap ${number(s.runtime.managedMemoryMb)} MB`;
    applyHealth(s);
  }

  function applyHealth(s) {
    const badge = $('health-badge');
    if (!s) {
      badge.className = 'health-badge waiting';
      badge.querySelector('strong').textContent = 'WAITING';
      $('health-score').textContent = '—';
      return;
    }
    const frameScore = scoreDown(fullFrameP95(s.frame), 12, 45);
    const queuePressure = Math.max(s.runtime.mainThreadQueued, s.runtime.schedulerQueued);
    const slowActionPenalty = Math.min(100, Math.max(0, (s.runtime.mainThreadLongestActionMs || 0) - 2) * 8);
    const queueScore = Math.max(0, scoreDown(queuePressure, 5, 180) - slowActionPenalty);
    const animationScore = s.animation.gpuSkinningEnabled && s.animation.gpuSkinningSupported ? 100 : s.animation.gpuSkinningEnabled ? 25 : 65;
    const memoryPenalty = s.frame.gen2Collections * 18 + Math.max(0, s.frame.allocatedKb - 256) / 20;
    const memoryScore = Math.max(0, 100 - memoryPenalty);
    const total = Math.round(frameScore * .45 + queueScore * .2 + animationScore * .2 + memoryScore * .15);
    const cls = total >= 80 ? 'healthy' : total >= 55 ? 'busy' : 'slow';
    badge.className = `health-badge ${cls}`;
    badge.querySelector('strong').textContent = cls === 'healthy' ? 'HEALTHY' : cls === 'busy' ? 'BUSY' : 'SLOW';
    $('health-score').textContent = total;
    setHealthLine('health-frame', frameScore);
    setHealthLine('health-main', queueScore);
    setHealthLine('health-animation', animationScore);
    setHealthLine('health-memory', memoryScore);
    $('health-advice').textContent = healthAdvice(s, total);
  }

  function scoreDown(value, good, bad) {
    if (value <= good) return 100;
    if (value >= bad) return 0;
    return Math.round(100 * (1 - (value - good) / (bad - good)));
  }
  function setHealthLine(id, score) {
    const el = $(id);
    el.textContent = score >= 80 ? 'good' : score >= 55 ? 'watch' : 'poor';
    el.className = score >= 80 ? 'good' : score >= 55 ? 'warn' : 'bad';
  }
  function healthAdvice(s, total) {
    const fullP95 = fullFrameP95(s.frame);
    if (!isFrameActive(s.frame) && fullP95 > Math.max(25, s.frame.p95Ms * 2)) return `Okno gry jest nieaktywne; pełny frametime p95 wynosi ${number(fullP95,1)} ms, ale CPU p95 tylko ${number(s.frame.p95Ms,1)} ms.`;
    if ((s.runtime.mainThreadLongestActionMs || 0) > 8) return `Pojedyncza akcja głównego wątku trwała ${number(s.runtime.mainThreadLongestActionMs,1)} ms: ${s.runtime.mainThreadLongestActionName || 'unnamed action'}.`;
    if (fullP95 > 25) return `Największy problem to pełny frametime p95 (${number(fullP95,1)} ms). Porównaj CPU i czas poza profilerem.`;
    if (s.runtime.mainThreadQueued > 64 || s.runtime.schedulerQueued > 64) return 'Narasta kolejka pracy w tle. Ogranicz ładowanie assetów lub zwiększ budżet tylko na ekranie ładowania.';
    if (s.frame.gen2Collections > 0) return 'Wystąpiła kolekcja Gen2. Obserwuj alokacje i wzrost managed heap podczas zmiany map lub otwierania UI.';
    if (s.animation.multiPoseEnabled && s.animation.multiPoseMeshInstances > 0 && s.animation.multiPoseDrawCalls > s.animation.multiPoseMeshInstances / 2) return 'Multi-pose działa, ale batche są małe. Sprawdź różnice materiałów i przezroczyste meshe rozbijające grupy.';
    return total >= 80 ? 'Klient pracuje stabilnie. Nie widać obecnie dominującego wąskiego gardła.' : 'Wydajność jest akceptowalna, ale warto obserwować p95 oraz alokacje podczas intensywnej walki.';
  }

  function renderMetrics(s) {
    if (!s) return;
    set('terrain-draws', number(s.rendering.terrainDrawCalls));
    set('terrain-triangles', number(s.rendering.terrainTriangles));
    set('terrain-blocks', `${number(s.rendering.terrainBlocks)} / ${number(s.rendering.terrainCells)}`);
    set('grass-draws', number(s.rendering.grassDrawCalls));
    set('cull-candidates', number(s.world.cullCandidates)); set('cull-visible', number(s.world.visibleObjects));
    set('cull-time', ms(s.world.cullMs)); set('anim-skips', `${number(s.world.animationSkips)} / ${number(s.world.animationUpdates)}`);
    set('lights-registered', number(s.rendering.registeredLights)); set('lights-visible', `${number(s.rendering.visibleLights)} / ${number(s.rendering.uploadedLights)}`);
    setStatus('terrain-gpu', s.rendering.terrainLightingGpu, 'GPU', 'CPU'); setStatus('objects-gpu', s.rendering.objectLightingGpu, 'GPU', 'CPU');
    const passes = s.passes || {};
    set('pass-scene', `${number(passes.sceneDrawMs, 2)} / ${number(passes.sceneAfterMs, 2)} ms`);
    set('pass-world', `${number(passes.worldObjectsMs, 2)} / ${number((passes.terrainOpaqueMs || 0) + (passes.terrainAfterMs || 0), 2)} ms`);
    set('pass-effects', `${number(passes.shadowMs, 2)} / ${number(passes.postProcessMs, 2)} ms`);
    set('pass-preview', `${number(passes.previewMs, 2)} ms · ${number(passes.previewRenders)} render`);

    setStatus('gpu-status', s.animation.gpuSkinningEnabled && s.animation.gpuSkinningSupported, 'ACTIVE', s.animation.gpuSkinningEnabled ? 'UNAVAILABLE' : 'DISABLED');
    set('gpu-meshes', number(s.animation.gpuSkinnedMeshes)); set('gpu-batches', `${number(s.animation.gpuBatchDrawCalls)} / ${number(s.animation.gpuBatchedMeshes)}`);
    set('gpu-efficiency', ratio(s.animation.gpuBatchedMeshes, s.animation.gpuBatchDrawCalls));
    setStatus('mp-status', s.animation.multiPoseEnabled, 'ACTIVE', 'LEGACY'); set('mp-objects', `${number(s.animation.multiPoseObjects)} / ${number(s.animation.multiPoseUniquePoses)}`);
    set('mp-instances', number(s.animation.multiPoseMeshInstances)); set('mp-efficiency', ratio(s.animation.multiPoseMeshInstances, s.animation.multiPoseDrawCalls));
    setStatus('static-status', s.animation.staticInstancingEnabled, 'ACTIVE', 'OFF'); set('static-objects', number(s.animation.staticInstancedObjects));
    set('static-instances', number(s.animation.staticMeshInstances)); set('static-draws', number(s.animation.staticDrawCalls));
    set('mp-attempts', `${number(s.animation.multiPoseAttempts)} / ${number(s.animation.multiPoseQueuedObjects)}`);
    set('mp-reject-policy', `${number(s.animation.multiPoseRejectedObject)} / ${number(s.animation.multiPoseRejectedMesh)}`);
    set('mp-reject-data', `${number(s.animation.multiPoseRejectedBuffers)} / ${number(s.animation.multiPoseRejectedBones)}`);
    set('cpu-fallbacks', number(s.animation.cpuFallbackDrawCalls));

    set('process-cpu', percent(s.runtime.processCpuPercent)); set('process-memory', `${number(s.runtime.workingSetMb)} / ${number(s.runtime.privateMemoryMb)} MB`);
    set('managed-memory', `${number(s.runtime.managedMemoryMb)} MB`); set('process-threads', number(s.runtime.threadCount));
    set('main-queue', `${number(s.runtime.mainThreadProcessed)} / ${number(s.runtime.mainThreadQueued)}`);
    set('task-queue', `${number(s.runtime.schedulerProcessed)} / ${number(s.runtime.schedulerQueued)}`); set('main-time', ms(s.runtime.mainThreadMs));
    const intervalCpuMs = s.frame.frameIntervalCpuMs || s.frame.cpuFrameMs || (s.frame.updateMs + s.frame.drawMs);
    set('frame-current', `${ms(s.frame.frameIntervalMs)} / ${ms(intervalCpuMs)}`);
    set('frame-unaccounted', ms(s.frame.frameIntervalUnaccountedMs));
    const syncState = typeof s.frame.vSyncEnabled === 'boolean' ? (s.frame.vSyncEnabled ? 'VSync' : 'uncapped') : 'sync unknown';
    set('frame-state', `${isFrameActive(s.frame) ? 'active' : 'inactive'} / ${syncState}`);
    const longestActionName = s.runtime.mainThreadLongestActionName || '—';
    set('main-longest', s.runtime.mainThreadLongestActionMs > 0 ? `${number(s.runtime.mainThreadLongestActionMs, 2)} ms · ${longestActionName}` : '—');
    set('gc-counts', `${number(s.frame.gen0Collections)} / ${number(s.frame.gen1Collections)} / ${number(s.frame.gen2Collections)}`);
    set('bmd-hit', `${number(s.assets.cacheHits)} / ${number(s.assets.cacheMisses)}`); set('bmd-gpu', `${number(s.assets.gpuMeshBuffers)} / ${number(s.assets.gpuBatchBuffers)}`);
    set('bmd-topology', number(s.assets.meshTopologies)); set('bmd-pruned', `${number(s.assets.prunedGpuMeshes)} / ${number(s.assets.prunedGpuBatches)} / ${number(s.assets.prunedTopologies)}`);
  }

  function set(id, value) { $(id).textContent = value; }
  function setStatus(id, ok, yes, no) { const el = $(id); el.textContent = ok ? yes : no; el.className = ok ? 'good' : 'warn'; }
  function ratio(a, b) { return b > 0 ? `${number(a / b, 1)}×` : '—'; }

  function renderCharts() {
    const data = state.snapshots.map(snapshotOf).filter(Boolean);
    drawChart('frame-chart', data, [
      { get: x => fullFrameP50(x.frame), color: COLORS.accent, label: 'p50' },
      { get: x => fullFrameP95(x.frame), color: COLORS.blue, label: 'p95' },
      { get: x => fullFrameP99(x.frame), color: COLORS.danger, label: 'p99' }
    ], { unit: 'ms', target: 16.67 });
    drawChart('render-chart', data, [
      { get: x => x.rendering.estimatedDrawCalls, color: COLORS.accent },
      { get: x => x.world.visibleObjects, color: COLORS.violet }
    ]);
    drawChart('cpu-chart', data, [
      { get: x => x.frame.updateMs, color: COLORS.blue },
      { get: x => x.frame.drawMs, color: COLORS.warning }
    ], { unit: 'ms', target: 16.67 });
    drawChart('animation-chart', data, [
      { get: x => x.animation.multiPoseMeshInstances, color: COLORS.violet },
      { get: x => x.animation.multiPoseDrawCalls, color: COLORS.danger },
      { get: x => x.animation.multiPoseUniquePoses, color: COLORS.steel }
    ]);
    drawChart('palette-chart', data, [
      { get: x => x.animation.paletteBytes / 1024, color: COLORS.blue },
      { get: x => x.animation.paletteUploads, color: COLORS.warning },
      { get: x => x.animation.paletteDirtyRows, color: COLORS.steel }
    ]);
    drawChart('memory-chart', data, [
      { get: x => x.runtime.workingSetMb, color: COLORS.accent },
      { get: x => x.runtime.managedMemoryMb, color: COLORS.steel }
    ], { unit: 'MB' });
    drawChart('runtime-chart', data, [
      { get: x => x.frame.allocatedKb, color: COLORS.blue },
      { get: x => x.runtime.mainThreadQueued, color: COLORS.danger },
      { get: x => x.runtime.schedulerQueued, color: COLORS.warning }
    ]);
  }

  function drawChart(id, data, series, options = {}) {
    const canvas = $(id);
    const rect = canvas.getBoundingClientRect();
    if (rect.width <= 0 || rect.height <= 0) return;
    const dpr = Math.min(window.devicePixelRatio || 1, 2);
    canvas.width = Math.floor(rect.width * dpr); canvas.height = Math.floor(rect.height * dpr);
    const ctx = canvas.getContext('2d'); ctx.scale(dpr, dpr);
    const width = rect.width, height = rect.height, pad = { l: 50, r: 16, t: 18, b: 28 };
    ctx.clearRect(0, 0, width, height);
    const values = series.flatMap(s => data.map(s.get)).filter(Number.isFinite);
    if (values.length === 0) {
      ctx.fillStyle = '#58687d'; ctx.font = '11px system-ui'; ctx.textAlign = 'center';
      ctx.fillText('Brak danych telemetrycznych', width / 2, height / 2); return;
    }
    let max = Math.max(...values, options.target || 0, 1); max *= 1.12;
    const plotW = width - pad.l - pad.r, plotH = height - pad.t - pad.b;
    ctx.font = '9px ui-monospace, monospace'; ctx.textAlign = 'right'; ctx.textBaseline = 'middle';
    for (let i = 0; i <= 4; i++) {
      const y = pad.t + plotH * i / 4, value = max * (1 - i / 4);
      ctx.strokeStyle = COLORS.grid; ctx.lineWidth = 1; ctx.beginPath(); ctx.moveTo(pad.l, y); ctx.lineTo(width - pad.r, y); ctx.stroke();
      ctx.fillStyle = COLORS.text; ctx.fillText(`${compact(value)}${options.unit ? ' ' + options.unit : ''}`, pad.l - 8, y);
    }
    if (options.target && options.target < max) {
      const y = pad.t + plotH * (1 - options.target / max);
      ctx.strokeStyle = 'rgba(255,203,107,.42)'; ctx.setLineDash([5,5]); ctx.beginPath(); ctx.moveTo(pad.l, y); ctx.lineTo(width-pad.r,y); ctx.stroke(); ctx.setLineDash([]);
    }
    const maxPoints = Math.max(20, Math.floor(plotW / 3));
    const step = Math.max(1, Math.ceil(data.length / maxPoints));
    const sampled = data.filter((_, index) => index % step === 0 || index === data.length - 1);
    series.forEach(item => {
      ctx.strokeStyle = item.color; ctx.lineWidth = 1.6; ctx.lineJoin = 'round'; ctx.lineCap = 'round'; ctx.beginPath();
      sampled.forEach((point, index) => {
        const value = Number(item.get(point)); if (!Number.isFinite(value)) return;
        const x = pad.l + plotW * (sampled.length <= 1 ? 1 : index / (sampled.length - 1));
        const y = pad.t + plotH * (1 - Math.min(value, max) / max);
        if (index === 0) ctx.moveTo(x, y); else ctx.lineTo(x, y);
      }); ctx.stroke();
    });
    ctx.fillStyle = COLORS.text; ctx.textAlign = 'left'; ctx.textBaseline = 'bottom';
    ctx.fillText(`${state.rangeSeconds < 120 ? state.rangeSeconds + ' s' : Math.round(state.rangeSeconds/60) + ' min'} history`, pad.l, height - 7);
  }

  function compact(value) {
    if (Math.abs(value) >= 1_000_000) return `${number(value / 1_000_000, 1)}M`;
    if (Math.abs(value) >= 1000) return `${number(value / 1000, 1)}k`;
    return number(value, value < 10 ? 1 : 0);
  }


  function renderAnalysis() {
    const report = state.analysis;
    if (!report || !report.primarySegment) {
      set('analysis-bottleneck', '—');
      set('analysis-summary', report?.summary || 'brak danych');
      set('analysis-fps', '—');
      set('analysis-frame-p95', '—');
      set('analysis-draw-share', '—');
      set('analysis-allocation', '—');
      set('analysis-memory-slope', '—');
      $('analysis-recommendations').innerHTML = '<div class="empty-state">Czekam na wystarczającą liczbę próbek.</div>';
      return;
    }

    const p = report.primarySegment;
    $('analysis-source').textContent = state.importedAnalysis ? `CSV · ${report.sourceName}` : `live · ${report.sourceName}`;
    set('analysis-bottleneck', report.primaryBottleneck);
    set('analysis-summary', report.summary);
    set('analysis-fps', `${number(p.fpsMedian, 1)} / ${number(p.fpsP05, 1)}`);
    set('analysis-frame-p95', `${ms(p.frameIntervalP95Ms)} / ${ms(p.cpuFrameP95Ms)}`);
    set('analysis-draw-share', `${number(p.drawSharePercent, 0)}%`);
    set('analysis-allocation', `${number(p.allocationMbPerSecond, 1)} MB/s`);
    set('analysis-memory-slope', `${number(p.workingSetSlopeMbPerMinute, 1)} MB/min`);

    set('analysis-samples', number(report.sampleCount));
    set('analysis-duration', `${number(report.durationSeconds, 1)} s`);
    set('analysis-interval', `${number(report.dataQuality?.medianSampleIntervalMs, 0)} ms`);
    set('analysis-transitions', number(report.dataQuality?.sceneTransitionCount));
    setStatus('analysis-pass-data', Boolean(report.dataQuality?.hasRenderPassBreakdown), 'available', 'legacy CSV');
    setStatus('analysis-mp-data', Boolean(report.dataQuality?.hasMultiPoseRejectionCounters), 'available', 'not recorded');
    set('analysis-quality-note', report.dataQuality?.note || '—');

    const recommendations = report.recommendations || [];
    $('analysis-recommendations').innerHTML = recommendations.length
      ? recommendations.map(item => `<div class="recommendation-item"><strong>${escapeHtml(item.title)}</strong><p>${escapeHtml(item.detail)}</p></div>`).join('')
      : '<div class="empty-state">Brak istotnych rekomendacji dla bieżącego zakresu.</div>';

    const segments = report.segments || [];
    $('analysis-segments').innerHTML = segments.length
      ? segments.map(segment => {
          const primary = segment.segmentId === p.segmentId ? ' class="primary-row"' : '';
          return `<tr${primary}>
            <td>${escapeHtml(segment.scene)}</td>
            <td>${segment.worldIndex ?? '—'} / ${segment.mapId ?? '—'}</td>
            <td>${number(segment.durationSeconds, 1)} s</td>
            <td>${number(segment.fpsMedian, 1)} / ${number(segment.fpsP05, 1)}</td>
            <td>${number(segment.frameIntervalP95Ms || segment.cpuFrameP95Ms, 2)} / ${number(segment.cpuFrameP95Ms, 2)} ms</td>
            <td>${number(segment.updateMedianMs, 2)} / ${number(segment.drawMedianMs, 2)} ms</td>
            <td>${number(segment.visibleObjectsMedian, 0)}</td>
            <td>${number(segment.gpuSkinnedMeshesMedian, 0)}</td>
            <td>${number(segment.workingSetSlopeMbPerMinute, 1)} MB/min</td>
          </tr>`;
        }).join('')
      : '<tr><td colspan="9" class="empty-cell">Brak danych</td></tr>';

    const spikes = report.spikes || [];
    $('analysis-spikes').innerHTML = spikes.length
      ? spikes.map(spike => `<tr>
          <td>${escapeHtml(new Date(spike.timestampUtc).toLocaleTimeString('pl-PL'))}</td>
          <td>${number(spike.frameMs, 2)} ms</td>
          <td>${number(spike.updateMs, 2)} ms</td>
          <td>${number(spike.drawMs, 2)} ms</td>
          <td>${number(spike.mainThreadMs, 2)} ms</td>
          <td>${escapeHtml(spike.category)}</td>
          <td>${escapeHtml(spike.dominantPass)}</td>
          <td>${number(spike.allocatedKb, 0)} KB</td>
        </tr>`).join('')
      : '<tr><td colspan="8" class="empty-cell">Brak wykrytych skoków</td></tr>';
  }

  function buildDemoAnalysis() {
    return {
      sourceName: 'demo telemetry', sampleCount: 181, durationSeconds: 36,
      primaryBottleneck: 'render-bound: world models',
      summary: 'Demo segment indicates a rendering-dominated workload with stable update time.',
      primarySegment: {
        segmentId: 1, scene: 'GameScene', worldIndex: 1, mapId: 0, durationSeconds: 36,
        fpsMedian: 96.4, fpsP05: 61.2, cpuFrameP95Ms: 10.4, frameIntervalP95Ms: 17.8, updateMedianMs: 2.8,
        drawMedianMs: 7.1, drawSharePercent: 72, allocationMbPerSecond: 8.4,
        workingSetSlopeMbPerMinute: 3.2, visibleObjectsMedian: 122, gpuSkinnedMeshesMedian: 180
      },
      segments: [{
        segmentId: 1, scene: 'GameScene', worldIndex: 1, mapId: 0, durationSeconds: 36,
        fpsMedian: 96.4, fpsP05: 61.2, cpuFrameP95Ms: 10.4, frameIntervalP95Ms: 17.8, updateMedianMs: 2.8,
        drawMedianMs: 7.1, visibleObjectsMedian: 122, gpuSkinnedMeshesMedian: 180,
        workingSetSlopeMbPerMinute: 3.2
      }],
      spikes: [{
        timestampUtc: new Date().toISOString(), frameMs: 25.4, updateMs: 3.1, drawMs: 21.8,
        mainThreadMs: .1, allocatedKb: 112, category: 'render', dominantPass: 'world objects (14.8 ms)'
      }],
      recommendations: [
        { title: 'Prioritize rendering', detail: 'World model rendering dominates the sampled CPU frame.' },
        { title: 'Verify multi-pose eligibility', detail: 'Use rejection counters to understand why identical monsters do not enter multi-pose batches.' }
      ],
      dataQuality: {
        medianSampleIntervalMs: 200, sceneTransitionCount: 0,
        hasRenderPassBreakdown: true, hasMultiPoseRejectionCounters: true,
        note: 'Demo data includes the expanded v3 telemetry fields.'
      }
    };
  }

  function renderEvents() {
    const list = $('event-list');
    const events = state.events.filter(x => x.event).slice(-200).reverse();
    $('event-count').textContent = `${events.length} events`;
    if (!events.length) { list.innerHTML = '<div class="empty-state">Brak zdarzeń. Alerty pojawią się tutaj automatycznie.</div>'; return; }
    list.innerHTML = events.map(envelope => {
      const event = envelope.event;
      const severity = event.severity || 'info';
      return `<div class="event-row"><span class="event-time">${escapeHtml(new Date(envelope.timestampUtc).toLocaleTimeString('pl-PL'))}</span><span class="event-severity ${severityClass(severity)}">${escapeHtml(severity)}</span><span class="event-category">${escapeHtml(event.category)}</span><span class="event-message">${escapeHtml(event.message)}</span></div>`;
    }).join('');
  }
  function severityClass(severity) { return severity === 'error' || severity === 'critical' ? 'bad' : severity === 'warning' ? 'warn' : 'good'; }
  function escapeHtml(value) { return String(value ?? '').replace(/[&<>'"]/g, char => ({'&':'&amp;','<':'&lt;','>':'&gt;',"'":'&#39;','"':'&quot;'}[char])); }

  function seedDemoHistory() {
    const now = Date.now();
    for (let i = 180; i >= 0; i--) {
      const t = (180 - i) * .2;
      const jitter = Math.sin(t * 1.7) * 1.3 + Math.sin(t * .31) * .8;
      const p95 = 10.8 + jitter + (Math.sin(t / 7) > .86 ? 8 : 0);
      const envelope = {
        protocolVersion: 3, kind: 'snapshot', sessionId: 'demo-session',
        timestampUtc: new Date(now - i * 200).toISOString(), snapshot: demoSnapshot(t, p95)
      };
      state.snapshots.push(envelope);
      state.latest = envelope;
    }
    state.status = { pipeConnected: true, activeSessionId: 'demo-session' };
    state.events = [
      { kind: 'event', sessionId: 'demo-session', timestampUtc: new Date(now - 54000).toISOString(), event: { severity: 'info', category: 'scene', message: 'Loaded GameScene' } },
      { kind: 'event', sessionId: 'demo-session', timestampUtc: new Date(now - 23000).toISOString(), event: { severity: 'warning', category: 'frame', message: 'Frame p95 is elevated: 20.8 ms' } }
    ];
  }

  function startDemo() {
    let t = 0;
    setInterval(() => {
      if (state.paused) return;
      t += .2;
      const jitter = Math.sin(t * 1.7) * 1.3 + Math.random() * 1.2;
      const p95 = 10.8 + jitter + (Math.sin(t / 7) > .86 ? 8 : 0);
      const snapshot = demoSnapshot(t, p95);
      const envelope = { protocolVersion: 3, kind: 'snapshot', sessionId: 'demo-session', timestampUtc: new Date().toISOString(), snapshot };
      state.snapshots.push(envelope); state.latest = envelope;
      state.status = { pipeConnected: true, activeSessionId: 'demo-session' };
      trimState(); scheduleRender();
    }, 200);
  }

  function demoSnapshot(t, p95) {
    return {
      session: { scene: 'GameScene', worldName: 'World 1', worldIndex: 1, mapId: 0, playerX: 13420, playerY: 10840, playerZ: 114, frameIndex: Math.floor(t*300), uptimeSeconds: t },
      frame: { fps: 1000 / Math.max(6.5, p95 * .72), ups: 60, frameIndex: Math.floor(t*300), frameIntervalFrameIndex: Math.max(0, Math.floor(t*300)-1), rollingWindowStartFrameIndex: Math.max(0, Math.floor(t*300)-300), rollingWindowEndFrameIndex: Math.floor(t*300), rollingSampleCount: 300, rollingSequence: Math.floor(t*10), updateMs: 2.7 + Math.random(), drawMs: 5.2 + Math.random()*1.6, cpuFrameMs: 8.6, frameIntervalMs: p95*.72, frameIntervalCpuMs: 8.6, frameIntervalUnaccountedMs: Math.max(0, p95*.72-8.6), p50Ms: 8.1 + Math.random(), p95Ms: 10.4, p99Ms: 13.9, worstMs: 17.8, wallP50Ms: 9.4, wallP95Ms: p95, wallP99Ms: p95+3.5, wallWorstMs: p95+7, allocatedKb: 90+Math.random()*110, processAllocatedKb: 96+Math.random()*120, framesOver16Ms:1, framesOver33Ms:0, wallFramesOver16Ms:p95>16?8:1, wallFramesOver33Ms:0, gen0Collections:1, gen1Collections:0, gen2Collections:0, isActive:true, inactiveSleepMs:20, isFixedTimeStep:false, targetElapsedMs:1, vSyncEnabled:false },
      world: { cullCandidates: 460, visibleObjects: 118+Math.floor(Math.sin(t)*12), cullMs:.42, cullWasRebuild:false, modelObjects:94, spriteObjects:7, transparentObjects:17, animationUpdates:42, animationSkips:38, lowQualityObjects:29 },
      rendering: { terrainDrawCalls:34, terrainTriangles:182000, terrainBlocks:112, terrainCells:1792, grassDrawCalls:12, registeredLights:23, activeLights:12, visibleLights:8, uploadedLights:8, terrainLightingGpu:true, objectLightingGpu:true, fxaaEnabled:true, alphaRgbEnabled:false, estimatedDrawCalls:145+Math.floor(Math.random()*14) },
      animation: { gpuSkinningEnabled:true, gpuSkinningSupported:true, gpuSkinnedMeshes:122, gpuBatchDrawCalls:14, gpuBatchedMeshes:71, staticInstancingEnabled:true, staticInstancedObjects:144, staticMeshInstances:322, staticDrawCalls:18, multiPoseEnabled:true, multiPoseObjects:32, multiPoseMeshInstances:128, multiPoseUniquePoses:11, multiPoseDrawCalls:8, paletteUploads:2, paletteDirtyRows:4, paletteCacheHits:18, paletteBytes:32768, cpuFallbackDrawCalls:4, sharedPaletteHits:48, sharedPaletteMisses:3, multiPoseAttempts:36, multiPoseQueuedObjects:32, multiPoseRejectedObject:2, multiPoseRejectedMesh:2, multiPoseRejectedBuffers:0, multiPoseRejectedBones:0, multiPoseRejectedPalette:0 },
      passes: { sceneDrawMs:6.8, sceneAfterMs:.5, postProcessMs:.4, frameworkDrawMs:.05, shadowMs:.7, worldBaseMs:1.6, worldObjectsMs:4.5, terrainOpaqueMs:1.2, terrainAfterMs:.2, previewMs:0, previewRenders:0, previewCacheHits:0, previewCacheMisses:0, previewBudgetSkips:0 },
      runtime: { mainThreadQueued:3, mainThreadProcessed:8, mainThreadMs:.38, mainThreadLongestActionMs:.12, mainThreadLongestActionQueueMs:.04, mainThreadLongestActionName:'UI refresh', mainThreadBudgetExceeded:false, mainThreadBudgetOverrunMs:0, latestSlowActionSequence:0, latestSlowActionName:null, latestSlowActionPriority:null, latestSlowActionMs:0, latestSlowActionQueueMs:0, latestSlowActionAgeMs:0, schedulerQueued:2, schedulerProcessed:3, simulationSteps:1, simulationElapsedMs:16.67, simulationAlpha:.4, processCpuPercent:18+Math.random()*6, workingSetMb:690+Math.sin(t/8)*18, privateMemoryMb:820, managedMemoryMb:128+Math.sin(t/5)*8, threadCount:34, telemetryDroppedMessages:0 },
      assets: { vertexBufferUpdates:8, indexBufferUploads:0, verticesTransformed:3200, meshesProcessed:7, cacheHits:114, cacheMisses:3, gpuMeshBuffers:89, gpuBatchBuffers:31, meshTopologies:152, prunedGpuMeshes:0, prunedGpuBatches:0, prunedTopologies:0 }
    };
  }

  init();
})();
