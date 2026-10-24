#!/usr/bin/env python3
"""Kordra AMR v4: Architect / Developer / Reviewer single-agent engineering."""
import argparse
import datetime
import hashlib
import json
import os
import re
from pathlib import Path
import subprocess
import sys
import tomllib

from amr_native import NativeError, discover, local_review, probe_auto_review, probe_profile
from changeguard import frontmatter, matches
from submodulecheck import configured_roots, pin_identity

ROOT = Path(__file__).resolve().parents[2]
EXCLUDED_PARTS = {'.git', '.amr-local', '__pycache__', 'bin', 'obj', 'TestResults'}
POLICY_VERSION = '4'
PROFILES = {'ROUTINE': ('gpt-6-luna', 'max'),
            'ENGINEERING': ('gpt-6-luna', 'max'),
            'CRITICAL': ('gpt-6.1-sol', 'high'),
            'DEBUGGER_MEDIUM': ('gpt-6.1-sol', 'medium')}
# Only historical execution receipts may use these v3 selections.
LEGACY_PROFILES = {'ROUTINE': ('gpt-6-luna', 'high'),
                   'ENGINEERING': ('gpt-6.1-sol', 'medium'),
                   'CRITICAL': ('gpt-6.1-sol', 'high')}
RISKS = {'auth', 'tenancy', 'platform', 'secrets', 'state-integrity', 'concurrency',
         'idempotency', 'rollback', 'production', 'remote-execution', 'migration',
         'architecture', 'critical-design', 'complex-debugging', 'reliability', 'milestone-review'}
SIGNALS = RISKS | {'integration', 'messaging', 'contracts', 'source-uncertainty', 'stale-docs'}
REQUIRED_TASK = ('task_id', 'objective', 'feature_id', 'module_id', 'actor',
                 'scope', 'permission', 'paths', 'consulted', 'invariants',
                 'decisions', 'tests', 'findings', 'next_action')
KINDS = {'lookup', 'docs', 'format', 'crud', 'dto', 'tests', 'source', 'feature',
         'integration', 'review', 'bugfix', 'architecture', 'complex-debugging'}


def safe_path(root, relative):
    if not isinstance(relative, str) or not relative or Path(relative).is_absolute():
        raise ValueError('expected nonempty repository-relative path')
    path = (root/relative).resolve()
    if not path.is_relative_to(root.resolve()):
        raise ValueError('path escapes repository')
    if set(path.relative_to(root.resolve()).parts) & EXCLUDED_PARTS or path.suffix == '.pyc':
        raise ValueError('task input resolves to excluded private/cache content')
    return path


def safe_output_path(root, relative):
    """Validate a precisely declared file target, whether new or already present."""
    if not isinstance(relative, str) or not relative:
        raise ValueError('expected nonempty repository-relative output path')
    normalized = relative.replace('\\', '/')
    parts = normalized.split('/')
    if (normalized.startswith('/') or (parts and parts[0].endswith(':'))
            or '..' in parts or any(part in EXCLUDED_PARTS for part in parts)
            or normalized.endswith('.pyc')):
        raise ValueError('unsafe task output path')
    root = Path(root).resolve()
    candidate = root / normalized
    current = root
    for part in parts:
        if not part or part == '.':
            continue
        current = current / part
        if current.is_symlink():
            raise ValueError('task output path traverses a symlink')
        if current.exists() and current != candidate and not current.is_dir():
            raise ValueError('task output parent is not a directory')
    resolved = candidate.resolve()
    if not resolved.is_relative_to(root):
        raise ValueError('output path escapes repository')
    if candidate.exists() and not candidate.is_file():
        raise ValueError('task output target must be a file')
    return candidate


def validate_task(task, root, *, require_inputs=True, legacy_policy=False):
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
    outputs = task.get('output_paths', [])
    if (not isinstance(outputs, list) or any(not isinstance(p, str) for p in outputs)
            or len(outputs) != len(set(outputs))):
        raise ValueError('output_paths must be a unique string array')
    for rel in task['paths'] + task['consulted']:
        path = safe_path(root, rel)
        if require_inputs and not path.is_file():
            raise ValueError(f'missing input file: {rel}')
    for rel in outputs:
        safe_output_path(root, rel)
    specs = [(rel, frontmatter(safe_path(root, rel).read_text('utf-8')))
             for rel in task['consulted'] if rel.endswith('.md')
             and safe_path(root, rel).is_file()]
    owners = [(rel, meta) for rel, meta in specs
              if meta.get('feature_id') == task['feature_id']
              and meta.get('module_id') == task['module_id']]
    if len(owners) > 1 or (require_inputs and len(owners) != 1):
        raise ValueError('consult exactly one matching owner feature spec')
    overlap = set(outputs) & (set(task['paths']) | set(task['consulted']))
    if owners:
        spec_path, meta = owners[0]
        # A feature often needs to add the mappings for files created by the
        # same task. Permit only that single owner document to be both consulted
        # and an output; all other input/output overlap remains invalid.
        if overlap - {spec_path}:
            raise ValueError('only the matching owner feature may also be a task output')
        mappings = json.loads(meta.get('source_paths', '[]')) + json.loads(meta.get('test_paths', '[]'))
        planned = (json.loads(meta.get('planned_source_paths', '[]'))
                   + json.loads(meta.get('planned_test_paths', '[]'))
                   + json.loads(meta.get('planned_doc_paths', '[]')))
        if (any(not isinstance(items, list) for items in (mappings, planned))
                or any(not isinstance(p, str) or not p for p in mappings + planned)):
            raise ValueError('feature path mappings must be JSON string arrays')
        if any(p != spec_path and not any(matches(m, p) for m in mappings) for p in task['paths']):
            raise ValueError('task paths not mapped to owner feature')
        if any(not any(matches(m, p) for m in planned) for p in outputs):
            raise ValueError('task outputs must be declared in the owner feature planned mappings')
    else:
        if overlap:
            raise ValueError('output_paths must be separate from task inputs without an owner feature')
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
    if not legacy_policy:
        attempts = task.get('failed_repairs', 0)
        if type(attempts) is not int or attempts < 0:
            raise ValueError('failed_repairs must be a nonnegative integer')
        if attempts and not task.get('failure_evidence'):
            raise ValueError('failed repairs require command/output/root-cause evidence')
        role = execution_role(task)
        if task.get('execution_role') and task['execution_role'] != role:
            raise ValueError('execution_role cannot downgrade an Architect or required debugger route')
        if role not in {'architect', 'developer', 'debugger'}:
            raise ValueError('execution_role must be architect, developer or debugger')
        if role == 'architect' and any(Path(p).suffix.lower() != '.md'
                for p in task['paths'] + task.get('output_paths', [])):
            raise ValueError('Architect tasks are bounded design documents; source implementation belongs to Developer')
    if not legacy_policy and role == 'debugger':
        defect = task.get('reported_defect')
        if attempts < 2 or not isinstance(defect, dict):
            raise ValueError('Debugger escalation requires two failed Luna repairs and a reported defect')
        if not all(isinstance(defect.get(k), str) and defect[k].strip()
                   for k in ('finding_id', 'summary', 'evidence')):
            raise ValueError('Debugger scope requires finding ID, summary and reproducible evidence')
        affected = defect.get('affected_paths')
        if (not isinstance(affected, list) or not affected
                or any(p not in task['paths'] for p in affected)):
            raise ValueError('Debugger paths must be restricted to the reported defect')
        if 'write_paths' in task and any(p not in affected for p in task['write_paths']):
            raise ValueError('Debugger writes must stay within the reported defect paths')
    return task


def execution_role(task):
    explicit = task.get('execution_role')
    if task.get('kind') == 'architecture' or set(task.get('signals', [])) & {'architecture', 'critical-design'}:
        return 'architect'
    if task.get('kind') == 'complex-debugging' or task.get('failed_repairs', 0) >= 2:
        return 'debugger'
    return explicit or 'developer'


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
    role = execution_role(task)
    risk = 'CRITICAL' if signals & RISKS or attempts >= 2 else 'STANDARD'
    if role == 'architect':
        return {'risk': 'CRITICAL', 'profile': 'CRITICAL', 'action': 'continue', 'role': role,
                'rationale': ['bounded architecture/design work; return after design for human approval']}
    if role == 'debugger':
        if attempts < 2:
            return {'risk': risk, 'profile': 'CRITICAL', 'action': 'blocked', 'role': role,
                    'rationale': ['debugger escalation waits for two evidenced Luna repair attempts']}
        high_risk = signals & {'auth', 'tenancy', 'platform', 'secrets', 'state-integrity',
                               'concurrency', 'rollback', 'production', 'remote-execution',
                               'migration', 'reliability'}
        profile = 'CRITICAL' if high_risk else 'DEBUGGER_MEDIUM'
        return {'risk': 'CRITICAL' if high_risk else 'STANDARD', 'profile': profile,
                'action': 'continue', 'role': role,
                'rationale': ['bounded reported-defect escalation after two Luna repairs'] + sorted(high_risk)}
    if task.get('kind') == 'lookup' and not signals:
        return {'risk': 'ROUTINE', 'profile': None, 'action': 'deterministic',
                'role': role, 'rationale': ['use rg/file enumeration; no model needed']}
    routine = task.get('kind') in {'docs', 'format', 'crud', 'dto', 'tests'}
    if routine and not signals and all(task.get(k) is True for k in (
            'bounded', 'spec_verified', 'tests_known')) and not task['findings']:
        return {'risk': risk, 'profile': 'ROUTINE', 'action': 'continue', 'role': role,
                'rationale': ['bounded task with verified spec, ownership and known tests']}
    return {'risk': risk, 'profile': 'ENGINEERING', 'action': 'continue', 'role': role,
            'rationale': sorted(signals) or ['substantive source work or unverified scope']}


def design_review_required(task):
    return task.get('kind') == 'architecture' or bool(
        set(task.get('signals', [])) & {'architecture', 'critical-design'})


def task_identity(task):
    """Bind design advice to the full validated task, including its boundaries."""
    return hashlib.sha256(json.dumps(task, sort_keys=True).encode()).hexdigest()


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
    boundaries = configured_roots(root)
    files = {rel: pin_identity(root) for rel in boundaries}
    for parent, dirs, names in os.walk(root, followlinks=False):
        for name in sorted(dirs):
            p = Path(parent)/name
            if p.is_symlink() and name not in EXCLUDED_PARTS:
                safe_path(root, p.relative_to(root).as_posix())
                files[p.relative_to(root).as_posix()] = hashlib.sha256(
                    os.readlink(p).encode()+b'\0mode='+str(p.lstat().st_mode).encode()).hexdigest()
        dirs[:] = sorted(d for d in dirs if d not in EXCLUDED_PARTS
                         and (Path(parent)/d).relative_to(root).as_posix() not in boundaries
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
            'status': read('status', '--porcelain', '--ignore-submodules=all'),
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


def gate_reuse_inputs(root, files, validation_context):
    """Conservative inputs: tooling tests consume docs; product checks do not.

    Only hashes are retained; environment values/config credentials stay in memory.
    A fixture context is an explicit contributor declaration, not an attestation of
    a database's state. Rotate it after fixture/service changes or unknown effects.
    """
    def fingerprint(value):
        return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()
    # Native tool-call correlation changes for every shell invocation; it is not
    # an input to repository tests/builds. Keep every other environment input.
    environment = fingerprint({k: v for k, v in os.environ.items() if k != 'CODEX_TOOL_CALL_ID'})
    external = {}
    for path in (Path.home()/'.nuget/NuGet/NuGet.Config', Path.home()/'.nuget/NuGet.Config'):
        try:
            external[str(path)] = hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else None
        except OSError:
            return {}  # Cannot inspect a potentially relevant input: do not reuse.
    product = {p: value for p, value in files.items()
               if not (p.endswith('.md') and (p.startswith('docs/') or p.endswith('/README.md') or p == 'README.md'))}
    common = {'environment': environment, 'python': [sys.executable, sys.version],
              'validation_context': validation_context}
    inputs = {'unit': fingerprint(dict(common, files=files))}
    if (root/'Kordra.slnx').is_file():
        try:
            info = subprocess.run(['dotnet', '--info'], cwd=root, capture_output=True,
                                  text=True, timeout=15)
            if info.returncode != 0:
                return inputs
        except (OSError, subprocess.TimeoutExpired):
            return inputs
        product_digest = fingerprint(dict(common, files=product, dotnet=info.stdout,
                                          external=external))
        inputs.update({name: product_digest for name in
                       ('product-restore', 'product-build', 'product-test')})
    return inputs


def reusable_gate(previous, name, command, signature, validation_context):
    """Reuse only a successful original execution, at most one hour old."""
    if (not isinstance(previous, dict) or previous.get('schemaVersion') != 1 or previous.get('local_status') != 'passed'
            or previous.get('source_stable') is not True or not signature):
        return None
    # Integration state is external to source: require a caller-managed fixture epoch.
    if name == 'product-test' and not validation_context:
        return None
    checks = previous.get('checks', [])
    if (not isinstance(checks, list) or any(not isinstance(c, dict) for c in checks)
            or len([c for c in checks if c.get('name') == name]) != 1):
        return None
    old = next(c for c in checks if c.get('name') == name)
    if old.get('execution') not in {'executed', 'reused'}:
        return None
    origin = old.get('reuse', {}) if old.get('execution') == 'reused' else {
        'executed_at': old.get('executed_at'), 'content': previous.get('content')}
    if not isinstance(origin, dict):
        return None
    try:
        age = datetime.datetime.now(datetime.timezone.utc) - datetime.datetime.fromisoformat(origin['executed_at'])
    except (KeyError, TypeError, ValueError):
        return None
    if (not datetime.timedelta(0) <= age <= datetime.timedelta(hours=1)
            or old.get('status') != 'passed' or old.get('exit_code') != 0
            or old.get('command') != command or old.get('input_digest') != signature):
        return None
    return dict(old, execution='reused', reuse=origin)


def gates(root, base=None, *, reuse=None, validation_context=None):
    if validation_context is not None and (not isinstance(validation_context, str) or not validation_context.strip()):
        raise ValueError('validation context must be a nonempty, nonsecret fixture epoch')
    before_tree = tree_identity(root)
    before = before_tree['digest']
    signatures = gate_reuse_inputs(root, before_tree['files'], validation_context)
    commands = [
        ('docs', [sys.executable, 'docs/tooling/doccheck.py', '--strict-sources']),
        ('scope', [sys.executable, 'docs/tooling/scopecheck.py']),
        ('unit', [sys.executable, '-m', 'unittest', 'discover', '-s', 'docs/tooling/tests', '-v'])]
    if (root/'.gitmodules').exists():
        commands.append(('clients', [sys.executable, 'docs/tooling/submodulecheck.py']))
    has_product = (root/'Kordra.slnx').is_file()
    if has_product:
        commands.extend([
            ('product-restore', ['dotnet', 'restore', 'Kordra.slnx', '--locked-mode']),
            ('product-build', ['dotnet', 'build', 'Kordra.slnx', '--configuration', 'Release', '--no-restore']),
            # Shared PostgreSQL saturation fixtures must not overlap across test projects.
            ('product-test', ['dotnet', 'test', 'Kordra.slnx', '-m:1', '--configuration', 'Release',
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
        cached = reusable_gate(reuse, name, cmd, signatures.get(name), validation_context)
        if cached:
            results.append(cached)
            continue
        try:
            p = subprocess.run(cmd, cwd=root, text=True, capture_output=True, timeout=300 if name == 'product-test' else 90)
            results.append({'name': name, 'command': cmd, 'status': 'passed' if p.returncode == 0
                            else 'failed', 'exit_code': p.returncode,
                            'output': p.stdout+p.stderr})
        except subprocess.TimeoutExpired:
            results.append({'name': name, 'command': cmd, 'status': 'failed', 'reason': 'timeout'})
        except OSError as exc:
            results.append({'name': name, 'command': cmd, 'status': 'failed', 'reason': str(exc)})
        results[-1].update(execution='executed', executed_at=now(), input_digest=signatures.get(name))
        if name.startswith('product-'):
            product_ready = results[-1]['status'] == 'passed'
    after = tree_identity(root)['digest']
    stable = before == after and signatures == gate_reuse_inputs(root, tree_identity(root)['files'], validation_context)
    return {'schemaVersion': 1, 'created_at': now(), 'content': before, 'source_stable': stable,
            'validation_context': validation_context,
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
    errors.extend(finding_errors(receipt.get('findings', []), digest))
    if receipt.get('requires_human_approval') and not receipt.get('human_approval_evidence'):
        errors.append('explicit human approval evidence required; native review is insufficient')
    return errors


def finding_errors(findings, digest):
    errors = []
    if not isinstance(findings, list) or any(not isinstance(f, dict) for f in findings):
        return ['findings must be an explicit array of objects']
    for finding in findings:
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
    return errors


def bir_result(ledger, head, root=None):
    """Validate a manually maintained PR ledger, without triggering any review.

    Evidence references are contributor assertions, not provider attestation.
    In particular this cannot enforce a limit in the native review service.
    """
    errors = []
    sha = lambda value: isinstance(value, str) and bool(re.fullmatch(r'[0-9a-f]{40}', value))
    nonempty = lambda value: isinstance(value, str) and bool(value.strip())
    if not isinstance(ledger, dict):
        return {'status': 'blocked', 'errors': ['expected BIR ledger object']}
    policy_version = ledger.get('policyVersion')
    schema_version = ledger.get('schemaVersion')
    legacy_policy = policy_version == '3.1'
    if legacy_policy:
        if type(schema_version) is not int or schema_version != 1:
            errors.append('historical v3.1 BIR ledgers require schemaVersion 1')
    elif policy_version == POLICY_VERSION:
        if type(schema_version) is not int or schema_version != 2:
            errors.append('AMR v4 BIR ledgers require schemaVersion 2; migrate initiator evidence')
    else:
        errors.append('unsupported BIR schema or policy version')
    pr = ledger.get('pr', {})
    if (not isinstance(pr, dict) or not nonempty(pr.get('repository'))
            or type(pr.get('number')) is not int or pr['number'] < 1):
        errors.append('explicit repository and PR number required')
    base = ledger.get('base_sha')
    if not sha(head) or ledger.get('head_sha') != head or not sha(base):
        errors.append('exact current head and immutable base SHAs required')
    rounds = ledger.get('rounds', [])
    findings = ledger.get('findings', [])
    if not isinstance(rounds, list) or not isinstance(findings, list):
        return {'status': 'blocked', 'errors': errors + ['rounds/findings must be arrays']}
    count = len(rounds)  # A request consumes a round even if failed or unknown.
    if not 1 <= count <= 3:
        errors.append('BIR requires 1–3 rounds; no fourth automated request permitted')
    seen, fingerprints, by_id = set(), set(), {}
    for finding in findings:
        if not isinstance(finding, dict) or any(not nonempty(finding.get(key)) for key in (
                'id', 'category', 'root_cause', 'bug_key', 'evidence')):
            errors.append('finding requires stable ID/category/root cause/bug key/evidence')
            continue
        ident = finding['id']
        key = tuple(' '.join(finding[k].lower().split()) for k in ('category', 'root_cause', 'bug_key'))
        if ident in by_id or key in fingerprints:
            errors.append('duplicate canonical finding or root-cause/bug fingerprint')
        by_id[ident] = finding
        fingerprints.add(key)
        if type(finding.get('blocking')) is not bool:
            errors.append('finding blocking flag must be boolean')
        state = finding.get('state')
        if state == 'fixed':
            followup = finding.get('followup_review', {})
            valid_followup = (isinstance(followup, dict) and followup.get('status') == 'verified'
                and followup.get('head_sha') == head and nonempty(followup.get('evidence')))
            if valid_followup:
                reviewer = followup.get('reviewer')
                if reviewer == 'codex_auto_code_review':
                    valid_followup = any(isinstance(r, dict) and r.get('status') == 'verified'
                        and r.get('head_sha') == head and r.get('review_id') == followup.get('review_id')
                        and nonempty(r.get('review_id')) for r in rounds)
                elif reviewer == 'sol_high' and legacy_policy:
                    valid_followup = (followup.get('observed_model'), followup.get('observed_effort')) == PROFILES['CRITICAL']
                elif reviewer != 'human_maintainer':
                    valid_followup = False
            if finding.get('reproduction_passed') is not True or not valid_followup:
                errors.append('fixed finding requires reproduction and current-head follow-up review')
        elif state == 'dismissed':
            if not nonempty(finding.get('rationale')) or finding.get('validated_sha') != head:
                errors.append('dismissal requires rationale and current-head evidence')
        elif state != 'open' or finding.get('blocking', True):
            errors.append('unresolved blocking or unknown finding')
    previous, review_ids, reviewed_heads = None, set(), set()
    commits = {base, head} if sha(base) and sha(head) else set()
    ancestry = [(base, head)] if commits else []
    for index, review in enumerate(rounds, 1):
        if not isinstance(review, dict):
            errors.append('invalid round object'); continue
        if type(review.get('number')) is not int or review['number'] != index:
            errors.append('review rounds must be contiguous and immutable')
        if not legacy_policy:
            initiator = review.get('initiator')
            if not isinstance(initiator, dict):
                errors.append('AMR v4 round requires declared initiator and evidence')
            else:
                trigger = initiator.get('trigger')
                role = initiator.get('role')
                if not nonempty(initiator.get('evidence')):
                    errors.append('AMR v4 round initiator requires evidence')
                if trigger == 'automatic':
                    if role != 'provider_automation':
                        errors.append('automatic review must identify provider_automation as initiator')
                elif trigger == 'manual':
                    if not isinstance(role, str) or role not in {
                            'luna_goal_coordinator', 'human_maintainer'}:
                        errors.append('manual review trigger has an unauthorized initiator role')
                    elif role == 'luna_goal_coordinator' and not nonempty(
                            initiator.get('session_evidence')):
                        errors.append('manual Luna trigger requires session-bound evidence')
                    elif role == 'human_maintainer' and not nonempty(
                            initiator.get('authorization_evidence')):
                        errors.append('manual maintainer trigger requires authorization evidence')
                else:
                    errors.append('AMR v4 initiator trigger must be automatic or manual')
        start, end = review.get('base_sha'), review.get('head_sha')
        if not sha(start) or not sha(end) or start == end:
            errors.append('round requires distinct immutable diff SHAs')
        else:
            if end in reviewed_heads:
                errors.append('PR revision was already reviewed; do not repeat an unchanged review')
            reviewed_heads.add(end)
            commits.update((start, end)); ancestry.extend(((start, end), (end, head)))
        if index == 1:
            if review.get('scope') != 'full' or start != base:
                errors.append('round 1 must cover full PR diff from pinned base')
        elif (previous is None or previous.get('status') != 'verified'
              or review.get('scope') != 'incremental' or start != previous.get('head_sha')):
            errors.append('follow-up requires last verified reviewed SHA as incremental base')
        if (review.get('status') not in {'requested', 'verified', 'failed', 'unknown'}
                or review.get('channel') != 'codex_auto_code_review'
                or not nonempty(review.get('request_evidence')) or not nonempty(review.get('context'))):
            errors.append('round requires request evidence, context and known status')
        if review.get('status') == 'verified':
            review_id = review.get('review_id')
            if (not nonempty(review_id) or review_id in review_ids
                    or not nonempty(review.get('completion_evidence'))):
                errors.append('verified round requires unique provider identity and terminal evidence')
            if nonempty(review_id):
                review_ids.add(review_id)
        if root is not None and sha(start) and sha(end):
            trees = []
            for commit in (start, end):
                tree = subprocess.run(['git', 'rev-parse', commit+'^{tree}'], cwd=root,
                    capture_output=True, text=True, timeout=15)
                trees.append(tree.stdout.strip() if tree.returncode == 0 else None)
            if trees[0] is not None and trees[0] == trees[1]:
                errors.append('review range has no source-tree changes; do not duplicate an unchanged PR review')
        new, tracked = review.get('new_findings'), review.get('tracked_findings')
        if (not isinstance(new, list) or not isinstance(tracked, list)
                or any(not nonempty(x) for x in new + tracked)):
            errors.append('round requires explicit finding ID arrays')
        else:
            if len(set(new)) != len(new) or set(new) & seen:
                errors.append('do not repost previously reported findings')
            if set(tracked) != seen or len(tracked) != len(set(tracked)):
                errors.append('carry all earlier canonical findings without dropping unresolved bugs')
            seen.update(new)
        previous = review
    if seen != set(by_id):
        errors.append('round findings must match canonical ledger; no untracked or dropped findings')
    provider_current = (previous is not None and previous.get('status') == 'verified'
                        and previous.get('head_sha') == head)
    escalation = ledger.get('escalation', {})
    escalation_review = escalation.get('review', {}) if isinstance(escalation, dict) else {}
    escalation_targets = {'human_maintainer'} | ({'sol_high'} if legacy_policy else set())
    escalated_current = (count == 3 and isinstance(escalation, dict) and isinstance(escalation_review, dict)
        and escalation.get('target') in escalation_targets
        and nonempty(escalation.get('reason')) and nonempty(escalation.get('evidence'))
        and escalation_review.get('reviewer') == escalation.get('target')
        and escalation_review.get('status') == 'verified'
        and escalation_review.get('head_sha') == head and nonempty(escalation_review.get('evidence')))
    if escalated_current:
        if escalation_review.get('reviewer') == 'sol_high' and legacy_policy:
            escalated_current = (escalation_review.get('observed_model'),
                                 escalation_review.get('observed_effort')) == PROFILES['CRITICAL']
        if escalation_review.get('scope') == 'incremental':
            last_verified = next((r for r in reversed(rounds)
                                  if isinstance(r, dict) and r.get('status') == 'verified'), None)
            escalated_current = (escalated_current and last_verified is not None
                and escalation_review.get('base_sha') == last_verified.get('head_sha')
                and nonempty(escalation_review.get('context')))
        elif escalation_review.get('scope') != 'full':
            escalated_current = False
    if not provider_current and not escalated_current:
        errors.append('completed provider review on current PR head required')
    if root is not None:
        for commit in commits:
            cp = subprocess.run(['git', 'rev-parse', '--verify', commit+'^{commit}'], cwd=root,
                                capture_output=True, text=True, timeout=15)
            if cp.returncode or cp.stdout.strip() != commit:
                errors.append('review SHA is not a locally verified commit')
        for older, newer in ancestry:
            if sha(older) and sha(newer):
                cp = subprocess.run(['git', 'merge-base', '--is-ancestor', older, newer],
                                    cwd=root, capture_output=True, timeout=15)
                if cp.returncode:
                    errors.append('review history is not ancestral; reconcile base changes/force pushes manually')
    exhausted = count >= 3
    if exhausted and errors:
        escalation = ledger.get('escalation', {})
        if (not isinstance(escalation, dict)
                or escalation.get('target') not in escalation_targets
                or not nonempty(escalation.get('reason')) or not nonempty(escalation.get('evidence'))):
            errors.append('exhausted review budget with remaining work requires escalation record')
    return {'status': 'blocked' if errors else 'ledger_valid', 'errors': sorted(set(errors)),
            'rounds_used': count, 'rounds_remaining': max(0, 3-count),
            'next_action': ('escalate_to_human_maintainer' if exhausted and errors else
                            'stop_automatic_review' if exhausted else
                            'reconcile_or_request_next_bounded_round' if errors else 'no_review_required_for_this_head'),
            'merge_approval': False, 'native_hard_cap': 'unverified',
            'provider_review_on_current_head': provider_current,
            'resolution_channel': (escalation_review['reviewer'] if escalated_current else
                                   'codex_auto_code_review' if provider_current else None),
            'enforcement': 'manual; validator does not trigger or stop provider reviews'}


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
    p.add_argument('--reuse', help='private prior gate receipt; never skip docs/scope/mapping')
    p.add_argument('--validation-context', help='nonsecret fixture epoch; rotate after DB/service changes')
    p = sub.add_parser('validate-bir', help='validate manual BIR PR ledger; never dispatch reviews')
    p.add_argument('--receipt', required=True, type=Path)
    p.add_argument('--head', required=True)
    sub.add_parser('probe-models')
    sub.add_parser('probe-auto-review')
    p = sub.add_parser('review')
    p.add_argument('--task', type=Path)
    p.add_argument('--purpose', choices=('code', 'design'), default='code')
    p = sub.add_parser('validate-review')
    p.add_argument('--receipt', type=Path, required=True)
    p.add_argument('--critical', action='store_true')
    amr_goal.add_parser(sub)
    args = parser.parse_args()
    result = {}
    code = 0
    if args.command == 'validate-bir':
        result = bir_result(json.loads(args.receipt.read_text()), args.head, ROOT)
        code = 0 if result['status'] == 'ledger_valid' else 1
    elif args.command == 'goal':
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
        previous = amr_goal.private_evidence(ROOT, args.reuse)[1] if args.reuse else None
        result = gates(ROOT, args.base, reuse=previous, validation_context=args.validation_context)
        save_private(ROOT, 'gates.json', result)
        code = 1 if result['local_status'] != 'passed' else 0
    elif args.command == 'probe-models':
        result = {}
        probed = {}
        for profile, (model, effort) in PROFILES.items():
            if (model, effort) not in probed:
                try:
                    probed[model, effort] = probe_profile(model, effort)
                except (NativeError, OSError) as exc:
                    probed[model, effort] = {'status': 'failed', 'reason': str(exc)}
            result[profile] = probed[model, effort]
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
        task = validate_task(json.loads(args.task.read_text()), ROOT) if args.task else None
        if args.purpose == 'design' and task is None:
            raise ValueError('design review requires explicit --task context')
        before = tree_identity(ROOT)['digest']
        instructions = (
            'Independently review FEAT-AMR-001 in docs/tooling/amr.py, amr_goal.py, amr_native.py, '
            'doccheck.py, changeguard.py, tests, .codex/config.toml and related docs. '
            'Read AGENTS.md. Focus on runtime correctness, fail-closed model/effort '
            'selection, stale evidence, approval boundaries and false passing gates. '
            'Review the current repository content and Git diff when available. '
            'Use read-only tools. Return actionable defects with paths/lines; '
            'do not claim provider PR review or modify files. Do not access secrets.')
        if task:
            instructions = ('Independently review the '+args.purpose+' for this exact task. '
                'Read AGENTS.md, WORKFLOW.md, owning feature, consulted docs and relevant source. '
                'For design, assess boundaries, failure paths and acceptance before implementation. '
                'Return actionable findings; never approve an ADR, merge or production operation. '
                'Use read-only tools; do not access secrets or modify files. Task:\n'+json.dumps(task))
        try:
            result = local_review(ROOT, *PROFILES['CRITICAL'], instructions=instructions)
        except (NativeError, OSError) as exc:
            result = {'status': 'failed', 'reason': str(exc), 'outcome': 'unknown',
                      'channel': 'native_local_review'}
        after = tree_identity(ROOT)['digest']
        result.update(content=before, independent=True, created_at=now(), purpose=args.purpose,
                      task_id=task['task_id'] if task else None,
                      task_digest=task_identity(task) if task else None,
                      evidence='native:'+str(result.get('thread_id'))+':'+str(result.get('turn_id')))
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
