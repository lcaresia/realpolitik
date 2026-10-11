"""Uploads a release to the mod's mod.io page (game Humankind 2599, mod 6436393) and makes it the active file.

Credentials come from _Modding/.env and are never printed:
  MOD_IO_API_PATH      the API base given by mod.io (https://u-<id>.modapi.io/v1)
  MOD_IO_ACCESS_TOKEN  an OAuth 2 access token with WRITE scope (mod.io > My account > Access > OAuth Access).
                       The plain MOD_IO_API_KEY is read-only and cannot upload files.

Usage (in WSL): python3 tools/modio-publish.py <version> [changelog file]
Uploads dist/<version>/Realpolitik_<version>_manual.zip (the file players install by hand; the Setup.exe lives on
GitHub Releases).
"""
import json
import os
import sys
import urllib.error
import urllib.request
import uuid

GAME_ID, MOD_ID = 2599, 6436393
ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))


def load_env():
    values = {}
    for line in open(os.path.join(ROOT, '.env'), encoding='utf-8-sig'):
        line = line.strip()
        if '=' in line and not line.startswith('#'):
            key, value = line.split('=', 1)
            values[key.strip()] = value.strip().strip('"').strip("'")
    return values


def multipart(fields, file_field, file_path):
    boundary = uuid.uuid4().hex
    parts = []
    for name, value in fields.items():
        parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{name}"\r\n\r\n{value}\r\n'.encode())
    name = os.path.basename(file_path)
    parts.append(f'--{boundary}\r\nContent-Disposition: form-data; name="{file_field}"; filename="{name}"\r\n'
                 f'Content-Type: application/zip\r\n\r\n'.encode())
    parts.append(open(file_path, 'rb').read())
    parts.append(f'\r\n--{boundary}--\r\n'.encode())
    return b''.join(parts), f'multipart/form-data; boundary={boundary}'


def main():
    if len(sys.argv) < 2:
        sys.exit('usage: modio-publish.py <version> [changelog file]')
    version = sys.argv[1]
    changelog = open(sys.argv[2], encoding='utf-8').read().strip() if len(sys.argv) > 2 else ''
    env = load_env()
    token, path = env.get('MOD_IO_ACCESS_TOKEN', ''), env.get('MOD_IO_API_PATH', '').rstrip('/')
    if not token or not path:
        sys.exit('MOD_IO_ACCESS_TOKEN (OAuth, write scope) or MOD_IO_API_PATH missing in .env')
    archive = os.path.join(ROOT, 'dist', version, f'Realpolitik_{version}_manual.zip')
    if not os.path.exists(archive):
        sys.exit(f'missing {archive}: run tools/make-release.ps1 -Version {version} first')
    body, content_type = multipart({'version': version, 'changelog': changelog, 'active': 'true'}, 'filedata', archive)
    request = urllib.request.Request(f'{path}/games/{GAME_ID}/mods/{MOD_ID}/files', data=body, method='POST', headers={
        'Authorization': 'Bearer ' + token, 'Content-Type': content_type, 'Accept': 'application/json',
        # Cloudflare in front of mod.io rejects the default Python user agent (error 1010).
        'User-Agent': 'Realpolitik-publisher/1.0',
    })
    try:
        with urllib.request.urlopen(request, timeout=300) as response:
            result = json.loads(response.read().decode())
    except urllib.error.HTTPError as error:
        sys.exit(f'upload failed: HTTP {error.code} {error.read().decode()[:300].replace(token, "***")}')
    print(f"uploaded: file {result.get('id')} version {result.get('version')} ({result.get('filesize')} bytes), active")


if __name__ == '__main__':
    main()
