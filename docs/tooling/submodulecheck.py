#!/usr/bin/env python3
"""Bounded Clients repository declaration, indexed pin and CI checkout checks."""
import argparse
import configparser
import hashlib
import json
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[2]
CLIENT_PATH = 'clients'
CLIENT_URL = 'https://github.com/NukeGeng/KORDRA-Clients.git'


def configured_roots(root):
    """Only the owner-approved boundary may escape Product traversal."""
    declaration = root / '.gitmodules'
    if declaration.is_symlink():
        raise ValueError('.gitmodules cannot be a symlink')
    if not declaration.exists():
        return frozenset()
    parser = configparser.ConfigParser(interpolation=None, strict=True)
    parser.optionxform = str
    try:
        parser.read_string(declaration.read_text('utf-8'))
    except (configparser.Error, UnicodeError) as exc:
        raise ValueError('invalid .gitmodules') from exc
    expected = {'path': CLIENT_PATH, 'url': CLIENT_URL}
    if (parser.defaults() or parser.sections() != ['submodule "clients"']
            or dict(parser['submodule "clients"']) != expected):
        raise ValueError('.gitmodules must declare only the approved clients path and portable URL')
    if (root / CLIENT_PATH).is_symlink():
        raise ValueError('clients cannot be a symlink')
    return frozenset({CLIENT_PATH})


def git(root, *args):
    result = subprocess.run(['git', *args], cwd=root, capture_output=True,
                            text=True, timeout=30)
    if result.returncode:
        raise ValueError('unable to inspect Clients Git state')
    return result.stdout.strip()


def pinned_clients(root):
    if not configured_roots(root):
        raise ValueError('Clients submodule declaration is missing')
    entries = git(root, 'ls-files', '--stage', '--', CLIENT_PATH).splitlines()
    if len(entries) != 1:
        raise ValueError('clients must be one indexed gitlink, not tracked frontend files')
    match = re.fullmatch(r'160000 ([0-9a-f]{40}|[0-9a-f]{64}) 0\tclients', entries[0])
    if not match:
        raise ValueError('clients must be a stage-0 gitlink with an immutable commit ID')
    return match[1]


def pin_identity(root):
    return hashlib.sha256(('gitlink\0' + pinned_clients(root)).encode()).hexdigest()


def check(root, require_checkout=False):
    pin = pinned_clients(root)
    if require_checkout:
        clients = root / CLIENT_PATH
        if not (clients / '.git').is_file() or (clients / '.git').is_symlink():
            raise ValueError('Clients checkout is not initialized as a submodule')
        if Path(git(clients, 'rev-parse', '--show-toplevel')).resolve() != clients.resolve():
            raise ValueError('Clients must have its own Git working tree')
        if git(clients, 'rev-parse', 'HEAD') != pin:
            raise ValueError('Clients checkout does not match the indexed pin')
        if git(clients, 'status', '--porcelain', '--untracked-files=all'):
            raise ValueError('CI Clients checkout must be clean')
    return {'status': 'passed', 'path': CLIENT_PATH, 'pin': pin,
            'checkout': 'verified' if require_checkout else 'not_requested'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--require-checkout', action='store_true')
    args = parser.parse_args()
    try:
        result = check(ROOT, args.require_checkout)
    except (OSError, ValueError, subprocess.TimeoutExpired) as exc:
        print(json.dumps({'status': 'failed', 'reason': str(exc)}))
        return 1
    print(json.dumps(result))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
