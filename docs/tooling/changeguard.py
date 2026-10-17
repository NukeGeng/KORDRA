#!/usr/bin/env python3
"""Enforce modified source files are owned by updated feature docs in the same PR."""
from pathlib import Path
import argparse, json, re, subprocess, sys
ROOT=Path(__file__).resolve().parents[2]

def frontmatter(text):
    if not text.startswith('---\n'): return {}
    parts=text.split('---\n',2)
    if len(parts)<3: return {}
    return {k.strip():v.strip() for line in parts[1].splitlines() if ':' in line for k,v in [line.split(':',1)]}

def matches(mapping, changed):
    mapping=mapping.replace('\\','/')
    return changed.startswith(mapping) if mapping.endswith('/') else mapping == changed

def check_changes(root, changed):
    governed=('src/', 'tests/', 'docs/tooling/', '.codex/', '.github/workflows/')
    src=sorted(s for s in changed if (s.startswith(governed) or s in {'global.json', 'Kordra.slnx'})
               and not s.endswith(('.md','.txt')))
    docs=[]
    specs=list((root/'docs/features').glob('*/features/*.md'))
    specs+=list((root/'docs/engineering/ai-orchestration/features').glob('*.md'))
    for p in sorted(specs):
        meta=frontmatter(p.read_text('utf-8'))
        try: paths=json.loads(meta.get('source_paths','[]'))+json.loads(meta.get('test_paths','[]'))
        except (ValueError,TypeError): paths=[]
        docs.append((p.relative_to(root).as_posix(),meta.get('feature_id','?'),paths))
    problems=[]
    for file in src:
        owned=[(doc,fid) for doc,fid,paths in docs if any(matches(mapping,file) for mapping in paths)]
        if not owned:
            problems.append(f'UNMAPPED {file}: add verified source_paths mapping to the owning feature spec')
        else:
            for doc,fid in owned:
                if doc not in changed: problems.append(f'STALE {file} belongs to {fid} ({doc}) but feature doc not updated in PR')
    return src,problems

def working_tree_paths(root):
    """Include unstaged, staged and untracked files in source ownership checks."""
    paths=set()
    commands=(['diff','--name-only','-z'],['diff','--cached','--name-only','-z'],
              ['ls-files','--others','--exclude-standard','-z'])
    for args in commands:
        cp=subprocess.run(['git',*args],cwd=root,capture_output=True,timeout=15)
        if cp.returncode: raise RuntimeError('unable to inspect Git working tree')
        paths.update(p.decode('utf-8','surrogateescape') for p in cp.stdout.split(b'\0') if p)
    return paths

def main():
    ap=argparse.ArgumentParser()
    ap.add_argument('--base',required=True,help='Existing git base ref or SHA (git diff base...HEAD)')
    ap.add_argument('--working-tree',action='store_true',help='also check staged, unstaged and untracked files')
    args=ap.parse_args()
    cp=subprocess.run(['git','diff','--name-only',f'{args.base}...HEAD'],cwd=ROOT, text=True, capture_output=True)
    if cp.returncode: print('ERROR unable to compare git refs:',cp.stderr.strip()); return 2
    changed=set(cp.stdout.splitlines())
    if args.working_tree:
        try: changed.update(working_tree_paths(ROOT))
        except (OSError,RuntimeError,subprocess.TimeoutExpired) as exc:
            print('ERROR unable to inspect working tree:',exc); return 2
    src,problems=check_changes(ROOT,changed)
    if not src: print('PASS no source changes detected'); return 0
    for problem in problems: print('ERROR',problem)
    print(f'Changed source files: {len(src)}; problems: {len(problems)}')
    return 1 if problems else 0
if __name__=='__main__': sys.exit(main())
