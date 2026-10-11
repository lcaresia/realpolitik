"""Link the mod's Harmony patches to the game methods they patch, on top of graphify's graphs.

graphify (tree-sitter) sees [HarmonyPatch(typeof(T), "M")] only as an attribute, so it does not connect a patch
class to the patched game method. This script parses the attributes deterministically, finds the game node for
T.M in the game graph, merges both graphs and adds a "patches" edge per resolved patch. It also writes a checklist
(resolved / unresolved) so the mapping can be verified.

Usage (WSL): python3 harmony_edges.py <mod src dir> <mod graph.json> <game graph.json> <out dir>
"""
import json
import os
import re
import sys

ATTRIBUTE = re.compile(
    r'\[HarmonyPatch\(\s*typeof\(\s*([\w.]+)\s*\)\s*(?:,\s*(?:nameof\(\s*[\w.]+\.(\w+)\s*\)|"(\w+)"))?[^\]]*\)\]')
CLASS = re.compile(r'\bclass\s+(\w+)')
TARGET_METHOD = re.compile(r'\bTargetMethods?\s*\(\s*\)')
ACCESS_TOOLS = re.compile(r'AccessTools\.(?:Declared)?Method\(\s*typeof\(\s*([\w.]+)\s*\)\s*,\s*"(\w+)"')
CLASS_BEFORE = re.compile(r'\bclass\s+(\w+)[^{]*\{')


def dynamic_targets(text, body):
    """Targets chosen by reflection, read from the tables the TargetMethod(s) body iterates:
    - a table of ("Namespace.Type", "Method") string pairs;
    - dictionary keys [typeof(T)] combined with AccessTools.DeclaredMethod(type, "Method") in the body;
    - typeof(T).GetMethods(...) filtered by m.Name.EndsWith("Suffix") (method suffix, prefixed with '*')."""
    pairs = re.findall(r'\(\s*"([\w.]+)"\s*,\s*"(\w+)"\s*\)', text)
    if pairs and 'TypeByName' in body:
        return pairs
    method = re.search(r'DeclaredMethod\(\s*\w+\s*,\s*"(\w+)"', body)
    if method:
        return [(t, method.group(1)) for t in re.findall(r'\[typeof\(\s*([\w.]+)\s*\)\]\s*=', text)]
    suffix = re.search(r'typeof\(\s*([\w.]+)\s*\)\.GetMethods\(.*?EndsWith\("(\w+)"\)', body, re.S)
    if suffix:
        return [(suffix.group(1), '*' + suffix.group(2))]
    return []


def find_patches(src_dir):
    """Every [HarmonyPatch(typeof(T), M)] with the class it decorates (the next class declaration)."""
    patches = []
    for root, _, files in os.walk(src_dir):
        if os.sep + 'obj' in root or os.sep + 'bin' in root:
            continue
        for name in files:
            if not name.endswith('.cs'):
                continue
            path = os.path.join(root, name)
            text = open(path, encoding='utf-8-sig').read()
            for match in ATTRIBUTE.finditer(text):
                type_name = match.group(1).split('.')[-1]
                method = match.group(2) or match.group(3)
                after = CLASS.search(text, match.end())
                line = text.count('\n', 0, match.start()) + 1
                patches.append({
                    'file': os.path.relpath(path, src_dir).replace(os.sep, '/'),
                    'line': line,
                    'type': type_name,
                    'method': method,
                    'patch_class': after.group(1) if after else None,
                })
            # Dynamic targets: TargetMethod()/TargetMethods() bodies resolved with AccessTools.Method(typeof(T), "M").
            for match in TARGET_METHOD.finditer(text):
                owners = list(CLASS_BEFORE.finditer(text, 0, match.start()))
                owner = owners[-1].group(1) if owners else None
                end = text.find('\n        }', match.end())
                body = text[match.end(): end if end > 0 else match.end() + 1500]
                line = text.count('\n', 0, match.start()) + 1
                found = ACCESS_TOOLS.findall(body)
                for type_name, method in found:
                    patches.append({'file': os.path.relpath(path, src_dir).replace(os.sep, '/'), 'line': line,
                                    'type': type_name.split('.')[-1], 'method': method, 'patch_class': owner})
                if not found:
                    found = dynamic_targets(text, body)
                    for type_name, method in found:
                        patches.append({'file': os.path.relpath(path, src_dir).replace(os.sep, '/'), 'line': line,
                                        'type': type_name.split('.')[-1], 'method': method, 'patch_class': owner})
                if not found:
                    patches.append({'file': os.path.relpath(path, src_dir).replace(os.sep, '/'), 'line': line,
                                    'type': '(dynamic)', 'method': None, 'patch_class': owner})
    return patches


def load(path):
    graph = json.load(open(path, encoding='utf-8'))
    return graph, graph['nodes'], graph.get('links') or graph.get('edges') or []


def main():
    src_dir, mod_path, game_path, out_dir = sys.argv[1:5]
    mod, mod_nodes, mod_links = load(mod_path)
    game, game_nodes, game_links = load(game_path)

    # Game lookup: class label -> files, and (file, method label) -> node id.
    class_files = {}
    methods = {}
    for node in game_nodes:
        label = node.get('label', '')
        file = node.get('source_file')
        if label.startswith('.') and label.endswith('()'):
            methods.setdefault((file, label[1:-2]), node['id'])
        elif node.get('_callable_class') or label[:1].isupper():
            class_files.setdefault(label, set()).add(file)

    # Mod lookup: (file, class label) -> node id.
    mod_classes = {}
    for node in mod_nodes:
        mod_classes.setdefault((node.get('source_file', '').split('CurrencyMod/', 1)[-1], node.get('label')), node['id'])

    resolved, unresolved, new_links = [], [], []
    for patch in find_patches(src_dir):
        target = None
        if patch['method']:
            for file in class_files.get(patch['type'], ()):
                if patch['method'].startswith('*'):
                    suffix = patch['method'][1:]
                    target = next((node_id for (f, m), node_id in methods.items() if f == file and m.endswith(suffix)), None)
                else:
                    target = methods.get((file, patch['method']))
                if target:
                    break
        source = mod_classes.get((patch['file'], patch['patch_class']))
        if target and source:
            new_links.append({'source': source, 'target': target, 'relation': 'patches', '_origin': 'harmony_edges',
                              'confidence': 'EXTRACTED', 'confidence_score': 1.0, 'context': 'harmony',
                              'source_file': 'CurrencyMod/' + patch['file'], 'source_location': 'L%d' % patch['line'],
                              'weight': 1.0})
            resolved.append(patch)
        else:
            patch['why'] = 'no game method' if not target else 'no mod class node'
            unresolved.append(patch)

    seen = {n['id'] for n in game_nodes}
    merged_nodes = game_nodes + [n for n in mod_nodes if n['id'] not in seen]
    merged = dict(game)
    merged['nodes'] = merged_nodes
    merged['links'] = game_links + mod_links + new_links
    os.makedirs(out_dir, exist_ok=True)
    json.dump(merged, open(os.path.join(out_dir, 'graph.json'), 'w', encoding='utf-8'))

    with open(os.path.join(out_dir, 'HARMONY_PATCHES.md'), 'w', encoding='utf-8') as report:
        report.write('# Harmony patches: mod -> game\n\n')
        report.write('%d attributes, %d linked, %d unresolved.\n\n' % (len(resolved) + len(unresolved), len(resolved), len(unresolved)))
        report.write('## Linked\n\n')
        for p in sorted(resolved, key=lambda p: (p['type'], p['method'] or '')):
            report.write('- `%s.%s` <- `%s` (%s:%d)\n' % (p['type'], p['method'], p['patch_class'], p['file'], p['line']))
        report.write('\n## Unresolved (check by hand)\n\n')
        for p in unresolved:
            report.write('- `%s.%s` <- `%s` (%s:%d): %s\n' % (p['type'], p['method'], p['patch_class'], p['file'], p['line'], p['why']))
    print('nodes %d, links %d, patches linked %d, unresolved %d'
          % (len(merged_nodes), len(merged['links']), len(resolved), len(unresolved)))


if __name__ == '__main__':
    main()
