#!/bin/bash
# Roda o gerador de retratos no WSL com o Codex de ~/.local/bin. Argumentos vão direto para gerar-retratos.py.
export PATH="$HOME/.local/bin:$HOME/.nvm/versions/node/v22.22.2/bin:/usr/bin:/bin"
cd "$(dirname "$0")"
python3 gerar-retratos.py "$@"
