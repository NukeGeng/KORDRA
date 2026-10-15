#!/usr/bin/env python3
"""Validate experiment registry and safe-default example; stdlib only."""
import csv, pathlib, sys, re
ROOT=pathlib.Path(__file__).resolve().parents[2]
BASE=ROOT/'docs/reference-environments/lab-01'
P=BASE/'experiments/coverage-matrix.csv'
EXPECTED={'P0':'READ_ONLY','P1':'SANDBOX_WRITE','P2':'CLONE_DISRUPTIVE'}
allowed={'P0','P1','P2','P3'}
rows=list(csv.DictReader(P.open(encoding='utf-8',newline='')))
errors=[]; ids=set(); phases=set()
for n,r in enumerate(rows,2):
    tag=r.get('case_id','')
    if not re.fullmatch(r'LAB-[A-Z]+-[0-9]{3}',tag): errors.append(f'line {n}: invalid case id')
    if tag in ids: errors.append(f'line {n}: duplicate {tag}')
    ids.add(tag)
    p=r.get('phase',''); phases.add(p)
    if p not in allowed: errors.append(f'{tag}: invalid phase')
    if p in EXPECTED and r.get('mode')!=EXPECTED[p] and not(p=='P0' and r.get('mode')=='OFFLINE'):
        errors.append(f'{tag}: unsafe phase/mode pair')
    slug=r.get('module_slug','')
    if not (ROOT/'docs/features'/slug/'module.md').is_file(): errors.append(f'{tag}: unknown module {slug}')
    if r.get('run_status') not in {'NOT_RUN','BLOCKED','PASS','FAIL','INCONCLUSIVE'}: errors.append(f'{tag}: invalid result')
    if not r.get('scenario') or not r.get('expected_evidence'): errors.append(f'{tag}: missing scenario/evidence')
profile=(BASE/'profile.example.yaml').read_text(encoding='utf-8')
for forbidden in ['changeEnabled: true','operationMode: execute','canConsume: true','canPublish: true','talosReboot: enabled']:
    if forbidden in profile: errors.append(f'unsafe example profile: {forbidden}')
if 'operationMode: observe_only' not in profile: errors.append('profile must default observe_only')
print(f'Checked {len(rows)} cases, {len(ids)} unique IDs, phases={sorted(phases)}; errors={len(errors)}')
for e in errors: print('ERROR',e)
sys.exit(bool(errors))
