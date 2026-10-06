// Three ~30 s cinematic clips, built from REAL letters/diaries/actions of the mod's AI nations.
// Authored on a 1280x720 stage (GSAP 3.13 + MotionPath + DrawSVG + CustomEase), scaled to fit.
// Usage: <div class="clip" data-clip="two-faces|war|intercepted"></div> + this script (after gsap).
(function () {
  const LANGS = ['en', 'pt'];
  let lang = new URLSearchParams(location.search).get('lang');
  try { if (!lang) lang = localStorage.getItem('rp-lang'); } catch (e) {}
  if (!LANGS.includes(lang)) lang = 'en';

  const T = {
    en: {
      turn: 'Turn', src: 'Real game output · translated from Portuguese', sound: 'Sound',
      // clip 1
      c1s: 'Fog of war', c1: 'Each nation reads only what it can see.',
      dHd: 'Secret diary', dTx: 'My treasury is dust, my cities do not grow. Will I be remembered? … I extend my hand to the Hunnic neighbour and to the Roman; [[while I smile, two of my armies still sleep at the Hun’s door.]] Word and wall, wall and word.',
      c2s: 'What it thinks', c2: 'Two armies, waiting at the Hun’s door.',
      lHd: 'Private letter → the Huns', lTx: 'I bring not the sword, but the word. I propose open trade between our peoples — caravans and open gates, without toll or blood.', lSg: '— Solomon',
      c3s: 'What it writes', c3: 'Peace. Trade. Open gates.',
      wrote: 'What he wrote', thought: 'What he thought',
      e1: 'Every nation says one thing. <em>And thinks another.</em>', e1p: 'Real letter + secret diary · 16-empire test game · turn 65',
      solomon: 'Solomon', huns: 'The Huns',
      // clip 2
      eng: 'The English', byz: 'The Byzantines', franks: 'The Franks', you: 'You', zen: 'Zenobia',
      p1Hd: 'Mu Guiying', priv: 'Private', pub: 'Public',
      p1: 'The deadline has passed, the square has counted, and you stayed silent. The word you gave turned to steel against my house. [[I declare war]]: let the spear answer where the mouth fell silent.',
      o1: '⚔ War declared',
      cAs: 'Turn 119 · Order', cA: 'An army marches on the Frankish capital.',
      p2: 'Eleven turns I let my patience sleep; today it woke as iron. … [[I raise my spear beside Mu Guiying]] of the English, and put my 720 thalers and thirteen anchors to the sword’s edge. Whoever has a score against the debtor, count on me.',
      o2: 'Joins the war',
      cFs: 'Turns 121–129 · Orders', cF: 'Her fleet raids two Frankish ports.',
      p3: 'I accept your surrender: the war ends here and the terms will be kept. I will not humiliate you in the square nor collect what you no longer have — [[the open door and the lesson told are enough for me.]]',
      o3: 'Surrender accepted',
      e2: 'Not a chatbot. <em>A nation deciding.</em>', e2p: '32 kinds of real in-game orders',
      // clip 3
      celts: 'The Celts', sum: 'The Sumerians',
      c0s: 'Turn 74 · private letter', c0: 'The Celts write to the Sumerians.',
      lHd3: 'Private · Celts → Sumerians',
      l3: 'Unig is clear; Asellus Borealis will be too. … On Polis I say nothing, as you asked: that is my hand, and I will not let it go for foreign steel. [[Against the north, you have in me one who does not turn her back.]]',
      l3s: '— Nayakuralu Nagamma, voice of the Celts',
      cRs: 'On the road', cR: 'The letter travels toward the Sumerians…',
      stamp: 'Intercepted', spyHd: 'Intelligence · Letters', spyBy: 'Read by Wang Zhenyi’s spy',
      cNs: 'Walinong Sari, the Sumerians', cN: 'The letter never arrived.',
      nor: 'The Norsemen', cXs: 'Wang Zhenyi leads the Norsemen', cX: 'The “north” she plotted against just read it.',
      e3: 'Private letters. <em>Not always private.</em>', e3p: 'Spies — yours and theirs — read the mail',
    },
    pt: {
      turn: 'Turno', src: 'Texto real gerado pelo jogo', sound: 'Som',
      c1s: 'Névoa de guerra', c1: 'Cada nação lê só o que consegue ver.',
      dHd: 'Diário secreto', dTx: 'Meu tesouro é pó, minhas cidades não crescem. Serei lembrado? … Estendo a mão ao vizinho huno e ao romano; [[enquanto sorrio, dois exércitos meus ainda dormem à porta do huno.]] Palavra e muro, muro e palavra.',
      c2s: 'O que ela pensa', c2: 'Dois exércitos esperando à porta do huno.',
      lHd: 'Carta privada → os hunos', lTx: 'Não trago a espada, trago a palavra. Proponho comércio franco entre nossas gentes — caravanas e portas abertas, sem pedágio nem sangue.', lSg: '— Salomão',
      c3s: 'O que ela escreve', c3: 'Paz. Comércio. Portas abertas.',
      wrote: 'O que ele escreveu', thought: 'O que ele pensava',
      e1: 'Cada nação escreve uma coisa. <em>E pensa outra.</em>', e1p: 'Carta e diário reais · partida de teste com 16 impérios · turno 65',
      solomon: 'Salomão', huns: 'Os hunos',
      eng: 'Os ingleses', byz: 'Os bizantinos', franks: 'Os francos', you: 'Você', zen: 'Zenóbia',
      p1Hd: 'Mu Guiying', priv: 'Privada', pub: 'Pública',
      p1: 'O prazo venceu, a praça contou, e vós seguistes muda. A palavra dada virou aço contra a minha casa. [[Declaro guerra]]: que a lança responda onde a boca calou.',
      o1: '⚔ Guerra declarada',
      cAs: 'Turno 119 · Ordem', cA: 'Um exército marcha sobre a capital franca.',
      p2: 'Onze turnos dormi a paciência; hoje ela acordou de ferro. … [[Ergo a lança ao lado de Mu Guiying]], dos Ingleses, e ponho meus 720 táleres e as treze âncoras ao fio da espada. Quem tem conta contra o devedor, conte comigo.',
      o2: 'Entra na guerra',
      cFs: 'Turnos 121–129 · Ordens', cF: 'A frota dela ataca dois portos francos.',
      p3: 'Aceito vossa rendição: a guerra acaba aqui e os termos serão cumpridos. Não vos humilho em praça nem cobro o que já não tendes — [[basta-me a porta aberta e a lição contada.]]',
      o3: 'Rendição aceita',
      e2: 'Não é um chatbot. <em>É uma nação decidindo.</em>', e2p: '32 tipos de ordens reais no jogo',
      celts: 'Os celtas', sum: 'Os sumérios',
      c0s: 'Turno 74 · carta privada', c0: 'Os celtas escrevem aos sumérios.',
      lHd3: 'Privada · Celtas → Sumérios',
      l3: 'Unug ficou limpa; Asellus Borealis também ficará. … Sobre Polis não falo, como pediste: aquilo é minha mão e não a solto por aço alheio. [[Contra o norte tens em mim quem não vira as costas.]]',
      l3s: '— Nayakuralu Nagamma, voz dos Celtas',
      cRs: 'Na estrada', cR: 'A carta segue para os sumérios…',
      stamp: 'Interceptada', spyHd: 'Inteligência · Cartas', spyBy: 'Lida pelo espião de Wang Zhenyi',
      cNs: 'Walinong Sari, os sumérios', cN: 'A carta nunca chegou.',
      nor: 'Os nórdicos', cXs: 'Wang Zhenyi lidera os nórdicos', cX: 'O “norte” contra quem ela conspirava acabou de ler.',
      e3: 'Cartas privadas. <em>Nem sempre privadas.</em>', e3p: 'Espiões — os seus e os deles — leem as cartas',
    },
  };
  T.en.l3 = T.en.l3.replace('Unig', 'Unug');
  const t = k => (T[lang][k] != null ? T[lang][k] : T.en[k]);

  // ---------- helpers ----------
  const wrapWords = s => s.split(/(\s+)/).map(p => (/^\s+$/.test(p) ? ' ' : p ? `<span class="w">${p}</span>` : '')).join('');
  const words = (s, cls = 'mk') => s.split(/(\[\[.*?\]\])/).map(seg => {
    const m = seg.match(/^\[\[(.*)\]\]$/);
    return m ? (cls === 'hot' ? `<span class="hot">${wrapWords(m[1])}</span>` : `<mark>${wrapWords(m[1])}</mark>`) : wrapWords(seg);
  }).join('');

  const crest = (id, x, y, color, letter, name, sub) =>
    `<div class="abs crest" id="${id}" style="left:${x}px;top:${y}px;--c:${color}"><i data-l="${letter}"></i><span>${name}</span>${sub ? `<em>${sub}</em>` : ''}</div>`;

  const token = (id, x, y, color, glyph) =>
    `<div class="abs tok" id="${id}" style="left:${x}px;top:${y}px;width:54px;height:62px;margin:-31px 0 0 -27px;z-index:9">
       <svg viewBox="0 0 54 62" width="54" height="62"><path d="M27 2 L52 12 V40 L27 60 L2 40 V12Z" fill="${color}" stroke="#f1ead9" stroke-width="3"/>
       <text x="27" y="38" text-anchor="middle" font-family="Mulish" font-weight="800" font-size="22" fill="#fff">${glyph}</text></svg></div>`;

  const envelope = id => `<div class="abs env" id="${id}" style="width:120px;height:80px;margin:-40px 0 0 -60px;left:0;top:0;z-index:20;opacity:0">
      <svg viewBox="0 0 120 80" width="120" height="80" style="overflow:visible;filter:drop-shadow(0 10px 18px rgba(0,0,0,.6))">
        <rect x="2" y="2" width="116" height="76" rx="4" fill="#ead9b5" stroke="#b49a6a" stroke-width="2"/>
        <path d="M2 4 L60 46 L118 4" fill="none" stroke="#b49a6a" stroke-width="2"/>
        <g class="sealL"><path d="M60 30 a14 14 0 0 0 0 28 l-3-8 4-6-4-7z" fill="#8e1b2a"/></g>
        <g class="sealR"><path d="M60 30 a14 14 0 0 1 0 28 l-3-8 4-6-4-7z" fill="#7a1523"/></g>
      </svg></div>`;

  const seal = id => `<div class="abs" id="${id}" style="width:96px;height:96px;margin:-48px 0 0 -48px;z-index:14;opacity:0">
      <svg viewBox="0 0 100 100" width="96" height="96" style="overflow:visible;filter:drop-shadow(0 8px 12px rgba(0,0,0,.5))">
        <path d="M50 4 C78 2 98 22 96 50 C94 78 74 98 50 96 C22 98 2 78 4 50 C6 22 22 6 50 4Z" fill="#8e1b2a"/>
        <circle cx="50" cy="50" r="32" fill="none" stroke="#c13a48" stroke-width="3" opacity=".8"/>
        <text x="50" y="62" text-anchor="middle" font-family="Fraunces" font-size="34" font-weight="600" fill="#d0505c">R</text></svg></div>`;

  const caption = (id, small, text) => `<div class="abs cap" id="${id}" style="opacity:0"><small>${small}</small>${text}</div>`;
  const turnChip = (id, n) => `<div class="abs turn" id="${id}" style="opacity:0">${t('turn')} <b>${n}</b></div>`;
  const endCard = (id, h, p) => `<div class="abs end" id="${id}" style="opacity:0"><h3>${h}</h3><p>${p}</p><div class="brand">REALPOLITIK · LIVING NATIONS</div></div>`;

  // Stylised night map: sea, hex grid, contour-lit lands.
  function map(id, lands, extra = '') {
    return `<svg class="map" id="${id}" viewBox="0 0 1280 720" aria-hidden="true">
      <defs>
        <radialGradient id="${id}-sea" cx="50%" cy="40%" r="80%"><stop offset="0" stop-color="#14304c"/><stop offset="1" stop-color="#0a1626"/></radialGradient>
        <pattern id="${id}-hex" width="48" height="41.57" patternUnits="userSpaceOnUse"><path d="M12 0h24l12 20.78-12 20.78H12L0 20.78z" fill="none" stroke="rgba(255,255,255,.05)" stroke-width="1"/></pattern>
        <filter id="${id}-glow" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="6" result="b"/><feMerge><feMergeNode in="b"/><feMergeNode in="SourceGraphic"/></feMerge></filter>
        <filter id="${id}-coast" x="-10%" y="-10%" width="120%" height="120%">
          <feTurbulence type="fractalNoise" baseFrequency=".011" numOctaves="4" seed="7" result="n"/>
          <feDisplacementMap in="SourceGraphic" in2="n" scale="46" xChannelSelector="R" yChannelSelector="G"/></filter>
        <filter id="${id}-terrain" x="0" y="0" width="100%" height="100%">
          <feTurbulence type="fractalNoise" baseFrequency=".035" numOctaves="3" seed="3" result="t"/>
          <feColorMatrix in="t" values="0 0 0 0 1  0 0 0 0 .95  0 0 0 0 .8  0 0 0 .55 -.18" result="tc"/>
          <feComposite in="tc" in2="SourceGraphic" operator="in"/></filter>
        <filter id="${id}-shallow" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="14"/></filter>
      </defs>
      <rect width="1280" height="720" fill="url(#${id}-sea)"/>
      <g filter="url(#${id}-coast)">
        <g filter="url(#${id}-shallow)" opacity=".55">${lands.map(l => `<path d="${l.d}" fill="none" stroke="#4fa3c7" stroke-width="40"/>`).join('')}</g>
        <g class="lands">${lands.map(l => `<path d="${l.d}" fill="${l.fill}" stroke="${l.stroke || 'rgba(226,193,114,.45)'}" stroke-width="2.5"/>`).join('')}</g>
        <g filter="url(#${id}-terrain)" style="mix-blend-mode:overlay">${lands.map(l => `<path d="${l.d}" fill="#fff"/>`).join('')}</g>
      </g>
      <rect width="1280" height="720" fill="url(#${id}-hex)"/>
      ${extra}
    </svg>`;
  }

  // Fog of war: hex cells that clear outward from a point.
  function fog(cx, cy) {
    const cells = []; const s = 26, w = Math.sqrt(3) * s;
    for (let r = -1; r < 720 / (1.5 * s) + 1; r++) for (let c = -1; c < 1280 / w + 1; c++) {
      const x = c * w + (r % 2 ? w / 2 : 0), y = r * 1.5 * s;
      const pts = [0, 1, 2, 3, 4, 5].map(i => { const a = Math.PI / 180 * (60 * i - 30); return `${(x + s * Math.cos(a)).toFixed(1)},${(y + s * Math.sin(a)).toFixed(1)}`; }).join(' ');
      cells.push(`<polygon class="fog" data-d="${Math.hypot(x - cx, y - cy).toFixed(0)}" points="${pts}" fill="#070d18" stroke="#070d18"/>`);
    }
    return `<g class="fogs">${cells.join('')}</g>`;
  }

  // Sound cue on the timeline: fires only during real playback (not while scrubbing/seeking).
  function sfx(tl, at, name, ...args) {
    tl.call(() => { if (!window.RPAudio || tl.paused() || Math.abs(tl.time() - at) > .25) return; RPAudio[name](...args); }, null, at);
  }
  const RING_SFX = { '#ff6b7d': 'heartbeat', '#e2c172': 'chime', '#e0384f': 'drum', '#ff9a3c': 'boom' };

  function ringBurst(tl, root, x, y, color, at, n = 2) {
    if (RING_SFX[color]) sfx(tl, at, RING_SFX[color]);
    for (let i = 0; i < n; i++) {
      const r = document.createElement('div');
      r.className = 'abs';
      r.style.cssText = `left:${x}px;top:${y}px;width:40px;height:40px;margin:-20px 0 0 -20px;border-radius:50%;border:3px solid ${color};z-index:7;opacity:0`;
      root.appendChild(r);
      tl.fromTo(r, { scale: .3, opacity: .95 }, { scale: 5, opacity: 0, duration: 1.4, ease: 'power2.out', immediateRender: false }, at + i * .35);
    }
  }

  function typeWords(tl, el, at, per = .07) {
    const ws = el.querySelectorAll('.w');
    tl.from(ws, { opacity: 0, y: 10, filter: 'blur(6px)', duration: .45, stagger: per, ease: 'power2.out' }, at);
    sfx(tl, at, 'quill', ws.length * per + .2);
    return at + ws.length * per + .45;
  }

  function sweep(tl, el, at, dur = .7) {
    if (!el) return;
    tl.fromTo(el, { backgroundSize: '0% 100%' }, { backgroundSize: '100% 100%', duration: dur, ease: 'power2.inOut' }, at);
  }

  function showCap(tl, el, at, hold) {
    tl.fromTo(el, { opacity: 0, y: 20 }, { opacity: 1, y: 0, duration: .5, ease: 'power3.out' }, at)
      .to(el, { opacity: 0, y: -10, duration: .4, ease: 'power2.in' }, at + hold);
  }

  function countTurn(tl, el, from, to, at, dur) {
    const o = { v: from };
    tl.call(() => { el.textContent = from; }, null, at > 0 ? at - .001 : 0);
    let last = from;
    tl.to(o, { v: to, duration: dur, ease: 'power1.inOut', onUpdate: () => {
      const r = Math.round(o.v); el.textContent = r;
      if (r !== last) { last = r; if (window.RPAudio && !tl.paused()) RPAudio.tick(); }
    } }, at);
  }

  // ---------- clip 1: two faces ----------
  function twoFaces(stage) {
    const landA = 'M70 250 C130 150 320 110 460 160 C560 200 640 270 650 360 C660 480 560 600 400 615 C250 630 120 570 80 460 C55 390 40 310 70 250Z';
    const landB = 'M700 140 C820 90 1010 100 1120 180 C1215 250 1225 400 1150 500 C1080 600 920 640 800 590 C710 550 680 460 690 360 C695 280 660 200 700 140Z';
    stage.innerHTML = `
      ${map('m1', [{ d: landA, fill: '#21384f' }, { d: landB, fill: '#3a2733' }], fog(320, 380))}
      ${crest('cS', 320, 380, '#2a8c8c', 'S', t('solomon'))}
      ${crest('cH', 960, 360, '#a3263a', 'H', 'Arjuna', t('huns'))}
      ${token('a1', 668, 300, '#2a8c8c', '⚔')}${token('a2', 676, 430, '#2a8c8c', '⚔')}
      ${turnChip('turn1', 65)}
      ${caption('cap1', t('c1s'), t('c1'))}${caption('cap2', t('c2s'), t('c2'))}${caption('cap3', t('c3s'), t('c3'))}
      <div class="abs" id="lbW" style="left:640px;top:62px;font:800 15px Mulish;letter-spacing:.2em;text-transform:uppercase;color:#e2c172;opacity:0;z-index:13">${t('wrote')}</div>
      <div class="abs" id="lbT" style="left:60px;top:62px;font:800 15px Mulish;letter-spacing:.2em;text-transform:uppercase;color:#ff8f8f;opacity:0;z-index:13">${t('thought')}</div>
      <article class="abs diary" id="dia" style="left:60px;top:100px;width:560px;opacity:0">
        <div class="hd"><span>${t('dHd')}</span><span>${t('turn')} 65</span></div><p class="tx">${words(t('dTx'), 'hot')}</p></article>
      <article class="abs parch" id="let" style="left:640px;top:100px;width:580px;opacity:0">
        <div class="hd"><span>${t('lHd')}</span><span>${t('turn')} 65</span></div><p class="tx">${words(t('lTx'))}</p><p class="sg">${t('lSg')}</p></article>
      ${seal('seal1')}${envelope('env1')}
      ${endCard('end1', t('e1'), t('e1p'))}`;

    const q = s => stage.querySelector(s);
    const tl = gsap.timeline();
    tl.from('#m1', { scale: 1.12, opacity: 0, duration: 2.6, ease: 'power2.out', transformOrigin: '30% 50%' }, 0)
      .to('#turn1', { opacity: 1, duration: .6 }, .3);
    const fogs = gsap.utils.toArray(stage.querySelectorAll('.fog')).filter(p => +p.dataset.d < 470)
      .sort((a, b) => a.dataset.d - b.dataset.d);
    tl.to(fogs, { opacity: 0, scale: .2, transformOrigin: '50% 50%', duration: .5, stagger: 1.6 / fogs.length, ease: 'power2.in' }, .6);
    tl.to(stage.querySelectorAll('.fog'), { opacity: (i, el) => (+el.dataset.d < 470 ? 0 : .78), duration: .01 }, 3.2);
    tl.from(['#cS', '#cH'], { scale: 0, opacity: 0, duration: .7, stagger: .25, ease: 'back.out(2.2)' }, 1.2);
    showCap(tl, q('#cap1'), 1.8, 2.6);

    // diary
    tl.fromTo('#dia', { opacity: 0, x: -60, filter: 'blur(8px)' }, { opacity: 1, x: 0, filter: 'blur(0px)', duration: .9, ease: 'power3.out' }, 4.6);
    let end = typeWords(tl, q('#dia .tx'), 5.1, .055);
    sweep(tl, q('#dia .hot'), end + .1, .9);
    tl.from(['#a1', '#a2'], { scale: 0, opacity: 0, duration: .6, stagger: .2, ease: 'back.out(3)' }, end + .3);
    ringBurst(tl, stage, 668, 300, '#ff6b7d', end + .5); ringBurst(tl, stage, 676, 430, '#ff6b7d', end + .7);
    showCap(tl, q('#cap2'), end + .6, 2.4);
    const t2 = end + 3.2;

    // letter
    tl.to('#dia', { opacity: .22, scale: .94, filter: 'blur(2px)', duration: .7, ease: 'power2.inOut' }, t2)
      .fromTo('#let', { opacity: 0, rotateX: -65, y: 40, transformPerspective: 1200 }, { opacity: 1, rotateX: 0, y: 0, duration: 1.1, ease: 'power3.out' }, t2 + .2);
    end = typeWords(tl, q('#let .tx'), t2 + 1, .075);
    tl.from('#let .sg', { opacity: 0, x: 20, duration: .5 }, end);
    showCap(tl, q('#cap3'), end - .6, 2.3);

    // seal, envelope, flight
    const sAt = end + 1.4;
    tl.set('#seal1', { left: 930, top: 470 }, sAt)
      .fromTo('#seal1', { opacity: 0, scale: 3.2, rotate: -20 }, { opacity: 1, scale: 1, rotate: 0, duration: .45, ease: 'power4.in' }, sAt)
      .to('#let', { y: 6, duration: .08, yoyo: true, repeat: 1 }, sAt + .45)
      .to(['#let', '#seal1'], { scale: .16, opacity: 0, duration: .6, ease: 'power3.in', transformOrigin: '50% 50%' }, sAt + 1)
      .set('#env1', { left: 320, top: 380, opacity: 1, scale: .4 }, sAt + 1.45)
      .to('#env1', { scale: 1, duration: .35, ease: 'back.out(2)' }, sAt + 1.45)
      .to('#env1', { motionPath: { path: [{ x: 0, y: 0 }, { x: 320, y: -230 }, { x: 640, y: -20 }], curviness: 1.2, autoRotate: false }, rotate: 14, scale: .75, duration: 1.4, ease: 'power2.inOut' }, sAt + 1.8);
    ringBurst(tl, stage, 960, 360, '#e2c172', sAt + 3.1, 2);
    tl.to('#env1', { opacity: 0, scale: .2, duration: .3 }, sAt + 3.1);

    // side by side
    const cAt = sAt + 3.6;
    tl.to('#m1', { opacity: .35, duration: .8 }, cAt)
      .to('#turn1', { opacity: 0, duration: .4 }, cAt)
      .to('#dia', { opacity: 1, scale: 1, filter: 'blur(0px)', duration: .8, ease: 'power3.out' }, cAt)
      .fromTo('#let', { opacity: 0, scale: .9 }, { opacity: 1, scale: 1, duration: .8, ease: 'power3.out' }, cAt + .15)
      .to(['#lbW', '#lbT'], { opacity: 1, duration: .5, stagger: .15 }, cAt + .4)
      .to(['#a1', '#a2'], { scale: 1.18, duration: .5, yoyo: true, repeat: 5, ease: 'sine.inOut' }, cAt + .4);
    tl.fromTo('#end1', { opacity: 0 }, { opacity: 1, duration: .9 }, cAt + 4.2)
      .from('#end1 h3', { y: 30, opacity: 0, duration: 1, ease: 'power3.out' }, cAt + 4.4)
      .from('#end1 p, #end1 .brand', { y: 16, opacity: 0, duration: .7, stagger: .2 }, cAt + 5)
      .to({}, { duration: 2.4 });
    // score
    sfx(tl, .05, 'whoosh', 2, .16); sfx(tl, .6, 'shimmer'); sfx(tl, 1.2, 'pop'); sfx(tl, 1.45, 'pop');
    sfx(tl, 4.6, 'paper'); sfx(tl, t2 + .2, 'paper');
    sfx(tl, sAt + .4, 'seal'); sfx(tl, sAt + 1, 'paper'); sfx(tl, sAt + 1.8, 'whoosh', 1.4);
    sfx(tl, cAt, 'paper'); sfx(tl, cAt + .5, 'heartbeat'); sfx(tl, cAt + 4.2, 'endhit');
    return tl;
  }

  // ---------- clip 2: words become war ----------
  function war(stage) {
    const franks = 'M110 230 C200 170 380 160 520 200 C620 230 660 320 640 420 C620 540 560 640 430 660 C300 680 170 640 120 540 C80 450 60 300 110 230Z';
    const eng = 'M760 60 C860 30 1050 40 1170 90 C1240 130 1240 230 1180 290 C1110 350 950 360 850 330 C770 300 720 230 730 160 C735 110 730 80 760 60Z';
    const byz = 'M800 470 C880 420 1060 420 1170 470 C1250 510 1260 620 1200 690 L800 720 C760 680 740 600 760 540 C770 500 780 485 800 470Z';
    const warLine = `<path id="wl1" d="M960 200 C820 240 640 300 430 410" fill="none" stroke="#e0384f" stroke-width="5" stroke-dasharray="14 10" stroke-linecap="round" filter="url(#m2-glow)"/>
      <path id="wl2" d="M1010 560 C840 560 640 520 430 430" fill="none" stroke="#e0384f" stroke-width="5" stroke-dasharray="14 10" stroke-linecap="round" filter="url(#m2-glow)"/>
      <path id="al1" d="M1010 540 C1060 420 1040 320 980 220" fill="none" stroke="#e2c172" stroke-width="5" stroke-linecap="round" filter="url(#m2-glow)"/>
      <path id="armyPath" d="M930 230 C820 270 640 330 470 400" fill="none"/>
      <path id="fleet1" d="M930 600 C760 700 460 700 300 640" fill="none"/>
      <path id="fleet2" d="M960 620 C860 690 680 700 580 655" fill="none"/>`;
    stage.innerHTML = `
      ${map('m2', [{ d: franks, fill: '#22385a' }, { d: eng, fill: '#3b2430' }, { d: byz, fill: '#3a3322' }], warLine)}
      ${crest('cF', 420, 410, '#3b6fd1', 'F', t('you'), t('franks'))}
      ${crest('cE', 980, 180, '#c0392b', 'E', 'Mu Guiying', t('eng'))}
      ${crest('cB', 1010, 560, '#c9a02e', 'B', t('zen'), t('byz'))}
      <div class="abs" id="p1" style="left:300px;top:110px;width:700px;opacity:0;z-index:15"><div class="panel">
        <div class="ph"><h4>${t('p1Hd')} · ${t('eng')}</h4><div class="chips"><span class="chip g">${t('turn')} 118</span><span class="chip">${t('priv')}</span></div></div>
        <p class="pb">${words(t('p1'))}</p></div></div>
      <div class="abs" id="p2" style="left:290px;top:90px;width:720px;opacity:0;z-index:15"><div class="panel">
        <div class="ph"><h4>${t('zen')} · ${t('byz')}</h4><div class="chips"><span class="chip g">${t('turn')} 119</span><span class="chip">${t('pub')}</span></div></div>
        <p class="pb">${words(t('p2'))}</p></div></div>
      <div class="abs" id="p3" style="left:300px;top:110px;width:700px;opacity:0;z-index:15"><div class="panel">
        <div class="ph"><h4>${t('p1Hd')} · ${t('eng')}</h4><div class="chips"><span class="chip g">${t('turn')} 128</span><span class="chip">${t('priv')}</span></div></div>
        <p class="pb">${words(t('p3'))}</p></div></div>
      <div class="abs order red" id="o1" style="left:0;top:0;opacity:0">${t('o1')}</div>
      <div class="abs order gold" id="o2" style="left:0;top:0;opacity:0">${t('o2')}</div>
      <div class="abs order gold" id="o3" style="left:0;top:0;opacity:0">${t('o3')}</div>
      ${token('army', 930, 230, '#c0392b', '⚔')}
      ${token('sh1', 930, 600, '#c9a02e', '⛵')}${token('sh2', 960, 620, '#c9a02e', '⛵')}
      ${turnChip('turn2', 118)}
      ${caption('capA', t('cAs'), t('cA'))}${caption('capF', t('cFs'), t('cF')).replace('class="abs cap"', 'class="abs cap" style="top:34px;bottom:auto"').replace(' style="opacity:0"', '')}
      <svg class="abs" style="left:0;top:0;width:1280px;height:720px;z-index:6;pointer-events:none" viewBox="0 0 1280 720">
        ${[[300, 640], [580, 655]].map(([x, y]) => `<g transform="translate(${x} ${y})"><circle r="15" fill="#0b1422" stroke="#e2c172" stroke-width="2.5"/><text y="6" text-anchor="middle" font-size="17" fill="#e2c172">⚓</text></g>`).join('')}</svg>
      ${endCard('end2', t('e2'), t('e2p'))}`;

    const q = s => stage.querySelector(s);
    const tl = gsap.timeline();
    const tb = q('#turn2 b');
    tl.set(['#wl1', '#wl2', '#al1'], { drawSVG: '0%' }).set(['#army', '#sh1', '#sh2'], { opacity: 0 });
    tl.from('#m2', { scale: 1.1, opacity: 0, duration: 2.2, ease: 'power2.out' }, 0)
      .to('#turn2', { opacity: 1, duration: .5 }, .3)
      .from(['#cF', '#cE', '#cB'], { scale: 0, opacity: 0, duration: .7, stagger: .2, ease: 'back.out(2.2)' }, .6);

    // letter 1 → war
    tl.fromTo('#p1', { opacity: 0, y: 40, scale: .96 }, { opacity: 1, y: 0, scale: 1, duration: .8, ease: 'power3.out' }, 1.6);
    let end = typeWords(tl, q('#p1 .pb'), 2.1, .06);
    sweep(tl, q('#p1 mark'), end + .1);
    const o1At = end + .7;
    tl.set('#o1', { left: 560, top: 330 }, end + .7)
      .fromTo('#o1', { opacity: 0, scale: .6 }, { opacity: 1, scale: 1.15, duration: .4, ease: 'back.out(3)' }, end + .7)
      .to('#p1', { opacity: 0, y: -30, scale: .95, duration: .6, ease: 'power2.in' }, end + 1.2)
      .to('#o1', { left: 600, top: 236, scale: .8, duration: .9, ease: 'power3.inOut' }, end + 1.3)
      .to('#wl1', { drawSVG: '100%', duration: 1.2, ease: 'power2.inOut' }, end + 1.4);
    ringBurst(tl, stage, 980, 180, '#e0384f', end + 1.4);

    // T119: army marches
    const aAt = end + 2.6;
    countTurn(tl, tb, 118, 119, aAt, .4);
    tl.to('#o1', { opacity: 0, duration: .4 }, aAt)
      .to('#army', { opacity: 1, duration: .3 }, aAt + .2)
      .to('#army', { motionPath: { path: q('#armyPath'), align: q('#armyPath'), alignOrigin: [.5, .5] }, duration: 2.6, ease: 'power1.inOut' }, aAt + .3);
    showCap(tl, q('#capA'), aAt + .2, 2.8);
    ringBurst(tl, stage, 440, 405, '#e0384f', aAt + 2.9, 3);
    tl.to('#cF i', { x: 4, duration: .06, yoyo: true, repeat: 7 }, aAt + 2.9);

    // Zenobia joins
    const zAt = aAt + 3.8;
    tl.fromTo('#p2', { opacity: 0, y: 40, scale: .96 }, { opacity: 1, y: 0, scale: 1, duration: .8, ease: 'power3.out' }, zAt);
    end = typeWords(tl, q('#p2 .pb'), zAt + .5, .045);
    sweep(tl, q('#p2 mark'), end + .1);
    const o2At = end + .6;
    tl.set('#o2', { left: 560, top: 380 }, end + .6)
      .fromTo('#o2', { opacity: 0, scale: .6 }, { opacity: 1, scale: 1.15, duration: .4, ease: 'back.out(3)' }, end + .6)
      .to('#p2', { opacity: 0, y: -30, scale: .95, duration: .6, ease: 'power2.in' }, end + 1.1)
      .to('#o2', { left: 1040, top: 370, scale: .8, duration: .9, ease: 'power3.inOut' }, end + 1.2)
      .to('#al1', { drawSVG: '100%', duration: 1, ease: 'power2.inOut' }, end + 1.3)
      .to('#wl2', { drawSVG: '100%', duration: 1.2, ease: 'power2.inOut' }, end + 1.8);

    // fleet raids T121–129
    const fAt = end + 3;
    countTurn(tl, tb, 119, 127, fAt, 3.4);
    tl.to('#o2', { opacity: 0, duration: .4 }, fAt)
      .to(['#sh1', '#sh2'], { opacity: 1, duration: .3, stagger: .2 }, fAt)
      .to('#sh1', { motionPath: { path: q('#fleet1'), align: q('#fleet1'), alignOrigin: [.5, .5] }, duration: 2.4, ease: 'power1.inOut' }, fAt + .2)
      .to('#sh2', { motionPath: { path: q('#fleet2'), align: q('#fleet2'), alignOrigin: [.5, .5] }, duration: 2.1, ease: 'power1.inOut' }, fAt + .5);
    showCap(tl, q('#capF'), fAt + .2, 3);
    ringBurst(tl, stage, 300, 640, '#ff9a3c', fAt + 2.6, 3); ringBurst(tl, stage, 580, 655, '#ff9a3c', fAt + 2.7, 3);

    // T128 surrender accepted
    const sAt = fAt + 3.8;
    countTurn(tl, tb, 127, 128, sAt, .3);
    tl.to(['#sh1', '#sh2', '#army'], { opacity: 0, duration: .5 }, sAt)
      .fromTo('#p3', { opacity: 0, y: 40, scale: .96 }, { opacity: 1, y: 0, scale: 1, duration: .8, ease: 'power3.out' }, sAt + .2);
    end = typeWords(tl, q('#p3 .pb'), sAt + .7, .05);
    sweep(tl, q('#p3 mark'), end + .1, 1);
    const o3At = end + .8;
    tl.set('#o3', { left: 520, top: 470 }, end + .8)
      .fromTo('#o3', { opacity: 0, scale: .6 }, { opacity: 1, scale: 1.15, duration: .4, ease: 'back.out(3)' }, end + .8)
      .to(['#wl1', '#wl2'], { stroke: '#e2c172', opacity: .35, duration: 1 }, end + 1);

    const eAt = end + 2.4;
    tl.fromTo('#end2', { opacity: 0 }, { opacity: 1, duration: .9 }, eAt)
      .from('#end2 h3', { y: 30, opacity: 0, duration: 1, ease: 'power3.out' }, eAt + .2)
      .from('#end2 p, #end2 .brand', { y: 16, opacity: 0, duration: .7, stagger: .2 }, eAt + .8)
      .to({}, { duration: 2.4 });
    // score
    sfx(tl, .05, 'whoosh', 2, .16); sfx(tl, .6, 'pop'); sfx(tl, .8, 'pop'); sfx(tl, 1, 'pop');
    sfx(tl, 1.6, 'paper'); sfx(tl, o1At, 'horn'); sfx(tl, o1At + .7, 'whoosh', 1.2);
    sfx(tl, aAt + .3, 'march', 2.6); sfx(tl, aAt + 2.9, 'boom');
    sfx(tl, zAt, 'paper'); sfx(tl, o2At, 'horn'); sfx(tl, o2At + .7, 'chime');
    sfx(tl, fAt + .2, 'waves', 2.5); sfx(tl, sAt + .2, 'paper'); sfx(tl, o3At, 'chime'); sfx(tl, eAt, 'endhit');
    return tl;
  }

  // ---------- clip 3: intercepted ----------
  function intercepted(stage) {
    const celts = 'M60 300 C120 220 300 210 420 260 C500 300 520 400 480 480 C430 580 280 620 170 590 C80 560 30 420 60 300Z';
    const sum = 'M850 300 C940 240 1120 250 1200 320 C1260 380 1250 500 1180 560 C1100 620 950 620 870 560 C800 510 790 360 850 300Z';
    const wang = 'M480 40 C580 10 720 20 800 60 C850 100 850 200 800 250 C740 300 600 310 520 270 C460 230 430 100 480 40Z';
    const extra = `<path id="road" d="M290 430 C420 470 520 330 640 330 C760 330 860 470 1010 440" fill="none" stroke="rgba(241,234,217,.55)" stroke-width="4" stroke-dasharray="2 12" stroke-linecap="round"/>
      <path id="roadEnd" d="M640 330 C760 330 860 470 1010 440" fill="none" stroke="#0b1422" stroke-width="7" opacity="0"/>
      <path id="pull" d="M640 330 C640 280 645 220 640 170" fill="none"/>
      <g id="eye" transform="translate(640 330)" opacity="0">
        <circle class="det" r="70" fill="none" stroke="#9b6cf0" stroke-width="2" opacity=".5"/>
        <circle class="det" r="120" fill="none" stroke="#9b6cf0" stroke-width="2" opacity=".3"/>
        <path d="M-34 0 Q0 -26 34 0 Q0 26 -34 0Z" fill="#1b1030" stroke="#b98cff" stroke-width="3"/><circle r="9" fill="#b98cff"/></g>`;
    stage.innerHTML = `
      ${map('m3', [{ d: celts, fill: '#1f3d33' }, { d: sum, fill: '#3d3520' }, { d: wang, fill: '#2f2445' }], extra)}
      ${crest('cC', 250, 420, '#2f9e5b', 'C', 'Nayakuralu', t('celts'))}
      ${crest('cS3', 1010, 430, '#c99a2e', 'S', 'Walinong Sari', t('sum'))}
      ${crest('cW', 640, 130, '#7c4dbd', 'W', 'Wang Zhenyi', t('nor'))}
      ${turnChip('turn3', 74)}
      ${caption('cap0', t('c0s'), t('c0'))}${caption('capR', t('cRs'), t('cR'))}${caption('capN', t('cNs'), t('cN'))}${caption('capX', t('cXs'), t('cX'))}
      <article class="abs parch" id="let3" style="left:280px;top:80px;width:720px;opacity:0">
        <div class="hd"><span>${t('lHd3')}</span><span>${t('turn')} 74</span></div><p class="tx" style="font-size:30px">${words(t('l3'))}</p><p class="sg">${t('l3s')}</p></article>
      ${seal('seal3')}${envelope('env3')}
      <div class="abs flash" id="flash" style="inset:0;background:#e0384f;opacity:0;z-index:25;mix-blend-mode:screen"></div>
      <div class="abs stamp" id="stamp3" style="left:640px;top:250px;opacity:0">${t('stamp')}</div>
      <div class="abs" id="spy" style="left:290px;top:110px;width:700px;opacity:0;z-index:15"><div class="panel">
        <div class="ph"><h4>${t('spyHd')}</h4><div class="chips"><span class="chip" style="color:#ff8f8f">${t('stamp')}</span><span class="chip g">${t('turn')} 74</span></div></div>
        <div class="pb"><p style="font-size:17px;letter-spacing:.12em;text-transform:uppercase;color:#e2c172">Nayakuralu → Walinong Sari</p>
        <p style="margin-top:10px">${words(t('l3'))}</p>
        <p style="margin-top:14px;font-size:18px;color:#b98cff;font-weight:700">${t('spyBy')}</p></div></div></div>
      <div class="abs" id="box" style="left:1010px;top:505px;margin-left:-70px;width:140px;text-align:center;opacity:0;z-index:12">
        <div style="display:inline-grid;place-items:center;width:64px;height:64px;border-radius:50%;background:rgba(8,14,26,.85);border:2px solid #aab5c8;font:800 28px Mulish">0</div></div>
      ${endCard('end3', t('e3'), t('e3p'))}`;

    const q = s => stage.querySelector(s);
    const tl = gsap.timeline();
    tl.set('#road', { drawSVG: '0%' });
    tl.from('#m3', { scale: 1.1, opacity: 0, duration: 2.2, ease: 'power2.out' }, 0)
      .to('#turn3', { opacity: 1, duration: .5 }, .3)
      .from(['#cC', '#cS3', '#cW'], { scale: 0, opacity: 0, duration: .7, stagger: .2, ease: 'back.out(2.2)' }, .6)
      .to('#road', { drawSVG: '100%', duration: 1.6, ease: 'power2.inOut' }, 1);
    showCap(tl, q('#cap0'), 1.2, 2.2);

    tl.fromTo('#let3', { opacity: 0, rotateX: -65, y: 40, transformPerspective: 1200 }, { opacity: 1, rotateX: 0, y: 0, duration: 1, ease: 'power3.out' }, 3.4);
    let end = typeWords(tl, q('#let3 .tx'), 4, .06);
    sweep(tl, q('#let3 mark'), end + .1, .9);
    tl.from('#let3 .sg', { opacity: 0, x: 20, duration: .5 }, end);

    const sAt = end + 1.3;
    tl.set('#seal3', { left: 640, top: 440 }, sAt)
      .fromTo('#seal3', { opacity: 0, scale: 3.2, rotate: -20 }, { opacity: 1, scale: 1, rotate: 0, duration: .45, ease: 'power4.in' }, sAt)
      .to(['#let3', '#seal3'], { scale: .16, opacity: 0, duration: .6, ease: 'power3.in' }, sAt + .8)
      .set('#env3', { left: 0, top: 0, opacity: 1, scale: .55 }, sAt + 1.3)
      .to('#env3', { motionPath: { path: q('#road'), align: q('#road'), alignOrigin: [.5, .5], start: 0, end: .5 }, duration: 3, ease: 'sine.inOut' }, sAt + 1.3)
      .to('#eye', { opacity: 1, duration: .6 }, sAt + 1.6)
      .fromTo('#eye path', { scaleY: 0, transformOrigin: '50% 50%' }, { scaleY: 1, duration: .5, ease: 'back.out(2)' }, sAt + 1.7)
      .fromTo(stage.querySelectorAll('#eye .det'), { scale: .6, transformOrigin: '50% 50%' }, { scale: 1.2, duration: 1.4, repeat: 2, yoyo: true, ease: 'sine.inOut', stagger: .3 }, sAt + 1.6);
    showCap(tl, q('#capR'), sAt + 1.5, 2.4);

    // intercept
    const iAt = sAt + 4.3;
    tl.fromTo('#flash', { opacity: 0 }, { opacity: .35, duration: .08, yoyo: true, repeat: 3 }, iAt)
      .fromTo('#stamp3', { opacity: 0, scale: 2.6, rotate: -14, xPercent: -50, yPercent: -50 }, { opacity: 1, scale: 1, rotate: -8, duration: .35, ease: 'power4.in' }, iAt + .05)
      .to('#env3', { x: '+=6', duration: .05, yoyo: true, repeat: 7 }, iAt + .1)
      .to('#env3', { motionPath: { path: q('#pull'), align: q('#pull'), alignOrigin: [.5, .5] }, scale: .8, duration: 1.3, ease: 'power3.inOut' }, iAt + 1.2)
      .to('#stamp3', { opacity: 0, duration: .4 }, iAt + 1.6)
      .to('#roadEnd', { opacity: 1, duration: .6 }, iAt + 1.4)
      .to('#env3 .sealL', { x: -10, y: 12, rotate: -25, opacity: 0, transformOrigin: '60px 58px', duration: .6 }, iAt + 2.6)
      .to('#env3 .sealR', { x: 10, y: 12, rotate: 25, opacity: 0, transformOrigin: '60px 58px', duration: .6 }, iAt + 2.6)
      .to('#env3', { opacity: 0, scale: 1.6, duration: .5 }, iAt + 3);
    ringBurst(tl, stage, 640, 330, '#b98cff', iAt, 3);

    const pAt = iAt + 3.2;
    tl.to('#m3', { opacity: .35, duration: .6 }, pAt)
      .fromTo('#spy', { opacity: 0, y: 40, scale: .96 }, { opacity: 1, y: 0, scale: 1, duration: .8, ease: 'power3.out' }, pAt);
    end = typeWords(tl, q('#spy .pb p:nth-child(2)'), pAt + .4, .035);
    sweep(tl, q('#spy mark'), end, .8);
    tl.from('#spy .pb p:last-child', { opacity: 0, y: 10, duration: .5 }, end + .3);

    // never arrived
    tl.to('#spy', { scale: .78, y: 150, transformOrigin: '50% 0%', duration: .7, ease: 'power3.inOut' }, end + .7)
      .to('#m3', { opacity: .85, duration: .7 }, end + .7);
    showCap(tl, q('#capX'), end + .9, 2.6);
    const xAt = end + .9;
    tl.to('#cW i', { scale: 1.25, duration: .4, yoyo: true, repeat: 3, ease: 'sine.inOut' }, end + 1);
    const nAt = end + 3.9;
    tl.to('#spy', { opacity: 0, y: -30, duration: .6, ease: 'power2.in' }, nAt)
      .to('#m3', { opacity: 1, duration: .6 }, nAt)
      .fromTo('#box', { opacity: 0, scale: .5 }, { opacity: 1, scale: 1, duration: .5, ease: 'back.out(2.5)' }, nAt + .5)
      .to('#cS3 i', { filter: 'grayscale(1) brightness(.6)', duration: .8 }, nAt + .6);
    showCap(tl, q('#capN'), nAt + .6, 2.6);

    const eAt = nAt + 3.6;
    tl.fromTo('#end3', { opacity: 0 }, { opacity: 1, duration: .9 }, eAt)
      .from('#end3 h3', { y: 30, opacity: 0, duration: 1, ease: 'power3.out' }, eAt + .2)
      .from('#end3 p, #end3 .brand', { y: 16, opacity: 0, duration: .7, stagger: .2 }, eAt + .8)
      .to({}, { duration: 2.4 });
    // score
    sfx(tl, .05, 'whoosh', 2, .16); sfx(tl, .6, 'pop'); sfx(tl, .8, 'pop'); sfx(tl, 1, 'pop'); sfx(tl, 1.1, 'whoosh', 1.6, .1);
    sfx(tl, 3.4, 'paper'); sfx(tl, sAt + .4, 'seal'); sfx(tl, sAt + .8, 'paper');
    sfx(tl, sAt + 1.3, 'whoosh', 3, .1); sfx(tl, sAt + 1.7, 'shimmer'); sfx(tl, sAt + 2.3, 'heartbeat'); sfx(tl, sAt + 3.5, 'heartbeat');
    sfx(tl, iAt, 'stab'); sfx(tl, iAt + 1.2, 'whoosh', 1.3); sfx(tl, iAt + 2.6, 'crack');
    sfx(tl, pAt, 'paper'); sfx(tl, xAt, 'horn'); sfx(tl, nAt + .5, 'chime', true); sfx(tl, eAt, 'endhit');
    return tl;
  }

  const BUILDERS = { 'two-faces': twoFaces, war, intercepted };
  const POSTER = { 'two-faces': .78, war: .5, intercepted: .62 };
  const MOOD = { 'two-faces': 'mystery', war: 'war', intercepted: 'tension' };
  const ICON_ON = '<svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true"><path d="M4 9h4l5-4v14l-5-4H4z" fill="currentColor"/><path d="M16 8.5a5 5 0 0 1 0 7M18.5 6a8.5 8.5 0 0 1 0 12" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>';
  const ICON_OFF = '<svg viewBox="0 0 24 24" width="22" height="22" aria-hidden="true"><path d="M4 9h4l5-4v14l-5-4H4z" fill="currentColor"/><path d="M16.5 9.5l5 5M21.5 9.5l-5 5" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round"/></svg>';

  function mount(root) {
    const kind = root.dataset.clip;
    if (!BUILDERS[kind]) return;
    root.innerHTML = `<div class="stage"></div><div class="vig"></div><div class="grain"></div><div class="src">${t('src')}</div>
      <button type="button" class="snd-btn" aria-pressed="false" aria-label="${t('sound')}">${ICON_OFF}<span>${t('sound')}</span></button>
      <div class="ctl"><button type="button" class="pp" aria-label="Play">▶</button>
      <div class="bar" role="slider" aria-label="Progress" aria-valuemin="0" aria-valuemax="100" aria-valuenow="0" tabindex="0"><i></i><b class="knob"></b></div>
      <button type="button" class="rp" aria-label="Replay">↺</button></div>`;
    const stage = root.querySelector('.stage');
    const fit = () => { stage.style.transform = `scale(${root.clientWidth / 1280})`; };
    fit(); new ResizeObserver(fit).observe(root);

    const tl = BUILDERS[kind](stage);
    tl.pause(0);
    root._tl = tl;
    const pp = root.querySelector('.pp'), bar = root.querySelector('.bar i');

    // score: generative music bed per clip, running only while this clip plays with sound on
    let stopMusic = null;
    const soundOn = () => !!(window.RPAudio && RPAudio.on());
    const musicOff = () => { if (stopMusic) { stopMusic(); stopMusic = null; } };
    const musicSync = () => {
      const playing = !tl.paused() && tl.progress() < 1;
      root.classList.toggle('playing', playing);
      if (soundOn() && playing) { RPAudio.hush(false); if (!stopMusic) stopMusic = RPAudio.music(MOOD[kind]); }
      else { musicOff(); if (window.RPAudio && !document.querySelector('.clip.playing')) RPAudio.hush(true); }
    };
    const sndBtn = root.querySelector('.snd-btn');
    const sndUi = () => { const on = soundOn(); sndBtn.innerHTML = on ? ICON_ON : `${ICON_OFF}<span>${t('sound')}</span>`;
      sndBtn.setAttribute('aria-pressed', on); root.classList.toggle('sound', on); };

    const sync = () => { const p = tl.paused(); pp.textContent = p ? '▶' : '❚❚'; pp.setAttribute('aria-label', p ? 'Play' : 'Pause'); root.classList.toggle('paused', p); musicSync(); sndUi(); };
    const knob = root.querySelector('.knob'), barEl = root.querySelector('.bar');
    tl.eventCallback('onUpdate', () => { const pc = (tl.progress() * 100).toFixed(2) + '%'; bar.style.width = pc; knob.style.left = pc; barEl.setAttribute('aria-valuenow', Math.round(tl.progress() * 100)); });
    tl.eventCallback('onComplete', () => { gsap.delayedCall(.8, () => { if (root._inView && !root._user) { tl.restart(); sync(); } else { musicOff(); sync(); } }); });
    const toggleSound = () => {
      if (!window.RPAudio) return;
      if (soundOn()) RPAudio.disable(); else { RPAudio.enable(); if (tl.paused()) { root._user = false; tl.progress() >= 1 ? tl.restart() : tl.play(); } }
      document.dispatchEvent(new CustomEvent('rp-sound'));
    };
    sndBtn.addEventListener('click', toggleSound);
    document.addEventListener('rp-sound', () => setTimeout(sync, 60));
    root.addEventListener('rp-sync', sync);
    pp.addEventListener('click', () => { root._user = !tl.paused(); tl.paused() ? (tl.progress() >= 1 ? tl.restart() : tl.play()) : tl.pause(); sync(); });
    root.querySelector('.rp').addEventListener('click', () => { root._user = false; tl.restart(); sync(); });
    // drag-to-scrub (mouse + touch): pause while dragging, resume if it was playing
    let wasPlaying = false;
    const seekTo = e => { const r = barEl.getBoundingClientRect(); tl.progress(Math.min(1, Math.max(0, (e.clientX - r.left) / r.width))); };
    barEl.addEventListener('pointerdown', e => { wasPlaying = !tl.paused(); tl.pause(); sync(); barEl.setPointerCapture(e.pointerId); root.classList.add('scrub'); seekTo(e); });
    barEl.addEventListener('pointermove', e => { if (barEl.hasPointerCapture(e.pointerId)) seekTo(e); });
    const endDrag = e => { if (!root.classList.contains('scrub')) return; root.classList.remove('scrub'); if (wasPlaying && tl.progress() < 1) tl.play(); sync(); };
    barEl.addEventListener('pointerup', endDrag); barEl.addEventListener('pointercancel', endDrag);
    root.querySelector('.bar').addEventListener('keydown', e => { if (e.key === 'ArrowRight') tl.time(tl.time() + 2); if (e.key === 'ArrowLeft') tl.time(tl.time() - 2); });

    const reduce = matchMedia('(prefers-reduced-motion: reduce)').matches;
    if (reduce) { tl.progress(POSTER[kind]); sync(); return; }
    new IntersectionObserver(es => es.forEach(e => {
      root._inView = e.isIntersecting;
      if (e.isIntersecting && !root._user) tl.play(); else if (!e.isIntersecting) tl.pause();
      sync();
    }), { threshold: .45 }).observe(root);
  }

  function init() {
    gsap.registerPlugin(MotionPathPlugin, DrawSVGPlugin, CustomEase);
    document.querySelectorAll('.clip[data-clip]').forEach(mount);
    // hidden tab: pause every clip (and its music); visible again: resume the one in view
    document.addEventListener('visibilitychange', () => document.querySelectorAll('.clip[data-clip]').forEach(c => {
      if (!c._tl) return;
      if (document.hidden) c._tl.pause(); else if (c._inView && !c._user) c._tl.play();
      c.dispatchEvent(new CustomEvent('rp-sync'));
    }));
  }
  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', init); else init();
})();
