"""Phase B of the English-code refactor: rename Portuguese namespaces, types and files.

- folder and namespace CurrencyMod.Diplomacia -> CurrencyMod.Diplomacy
- Ia* types -> Ai* (IaModule -> AiModule, ...), with their files
Persisted names are kept on purpose: the "DiplomaciaIA" folders and save entry, the DPAPI entropy string and the
hot-reload carry key are data, not code.

Usage (WSL, repo root _Modding): python3 tools/rename_to_english.py
"""
import os
import re
import subprocess

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SRC = os.path.join(ROOT, 'src', 'CurrencyMod')
TYPES = ['IaModule', 'IaWorld', 'IaNation', 'IaConfig', 'IaLog', 'IaRuntime', 'IaCommands', 'IaSavePatches', 'IaJobs']
PROTECTED = ['"CurrencyMod.Diplomacia.Carry"']
TYPE_RE = re.compile(r'\b(' + '|'.join(TYPES) + r')\b')
NAMESPACE_RE = re.compile(r'(?<=CurrencyMod\.)Diplomacia\b|\bDiplomacia(?=\.)')


def git(*args):
    subprocess.run(['git', '-C', ROOT] + list(args), check=True)


def rewrite(path, path_rewrite=False):
    text = open(path, encoding='utf-8-sig').read()
    had_bom = open(path, 'rb').read(3) == b'\xef\xbb\xbf'
    keep = {token: '\0%d\0' % i for i, token in enumerate(PROTECTED)}
    for token, mark in keep.items():
        text = text.replace(token, mark)
    new = NAMESPACE_RE.sub('Diplomacy', text)
    new = TYPE_RE.sub(lambda m: 'Ai' + m.group(1)[2:], new)
    if path_rewrite:
        new = new.replace('Diplomacia\\', 'Diplomacy\\')
    for token, mark in keep.items():
        new = new.replace(mark, token)
    if new != text.replace('\0', '\0'):
        with open(path, 'w', encoding='utf-8-sig' if had_bom else 'utf-8', newline='') as handle:
            handle.write(new)
        return True
    return False


def main():
    old_dir = os.path.join(SRC, 'Diplomacia')
    if os.path.isdir(old_dir):
        git('mv', 'src/CurrencyMod/Diplomacia', 'src/CurrencyMod/Diplomacy')
    for name in TYPES:
        old = os.path.join(SRC, 'Diplomacy', name + '.cs')
        if os.path.exists(old):
            git('mv', 'src/CurrencyMod/Diplomacy/%s.cs' % name, 'src/CurrencyMod/Diplomacy/Ai%s.cs' % name[2:])
    changed = 0
    for base in (SRC, os.path.join(ROOT, 'tools', 'ia-bench', 'src')):
        for root, _, files in os.walk(base):
            if os.sep + 'obj' in root or os.sep + 'bin' in root:
                continue
            for name in files:
                if name.endswith('.cs'):
                    changed += rewrite(os.path.join(root, name))
    for project in (os.path.join(SRC, 'CurrencyMod.csproj'), os.path.join(ROOT, 'tools', 'ia-bench', 'IaBench.csproj')):
        changed += rewrite(project, path_rewrite=True)
    print('files changed:', changed)


if __name__ == '__main__':
    main()
