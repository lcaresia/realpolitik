// Procedural orchestral score + sound effects for the clips (Web Audio, no audio files, no licences).
// window.RPAudio: enable(), disable(), on(), music(mood) -> stop(), and one method per effect.
(function () {
  let ctx = null, master, musicBus, sfxBus, revSend, noiseBuf, meter, enabled = false;

  function ensure() {
    if (ctx) return ctx.state === 'suspended' ? ctx.resume() : null;
    const AC = window.AudioContext || window.webkitAudioContext;
    ctx = new AC();
    const comp = ctx.createDynamicsCompressor();
    comp.threshold.value = -14; comp.ratio.value = 3.5; comp.attack.value = .01; comp.release.value = .3;
    master = ctx.createGain(); master.gain.value = .9;
    master.connect(comp).connect(ctx.destination);
    meter = ctx.createAnalyser(); meter.fftSize = 2048; comp.connect(meter);
    musicBus = ctx.createGain(); musicBus.gain.value = .5; musicBus.connect(master);
    sfxBus = ctx.createGain(); sfxBus.gain.value = .8; sfxBus.connect(master);
    // reverb: synthetic concert-hall impulse (stereo, slightly decorrelated)
    const len = ctx.sampleRate * 3.6, ir = ctx.createBuffer(2, len, ctx.sampleRate);
    for (let c = 0; c < 2; c++) { const d = ir.getChannelData(c); for (let i = 0; i < len; i++) d[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / len, 3.6) * (i < 600 ? i / 600 : 1); }
    const conv = ctx.createConvolver(); conv.buffer = ir;
    revSend = ctx.createGain(); revSend.gain.value = .6; revSend.connect(conv).connect(master);
    noiseBuf = ctx.createBuffer(1, ctx.sampleRate * 2, ctx.sampleRate);
    const nd = noiseBuf.getChannelData(0); for (let i = 0; i < nd.length; i++) nd[i] = Math.random() * 2 - 1;
  }

  const now = () => ctx.currentTime;
  // A music bus carries its own reverb send (bus._send) so muting/stopping the bus silences the reverb feed too.
  function out(node, bus, send) { node.connect(bus || sfxBus); if (send) { const s = ctx.createGain(); s.gain.value = send; node.connect(s).connect((bus && bus._send) || revSend); } }
  function envGain(t, a, peak, d, hold = 0) {
    const g = ctx.createGain(); g.gain.setValueAtTime(0, t);
    g.gain.linearRampToValueAtTime(peak, t + a);
    if (hold > 0) g.gain.setValueAtTime(peak, t + a + hold);
    g.gain.exponentialRampToValueAtTime(.0001, t + a + Math.max(hold, 0) + d);
    return g;
  }
  function osc(type, f, t, dur, { gain = .2, a = .005, d = dur, detune = 0, glide, bus, send, lp, q = .7, hold = 0 } = {}) {
    const o = ctx.createOscillator(); o.type = type; o.frequency.setValueAtTime(f, t); o.detune.value = detune;
    if (glide) o.frequency.exponentialRampToValueAtTime(glide, t + dur);
    const g = envGain(t, a, gain, d, hold); let n = o.connect(g);
    if (lp) { const fl = ctx.createBiquadFilter(); fl.type = 'lowpass'; fl.frequency.value = lp; fl.Q.value = q; n = g.connect(fl); }
    out(n, bus, send); o.start(t); o.stop(t + a + Math.max(hold, 0) + d + .05); return o;
  }
  function noise(t, dur, { type = 'bandpass', f = 1000, f2, q = 1, gain = .2, a = .005, d = dur, bus, send } = {}) {
    const s = ctx.createBufferSource(); s.buffer = noiseBuf; s.loop = true;
    const fl = ctx.createBiquadFilter(); fl.type = type; fl.frequency.setValueAtTime(f, t); fl.Q.value = q;
    if (f2) fl.frequency.exponentialRampToValueAtTime(f2, t + dur);
    const g = envGain(t, a, gain, d);
    out(s.connect(fl).connect(g), bus, send); s.start(t, Math.random()); s.stop(t + a + d + .05); return fl;
  }
  function vibrato(o, t, rate = 5.4, cents = 6, delay = .25) {
    const l = ctx.createOscillator(), g = ctx.createGain(); l.frequency.value = rate; g.gain.setValueAtTime(0, t); g.gain.linearRampToValueAtTime(cents, t + delay + .3);
    l.connect(g).connect(o.detune); l.start(t); return l;
  }

  // ---------- orchestral instruments ----------
  const N = m => 440 * Math.pow(2, (m - 69) / 12); // midi -> Hz
  const INS = {
    // string section: 3 detuned saws, vibrato, soft lowpass
    strings(m, t, len, { gain = .05, a = .35, r = .6, bus, bright = 1800 } = {}) {
      const f = N(m), fl = ctx.createBiquadFilter(); fl.type = 'lowpass'; fl.frequency.value = bright; fl.Q.value = .4;
      const g = envGain(t, a, gain, r, len - a); fl.connect(g); out(g, bus, .55);
      [-11, 0, 11].forEach(dt => { const o = ctx.createOscillator(); o.type = 'sawtooth'; o.frequency.value = f; o.detune.value = dt;
        const l = vibrato(o, t, 5 + Math.random(), 7); o.connect(fl); o.start(t); o.stop(t + len + r + .1); l.stop(t + len + r + .1); });
    },
    // short bowed string for ostinatos
    spic(m, t, { gain = .05, bus } = {}) { osc('sawtooth', N(m), t, .22, { gain, a: .01, d: .2, lp: 2200, bus, send: .3 }); osc('sawtooth', N(m), t, .22, { gain: gain * .6, a: .01, d: .2, detune: 9, lp: 2200, bus }); },
    // french horn: two saws, filter opens on attack, gentle vibrato
    horn(m, t, len, { gain = .07, bus } = {}) {
      const f = N(m), fl = ctx.createBiquadFilter(); fl.type = 'lowpass'; fl.Q.value = 1.1;
      fl.frequency.setValueAtTime(350, t); fl.frequency.linearRampToValueAtTime(1500, t + .12); fl.frequency.linearRampToValueAtTime(1000, t + Math.min(len, .8));
      const g = envGain(t, .07, gain, .45, len - .07); fl.connect(g); out(g, bus, .5);
      [-5, 5].forEach(dt => { const o = ctx.createOscillator(); o.type = 'sawtooth'; o.frequency.value = f; o.detune.value = dt;
        const l = vibrato(o, t, 5.2, 5, .35); o.connect(fl); o.start(t); o.stop(t + len + .5); l.stop(t + len + .5); });
    },
    // choir "aah": saw through two vocal formants
    choir(m, t, len, { gain = .05, bus } = {}) {
      const f = N(m), g = envGain(t, .8, gain, 1, len - .8); out(g, bus, .7);
      [[750, 5], [1150, 6], [2600, 8]].forEach(([ff, q], i) => { const b = ctx.createBiquadFilter(); b.type = 'bandpass'; b.frequency.value = ff; b.Q.value = q;
        const bg = ctx.createGain(); bg.gain.value = [1, .6, .25][i]; b.connect(bg).connect(g);
        [-8, 8].forEach(dt => { const o = ctx.createOscillator(); o.type = 'sawtooth'; o.frequency.value = f; o.detune.value = dt; const l = vibrato(o, t, 4.8, 9, .5);
          o.connect(b); o.start(t); o.stop(t + len + 1.1); l.stop(t + len + 1.1); }); });
    },
    harp(m, t, { gain = .06, bus } = {}) { osc('triangle', N(m), t, 1.6, { gain, a: .003, d: 1.6, bus, send: .65 }); osc('sine', N(m + 12), t, .8, { gain: gain * .35, a: .003, d: .8, bus, send: .65 }); },
    flute(m, t, len, { gain = .05, bus } = {}) {
      const o = osc('sine', N(m), t, len, { gain, a: .09, d: .35, hold: len - .09, bus, send: .55 }); vibrato(o, t, 5.5, 8, .3).stop(t + len + .5);
      noise(t, .12, { f: N(m) * 2, q: 6, gain: gain * .25, a: .03, bus });
    },
    timpani(m, t, { gain = .35, bus } = {}) { osc('sine', N(m) * 1.03, t, 1.4, { gain, glide: N(m), a: .004, d: 1.4, bus, send: .4 }); noise(t, .25, { type: 'lowpass', f: 260, gain: gain * .5, bus }); },
    taiko(t, { gain = .45, bus } = {}) { osc('sine', 62, t, .55, { gain, glide: 42, a: .003, bus, send: .35 }); noise(t, .18, { type: 'lowpass', f: 180, gain: gain * .6, bus }); },
    frame(t, { gain = .12, bus } = {}) { noise(t, .12, { f: 1600, q: 1, gain, a: .002, bus, send: .25 }); },
    bass(m, t, len, { gain = .12, bus } = {}) { osc('sawtooth', N(m), t, len, { gain, a: .05, d: .4, hold: len - .05, lp: 380, q: 1, bus }); osc('sine', N(m - 12), t, len, { gain: gain * .8, a: .05, d: .4, hold: len - .05, bus }); },
  };

  // ---------- compositions (8th-note steps, 4 bars = 32 steps, looping) ----------
  // Diplomacy theme (D dorian, i–VII–IV–i): majestic, measured, the Civ/Humankind "council chamber" feel.
  // War theme (D minor, i–VI–VII–i): driving string ostinato, heroic horns, taiko + timpani.
  // Intrigue theme (A minor, i–VI–iv–V): tremolo strings, rising horn motif, low pulse, timpani roll.
  const SCORES = {
    mystery: { bpm: 78, chords: [[38, [50, 53, 57]], [36, [48, 52, 55]], [43, [55, 59, 62]], [38, [50, 53, 57]]],
      lead: 'flute', melody: [[0, 69, 6], [6, 67, 2], [8, 64, 4], [12, 67, 4], [16, 71, 6], [22, 69, 2], [24, 65, 4], [28, 62, 4]],
      harp: true, choir: true, perc: { frame: [0, 5], timp: [0] } },
    war: { bpm: 104, chords: [[38, [50, 53, 57]], [34, [46, 50, 53]], [36, [48, 52, 55]], [38, [50, 53, 57]]],
      lead: 'horn', melody: [[0, 62, 4], [4, 69, 4], [8, 70, 6], [14, 69, 2], [16, 67, 4], [20, 69, 4], [24, 65, 3], [27, 67, 1], [28, 69, 4]],
      ostinato: [0, 12, 7, 12, 0, 12, 7, 12], perc: { taiko: [0, 3, 6], timp: [0, 4], frame: [2, 6], roll: true } },
    tension: { bpm: 88, chords: [[45, [57, 60, 64]], [41, [53, 57, 60]], [38, [50, 53, 57]], [40, [52, 56, 59]]],
      lead: 'horn', melody: [[0, 57, 2], [2, 60, 2], [4, 64, 4], [8, 65, 6], [16, 62, 2], [18, 65, 2], [20, 69, 4], [24, 68, 8]],
      tremolo: true, pulse: true, perc: { taiko: [0, 3], timp: [0], roll: true } },
  };

  const live = new Set();
  function music(mood) {
    if (!ctx || !enabled) return () => {};
    const sc = SCORES[mood] || SCORES.mystery;
    const bus = ctx.createGain(); bus.gain.setValueAtTime(0, now()); bus.gain.linearRampToValueAtTime(1, now() + 1.5); bus.connect(musicBus);
    bus._send = ctx.createGain(); bus._send.gain.setValueAtTime(0, now()); bus._send.gain.linearRampToValueAtTime(1, now() + 1.5); bus._send.connect(revSend);
    const st = 60 / sc.bpm / 2; // one 8th note
    let next = now() + .12, step = 0, alive = true;
    function play(s, t) {
      const i = s % 32, bar = Math.floor(i / 8), inBar = i % 8, [root, triad] = sc.chords[bar], barLen = st * 8;
      if (inBar === 0) {
        INS.bass(root, t, barLen * .95, { bus });
        triad.forEach(m => INS.strings(m, t, barLen, { gain: sc.ostinato ? .022 : .03, a: sc.ostinato ? .15 : .5, bus }));
        if (sc.choir) INS.choir(triad[0] + 12, t, barLen, { gain: .028, bus });
      }
      if (sc.ostinato) INS.spic(root + 12 + sc.ostinato[inBar], t, { gain: inBar === 0 ? .05 : .035, bus });
      if (sc.tremolo) [triad[1] + 12, triad[2] + 12].forEach(m => { INS.spic(m, t, { gain: .014, bus }); INS.spic(m, t + st / 2, { gain: .011, bus }); });
      if (sc.pulse && inBar % 2 === 0) INS.bass(root, t, st * .9, { gain: .1, bus });
      if (sc.harp) INS.harp(triad[inBar % 3] + (inBar >= 3 ? 24 : 12), t, { gain: .045, bus });
      // lead melody (+ an octave-lower double for the horns)
      sc.melody.forEach(([at, m, len]) => { if (at === i) {
        if (sc.lead === 'flute') INS.flute(m + 12, t, len * st, { gain: .045, bus });
        else { INS.horn(m, t, len * st, { gain: .065, bus }); INS.horn(m - 12, t, len * st, { gain: .035, bus }); }
      } });
      const p = sc.perc || {};
      if ((p.taiko || []).includes(inBar)) INS.taiko(t, { gain: inBar === 0 ? .5 : .32, bus });
      if ((p.timp || []).includes(inBar)) INS.timpani(root, t, { gain: inBar === 0 ? .3 : .2, bus });
      if ((p.frame || []).includes(inBar)) INS.frame(t, { gain: .09, bus });
      if (p.roll && bar === 3 && inBar >= 4) { INS.timpani(root, t, { gain: .08 + (inBar - 4) * .05, bus }); INS.timpani(root, t + st / 2, { gain: .08 + (inBar - 4) * .05, bus }); }
    }
    function schedule() { if (!alive) return; while (next < now() + .35) { play(step, next); next += st; step++; } }
    const id = setInterval(schedule, 80); schedule();
    live.add(id);
    return () => { if (!alive) return; live.delete(id); alive = false; clearInterval(id); const t = now();
      [bus.gain, bus._send.gain].forEach(g => { g.cancelScheduledValues(t); g.setValueAtTime(g.value, t); g.linearRampToValueAtTime(0, t + .35); });
      setTimeout(() => { bus.disconnect(); bus._send.disconnect(); }, 450); };
  }

  // ---------- sound effects ----------
  const fx = {
    quill(dur = 2) { const t0 = now(); let t = t0; while (t < t0 + dur) { noise(t, .04 + Math.random() * .05, { f: 2800 + Math.random() * 3500, q: 2.5, gain: .045 + Math.random() * .06, a: .004 }); t += .05 + Math.random() * .09; if (Math.random() < .12) t += .18; } },
    paper() { const t = now(); noise(t, .45, { type: 'highpass', f: 1400, gain: .14, a: .03 }); noise(t + .05, .35, { f: 3000, f2: 900, q: .8, gain: .09, a: .02, send: .2 }); },
    seal() { const t = now(); osc('sine', 130, t, .35, { gain: .9, glide: 42 }); noise(t, .14, { type: 'lowpass', f: 700, gain: .5 }); noise(t + .015, .06, { f: 2600, q: 3, gain: .25 }); },
    crack() { const t = now(); noise(t, .08, { f: 3200, q: 4, gain: .35 }); noise(t + .05, .1, { f: 1800, q: 3, gain: .25 }); osc('sine', 200, t, .12, { gain: .3, glide: 90 }); },
    pop() { const t = now(); osc('sine', 320, t, .16, { gain: .2, glide: 140, send: .25 }); osc('triangle', 640, t, .1, { gain: .04 }); },
    whoosh(dur = 1, gain = .2) { const t = now(); const fl = noise(t, dur, { f: 300, q: 1.4, gain, a: dur * .45, d: dur * .55, send: .35 });
      fl.frequency.exponentialRampToValueAtTime(2400, t + dur * .5); fl.frequency.exponentialRampToValueAtTime(400, t + dur); },
    chime(minor) { const t = now(); (minor ? [880, 1046.5, 1318.5] : [1046.5, 1568, 2093]).forEach((f, i) => osc('sine', f, t + i * .07, 1.8, { gain: .08, send: .7 })); },
    ping() { const t = now(); osc('sine', 660, t, .9, { gain: .07, send: .6 }); osc('sine', 990, t, .6, { gain: .03, send: .6 }); },
    tick() { const t = now(); osc('square', 2200, t, .03, { gain: .03, lp: 3000 }); },
    drum(s = 1) { const t = now(); INS.taiko(t, { gain: .7 * s }); },
    heartbeat() { const t = now(); INS.taiko(t, { gain: .35 }); INS.taiko(t + .18, { gain: .22 }); },
    march(dur = 2.6) { const t0 = now(); for (let i = 0, t = t0; t < t0 + dur; i++, t += .3) { INS.taiko(t, { gain: i % 4 === 0 ? .55 : .3 }); if (i % 2) INS.frame(t + .15, { gain: .1 }); } },
    boom() { const t = now(); osc('sine', 70, t, 1.4, { gain: 1, glide: 30, send: .4 }); noise(t, 1.1, { type: 'lowpass', f: 1200, f2: 120, gain: .55, send: .5 }); noise(t, .08, { f: 3000, q: 1, gain: .25 }); },
    horn() { const t = now(); [50, 57, 62].forEach((m, i) => INS.horn(m, t + i * .03, 1.6, { gain: .09 })); INS.timpani(38, t, { gain: .5 }); },
    stab() { const t = now(); [233.1, 246.9, 329.6, 466.2].forEach(f => osc('sawtooth', f, t, 1.3, { gain: .055, lp: 2600, send: .5 }));
      osc('sine', 55, t, 1.2, { gain: .7, glide: 35 }); noise(t, .25, { type: 'highpass', f: 2500, gain: .18, send: .4 }); },
    shimmer() { const t = now(); for (let i = 0; i < 5; i++) osc('sine', 1100 + i * 230, t + i * .06, 1.6, { gain: .03, glide: 2400 + i * 300, a: .3, send: .8 }); },
    waves(dur = 2.5) { const t = now(); noise(t, dur, { type: 'lowpass', f: 500, f2: 900, gain: .16, a: dur * .4, d: dur * .6, send: .3 }); },
    endhit() { const t = now(); [38, 45, 50, 57, 62].forEach(m => INS.horn(m, t, 2.2, { gain: .06 })); INS.timpani(38, t, { gain: .6 }); INS.taiko(t, { gain: .6 }); fx.chime(); },
  };

  const api = {
    enable() { enabled = true; ensure(); }, disable() { enabled = false; },
    on: () => enabled && !!ctx && ctx.state === 'running',
    music, live: () => live.size,
    level() { if (!meter) return 0; const a = new Float32Array(meter.fftSize); meter.getFloatTimeDomainData(a); return Math.sqrt(a.reduce((s, v) => s + v * v, 0) / a.length); },
    // mute effects already scheduled (e.g. a 2.6 s march) when playback pauses; restore on play
    hush(on) { if (!ctx) return; const t = now(), g = sfxBus.gain; g.cancelScheduledValues(t); g.setValueAtTime(g.value, t); g.linearRampToValueAtTime(on ? 0 : .8, t + (on ? .12 : .05)); },
  };
  Object.keys(fx).forEach(k => { api[k] = (...a) => { if (api.on()) try { fx[k](...a); } catch (e) { console.warn('RPAudio.' + k, e); } }; });
  window.RPAudio = api;
})();
