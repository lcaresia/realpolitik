#!/usr/bin/env bash
# Gera build_v/ (master 9:16, 1080x1920) a partir de build/index.html (master 16:9).
# Uso (no WSL): bash make_vertical.sh      -> depois: cd build_v && npx hyperframes@0.8.140 render -f 60 ...
set -e
cd "$(dirname "$0")"
mkdir -p build_v
rsync -a --delete --exclude renders --exclude snapshots build/ build_v/ 2>/dev/null || { rm -rf build_v/assets; cp -r build/assets build_v/assets; cp build/{hyperframes.json,meta.json,package.json} build_v/; }
cp build/index.html build_v/index.html
cd build_v
f=index.html
# canvas vertical
sed -i 's|data-resolution="landscape"|data-resolution="portrait"|; s|width=1920, height=1080|width=1080, height=1920|; s|width:1920px;|width:1080px;|; s|height:1080px;|height:1920px;|; s|data-width="1920" data-height="1080"|data-width="1080" data-height="1920"|' $f
sed -i 's|data-composition-id="main"|data-composition-id="main"|' $f
# coordenadas numéricas do timeline (centro 1920x1080 -> 1080x1920)
sed -i 's|left:960px;top:540px;width:240px|left:540px;top:960px;width:240px|; s|\.seal, \.sealc { position:absolute; left:960px; top:540px;|.seal, .sealc { position:absolute; left:540px; top:960px;|' $f
sed -i 's|{ x: 0, y: 235, scale: 0.42|{ x: 0, y: 262, scale: 0.42|' $f
sed -i 's|Math.floor(rng() \* 1900)|Math.floor(rng() * 1060)|; s|top:${Math.floor(rng() \* 1080)}px|top:${Math.floor(rng() * 1900)}px|' $f
sed -i 's|tl.fromTo("#wipe", { x: -900, opacity: 0.9 }, { x: 2300,|tl.fromTo("#wipe", { x: -900, opacity: 0.9 }, { x: 1900,|' $f
# estilos do layout vertical (sobrescrevem o horizontal)
python3 - 2>/dev/null || true
cat > /tmp/vert.css <<'EOF'
      /* ===== LAYOUT 9:16 (zonas seguras: nada importante nos 8% de cima e nos 15% de baixo) ===== */
      .card { width:940px; padding:56px 66px 64px; } .card .tx { font-size:44px; } .card .lab { font-size:20px; margin-bottom:26px; } .card .sig { font-size:32px; }
      #cardA { margin:-270px 0 0 -470px !important; } #cardB { margin:-340px 0 0 -470px !important; }
      .card.small { width:700px; padding:36px 44px 40px; } .small .tx { font-size:30px; } .small .lab { font-size:15px; }
      #cardW2 { left:30px !important; top:300px !important; } #cardZ { left:350px !important; right:auto !important; top:760px !important; }
      #chipJoin { left:520px !important; top:640px !important; }
      .stamp { font-size:64px; border-width:7px; padding:12px 28px; }
      #stLying { margin:-90px 0 0 -330px !important; } #stWar { margin:-70px 0 0 -395px !important; } #stInt { margin:-70px 0 0 -345px !important; }
      .big { font-size:96px; } .mid { font-size:56px; } .sub { font-size:22px; }
      .shot { width:1000px !important; height:563px !important; border-width:8px; }
      .cap { top:520px !important; padding:0 40px; } #capAI { top:1230px !important; } .cap .mid, .cap .big { font-size:56px !important; }
      #t2a .big { font-size:60px !important; }
      #t2b, #t2c { bottom:470px !important; } #t2b .big, #t2c .big { font-size:80px !important; } #t2d { bottom:380px !important; } #t2d .big { font-size:170px !important; }
      #s3b .sub, #s4b .sub, #s3b > .sub { bottom:470px !important; font-size:20px !important; padding:0 30px; }
      .slam { font-size:40px; bottom:20px; } .lg { font-size:46px; min-width:420px; padding:8px 24px; } #langs { top:90px !important; gap:12px !important; }
      #odo { font-size:300px !important; } #s3d .cap { top:420px !important; }
      #chip0 { left:60px !important; top:1280px !important; } #chip1 { left:560px !important; top:1280px !important; }
      #chip2 { left:60px !important; top:1390px !important; } #chip3 { left:560px !important; top:1390px !important; }
      #chip4 { left:60px !important; top:1500px !important; } #chip5 { left:560px !important; top:1500px !important; }
      .chip { font-size:26px; padding:12px 20px; }
      #t4 { font-size:60px !important; } #s4c .fill { padding:0 60px !important; }
      .mosaic { width:960px !important; height:860px !important; left:60px !important; top:520px !important; }
      .mosaic .m { width:460px !important; height:259px !important; }
      .mosaic .m:nth-child(1){left:0 !important;top:0 !important} .mosaic .m:nth-child(2){left:500px !important;top:0 !important}
      .mosaic .m:nth-child(3){left:0 !important;top:290px !important} .mosaic .m:nth-child(4){left:500px !important;top:290px !important}
      .mosaic .m:nth-child(5){left:0 !important;top:580px !important} .mosaic .m:nth-child(6){left:500px !important;top:580px !important}
      #tMos .mid { font-size:44px !important; }
      .env { width:820px; height:520px; } .env .to { font-size:46px; margin-top:140px; }
      #ring1, #ring2 { left:540px !important; top:640px !important; } #lkSeal { left:540px !important; top:700px !important; }
      #lk1 { font-size:112px !important; letter-spacing:.02em !important; } #lkBar { width:900px !important; }
      #lk2 { font-size:30px !important; } #lk3 > div:first-child { font-size:60px !important; } #lk3 > div:last-child { font-size:24px !important; }
      #wipe { height:2300px; }
      #vPan, #vCouncil, #vCycle, #vMail { left:48px !important; top:686px !important; width:984px !important; height:547px !important; }
      .badge { left:0; right:0; bottom:auto; top:175px; text-align:center; font-size:15px; }
      #fl1 { font-size:46px !important; } #fl2 { font-size:80px !important; }
      #cardF { margin-top:0 !important; }
EOF
awk 'FNR==NR{css=css $0 "\n"; next} /<\/style>/ && !done {printf "%s", css; done=1} {print}' /tmp/vert.css $f > /tmp/v.html && cp /tmp/v.html $f
echo "build_v/index.html pronto"
