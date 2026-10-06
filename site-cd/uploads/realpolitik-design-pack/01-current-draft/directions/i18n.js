// Draft-only language switch for the art-direction mockups (?lang=pt).
// English lives in the HTML; this file maps CSS selectors to translated HTML.
// Letters in PT are the ORIGINAL texts the game generated (Teste16), not translations.
(function () {
  const params = new URLSearchParams(location.search);
  let lang = params.get('lang');
  try { if (lang) localStorage.setItem('rp-lang', lang); else lang = localStorage.getItem('rp-lang'); } catch (e) {}
  lang = lang === 'pt' ? 'pt' : 'en';

  const common = {
    '.btn-primary': 'Comprar — US$ 10',
    '.meta': 'Windows · Steam · Um jogador · 5 idiomas',
  };

  const PT = {
    'index': {
      'h1': 'Realpolitik: Living Nations — 3 direções de arte',
      'main > p': 'Só o topo da página, com o movimento principal funcionando. Em português, as cartas são o texto original gerado pelo jogo (Teste16).',
      'a.d:nth-of-type(1)': '<b>A · O Arquivo</b>Sala de mapas à noite, papel envelhecido, selo de cera. A carta é escrita traço a traço; o diário secreto desliza por baixo.',
      'a.d:nth-of-type(2)': '<b>B · Cabinet Noir</b>Dossiê de espionagem vazado. Serifa editorial enorme, metadados em fonte de máquina, um único vermelho. As tarjas caem: o que escreveram × o que queriam dizer.',
      'a.d:nth-of-type(3)': '<b>C · Statecraft</b>A linguagem visual do próprio jogo. Uma frase da carta vira ordem de exército no mapa.',
    },
    'a-archive': Object.assign({}, common, {
      '.eyebrow': 'Um mod para HUMANKIND',
      'h1': 'Seus rivais finalmente têm <em>algo a dizer.</em>',
      '.sub': 'Cada império da IA pensa, lembra, blefa — e escreve para você. Depois, cumpre (ou não) o que escreveu.',
      '.btn-ghost': '▶ Veja em ação',
      '#letter .l-head': '<span>Carta privada</span><span>Turno 65</span>',
      '@#lbody': 'Não trago a espada, trago a palavra. Proponho comércio franco entre nossas gentes — caravanas e portas abertas, sem pedágio nem sangue.',
      '.l-sign': '— Salomão, para Arjuna dos hunos',
      '#diary .l-head': '<span>Diário secreto dele · mesmo turno</span><span>nunca enviado</span>',
      '#diary p': '“…enquanto sorrio, dois exércitos meus ainda dormem à porta do huno. Palavra e muro, muro e palavra.”',
      '.draft': 'Carta e diário reais (Teste16, turno 65) · texto original gerado pelo jogo.',
      '.sample > :nth-child(1) h3': 'Sala de mapas à noite',
      '.sample > :nth-child(1) p': 'Azul-noite, papel envelhecido, dourado de vela, vermelho de cera. Como ler despachos à luz do lampião.',
      '.sample > :nth-child(2) p': 'Serifa expressiva nos títulos, uma tipografia de época nas cartas, Inter em tudo que precisa ser lido rápido.',
      '.sample > :nth-child(3) h3': 'Movimento',
      '.sample > :nth-child(3) p': 'O selo se parte, a carta é escrita traço a traço e o diário surge por baixo — o que disseram × o que queriam dizer.',
    }),
    'b-dossier': Object.assign({}, common, {
      '.file': '<span>Arquivo <b>#T65-E13</b></span><span>Classificação: <b>diário secreto</b></span><span>Um mod para HUMANKIND</span>',
      'h1': 'Eles escrevem para você. <i>Eles mentem</i> para você.',
      '.sub': 'Cada império da IA mantém um diário secreto, lembra o que você fez e escreve cartas na própria voz — depois transforma as palavras em ordens reais.',
      '.btn-ghost': '▶ Veja em 60 s',
      '.said .lbl': '<span>O que ele escreveu</span><span>Turno 65</span>',
      '#said': '“Não trago a espada, trago a palavra. Proponho comércio franco entre nossas gentes — caravanas e portas abertas, sem pedágio nem sangue.”',
      '.said .who': 'Salomão → Arjuna dos hunos · carta privada',
      '.meant .lbl': '<span>O que ele pensava</span><span>Diário secreto</span>',
      '#stamp': 'REVELADO',
      '.meant .quote': '“<span class="redact">Estendo a mão</span> ao vizinho huno e ao romano; <span class="redact">enquanto sorrio, dois exércitos meus</span> <span class="redact">ainda dormem à porta do huno.</span> Palavra e muro, muro e palavra.”',
      '.meant .who': 'Mesmo turno · nunca enviado',
      '.draft': 'Carta e diário reais (Teste16, turno 65) · texto original gerado pelo jogo.',
      '.sample > :nth-child(1) h3': 'Dossiê de inteligência',
      '.sample > :nth-child(1) p': 'Quase preto, branco-papel, um vermelho de alerta. Papel quadriculado, números de arquivo, carimbos. A página inteira parece um arquivo vazado.',
      '.sample > :nth-child(2) p': 'Títulos enormes em serifa editorial, metadados em fonte de máquina de escrever. A mais moderna das três; a menos “fantasia”.',
      '.sample > :nth-child(3) h3': 'Movimento',
      '.sample > :nth-child(3) p': 'As tarjas de censura caem e revelam o diário. Depois: um envelope interceptado no meio da rota, arquivos deslizando na mesa.',
    }),
    'c-statecraft': Object.assign({}, common, {
      '.eyebrow': 'Um mod para HUMANKIND',
      'h1': 'Diplomacia com nações <span>que falam sério.</span>',
      '.sub': 'Cada império da IA escreve para você na própria voz — e as palavras viram ordens reais no mapa.',
      '.btn-ghost': '▶ Veja em 60 s',
      '.meta': 'Windows · Steam · Um jogador · 5 idiomas · Parece que veio com o jogo',
      '.panel-h h2': 'Alexandre · Os mongóis',
      '.panel-h .chip': 'Turno 78',
      '.chips': '<span class="chip">Privada</span><span class="chip">Para os celtas</span>',
      '.panel-b': '<p>O prazo era hoje… Não peço mais: conto. <mark id="mk">Cada nau tua será contada em ferro.</mark> Não é ameaça de rei cansado — é a conta que meu império sempre cobra.</p>',
      '#order': '<b>Ordem dada</b> Exército A78 → atacar San Lorenzo',
      '#toast': '<h3>ATAQUE EM ANDAMENTO</h3><p>Os mongóis atacam San Lorenzo</p>',
      '#lblCamp': 'Acampamento mongol',
      '.draft': 'Carta e ação reais (Teste16, turno 78) · texto original gerado pelo jogo.',
      '.sample > :nth-child(1) h3': 'Paleta nativa',
      '.sample > :nth-child(1) p': 'Painéis azul-ardósia, versalete dourado, ações em magenta — a página parece as telas do próprio mod, então os prints se encaixam.',
      '.sample > :nth-child(2) p': 'Perto da tipografia do jogo. Familiar na hora para quem joga Humankind; menos marcante para quem não joga.',
      '.sample > :nth-child(3) h3': 'Movimento',
      '.sample > :nth-child(3) p': 'Uma frase da carta acende, vira ordem, o exército percorre a rota e o aviso nativo aparece. “As palavras viram ordens.”',
    }),
  };

  const page = (location.pathname.split('/').pop() || 'index.html').replace('.html', '') || 'index';

  if (lang === 'pt' && PT[page]) {
    document.documentElement.lang = 'pt-BR';
    for (const [sel, html] of Object.entries(PT[page])) {
      if (sel[0] === '@') { const el = document.querySelector(sel.slice(1)); if (el) el.dataset.text = html; continue; }
      const el = document.querySelector(sel); if (el) el.innerHTML = html;
    }
  }

  // Keep the chosen language on links between drafts, and add the EN | PT switch.
  document.querySelectorAll('a[href$=".html"]').forEach(a => { a.href = a.getAttribute('href') + '?lang=' + lang; });
  const sw = document.createElement('span');
  sw.innerHTML = ' · ' + ['en', 'pt'].map(l => l === lang ? `<b>${l.toUpperCase()}</b>` : `<a href="?lang=${l}">${l.toUpperCase()}</a>`).join(' | ');
  (document.querySelector('.tag') || document.querySelector('main > p')).appendChild(sw);
})();
