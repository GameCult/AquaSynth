// Recipe for fixtures/browser-capture.webm (needs playwright-core and a chromium headless shell; edit the executablePath). Records a real browser A/V clip: WebAudio beeps + canvas flashes at the same scheduled instants, muxed by MediaRecorder. Not the Playground UI.
const { chromium } = require('playwright-core');
const fs = require('fs');
(async () => {
  // CHROME=/path/to/chrome-headless-shell node record-browser-capture.cjs  (writes clip.webm and events.txt in the cwd)
  const browser = await chromium.launch({ executablePath: process.env.CHROME, args: ['--autoplay-policy=no-user-gesture-required'] });
  const pg = await (await browser.newContext({ viewport: { width: 320, height: 180 } })).newPage();
  await pg.setContent('<canvas id=c width=320 height=180></canvas>');
  const res = await pg.evaluate(async () => {
    const c = document.getElementById('c'), g = c.getContext('2d'), ctx = new AudioContext(), dest = ctx.createMediaStreamDestination();
    await ctx.resume();
    const stream = new MediaStream([...c.captureStream(30).getVideoTracks(), ...dest.stream.getAudioTracks()]);
    const rec = new MediaRecorder(stream, { mimeType: 'video/webm;codecs=vp8,opus' }), chunks = [];
    rec.ondataavailable = e => chunks.push(e.data);
    const hits = [1.0, 1.9, 2.8, 3.4, 4.5, 5.2, 6.3]; // seconds after recording start (intended instants)
    let flashes = [], t0, on = 0;
    const draw = (now) => {
      const t = (now - t0) / 1000; let lit = false;
      for (const h of hits) if (t >= h && t < h + 0.12) lit = true;
      g.fillStyle = lit ? '#fff' : `rgb(${20 + (Math.random() * 3) | 0},20,20)`; g.fillRect(0, 0, 320, 180);
      if (lit && !on) flashes.push(t); on = lit ? 1 : 0;
      if (t < 8) requestAnimationFrame(draw); else rec.stop();
    };
    const done = new Promise(r => rec.onstop = r);
    rec.start(100);
    t0 = performance.now(); const a0 = ctx.currentTime;
    for (const h of hits) { const o = ctx.createOscillator(), gn = ctx.createGain(); o.frequency.value = 880; gn.gain.setValueAtTime(0, a0 + h); gn.gain.linearRampToValueAtTime(0.5, a0 + h + 0.002); gn.gain.exponentialRampToValueAtTime(0.001, a0 + h + 0.15); o.connect(gn); gn.connect(dest); o.start(a0 + h); o.stop(a0 + h + 0.2); }
    requestAnimationFrame(draw);
    await done;
    const blob = new Blob(chunks, { type: 'video/webm' }), buf = new Uint8Array(await blob.arrayBuffer());
    let s = ''; for (let i = 0; i < buf.length; i += 8192) s += String.fromCharCode(...buf.subarray(i, i + 8192));
    return { b64: btoa(s), hits, flashes, baseLatency: ctx.baseLatency, outputLatency: ctx.outputLatency };
  });
  fs.writeFileSync('clip.webm', Buffer.from(res.b64, 'base64'));
  fs.writeFileSync('events.txt', res.hits.map((h, i) => `${h},hit${i}`).join('\n') + '\n');
  console.log(JSON.stringify({ hits: res.hits, flashes: res.flashes, base: res.baseLatency, out: res.outputLatency, bytes: fs.statSync('clip.webm').size }));
  await browser.close();
})();
