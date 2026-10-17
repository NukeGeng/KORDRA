#!/usr/bin/env python3
"""Kordra AMR v3: single-agent routing and evidence alongside existing governance checks."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tomllib

from amr_native import NativeError, discover, local_review, probe_auto_review, probe_profile
from changeguard import frontmatter, matches

ROOT = Path(__file__).resolve().parents[2]
EXCLUDED_PARTS = {'.git', '.amr-local', '__pycache__', 'bin', 'obj', 'TestResults'}
PROFILES = {'ROUTINE': ('gpt-6-luna', 'high'),
            'ENGINEERING': ('gpt-6.1-sol', 'medium'),
            'CRITICAL': ('gpt-6.1-sol', 'high')}
RISKS = {'auth', 'tenancy', 'platform', 'secrets', 'state-integrity', 'concurrency',
         'idempotency', 'rollback', 'production', 'remote-execution', 'migration'}
SIGNALS = RISKS | {'integration', 'messaging', 'contracts', 'source-uncertainty', 'stale-docs'}
REQUIRED_TASK = ('task_id', 'objective', 'feature_id', 'module_id', 'actor',
                 'scope', 'permission', 'paths', 'consulted', 'invariants',
                 'decisions', 'tests', 'findings', 'next_action')
KINDS = {'lookup', 'docs', 'format', 'crud', 'dto', 'tests', 'source', 'feature', 'integration', 'review'}


def safe_path(root, relative):
    if not isinstance(relative, str) or not relative or Path(relative).is_absolute():
        raise ValueError('expected nonempty repository-relative path')
    path = (root/relative).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError('path escapes repository')
    if set(path.relative_to(root.resolve()).parts) & EXCLUDED_PARTS or path.suffix == '.pyc':
        raise ValueError('task input resolves to excluded private/cache content')
    return path


def validate_task(task, root, *, require_inputs=True):
    for key in REQUIRED_TASK:
        if key not in task:
            raise ValueError(f'missing task field: {key}')
    for key in REQUIRED_TASK[:7] + ('next_action',):
        if not isinstance(task[key], str) or not task[key].strip():
            raise ValueError(f'expected nonempty string: {key}')
    for key in ('paths', 'consulted', 'invariants', 'decisions', 'tests', 'findings'):
        if not isinstance(task[key], list):
            raise ValueError(f'expected array: {key}')
    if not task['paths'] or not task['consulted'] or not task['invariants']:
        raise ValueError('paths, consulted documents and invariants must be explicit')
    for rel in task['paths'] + task['consulted']:
        path = safe_path(root, rel)
        if require_inputs and not path.is_file():
            raise ValueError(f'missing input file: {rel}')
    specs = [(rel, frontmatter(safe_path(root, rel).read_text('utf-8')))
             for rel in task['consulted'] if rel.endswith('.md')
             and safe_path(root, rel).is_file()]
    owners = [(rel, meta) for rel, meta in specs
              if meta.get('feature_id') == task['feature_id']
              and meta.get('module_id') == task['module_id']]
    if len(owners) > 1 or (require_inputs and len(owners) != 1):
        raise ValueError('consult exactly one matching owner feature spec')
    if owners:
        spec_path, meta = owners[0]
        mappings = json.loads(meta.get('source_paths', '[]')) + json.loads(meta.get('test_paths', '[]'))
        if any(p != spec_path and not any(matches(m, p) for m in mappings) for p in task['paths']):
            raise ValueError('task paths not mapped to owner feature')
    else:
        # Checkpoint reads may outlive an explicitly declared deletion. The
        # immutable plan digest preserves the task metadata validated at init;
        # dispatch still uses the strict default and requires the owner file.
        meta = {'status': 'proposed', 'test_paths': '[]'}
    if task.get('kind', 'source') not in KINDS:
        raise ValueError('unknown task kind')
    for key in ('bounded', 'spec_verified', 'tests_known'):
        if key in task and not isinstance(task[key], bool):
            raise ValueError(f'expected boolean: {key}')
    # A caller's assertion cannot promote a proposed spec into approved intent.
    if meta.get('status') not in ('accepted', 'implemented', 'verified'):
        task['spec_verified'] = False
    test_paths = json.loads(meta.get('test_paths', '[]'))
    if not test_paths or not all(safe_path(root, p).is_file() for p in test_paths):
        task['tests_known'] = False
    signals = task.get('signals', [])
    if not isinstance(signals, list) or any(s not in SIGNALS for s in signals):
        raise ValueError('unknown risk signal')
    attempts = task.get('failed_repairs', 0)
    if type(attempts) is not int or attempts < 0:
        raise ValueError('failed_repairs must be a nonnegative integer')
    if attempts and not task.get('failure_evidence'):
        raise ValueError('failed repairs require command/output/root-cause evidence')
    return task


def classify(task):
    signals = set(task.get('signals', []))
    # Paths provide an independent conservative floor; never trust confidence
    # or a claim that an authorization change is just routine CRUD.
    for p in task['paths']:
        lower = p.lower()
        if any(x in lower for x in ('auth', 'tenant', 'secret', 'permission', 'approval',
                                     'rollback', 'lease', 'fencing', 'migration')):
            signals.add('auth')
        if lower.startswith(('docs/reference-environments/', 'infra/', 'deploy/')):
            signals.add('production')
        if lower in ('agents.md', '.codex/config.toml') or lower.startswith(
                ('docs/tooling/amr', '.codex/rules/')):
            signals.add('platform')
    attempts = task.get('failed_repairs', 0)
    if attempts >= 4:
        return {'risk': 'CRITICAL', 'profile': 'CRITICAL', 'action': 'blocked',
                'rationale': ['repair limit reached for this root cause; human investigation required']}
    if signals & RISKS or attempts >= 2:
        return {'risk': 'CRITICAL', 'profile': 'CRITICAL', 'action': 'continue',
                'rationale': sorted(signals & RISKS) +
                    (['two evidenced failed repairs: escalate'] if attempts >= 2 else [])}
    if task.get('kind') == 'lookup' and not signals:
        return {'risk': 'ROUTINE', 'profile': None, 'action': 'deterministic',
                'rationale': ['use rg/file enumeration; no model needed']}
    routine = task.get('kind') in {'docs', 'format', 'crud', 'dto', 'tests'}
    if routine and not signals and all(task.get(k) is True for k in (
            'bounded', 'spec_verified', 'tests_known')) and not task['findings']:
        return {'risk': 'ROUTINE', 'profile': 'ROUTINE', 'action': 'continue',
                'rationale': ['bounded task with verified spec, ownership and known tests']}
    return {'risk': 'STANDARD', 'profile': 'ENGINEERING', 'action': 'continue',
            'rationale': sorted(signals) or ['substantive source work or unverified scope']}


def resolve(route, capabilities):
    result = dict(route)
    if route['profile'] is None or route['action'] == 'blocked':
        return result
    model, effort = PROFILES[route['profile']]
    result.update(model=model, effort=effort)
    available = any(x['model'] == model and effort in x['efforts']
                    for x in capabilities.get('models', []))
    result['support'] = 'advertised' if available else 'unsupported'
    if not available:
        result['action'] = 'blocked'
        result['rationale'] = result['rationale'] + ['model/effort unavailable; no silent fallback']
    return result


def tree_identity(root):
    """Content identity, including additions/deletions, even without Git.

    Private receipts live in .amr-local and never invalidate their own snapshot.
    Do not follow symlinks or collect content outside the repository.
    """
    files = {}
    for parent, dirs, names in os.walk(root, followlinks=False):
        for name in sorted(dirs):
            p = Path(parent)/name
            if p.is_symlink() and name not in EXCLUDED_PARTS:
                safe_path(root, p.relative_to(root).as_posix())
                files[p.relative_to(root).as_posix()] = hashlib.sha256(
                    os.readlink(p).encode()+b'\0mode='+str(p.lstat().st_mode).encode()).hexdigest()
        dirs[:] = sorted(d for d in dirs if d not in EXCLUDED_PARTS
                         and not (Path(parent)/d).is_symlink())
        for name in sorted(names):
            p = Path(parent)/name
            if p.suffix == '.pyc':
                continue
            rel = p.relative_to(root).as_posix()
            if p.is_symlink():
                safe_path(root, rel)
            data = os.readlink(p).encode() if p.is_symlink() else p.read_bytes()
            files[rel] = hashlib.sha256(data+b'\0mode='+str(p.lstat().st_mode).encode()).hexdigest()
    digest = hashlib.sha256(json.dumps(files, sort_keys=True).encode()).hexdigest()
    return {'digest': digest, 'files': files}


def git_identity(root):
    def read(*args):
        p = subprocess.run(['git', *args], cwd=root, text=True,
                           capture_output=True, timeout=15)
        return p.stdout.strip() if p.returncode == 0 else None
    # `ls-files --stage` captures the index's selected blob and file mode without
    # relying on HEAD, so this also works in fresh repositories and detects
    # staged-content changes that leave porcelain status unchanged.
    index = subprocess.run(['git','ls-files','--stage','-z'],cwd=root,
                           capture_output=True,timeout=30)
    index_bytes = index.stdout if index.returncode == 0 else b''
    return {'head': read('rev-parse', 'HEAD'),
            'branch': read('branch', '--show-current'),
            'status': read('status', '--porcelain'),
            'index': hashlib.sha256(index_bytes).hexdigest()}


def snapshot(root, task, route):
    return {'schemaVersion': 1, 'created_at': now(), 'task': task, 'route': route,
            'git': git_identity(root), 'content': tree_identity(root),
            'actual_model': 'unknown', 'usage': 'unknown'}


def validate_snapshot(root, handoff):
    if handoff.get('schemaVersion') != 1:
        raise ValueError('unsupported handoff schemaVersion')
    validate_task(handoff['task'], root)
    try:
        current_content = tree_identity(root)
    except ValueError as exc:
        raise ValueError(f'stale or unsafe context: {exc}') from exc
    if handoff['content'] != current_content or handoff['git'] != git_identity(root):
        raise ValueError('stale context: source/contracts/Git identity changed; revalidate decisions and tests')


def now():
    return datetime.datetime.now(datetime.timezone.utc).isoformat()


def save_private(root, name, data):
    directory = root/'.amr-local'
    if directory.is_symlink():
        raise ValueError('private evidence directory cannot be a symlink')
    directory.mkdir(mode=0o700, exist_ok=True)
    os.chmod(directory, 0o700)
    path = directory/name
    if path.is_symlink():
        raise ValueError('private receipt cannot be a symlink')
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(fd, 'w') as stream:
        json.dump(data, stream, indent=2, ensure_ascii=False)
        stream.write('\n')
    os.chmod(path, 0o600)
    return path


def gates(root, base=None):
    before = tree_identity(root)['digest']
    commands = [
        ('docs', [sys.executable, 'docs/tooling/doccheck.py', '--strict-sources']),
        ('scope', [sys.executable, 'docs/tooling/scopecheck.py']),
        ('unit', [sys.executable, '-m', 'unittest', 'discover', '-s', 'docs/tooling/tests', '-v'])]
    has_product = (root/'Kordra.slnx').is_file()
    if has_product:
        commands.extend([
            ('product-restore', ['dotnet', 'restore', 'Kordra.slnx', '--locked-mode']),
            ('product-build', ['dotnet', 'build', 'Kordra.slnx', '--configuration', 'Release', '--no-restore']),
            ('product-test', ['dotnet', 'test', 'Kordra.slnx', '--configuration', 'Release',
                              '--no-build', '--no-restore', '--logger', 'trx'])])
    if base:
            commands.append(('mapping', [sys.executable, 'docs/tooling/changeguard.py', '--base', base, '--working-tree']))
    results = []
    product_ready = True
    for name, cmd in commands:
        if name.startswith('product-') and not product_ready:
            results.append({'name': name, 'command': cmd, 'status': 'blocked',
                            'reason': 'prior product stage failed; do not test stale binaries'})
            continue
        try:
            p = subprocess.run(cmd, cwd=root, text=True, capture_output=True, timeout=90)
            results.append({'name': name, 'command': cmd, 'status': 'passed' if p.returncode == 0
                            else 'failed', 'exit_code': p.returncode,
                            'output': p.stdout+p.stderr})
        except subprocess.TimeoutExpired:
            results.append({'name': name, 'command': cmd, 'status': 'failed', 'reason': 'timeout'})
        except OSError as exc:
            results.append({'name': name, 'command': cmd, 'status': 'failed', 'reason': str(exc)})
        if name.startswith('product-'):
            product_ready = results[-1]['status'] == 'passed'
    after = tree_identity(root)['digest']
    stable = before == after
    return {'schemaVersion': 1, 'created_at': now(), 'content': before, 'source_stable': stable,
            'checks': results, 'local_status': 'passed' if all(
                x['status'] == 'passed' for x in results) and stable else 'failed',
            'source_mapping': 'executed' if base else 'not_configured',
            'product_tests': 'executed' if has_product else 'not_configured: no product solution'}


def completion_errors(receipt, digest, critical=False, product_required=False):
    """Validate a human-authored review ledger; it is not proof of provider identity."""
    errors = []
    if receipt.get('schemaVersion') != 1 or receipt.get('content') != digest:
        errors.append('missing schema or stale content identity')
    if receipt.get('local_status') != 'passed' or receipt.get('source_stable') is not True:
        errors.append('gate run failed or source changed during checks')
    checks = receipt.get('checks', [])
    if not checks or any(x.get('status') != 'passed' for x in checks):
        errors.append('required checks missing or not passed')
    if not {'docs', 'scope', 'unit'} <= {x.get('name') for x in checks}:
        errors.append('mandatory repository gates missing')
    if product_required and (receipt.get('product_tests') != 'executed' or not {
            'product-restore', 'product-build', 'product-test'} <= {x.get('name') for x in checks}):
        errors.append('product solution exists but required product checks are missing')
    review = receipt.get('review', {})
    if critical and (review.get('status') != 'verified' or review.get('independent') is not True
                     or review.get('content') != digest or not review.get('evidence')):
        errors.append('current independent review evidence required')
    for finding in receipt.get('findings', []):
        state = finding.get('state')
        if not finding.get('id') or not finding.get('evidence'):
            errors.append('finding ID and validation evidence required')
        if state == 'fixed':
            followup = finding.get('followup_review', {})
            if (finding.get('reproduction_passed') is not True or not isinstance(followup, dict)
                    or followup.get('status') != 'verified' or followup.get('content') != digest
                    or not followup.get('evidence')):
                errors.append('fix requires rerun reproduction and follow-up review')
        elif state == 'dismissed':
            if not finding.get('rationale'):
                errors.append('dismissal requires evidence-based rationale')
        elif state != 'open' or finding.get('blocking', True):
            errors.append('unresolved blocking or unknown finding')
    if receipt.get('requires_human_approval') and not receipt.get('human_approval_evidence'):
        errors.append('explicit human approval evidence required; native review is insufficient')
    return errors


def launch_command(root, task, route):
    if route['action'] != 'continue' or route.get('support') != 'advertised':
        raise ValueError('route is blocked or deterministic; cannot invoke a model')
    if task['scope'] != 'local-development' or set(task.get('signals', [])) & {
            'production', 'secrets', 'remote-execution'}:
        raise ValueError('sensitive operations require the authorized human operations workflow')
    prompt = ('Read AGENTS.md and WORKFLOW.md. Resume the structured task in '
              '.amr-local/handoff.json; validate it with `python3 docs/tooling/amr.py '
              'validate-context` before relying on decisions/tests. Keep production '
              'operations under explicit human approval. Task: '+task['objective'])
    return ['codex', '--strict-config', '--cd', str(root), '--model', route['model'],
            '-c', 'model_reasoning_effort='+json.dumps(route['effort']),
            '-c', 'agents.enabled=false',
            '--sandbox', 'workspace-write', '--ask-for-approval', 'on-request',
            '-c', 'approvals_reviewer="user"', prompt]


def main():
    import amr_goal
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    sub.add_parser('discover')
    for command in ('route', 'context', 'launch'):
        p = sub.add_parser(command)
        p.add_argument('--task', required=True, type=Path)
        if command == 'launch':
            p.add_argument('--dry-run', action='store_true')
    sub.add_parser('validate-context')
    p = sub.add_parser('gates'); p.add_argument('--base')
    sub.add_parser('probe-models')
    sub.add_parser('probe-auto-review')
    sub.add_parser('review')
    p = sub.add_parser('validate-review')
    p.add_argument('--receipt', type=Path, required=True)
    p.add_argument('--critical', action='store_true')
    amr_goal.add_parser(sub)
    args = parser.parse_args()
    result = {}
    code = 0
    if args.command == 'goal':
        result = amr_goal.main(ROOT, args)
    elif args.command == 'discover':
        result = discover(ROOT)
        save_private(ROOT, 'capabilities.json', result)
    elif args.command in ('route', 'context', 'launch'):
        task = validate_task(json.loads(args.task.read_text()), ROOT)
        capabilities = discover(ROOT)
        route = resolve(classify(task), capabilities)
        result = route
        if args.command in ('context', 'launch'):
            save_private(ROOT, 'handoff.json', snapshot(ROOT, task, route))
        if args.command == 'launch':
            cmd = launch_command(ROOT, task, route)
            if args.dry_run:
                result = {'command': cmd, 'route': route, 'execution': 'not_run'}
            else:
                return subprocess.call(cmd, cwd=ROOT)
        code = 2 if route['action'] == 'blocked' else 0
    elif args.command == 'validate-context':
        validate_snapshot(ROOT, json.loads((ROOT/'.amr-local/handoff.json').read_text()))
        result = {'status': 'current'}
    elif args.command == 'gates':
        result = gates(ROOT, args.base)
        save_private(ROOT, 'gates.json', result)
        code = 1 if result['local_status'] != 'passed' else 0
    elif args.command == 'probe-models':
        result = {}
        for profile, (model, effort) in PROFILES.items():
            try:
                result[profile] = probe_profile(model, effort)
            except (NativeError, OSError) as exc:
                result[profile] = {'status': 'failed', 'reason': str(exc)}
            save_private(ROOT, 'model-probes.json', result)
        code = 0 if all(x['status'] == 'verified' for x in result.values()) else 1
    elif args.command == 'probe-auto-review':
        config = tomllib.loads((ROOT/'.codex/config.toml').read_text())
        try:
            result = probe_auto_review(*PROFILES['ENGINEERING'],
                                       extra_policy=config['auto_review']['extra_policy'])
        except (NativeError, OSError) as exc:
            result = {'status': 'failed', 'reason': str(exc), 'outcome': 'unknown',
                      'created_at': now()}
        save_private(ROOT, 'auto-review.json', result)
        code = 0 if result['status'] == 'verified' else 1
    elif args.command == 'review':
        before = tree_identity(ROOT)['digest']
        instructions = (
            'Independently review FEAT-AMR-001 in docs/tooling/amr.py, amr_native.py, '
            'doccheck.py, changeguard.py, tests, .codex/config.toml and related docs. '
            'Read AGENTS.md. Focus on runtime correctness, fail-closed model/effort '
            'selection, stale evidence, approval boundaries and false passing gates. '
            'Review the current repository content and Git diff when available. '
            'Use read-only tools. Return actionable defects with paths/lines; '
            'do not claim provider PR review or modify files.')
        try:
            result = local_review(ROOT, *PROFILES['CRITICAL'], instructions=instructions)
        except (NativeError, OSError) as exc:
            result = {'status': 'failed', 'reason': str(exc), 'outcome': 'unknown',
                      'channel': 'native_local_review'}
        after = tree_identity(ROOT)['digest']
        result.update(content=before, independent=True, created_at=now())
        result['source_stable'] = before == after
        if before != after and result['status'] == 'verified':
            result['status'] = 'stale'
        save_private(ROOT, 'native-review.json', result)
        code = 0 if result['status'] == 'verified' else 1
    else:
        receipt = json.loads(args.receipt.read_text())
        errors = completion_errors(receipt, tree_identity(ROOT)['digest'], args.critical,
                                   product_required=(ROOT/'Kordra.slnx').is_file())
        result = {'status': 'blocked' if errors else 'ledger_valid', 'errors': errors,
                  'limitation': 'structural ledger validation, not provider or human identity attestation'}
        code = 1 if errors else 0
    print(json.dumps(result, indent=2, ensure_ascii=False))
    return code


if __name__ == '__main__':
    try:
        sys.exit(main())
    except (ValueError, KeyError, TypeError, OSError, NativeError, subprocess.SubprocessError) as exc:
        print(json.dumps({'status': 'failed', 'reason': str(exc)}), file=sys.stderr)
        sys.exit(2)
