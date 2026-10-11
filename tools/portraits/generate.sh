#!/bin/bash
# Runs the portrait generator in WSL with the Codex from ~/.local/bin. Arguments go straight to generate_portraits.py.
export PATH="$HOME/.local/bin:$HOME/.nvm/versions/node/v22.22.2/bin:/usr/bin:/bin"
cd "$(dirname "$0")"
python3 generate_portraits.py "$@"
