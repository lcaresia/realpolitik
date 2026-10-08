// Shared by obrigado.html and download.html: talks to the store Worker (Cloudflare) and renders downloads.
window.RP = (function () {
  var API = 'https://realpolitik-loja.lcaresia.workers.dev';
  var T = {
    en: {
      thanksH: 'Thank you.', thanksP: 'Your order is confirmed. Keep this key: it unlocks AI Diplomacy on up to 3 PCs.',
      waiting: 'Confirming your payment with Stripe…', slow: 'This is taking longer than usual. You can safely reload this page.',
      keyLbl: 'Your license key', copy: 'Copy', copied: 'Copied', where: 'In the game: main menu → AI Diplomacy → License → paste the key.',
      noRefund: 'Note: a previous order from this customer was refunded, so this order is not eligible for another refund.',
      expired: 'This confirmation page has expired. Use your key on the download page, or recover it there with your receipt.',
      blocked: 'This order is not active (refunded or disputed).', err: 'Something went wrong. Reload the page, or contact us.',
      dlH: 'Download', dlP: 'Paste your license key to see every version. Links expire after 10 minutes; come back any time.',
      keyPh: 'RPLN-XXXXX-XXXXX-XXXXX-XXXXX', show: 'Show downloads', latest: 'Latest', older: 'Older versions (in case a game patch breaks something)',
      tested: 'Tested with HUMANKIND', none: 'No version is published yet. The game and this page will show it as soon as it is out.',
      badKey: 'That key was not found. Check it and try again.', codeUsed: 'This download link was already used or expired. Paste your key below.',
      lostH: 'Lost your key?', lostP: 'Enter the email you used to buy and the receipt number from the Stripe receipt email (“Receipt #1234-5678”).',
      receiptPh: 'Receipt number', recFail: 'No order matches that email and receipt number.', saveTxt: 'Save as .txt', saveHint: 'Save the key now: it is shown on this page and in no email.',
      emailPh: 'you@example.com', send: 'Send', sent: 'If that email bought the mod, the key is on its way.', noMail: 'Email delivery is not set up yet. Please contact us and we will send your key.',
      busy: 'Too many attempts. Wait a minute and try again.', back: '← Back to the page', install: 'How to install'
    },
    pt: {
      thanksH: 'Obrigado.', thanksP: 'Seu pedido está confirmado. Guarde esta chave: ela libera a Diplomacia IA em até 3 PCs.',
      waiting: 'Confirmando o pagamento com a Stripe…', slow: 'Está demorando mais que o normal. Pode recarregar esta página.',
      keyLbl: 'Sua chave de licença', copy: 'Copiar', copied: 'Copiada', where: 'No jogo: menu principal → Diplomacia IA → Licença → cole a chave.',
      noRefund: 'Atenção: um pedido anterior deste cliente foi reembolsado, então este pedido não tem direito a outro reembolso.',
      expired: 'Esta página de confirmação expirou. Use sua chave na página de download ou recupere-a lá com o recibo.',
      blocked: 'Este pedido não está ativo (reembolsado ou contestado).', err: 'Algo deu errado. Recarregue a página ou fale com a gente.',
      dlH: 'Download', dlP: 'Cole sua chave de licença para ver todas as versões. Os links expiram em 10 minutos; volte quando quiser.',
      keyPh: 'RPLN-XXXXX-XXXXX-XXXXX-XXXXX', show: 'Ver downloads', latest: 'Mais recente', older: 'Versões anteriores (caso um patch do jogo quebre algo)',
      tested: 'Testada com HUMANKIND', none: 'Nenhuma versão publicada ainda. O jogo e esta página mostram assim que sair.',
      badKey: 'Chave não encontrada. Confira e tente de novo.', codeUsed: 'Este link de download já foi usado ou expirou. Cole sua chave abaixo.',
      lostH: 'Perdeu a chave?', lostP: 'Digite o e-mail da compra e o número do recibo que a Stripe mandou por e-mail (“Receipt #1234-5678”).',
      receiptPh: 'Número do recibo', recFail: 'Nenhum pedido com esse e-mail e esse número de recibo.', saveTxt: 'Salvar em .txt', saveHint: 'Guarde a chave agora: ela aparece só nesta página, em nenhum e-mail.',
      emailPh: 'voce@exemplo.com', send: 'Enviar', sent: 'Se esse e-mail comprou o mod, a chave está a caminho.', noMail: 'O envio por e-mail ainda não está ativo. Fale com a gente e mandamos sua chave.',
      busy: 'Tentativas demais. Espere um minuto e tente de novo.', back: '← Voltar à página', install: 'Como instalar'
    },
    es: {
      thanksH: 'Gracias.', thanksP: 'Tu pedido está confirmado. Guarda esta clave: activa la Diplomacia IA en hasta 3 PC.',
      waiting: 'Confirmando el pago con Stripe…', slow: 'Está tardando más de lo normal. Puedes recargar esta página.',
      keyLbl: 'Tu clave de licencia', copy: 'Copiar', copied: 'Copiada', where: 'En el juego: menú principal → Diplomacia IA → Licencia → pega la clave.',
      noRefund: 'Nota: un pedido anterior de este cliente fue reembolsado, así que este pedido no admite otro reembolso.',
      expired: 'Esta página de confirmación caducó. Usa tu clave en la página de descarga o recupérala allí con el recibo.',
      blocked: 'Este pedido no está activo (reembolsado o disputado).', err: 'Algo salió mal. Recarga la página o escríbenos.',
      dlH: 'Descarga', dlP: 'Pega tu clave de licencia para ver todas las versiones. Los enlaces caducan en 10 minutos; vuelve cuando quieras.',
      keyPh: 'RPLN-XXXXX-XXXXX-XXXXX-XXXXX', show: 'Ver descargas', latest: 'Más reciente', older: 'Versiones anteriores (por si un parche del juego rompe algo)',
      tested: 'Probada con HUMANKIND', none: 'Aún no hay versiones publicadas. El juego y esta página la mostrarán en cuanto salga.',
      badKey: 'No encontramos esa clave. Revísala e inténtalo de nuevo.', codeUsed: 'Este enlace ya se usó o caducó. Pega tu clave abajo.',
      lostH: '¿Perdiste la clave?', lostP: 'Escribe el correo de la compra y el número del recibo que Stripe te envió por correo («Receipt #1234-5678»).',
      receiptPh: 'Número de recibo', recFail: 'Ningún pedido coincide con ese correo y ese número de recibo.', saveTxt: 'Guardar como .txt', saveHint: 'Guarda la clave ahora: solo aparece en esta página, en ningún correo.',
      emailPh: 'tu@ejemplo.com', send: 'Enviar', sent: 'Si ese correo compró el mod, la clave va en camino.', noMail: 'El envío por correo aún no está activo. Escríbenos y te mandamos la clave.',
      busy: 'Demasiados intentos. Espera un minuto.', back: '← Volver a la página', install: 'Cómo instalar'
    },
    fr: {
      thanksH: 'Merci.', thanksP: 'Votre commande est confirmée. Gardez cette clé : elle active la Diplomatie IA sur 3 PC maximum.',
      waiting: 'Confirmation du paiement avec Stripe…', slow: 'C’est plus long que d’habitude. Vous pouvez recharger cette page.',
      keyLbl: 'Votre clé de licence', copy: 'Copier', copied: 'Copiée', where: 'En jeu : menu principal → Diplomatie IA → Licence → collez la clé.',
      noRefund: 'Remarque : une commande précédente de ce client a été remboursée, cette commande n’ouvre donc pas droit à un autre remboursement.',
      expired: 'Cette page de confirmation a expiré. Utilisez votre clé sur la page de téléchargement ou récupérez-la là-bas avec votre reçu.',
      blocked: 'Cette commande n’est pas active (remboursée ou contestée).', err: 'Une erreur est survenue. Rechargez la page ou contactez-nous.',
      dlH: 'Téléchargement', dlP: 'Collez votre clé de licence pour voir toutes les versions. Les liens expirent après 10 minutes ; revenez quand vous voulez.',
      keyPh: 'RPLN-XXXXX-XXXXX-XXXXX-XXXXX', show: 'Voir les téléchargements', latest: 'Dernière version', older: 'Versions précédentes (si un patch du jeu casse quelque chose)',
      tested: 'Testée avec HUMANKIND', none: 'Aucune version publiée pour l’instant. Le jeu et cette page l’afficheront dès sa sortie.',
      badKey: 'Clé introuvable. Vérifiez-la et réessayez.', codeUsed: 'Ce lien a déjà servi ou a expiré. Collez votre clé ci-dessous.',
      lostH: 'Clé perdue ?', lostP: 'Saisissez l’e-mail de l’achat et le numéro du reçu envoyé par Stripe (« Receipt #1234-5678 »).',
      receiptPh: 'Numéro de reçu', recFail: 'Aucune commande ne correspond à cet e-mail et ce numéro de reçu.', saveTxt: 'Enregistrer en .txt', saveHint: 'Gardez la clé maintenant : elle n’apparaît que sur cette page, dans aucun e-mail.',
      emailPh: 'vous@exemple.com', send: 'Envoyer', sent: 'Si cet e-mail a acheté le mod, la clé est en route.', noMail: 'L’envoi par e-mail n’est pas encore actif. Contactez-nous et nous enverrons votre clé.',
      busy: 'Trop de tentatives. Patientez une minute.', back: '← Retour à la page', install: 'Installation'
    },
    de: {
      thanksH: 'Danke.', thanksP: 'Deine Bestellung ist bestätigt. Bewahre diesen Schlüssel auf: Er schaltet die KI-Diplomatie auf bis zu 3 PCs frei.',
      waiting: 'Zahlung wird mit Stripe bestätigt…', slow: 'Das dauert länger als üblich. Du kannst die Seite neu laden.',
      keyLbl: 'Dein Lizenzschlüssel', copy: 'Kopieren', copied: 'Kopiert', where: 'Im Spiel: Hauptmenü → KI-Diplomatie → Lizenz → Schlüssel einfügen.',
      noRefund: 'Hinweis: Eine frühere Bestellung dieses Kunden wurde erstattet, daher ist diese Bestellung nicht erneut erstattungsfähig.',
      expired: 'Diese Bestätigungsseite ist abgelaufen. Nutze deinen Schlüssel auf der Download-Seite oder stelle ihn dort mit deiner Quittung wieder her.',
      blocked: 'Diese Bestellung ist nicht aktiv (erstattet oder angefochten).', err: 'Etwas ist schiefgelaufen. Lade die Seite neu oder kontaktiere uns.',
      dlH: 'Download', dlP: 'Füge deinen Lizenzschlüssel ein, um alle Versionen zu sehen. Links laufen nach 10 Minuten ab; komm jederzeit wieder.',
      keyPh: 'RPLN-XXXXX-XXXXX-XXXXX-XXXXX', show: 'Downloads anzeigen', latest: 'Neueste', older: 'Ältere Versionen (falls ein Spiel-Patch etwas kaputt macht)',
      tested: 'Getestet mit HUMANKIND', none: 'Noch keine Version veröffentlicht. Spiel und diese Seite zeigen sie, sobald sie da ist.',
      badKey: 'Schlüssel nicht gefunden. Prüfe ihn und versuche es erneut.', codeUsed: 'Dieser Link wurde schon benutzt oder ist abgelaufen. Füge unten deinen Schlüssel ein.',
      lostH: 'Schlüssel verloren?', lostP: 'Gib die E-Mail des Kaufs und die Belegnummer aus der Stripe-Quittung ein („Receipt #1234-5678“).',
      receiptPh: 'Belegnummer', recFail: 'Keine Bestellung passt zu dieser E-Mail und Belegnummer.', saveTxt: 'Als .txt speichern', saveHint: 'Speichere den Schlüssel jetzt: Er steht nur auf dieser Seite, in keiner E-Mail.',
      emailPh: 'du@beispiel.de', send: 'Senden', sent: 'Falls diese E-Mail die Mod gekauft hat, ist der Schlüssel unterwegs.', noMail: 'Der E-Mail-Versand ist noch nicht aktiv. Schreib uns, dann senden wir deinen Schlüssel.',
      busy: 'Zu viele Versuche. Warte eine Minute.', back: '← Zurück zur Seite', install: 'Installation'
    }
  };
  var lang = 'en';
  try { lang = new URLSearchParams(location.search).get('lang') || localStorage.getItem('rp-lang') || (navigator.language || 'en').slice(0, 2); } catch (e) {}
  if (!T[lang]) lang = 'en';
  document.documentElement.lang = lang;
  var t = T[lang];

  function api(method, path, body) {
    return fetch(API + path, {
      method: method,
      headers: body ? { 'content-type': 'application/json' } : {},
      body: body ? JSON.stringify(body) : undefined
    }).then(function (r) {
      return r.json().catch(function () { return {}; }).then(function (j) { j._status = r.status; return j; });
    });
  }
  function el(tag, attrs, kids) {
    var e = document.createElement(tag);
    for (var k in attrs || {}) { if (k === 'text') e.textContent = attrs[k]; else if (k.slice(0, 2) === 'on') e.addEventListener(k.slice(2), attrs[k]); else e.setAttribute(k, attrs[k]); }
    (kids || []).forEach(function (c) { if (c) e.appendChild(c); });
    return e;
  }
  function track(n, d) { try { if (window.umami) window.umami.track(n, d); } catch (e) {} }
  function fmtSize(b) { return b ? (b / 1048576).toFixed(1) + ' MB' : ''; }
  function renderDownloads(box, j) {
    box.innerHTML = '';
    if (j._status === 503 || !j.versions || !j.versions.length) { box.appendChild(el('p', { 'class': 'note', text: t.none })); return; }
    var vs = j.versions.slice().reverse();
    var latest = vs.filter(function (v) { return v.version === j.latest; })[0] || vs[0];
    box.appendChild(el('div', { 'class': 'card' }, [
      el('div', { 'class': 'tag', text: t.latest }),
      el('div', { 'class': 'ver', text: 'v' + latest.version }),
      el('div', { 'class': 'note', text: [latest.date, latest.testedGame ? t.tested + ' ' + latest.testedGame : '', fmtSize(latest.size)].filter(Boolean).join(' · ') }),
      latest.notes ? el('p', { text: latest.notes }) : null,
      el('a', { 'class': 'btn', href: latest.url, 'data-umami-event': 'download_click', 'data-umami-event-version': latest.version, text: '↓ ' + latest.file })
    ]));
    var rest = vs.filter(function (v) { return v !== latest; });
    if (rest.length) {
      var ul = el('ul', {}, rest.map(function (v) { return el('li', {}, [el('a', { href: v.url, 'data-umami-event': 'download_click', 'data-umami-event-version': v.version, text: 'v' + v.version + ' — ' + v.file }), el('span', { 'class': 'note', text: '  ' + [v.date, fmtSize(v.size)].filter(Boolean).join(' · ') })]); }));
      box.appendChild(el('details', {}, [el('summary', { text: t.older }), ul]));
    }
  }
  function errorText(j) {
    if (j._status === 429) return t.busy;
    if (j.error === 'invalid_key') return t.badKey;
    if (j.error === 'code_invalid') return t.codeUsed;
    if (j.error === 'not_found') return t.recFail;
    if (j.error === 'refunded' || j.error === 'disputed' || j.error === 'revoked') return t.blocked;
    return t.err;
  }
  function fillText() {
    document.querySelectorAll('[data-t]').forEach(function (n) { n.textContent = t[n.getAttribute('data-t')]; });
    document.querySelectorAll('[data-ph]').forEach(function (n) { n.setAttribute('placeholder', t[n.getAttribute('data-ph')]); });
  }
  return { track: track, api: api, t: t, lang: lang, el: el, renderDownloads: renderDownloads, errorText: errorText, fillText: fillText };
})();
