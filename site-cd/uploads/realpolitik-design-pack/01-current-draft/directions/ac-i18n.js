// Language switch for the A+C draft. English lives in the HTML (data-i18n keys);
// other languages override by key. In PT the letters are the ORIGINAL game output.
(function () {
  const LANGS = ['en', 'pt'];
  const params = new URLSearchParams(location.search);
  let lang = params.get('lang');
  try { if (lang) localStorage.setItem('rp-lang', lang); else lang = localStorage.getItem('rp-lang'); } catch (e) {}
  if (!LANGS.includes(lang)) lang = 'en';

  const T = {
    pt: {
      'note': 'Rascunho · direção A+C',
      'buy': 'Comprar — US$ 10',
      'watch': '▶ Veja em ação',
      'meta': 'Windows · Steam · Um jogador · 5 idiomas',
      'eyebrow': 'Um mod para HUMANKIND',
      'h1': 'Seus rivais finalmente têm <em>algo a dizer.</em>',
      'sub': 'Cada império da IA pensa, lembra, blefa — e escreve para você. Depois, age de acordo com o que escreveu.',
      'l.head': '<span>Carta privada</span><span>Turno 65</span>',
      'l.body': 'Não trago a espada, trago a palavra. Proponho comércio franco entre nossas gentes — caravanas e portas abertas, sem pedágio nem sangue.',
      'l.sign': '— Salomão, para Arjuna dos hunos',
      'd.head': '<span>Diário secreto dele · mesmo turno</span><span>nunca enviado</span>',
      'd.body': '“…enquanto sorrio, dois exércitos meus ainda dormem à porta do huno. Palavra e muro, muro e palavra.”',
      'l.src': 'Carta e diário reais (partida de teste com 16 impérios, turno 65) · texto original gerado pelo jogo.',
      'cta1.h': 'Seus rivais <span>já estão escrevendo.</span>',
      'cta2.h': 'Cada carta aqui foi <span>escrita pelo jogo.</span> As suas são as próximas.',
      'cta.m': 'Pagamento único · Windows · Steam · Um jogador',
      'f2.kicker': 'Nações vivas',
      'f2.h2': 'Duas caras. <span>Um turno.</span>',
      'o.kicker': 'As palavras viram ordens',
      'o.h2': 'Não é um chatbot. <span>É uma nação decidindo.</span>',
      'o.lead': 'O que uma nação escreve, ela pode fazer: declarar guerra, mover um exército, exigir tributo, patrocinar rebeldes, votar no Congresso, se render com termos. 32 tipos de ação reais dentro do jogo.',
      'o.who': 'Zenóbia · Os bizantinos',
      'o.t119': 'Turno 119',
      'o.pub': 'Pública',
      'o.letter': '<p>Onze turnos dormi a paciência; hoje ela acordou de ferro… <mark>Ergo a lança ao lado de Mu Guiying</mark>… Quem tem conta contra o devedor, conte comigo.</p>',
      'o.t119s': 'T119',
      'o.a1': '<b>Ordem</b>Declara guerra aos francos — atendendo ao chamado da aliada',
      'o.a2': '<b>Ordem</b>A frota dela ataca dois portos francos',
      'o.a3': '<b>Resultado</b>A guerra vira. Zenóbia oferece a rendição',
      'o.todo': 'Captura a fazer: a carta na tela → o exército marchando no mapa (clipe curto)',
      's.kicker': 'Espionagem',
      's.h2': 'Seus espiões <span>leem a correspondência deles.</span>',
      's.lead': 'As cartas privadas entre as nações da IA atravessam o mapa. Um espião no lugar certo pode lê-las — e a carta interceptada nunca chega.',
      's.ticks': '<li>Uma aba “Cartas” dentro da tela nativa de inteligência</li><li>A chance por carta depende da sua rede de espiões</li><li>As nações da IA também espionam umas às outras</li>',
      's.cap': '<b>Print real, turno 111:</b> a carta dos teutões para os hunos, lida pelo seu espião.',
      'n.kicker': 'Parece que veio com o jogo',
      'n.h2': 'Todas as telas são <span>nativas.</span>',
      'n.lead': 'As cartas ficam numa aba nova da tela de diplomacia. Sua economia ganha um Banco Central próprio. Nada de sobreposição ou janela de navegador — tudo feito com a interface do próprio jogo.',
      'n.cap': '<b>Prints reais.</b> Banco Central (interface em inglês) e a aba de Cartas na diplomacia.',
      'f.kicker': 'Realpolitik: Living Nations para HUMANKIND',
      'f.h2': 'Os outros impérios estão <span>esperando sua resposta.</span>',
      'footer': 'Mod não oficial, feito por fã. Sem afiliação ou endosso da Amplitude Studios ou da SEGA. HUMANKIND é marca registrada dos respectivos donos.',
    },
  };

  if (T[lang]) {
    document.documentElement.lang = lang === 'pt' ? 'pt-BR' : lang;
    document.querySelectorAll('[data-i18n]').forEach(el => { const v = T[lang][el.dataset.i18n]; if (v != null) el.innerHTML = v; });
    document.querySelectorAll('[data-i18n-text]').forEach(el => { const v = T[lang][el.dataset.i18nText]; if (v != null) el.dataset.text = v; });
  }

  const box = document.getElementById('lang');
  if (box) box.innerHTML = LANGS.map(l => l === lang ? `<b>${l.toUpperCase()}</b>` : `<a href="?lang=${l}">${l.toUpperCase()}</a>`).join(' ');
})();
