# Enviar o Setup para a Microsoft analisar (grátis, a cada versão)

O Setup não tem assinatura de código. Não existe assinatura gratuita para um produto pago e fechado no Brasil:
- a SignPath Foundation é só para código aberto, sem chave de licença;
- a assinatura do Azure para pessoa física atende só EUA e Canadá.

Até a reputação crescer, o Windows mostra o aviso azul do SmartScreen. Enviar cada versão à Microsoft acelera isso e
não custa nada. O arquivo **não** fica público (diferente do VirusTotal).

## Passo a passo (5 minutos, com a sua conta Microsoft)

1. Abra https://www.microsoft.com/en-us/wdsi/filesubmission e entre com a sua conta Microsoft.
2. Escolha **Software developer** (você é o autor do arquivo).
3. Produto: **Microsoft Defender SmartScreen**.
4. Envie `_Modding\dist\<versão>\Realpolitik_Setup_<versão>.exe`.
5. Detecção: **"Incorrectly detected as malware/malicious"**, ou "unknown/unrecognized app".
6. Comentário sugerido:
   > Installer for "Realpolitik: Living Nations for HUMANKIND", a paid single-player mod for the game HUMANKIND.
   > Built with Inno Setup 6.7.3. It copies the mod and the official BepInEx 5.4.23.5 into the game folder. No
   > network access during install.
7. Guarde o número do envio. A resposta chega por e-mail em alguns dias.

Repita a cada versão nova, porque cada arquivo novo começa sem reputação. Envie também o zip manual se algum comprador
relatar bloqueio.

## Se um dia valer comprar o certificado

- Certum ou SSL.com, pessoa física: US$ 130–230 por ano, com validação de cerca de 2 semanas.
- Depois: `tools\gerar-release.ps1 -Versao X -Certificado C:\caminho\cert.pfx`, com a senha em
  `$env:REALPOLITIK_PFX_SENHA`. Precisa do `signtool.exe` (Windows SDK).
