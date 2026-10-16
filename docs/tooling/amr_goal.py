"""Serial, restart-safe goal execution using Kordra's existing AMR adapters.

This is a local contributor checkpoint, not a deployment workflow or an
authorization service. Model output never advances a dependency by itself.
"""
import contextlib
import copy
import hashlib
import json
import os
from pathlib import Path
import re
import tempfile
import uuid

try:  # Workspace runs on macOS/Linux; retain native Windows CLI support too.
    import fcntl
except ImportError:  # pragma: no cover - exercised on Windows CI/developers
    fcntl = None

import amr
from amr_native import NativeError, discover, execute_task, local_review


def digest(value):
    return hashlib.sha256(json.dumps(value, sort_keys=True).encode()).hexdigest()


def identifier(value):
    if not isinstance(value, str) or not re.fullmatch(r'[A-Za-z0-9][A-Za-z0-9_-]{0,79}', value):
        raise ValueError('invalid goal/task identifier')
    return value


def private_directory(root):
    directory = root/'.amr-local'
    if directory.is_symlink():
        raise ValueError('private directory is a symlink')
    directory.mkdir(mode=0o700, exist_ok=True)
    target = directory/'goals'
    if target.is_symlink():
        raise ValueError('goal directory is a symlink')
    target.mkdir(mode=0o700, exist_ok=True)
    for path in (directory, target):
        path.chmod(0o700)
    return target


def atomic_save(path, value):
    if path.is_symlink():
        raise ValueError('checkpoint cannot be a symlink')
    fd, name = tempfile.mkstemp(prefix='.checkpoint-', dir=path.parent)
    try:
        with os.fdopen(fd, 'w') as stream:
            json.dump(value, stream, indent=2, ensure_ascii=False)
            stream.write('\n'); stream.flush(); os.fsync(stream.fileno())
        os.replace(name, path)
        directory_fd = os.open(path.parent, os.O_RDONLY)
        try:
            os.fsync(directory_fd)
        finally:
            os.close(directory_fd)
    finally:
        if os.path.exists(name):
            os.unlink(name)


@contextlib.contextmanager
def checkpoint(root, goal_id):
    directory = private_directory(root)
    path = directory/(identifier(goal_id)+'.json')
    lock = directory/'executor.lock'
    # One writer per checkout, across all goal IDs; never steal a live claim.
    fd = os.open(lock, os.O_RDWR | os.O_CREAT | getattr(os, 'O_NOFOLLOW', 0), 0o600)
    try:
        try:
            if fcntl:
                fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
            else:
                import msvcrt
                os.lseek(fd, 0, os.SEEK_SET)
                if os.fstat(fd).st_size == 0:
                    os.write(fd, b'0')
                    os.lseek(fd, 0, os.SEEK_SET)
                msvcrt.locking(fd, msvcrt.LK_NBLCK, 1)
        except BlockingIOError as exc:
            raise ValueError('another goal executor is live in this checkout') from exc
        if path.is_symlink():
            raise ValueError('checkpoint cannot be a symlink')
        yield path
    finally:
        os.close(fd)


def validate_plan(root, plan):
    if plan.get('schemaVersion') != 1:
        raise ValueError('unsupported plan schemaVersion')
    identifier(plan.get('goal_id'))
    for field in ('milestone', 'base'):
        if not isinstance(plan.get(field), str) or not plan[field].strip():
            raise ValueError('missing plan '+field)
    if not isinstance(plan.get('objective'), str) or not plan['objective'].strip():
        raise ValueError('goal plan requires the active user objective text')
    for field in ('objective_path', 'progress_path'):
        if not amr.safe_path(root, plan.get(field)).is_file():
            raise ValueError('missing plan document '+field)
    # Store an immutable commit rather than a moving branch name.
    import subprocess
    resolved = subprocess.run(['git', 'rev-parse', '--verify', plan['base']+'^{commit}'],
                              cwd=root, capture_output=True, text=True, timeout=15)
    if resolved.returncode:
        raise ValueError('plan base is not a verified Git commit')
    plan['base'] = resolved.stdout.strip()
    if not isinstance(plan.get('tasks'), list) or not plan['tasks']:
        raise ValueError('goal requires tasks')
    ids = []
    for task in plan['tasks']:
        amr.validate_task(task, root)
        ids.append(identifier(task['task_id']))
        if task['scope'] != 'local-development' or set(task.get('signals', [])) & {
                'production', 'secrets', 'remote-execution'}:
            raise ValueError('goal executor is local-development only')
        if task.get('milestone') != plan['milestone']:
            raise ValueError('task milestone differs from plan')
        for key in ('depends_on', 'write_paths'):
            if not isinstance(task.get(key), list) or any(not isinstance(p, str) for p in task[key]):
                raise ValueError('expected string array: '+key)
            if len(task[key]) != len(set(task[key])):
                raise ValueError('duplicate '+key)
        for rel in task['write_paths']:
            if rel not in task['paths'] or not amr.safe_path(root, rel).is_file():
                raise ValueError('write paths must be existing feature-mapped task inputs')
    if len(ids) != len(set(ids)):
        raise ValueError('duplicate task IDs')
    edges = {t['task_id']: t['depends_on'] for t in plan['tasks']}
    visited, visiting = set(), set()
    def visit(task_id):
        if task_id not in edges:
            raise ValueError('missing dependency '+task_id)
        if task_id in visiting:
            raise ValueError('cyclic task dependencies')
        if task_id in visited:
            return
        visiting.add(task_id)
        for dependency in edges[task_id]:
            visit(dependency)
        visiting.remove(task_id); visited.add(task_id)
    for task_id in ids:
        visit(task_id)
    return plan


def init(root, plan):
    plan = validate_plan(root, copy.deepcopy(plan))
    with checkpoint(root, plan['goal_id']) as path:
        if path.exists():
            raise ValueError('goal already exists; inspect existing checkpoint')
        state = {'schemaVersion': 1, 'plan': plan, 'plan_digest': digest(plan),
                 'objective_hash': hashlib.sha256(amr.safe_path(root, plan['objective_path']).read_bytes()).hexdigest(),
                 'created_at': amr.now(), 'events': [],
                 'tasks': {t['task_id']: {'task_id': t['task_id'], 'status': 'pending', 'attempts': 0,
                                         'validation_attempts': 0} for t in plan['tasks']}}
        atomic_save(path, state)
        return projection(root, state)


def load(root, path):
    state = json.loads(path.read_text())
    if state.get('schemaVersion') != 1 or digest(state['plan']) != state['plan_digest']:
        raise ValueError('unsupported or corrupt goal checkpoint')
    # Validation works on a copy: never silently rewrite the pinned plan.
    validate_plan(root, copy.deepcopy(state['plan']))
    observed = hashlib.sha256(amr.safe_path(root, state['plan']['objective_path']).read_bytes()).hexdigest()
    if observed != state['objective_hash']:
        raise ValueError('objective changed; reconcile intent in a new plan')
    expected = {t['task_id'] for t in state['plan']['tasks']}
    if set(state['tasks']) != expected:
        raise ValueError('checkpoint task identities differ from plan')
    for task_id, record in state['tasks'].items():
        if record.get('task_id') != task_id:
            raise ValueError('checkpoint record task identity mismatch')
        if record.get('status') not in {'pending', 'running', 'unknown', 'validation_failed',
                                        'awaiting_triage', 'completed', 'failed'}:
            raise ValueError('unknown checkpoint task state')
        for counter in ('attempts', 'validation_attempts'):
            if type(record.get(counter)) is not int or record[counter] < 0:
                raise ValueError('invalid checkpoint attempt counter')
        if record['status'] == 'completed':
            try:
                content = record['validated_identity']['content']
                triage = record['triage']
                ledger = dict(record['gates'], review=record['review'], findings=triage['findings'])
                errors = amr.completion_errors(ledger, content, critical=True,
                                              product_required=record['product_required'])
                valid = (verified_execution(record, record['route']) and verified_review(record['review'])
                         and triage['task_id'] == task_id and triage['content'] == content
                         and triage['reviewed_by'] and triage['rationale'] and not errors
                         and record['product_required'] is (record['gates']['product_tests'] == 'executed'))
            except (KeyError, TypeError, AttributeError):
                valid = False
            if not valid:
                raise ValueError('completed checkpoint lacks task-bound execution/gates/review/triage')
    return state


def event(state, task_id, status, detail):
    state['tasks'][task_id]['status'] = status
    state['events'].append({'at': amr.now(), 'task_id': task_id, 'status': status, 'detail': detail})


def next_task(state):
    # Never advance around unresolved work, even if another task is independent.
    unresolved = {'running', 'unknown', 'validation_failed', 'awaiting_triage', 'failed'}
    if any(t['status'] in unresolved for t in state['tasks'].values()):
        return None
    return next((t for t in state['plan']['tasks']
                 if state['tasks'][t['task_id']]['status'] == 'pending'
                 and all(state['tasks'][d]['status'] == 'completed' for d in t['depends_on'])), None)


def projection(root, state):
    task = next_task(state)
    return {'goal_id': state['plan']['goal_id'], 'milestone': state['plan']['milestone'],
            'objective_path': state['plan']['objective_path'],
            'objective': state['plan'].get('objective'),
            'progress_path': state['plan']['progress_path'],
            'next_task': task['task_id'] if task else None,
            'status': 'completed' if all(t['status'] == 'completed' for t in state['tasks'].values()) else 'active',
            'tasks': state['tasks'], 'events': state['events'],
            'limitation': 'local execution checkpoints; not native GOAL control, merge or production approval'}


def status(root, goal_id):
    with checkpoint(root, goal_id) as path:
        state = load(root, path)
        # Holding the lock proves no executor still owns this running claim.
        for task_id, record in state['tasks'].items():
            if record['status'] == 'running':
                event(state, task_id, 'unknown', 'executor stopped before durable terminal checkpoint; no automatic replay')
        atomic_save(path, state)
        return projection(root, state)


def current_identity(root):
    return {'content': amr.tree_identity(root)['digest'], 'git': amr.git_identity(root)}


def verified_execution(record, route):
    execution = record.get('execution', {})
    profile = route.get('profile')
    return (profile in amr.PROFILES and route.get('action') == 'continue'
            and route.get('model') == amr.PROFILES[profile][0]
            and route.get('effort') == amr.PROFILES[profile][1]
            and route.get('support') == 'advertised'
            and execution.get('requested_model') == route['model']
            and execution.get('requested_effort') == route['effort']
            and execution.get('execution') == 'native_session'
            and execution.get('task_id') == record.get('task_id')
            and execution.get('thread_id') == record.get('native_thread_id')
            and execution.get('status') == 'verified' and execution.get('turn_status') == 'completed'
            and execution.get('rerouted') is False and bool(execution.get('thread_id'))
            and bool(execution.get('turn_id'))
            and execution.get('observed_model') == route['model']
            and execution.get('observed_effort') == route['effort'])


def validate_work(root, state, task, path):
    record = state['tasks'][task['task_id']]
    if not verified_execution(record, record['route']):
        raise ValueError('no verified terminal execution; reconcile unknown work instead')
    git = amr.git_identity(root)
    if any(git[k] != record['input_identity']['git'][k] for k in ('head', 'branch')):
        raise ValueError('task Git HEAD/branch changed; uncommitted review cannot cover committed task edits')
    if record['validation_attempts'] >= 4:
        raise ValueError('validation repair limit reached; contributor investigation required')
    record['validation_attempts'] += 1
    event(state, task['task_id'], 'running', 'validating completed work; execution will not replay')
    atomic_save(path, state)
    record['gates'] = amr.gates(root, state['plan']['base'])
    record['product_required'] = record['gates'].get('product_tests') == 'executed'
    record['validated_identity'] = current_identity(root)
    if record['gates']['local_status'] != 'passed' or record['gates'].get('source_mapping') != 'executed':
        event(state, task['task_id'], 'validation_failed', 'required gates failed')
        atomic_save(path, state); return
    model, effort = amr.PROFILES['CRITICAL']
    review_before = current_identity(root)
    review = local_review(root, model, effort,
        instructions='Review the current uncommitted changes against AGENTS.md and this task context. '
                     'Read the owning feature and consulted docs; check declared invariants and actionable '
                     'regressions. Do not modify files or claim unexecuted checks. Task:\n'+json.dumps(task),
        target={'type': 'uncommittedChanges'})
    review.update(content=review_before['content'], independent=True,
                  source_stable=review_before == current_identity(root),
                  evidence='native:'+str(review.get('thread_id'))+':'+str(review.get('turn_id')))
    record['review'] = review
    if not verified_review(review) or not review['source_stable']:
        event(state, task['task_id'], 'validation_failed', 'native review missing, failed or stale')
    else:
        event(state, task['task_id'], 'awaiting_triage', 'inspect native review and supply exact-content findings triage')
    atomic_save(path, state)


def verified_review(review):
    return (review.get('status') == 'verified' and review.get('turn_status') == 'completed'
            and review.get('rerouted') is False and bool(review.get('thread_id'))
            and bool(review.get('turn_id')) and bool(review.get('review_output'))
            and review.get('channel') == 'native_local_review'
            and review.get('observed_model') == amr.PROFILES['CRITICAL'][0]
            and review.get('observed_effort') == amr.PROFILES['CRITICAL'][1])


def step(root, goal_id):
    with checkpoint(root, goal_id) as path:
        state = load(root, path)
        task = next_task(state)
        if task is None:
            raise ValueError('no eligible task; inspect status, triage or reconcile unfinished work')
        task = amr.validate_task(copy.deepcopy(task), root)
        record = state['tasks'][task['task_id']]
        current_files = amr.tree_identity(root)['files']
        for dependency in task['depends_on']:
            artifacts = state['tasks'][dependency].get('output_artifacts', {})
            if not artifacts or any(current_files.get(name) != value for name, value in artifacts.items()):
                raise ValueError('completed dependency inputs changed; create a fresh reviewed task plan')
        route = amr.resolve(amr.classify(task), discover(root))
        record['route'] = route
        if route['action'] != 'continue' or route.get('support') != 'advertised':
            event(state, task['task_id'], 'failed', 'exact model unavailable or task requires deterministic execution')
            atomic_save(path, state); return projection(root, state)
        before = amr.tree_identity(root)
        record.update(task_id=task['task_id'], attempts=record['attempts']+1, attempt_id=str(uuid.uuid4()),
                      input_identity=current_identity(root),
                      context_paths=task['paths']+task['consulted'])
        event(state, task['task_id'], 'running', 'dispatch intent saved before native invocation')
        atomic_save(path, state)
        def started(thread_id):
            record['native_thread_id'] = thread_id
            atomic_save(path, state)
        try:
            record['execution'] = execute_task(root, task, route['model'], route['effort'],
                goal_context={'goal_id': goal_id, 'objective': state['plan']['objective'],
                              'milestone': state['plan']['milestone'],
                              'task_id': task['task_id'], 'depends_on': task['depends_on']},
                sandbox='workspace-write' if task['write_paths'] else 'read-only', on_start=started)
            after = amr.tree_identity(root)
            changed = sorted(p for p in before['files'].keys() | after['files'].keys()
                             if before['files'].get(p) != after['files'].get(p))
            record['files_changed'] = changed
            if set(changed) - set(task['write_paths']):
                event(state, task['task_id'], 'failed', 'execution changed undeclared files; inspect and recover manually')
            elif not verified_execution(record, route):
                event(state, task['task_id'], 'unknown', 'native execution not verified; no automatic replay')
            else:
                validate_work(root, state, task, path)
        except (NativeError, OSError, ValueError) as exc:
            event(state, task['task_id'], 'unknown', str(exc))
        atomic_save(path, state)
        return projection(root, state)


def validate(root, goal_id, task_id):
    with checkpoint(root, goal_id) as path:
        state = load(root, path)
        task = next(t for t in state['plan']['tasks'] if t['task_id'] == task_id)
        if state['tasks'][task_id]['status'] not in {'validation_failed', 'awaiting_triage'}:
            raise ValueError('task is not awaiting revalidation')
        validate_work(root, state, task, path)
        return projection(root, state)


def accept(root, goal_id, task_id, triage):
    with checkpoint(root, goal_id) as path:
        state = load(root, path); record = state['tasks'][task_id]
        identity = current_identity(root)
        if record['status'] != 'awaiting_triage' or record.get('validated_identity') != identity:
            raise ValueError('task not reviewed or source/Git changed; revalidate')
        if triage.get('task_id') != task_id or triage.get('content') != identity['content']:
            raise ValueError('triage task/content mismatch')
        if not isinstance(triage.get('reviewed_by'), str) or not triage['reviewed_by'].strip():
            raise ValueError('triage requires contributor identity')
        if not isinstance(triage.get('rationale'), str) or not triage['rationale'].strip():
            raise ValueError('triage requires review interpretation')
        if not isinstance(triage.get('findings'), list):
            raise ValueError('triage requires explicit findings array')
        ledger = dict(record['gates'], review=record['review'], findings=triage['findings'])
        errors = amr.completion_errors(ledger, identity['content'], critical=True,
                                      product_required=record.get('product_required') is True)
        if not verified_execution(record, record['route']):
            errors.append('verified native task execution required')
        if not verified_review(record['review']):
            errors.append('verified native review identity and terminal output required')
        if record.get('product_required') is not (record['gates'].get('product_tests') == 'executed'):
            errors.append('product requirement differs from saved gate receipt')
        if errors:
            raise ValueError('; '.join(errors))
        record['triage'] = triage
        current_files = amr.tree_identity(root)['files']
        paths = set(record['context_paths']) | set(record.get('files_changed', []))
        record['output_artifacts'] = {name: current_files[name] for name in paths if name in current_files}
        event(state, task_id, 'completed', 'current gates, independent native review and contributor triage recorded')
        atomic_save(path, state)
        return projection(root, state)


def reconcile(root, goal_id, task_id, evidence):
    with checkpoint(root, goal_id) as path:
        state = load(root, path); record = state['tasks'][task_id]
        if record['status'] not in {'running', 'unknown'}:
            raise ValueError('only interrupted/unknown execution needs reconciliation')
        if not isinstance(evidence, str) or not evidence.strip():
            raise ValueError('reconciliation requires contributor evidence')
        record['reconciliation'] = {'evidence': evidence, 'identity': current_identity(root)}
        # Do not synthesize success from prose; actual execution receipt is mandatory.
        target = 'validation_failed' if verified_execution(record, record['route']) else 'failed'
        event(state, task_id, target, 'reconciled without replay; new work needs an explicit new task/plan')
        atomic_save(path, state)
        return projection(root, state)


def add_parser(sub):
    parser = sub.add_parser('goal', help='persistent local goal execution')
    commands = parser.add_subparsers(dest='goal_command', required=True)
    p = commands.add_parser('init'); p.add_argument('--plan', required=True, type=Path)
    for command in ('status', 'step', 'validate', 'accept', 'reconcile'):
        p = commands.add_parser(command); p.add_argument('--id', required=True)
        if command in {'validate', 'accept', 'reconcile'}:
            p.add_argument('--task-id', required=True)
        if command == 'accept':
            p.add_argument('--triage', required=True, type=Path)
        if command == 'reconcile':
            p.add_argument('--evidence', required=True)


def main(root, args):
    if args.goal_command == 'init':
        return init(root, json.loads(args.plan.read_text()))
    if args.goal_command == 'status':
        return status(root, args.id)
    if args.goal_command == 'step':
        return step(root, args.id)
    if args.goal_command == 'validate':
        return validate(root, args.id, args.task_id)
    if args.goal_command == 'accept':
        return accept(root, args.id, args.task_id, json.loads(args.triage.read_text()))
    return reconcile(root, args.id, args.task_id, args.evidence)
