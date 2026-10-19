#!/usr/bin/env python3
"""Static integrity checker for DevOps Control Plane docs (stdlib only)."""
from pathlib import Path
import argparse, re, sys, datetime, json, os
ROOT=Path(__file__).resolve().parents[2]
DOCS=ROOT/'docs'
STATUSES={'proposed','accepted','implemented','verified','deprecated','superseded'}
REQUIRED_FEATURE_SECTIONS=['## User story / outcome','## Pre-conditions','## Main flow','## Authorization / tenant-scope review','## Negative & recovery scenarios','## Acceptance criteria','## Source & test traceability']
FEATURE_ROOTS=('docs/features/', 'docs/engineering/ai-orchestration/')
LINK=re.compile(r'(?<!!)\[[^]]*\]\(([^)]+)\)')
LOCAL_DIRECTORIES={'.git','.temp','.amr-local'}


def markdown_files(root):
    """Check maintained/untracked docs, excluding reserved local workspace copies.

    Do not use Git ignore rules: those must not hide canonical documentation.
    Prune local directories before descending and do not follow directory symlinks.
    """
    for directory, directories, names in os.walk(root,followlinks=False):
        directories[:]=[name for name in directories if name not in LOCAL_DIRECTORIES]
        for name in names:
            if name.endswith('.md'):
                yield Path(directory)/name


def frontmatter(txt):
    if not txt.startswith('---\n'): return {}
    parts=txt.split('---\n',2)
    if len(parts)<3: return {}
    out={}
    for line in parts[1].splitlines():
        if ':' not in line: continue
        key,value=line.split(':',1)
        out[key.strip()]=value.strip()
    return out


def main():
    parser=argparse.ArgumentParser()
    parser.add_argument('--strict-sources',action='store_true')
    args=parser.parse_args()
    files=sorted(markdown_files(ROOT))
    errors=[]; warnings=[]; ids={}; modules={}
    for p in files:
        rel=p.relative_to(ROOT).as_posix(); data=p.read_text('utf-8')
        if rel.startswith('docs/templates/') or rel in ('README.md','AGENTS.md','WORKFLOW.md','GOAL.md','docs/features/AGENTS.md','docs/tooling/README.md') or rel=='.github/pull_request_template.md': pass
        else:
            meta=frontmatter(data)
            for key in ('id','status','owner','last_reviewed'):
                if not meta.get(key): errors.append(f'{rel}: missing metadata {key}')
            if meta.get('status') and meta['status'] not in STATUSES: errors.append(f'{rel}: invalid status {meta["status"]}')
            if meta.get('id'):
                if meta['id'] in ids: errors.append(f'{rel}: duplicate id {meta["id"]} in {ids[meta["id"]]}')
                ids[meta['id']]=rel
            try: datetime.date.fromisoformat(meta.get('last_reviewed',''))
            except ValueError: errors.append(f'{rel}: invalid last_reviewed')
            if p.name=='module.md' and rel.startswith(FEATURE_ROOTS):
                if not meta.get('module_id'): errors.append(f'{rel}: missing module_id')
                if meta.get('module_id') in modules: errors.append(f'{rel}: duplicate module_id')
                modules[meta.get('module_id','')]=rel
            if '/features/' in rel and rel.startswith(FEATURE_ROOTS) and p.name!='README.md':
                if p.parent.name=='features':
                    for key in ('module_id','feature_id','phase','source_paths','test_paths'):
                        if key not in meta: errors.append(f'{rel}: missing feature metadata {key}')
                    if meta.get('feature_id') and meta.get('id')!=meta.get('feature_id'): errors.append(f'{rel}: feature id mismatch')
                    for section in REQUIRED_FEATURE_SECTIONS:
                        if section not in data: errors.append(f'{rel}: missing section {section}')
                    sibling=p.parent.parent/'module.md'
                    if not sibling.exists(): errors.append(f'{rel}: no owning module.md')
                    else:
                        m=frontmatter(sibling.read_text('utf-8'))
                        if m.get('module_id')!=meta.get('module_id'): errors.append(f'{rel}: module_id mismatch with owner')
                        if f'features/{p.name}' not in sibling.read_text('utf-8'): errors.append(f'{rel}: missing registry entry in module.md')
                    for key in ('source_paths','test_paths'):
                        try:
                            refs=json.loads(meta.get(key,'[]'))
                            if not isinstance(refs,list): raise ValueError('not a list')
                            if args.strict_sources and meta.get('status') in ('implemented','verified'):
                                if not refs: errors.append(f'{rel}: {key} empty for {meta.get("status")}')
                                for s in refs:
                                    if not (ROOT/s).exists(): errors.append(f'{rel}: missing source/test file {s}')
                        except (ValueError,TypeError, json.JSONDecodeError): errors.append(f'{rel}: invalid JSON array metadata {key}')
                    if meta.get('status') in ('implemented','verified') and meta.get('owner')=='unassigned': warnings.append(f'{rel}: implemented/verified but unassigned owner')
        # Read markdown links outside inline/fenced code with best effort (not a full Markdown parser).
        scrubbed=re.sub(r'```[\s\S]*?```','',data)
        scrubbed=re.sub(r'`[^`\n]*`','',scrubbed)
        for match in LINK.finditer(scrubbed):
            target=match.group(1).strip().split('#',1)[0].split('?',1)[0]
            if not target or '://' in target or target.startswith(('mailto:','#')): continue
            local=(p.parent/target).resolve()
            if not local.is_relative_to(ROOT.resolve()): errors.append(f'{rel}: link escapes repository: {target}')
            elif not local.exists(): errors.append(f'{rel}: broken local link: {target}')
    for required in ('AGENTS.md','GOAL.md','WORKFLOW.md','docs/README.md','docs/governance/source-of-truth.md','docs/contracts/permissions.md'):
        if not (ROOT/required).exists(): errors.append(f'missing required: {required}')
    print(f'Checked {len(files)} markdown files | {len(ids)} tracked IDs | {len(modules)} modules | {len(errors)} errors | {len(warnings)} warnings')
    for x in errors: print('ERROR:',x)
    for x in warnings: print('WARN:',x)
    return 1 if errors else 0
if __name__=='__main__': sys.exit(main())
