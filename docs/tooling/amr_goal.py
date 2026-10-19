"""Serial, restart-safe GOAL execution using Kordra AMR v4 adapters.

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


GOAL_STATES = {'active', 'awaiting_model_switch', 'awaiting_approval', 'blocked', 'completed'}
UNRESOLVED_TASK_STATES = {'running', 'unknown', 'validation_failed', 'awaiting_triage', 'failed'}
SAFE_BOUNDARY_TASK_STATES = {'pending', 'completed'}
PROFILE_SELECTIONS = set(amr.PROFILES.values())
PROFILE_CAPABILITIES = {
    'ROUTINE': {amr.PROFILES['ROUTINE']},
    'ENGINEERING': {amr.PROFILES['ENGINEERING']},
    'CRITICAL': {amr.PROFILES['CRITICAL']},
    'DEBUGGER_MEDIUM': {amr.PROFILES['DEBUGGER_MEDIUM']},
}
LEGACY_CAPABILITIES = {
    'ROUTINE': set(amr.LEGACY_PROFILES.values()),
    'ENGINEERING': {amr.LEGACY_PROFILES['ENGINEERING'], amr.LEGACY_PROFILES['CRITICAL']},
    'CRITICAL': {amr.LEGACY_PROFILES['CRITICAL']},
}
V31_CAPABILITIES = {
    'ROUTINE': {amr.PROFILES['ROUTINE']},
    'ENGINEERING': {amr.PROFILES['ENGINEERING'], amr.PROFILES['CRITICAL']},
    'CRITICAL': {amr.PROFILES['CRITICAL']},
}


def review_required(state, task_id):
    record = state['tasks'][task_id]
    policy = record.get('review_policy_version', record.get('policy_version', '3'))
    if policy == '3':
        return True  # Historical v3 receipts retain their original obligations.
    if policy == amr.POLICY_VERSION:
        return False  # v4 review belongs to the feature PR, never goal validate/accept.
    tasks = state['plan']['tasks']
    task = next(t for t in tasks if t['task_id'] == task_id)
    if amr.classify(task)['risk'] == 'CRITICAL':
        return True
    # Determine the final scheduled task from the immutable DAG, not mutable
    # completion flags. A reordered/tampered checkpoint cannot waive the review.
    scheduled = set()
    last = None
    while len(scheduled) < len(tasks):
        candidate = next(t for t in tasks if t['task_id'] not in scheduled
                         and set(t['depends_on']) <= scheduled)
        last = candidate['task_id']
        scheduled.add(last)
    return task_id == last


def valid_task_review(state, task_id):
    record = state['tasks'][task_id]
    review = record.get('review', {})
    if review_required(state, task_id):
        return verified_review(review)
    effective_policy = record.get('review_policy_version', record.get('policy_version'))
    if (effective_policy == amr.POLICY_VERSION and review.get('status') == 'verified'
            and review.get('content') == record['gates'].get('content')
            and review.get('source_stable') is True):
        return verified_review(review)  # Retain valid historical advice without making it a PR review.
    return (record.get('policy_version') in {'3', '3.1', amr.POLICY_VERSION}
            and review.get('status') == 'not_required'
            and review.get('channel') == 'pr_deferred'
            and review.get('independent') is False and review.get('source_stable') is True
            and review.get('content') == record['gates'].get('content'))


def migrate_unfinished_review_policy(record):
    """Move an unfinished historical task to v4 review semantics, keeping execution history."""
    policy = record.get('policy_version', '3')
    if policy in {'3', '3.1'} and record.get('status') != 'completed':
        record['review_policy_version'] = amr.POLICY_VERSION
        record['review_policy_migration'] = {'from': policy, 'to': amr.POLICY_VERSION,
            'scope': 'GOAL review obligations only; native execution receipt retains original policy',
            'at': amr.now()}


def valid_human_design_approval(approval, content, task):
    """Bind an explicit human design decision to the exact task and artifact."""
    if not isinstance(approval, dict):
        return False
    return (approval.get('decision') == 'approved'
            and approval.get('task_id') == task['task_id']
            and approval.get('task_digest') == amr.task_identity(task)
            and approval.get('content') == content
            and isinstance(approval.get('approved_by'), str) and bool(approval['approved_by'].strip())
            and isinstance(approval.get('scope'), str) and bool(approval['scope'].strip())
            and isinstance(approval.get('decision_evidence'), str)
            and bool(approval['decision_evidence'].strip()))


def valid_design_review(review, content, task):
    if not isinstance(review, dict):
        return False
    triage = review.get('triage', {})
    task_id, task_digest = task['task_id'], amr.task_identity(task)
    return (isinstance(triage, dict) and verified_review(review)
            and review.get('independent') is True and review.get('source_stable') is True
            and review.get('purpose') == 'design' and review.get('task_id') == task_id
            and review.get('task_digest') == task_digest
            and review.get('content') == content and bool(review.get('evidence'))
            and triage.get('content') == content and triage.get('task_id') == task_id
            and triage.get('task_digest') == task_digest
            and isinstance(triage.get('reviewed_by'), str) and bool(triage['reviewed_by'].strip())
            and isinstance(triage.get('rationale'), str) and bool(triage['rationale'].strip())
            and isinstance(triage.get('findings'), list)
            and not amr.finding_errors(triage['findings'], content))


def dispatched_task(state, task_id):
    """Recover the actual source-normalized task without reinterpreting history."""
    record = state['tasks'][task_id]
    planned = next(t for t in state['plan']['tasks'] if t['task_id'] == task_id)
    if record.get('policy_version', '3') == '3':
        return copy.deepcopy(planned)
    normalized = record.get('dispatch_task')
    if not isinstance(normalized, dict) or record.get('dispatch_task_digest') != amr.task_identity(normalized):
        raise ValueError('missing or corrupt dispatch-normalized task snapshot')
    expected = copy.deepcopy(planned)
    # validate_task can only clear these two source-derived claims, never
    # promote them or alter the objective, scope, paths, invariants or plan.
    for flag in ('spec_verified', 'tests_known'):
        if flag in normalized and normalized[flag] is False:
            expected[flag] = False
    if amr.task_identity(normalized) != amr.task_identity(expected):
        raise ValueError('dispatch task differs from immutable plan beyond source-derived downgrades')
    return copy.deepcopy(normalized)


def default_control(state):
    statuses = {record.get('status') for record in state.get('tasks', {}).values()}
    if statuses and statuses <= {'completed'}:
        current = 'completed'
    elif 'awaiting_triage' in statuses:
        current = 'awaiting_approval'
    elif statuses & UNRESOLVED_TASK_STATES:
        current = 'blocked'
    else:
        current = 'active'
    reason = ('legacy checkpoint requires contributor finding triage'
              if current == 'awaiting_approval' else
              'legacy checkpoint has unresolved execution or validation work'
              if current == 'blocked' else None)
    pause = {'reason': reason, 'created_at': amr.now(), 'baseline_identity': None} if reason else None
    return {'status': current, 'model_switch': None, 'switch_history': [],
            'pause_history': [], 'pause': pause,
            'runtime_selection': None}


def ensure_control(state):
    control = state.setdefault('control', default_control(state))
    if not isinstance(control, dict) or control.get('status') not in GOAL_STATES:
        raise ValueError('invalid goal control state')
    control.setdefault('switch_history', [])
    control.setdefault('pause_history', [])
    if not isinstance(control['switch_history'], list) or not isinstance(control['pause_history'], list):
        raise ValueError('invalid goal control history')
    if control['status'] == 'awaiting_model_switch':
        switch = control.get('model_switch')
        if (not isinstance(switch, dict) or not switch.get('task_id')
                or not switch.get('reason') or not isinstance(switch.get('baseline_identity'), dict)
                or not switch.get('recommended_model') or not switch.get('recommended_effort')):
            raise ValueError('model-switch checkpoint is incomplete')
    if control['status'] in {'awaiting_approval', 'blocked'}:
        pause = control.get('pause')
        if not isinstance(pause, dict) or not pause.get('reason'):
            raise ValueError('paused goal checkpoint is incomplete')
    return control


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


def validate_plan(root, plan, *, require_inputs=True, legacy_task_ids=None):
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
    legacy_task_ids = legacy_task_ids or set()
    for task in plan['tasks']:
        amr.validate_task(task, root, require_inputs=require_inputs,
                          legacy_policy=task.get('task_id') in legacy_task_ids)
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
            path = amr.safe_path(root, rel)
            if rel not in task['paths'] or (require_inputs and not path.is_file()):
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
                                         'validation_attempts': 0} for t in plan['tasks']},
                 'control': {'status': 'active', 'model_switch': None, 'switch_history': [],
                             'pause_history': [], 'pause': None,
                             'runtime_selection': None}}
        atomic_save(path, state)
        return projection(root, state)


def load(root, path):
    state = json.loads(path.read_text())
    if state.get('schemaVersion') != 1 or digest(state['plan']) != state['plan_digest']:
        raise ValueError('unsupported or corrupt goal checkpoint')
    # Historical role constraints are retained per dispatch receipt. New tasks
    # always use v4 validation; reading an old checkpoint must not reinterpret
    # its former Architect paths or silently rewrite the pinned plan.
    legacy_task_ids = {task_id for task_id, record in state.get('tasks', {}).items()
                       if isinstance(record, dict)
                       and record.get('policy_version', '3') in {'3', '3.1'}}
    validate_plan(root, copy.deepcopy(state['plan']), require_inputs=False,
                  legacy_task_ids=legacy_task_ids)
    observed = hashlib.sha256(amr.safe_path(root, state['plan']['objective_path']).read_bytes()).hexdigest()
    if observed != state['objective_hash']:
        raise ValueError('objective changed; reconcile intent in a new plan')
    expected = {t['task_id'] for t in state['plan']['tasks']}
    if set(state['tasks']) != expected:
        raise ValueError('checkpoint task identities differ from plan')
    for task_id, record in state['tasks'].items():
        if record.get('task_id') != task_id:
            raise ValueError('checkpoint record task identity mismatch')
        if record.get('policy_version', '3') not in {'3', '3.1', amr.POLICY_VERSION}:
            raise ValueError('unsupported execution policy version')
        if record.get('review_policy_version') not in {None, amr.POLICY_VERSION}:
            raise ValueError('unsupported review policy migration')
        migration = record.get('review_policy_migration')
        if record.get('review_policy_version') == amr.POLICY_VERSION:
            if (record.get('policy_version', '3') not in {'3', '3.1'}
                    or not isinstance(migration, dict)
                    or migration.get('from') != record.get('policy_version', '3')
                    or migration.get('to') != amr.POLICY_VERSION
                    or migration.get('scope') != 'GOAL review obligations only; native execution receipt retains original policy'
                    or not isinstance(migration.get('at'), str) or not migration['at']):
                raise ValueError('invalid historical review-policy migration receipt')
        elif migration is not None:
            raise ValueError('review-policy migration receipt has no matching migrated version')
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
                errors = amr.completion_errors(ledger, content, critical=review_required(state, task_id),
                                              product_required=record['product_required'])
                task = dispatched_task(state, task_id)
                policy = record.get('policy_version', '3')
                if policy in {'3', '3.1'}:
                    design_valid = (not amr.design_review_required(task)
                        or valid_design_review(record.get('design_review'), record['input_identity']['content'], task))
                else:
                    design_valid = (amr.execution_role(task) != 'architect'
                        or valid_human_design_approval(record.get('human_design_approval'), content, task))
                valid = (verified_execution(record, record['route']) and valid_task_review(state, task_id)
                         and design_valid
                         and triage['task_id'] == task_id and triage['content'] == content
                         and triage['reviewed_by'] and triage['rationale'] and not errors
                         and record['product_required'] is (record['gates']['product_tests'] == 'executed'))
            except (KeyError, TypeError, AttributeError):
                valid = False
            if not valid:
                raise ValueError('completed checkpoint lacks task-bound execution/gates/review/triage')
    ensure_control(state)
    control = state['control']
    statuses = {record['status'] for record in state['tasks'].values()}
    if control['status'] == 'completed' and statuses != {'completed'}:
        raise ValueError('completed goal state contains unfinished tasks')
    if control['status'] == 'awaiting_model_switch':
        switch = control['model_switch']
        if (switch['task_id'] not in state['tasks']
                or state['tasks'][switch['task_id']]['status'] != 'pending'
                or next_task(state) is None
                or next_task(state)['task_id'] != switch['task_id']):
            raise ValueError('model-switch checkpoint no longer identifies the next pending task')
    return state


def event(state, task_id, status, detail):
    control = ensure_control(state)
    if task_id is not None:
        state['tasks'][task_id]['status'] = status
    state['events'].append({'at': amr.now(), 'task_id': task_id, 'status': status, 'detail': detail})
    if status in {'unknown', 'validation_failed', 'failed'}:
        control.update(status='blocked', pause={'reason': detail, 'created_at': amr.now(),
                                                'baseline_identity': None,
                                                'next_action': 'Inspect the current execution/gate evidence; reconcile or create a fresh plan.'})
    elif status == 'awaiting_triage':
        control.update(status='awaiting_approval', pause={
            'reason': 'contributor finding triage is required; this is not production authorization',
            'created_at': amr.now(), 'baseline_identity': None,
            'next_action': 'Inspect changes and applicable review output; submit exact-content contributor triage.'})
    elif status == 'completed':
        control.update(status='completed' if all(
            record.get('status') == 'completed' for record in state['tasks'].values()) else 'active',
            pause=None, model_switch=None)
    elif status == 'running':
        control.update(status='active', pause=None, model_switch=None)


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
    control = ensure_control(state)
    status = ('completed' if all(t['status'] == 'completed' for t in state['tasks'].values())
              else control['status'])
    return {'goal_id': state['plan']['goal_id'], 'milestone': state['plan']['milestone'],
            'objective_path': state['plan']['objective_path'],
            'objective': state['plan'].get('objective'),
            'progress_path': state['plan']['progress_path'],
            'next_task': task['task_id'] if task else None,
            'status': status,
            'control': control,
            'tasks': state['tasks'], 'events': state['events'],
            'native_goal_control': 'not_available_in_local_cli',
            'limitation': 'local execution checkpoints; native GOAL control, merge and production approval remain external'}


def safe_model_switch_boundary(state):
    return all(record.get('status') in SAFE_BOUNDARY_TASK_STATES
               for record in state['tasks'].values())


def pending_routes(root, state):
    routes = []
    for candidate in state['plan']['tasks']:
        if state['tasks'][candidate['task_id']]['status'] != 'pending':
            continue
        validated = amr.validate_task(copy.deepcopy(candidate), root)
        routes.append((candidate, amr.classify(validated)))
    return routes


def selection_is_advertised(candidates, model, effort):
    return any(item.get('model') == model and effort in item.get('efforts', [])
               for item in candidates.get('models', []))


def route_for_selection(route, model, effort, candidates):
    """Use an already selected, advertised profile when it meets the task floor."""
    profile = route.get('profile')
    selection = (model, effort)
    if (profile not in PROFILE_CAPABILITIES
            or selection not in PROFILE_CAPABILITIES[profile]
            or not selection_is_advertised(candidates, model, effort)):
        return route
    if route.get('action') == 'blocked' and route.get('support') != 'unsupported':
        return route
    if (route.get('action') == 'continue' and route.get('support') == 'advertised'
            and (route.get('model'), route.get('effort')) == selection):
        return route
    if route.get('support') != 'unsupported':
        return route  # A saved Sol choice cannot override available Luna Max.
    return route


def recommend(root, goal_id, current_model, current_effort):
    """Persist a justified model-switch pause at a verified task boundary.

    current_model/current_effort are caller-reported because this local CLI
    cannot observe the selected model of the desktop coordinator thread.
    """
    with checkpoint(root, goal_id) as path:
        state = load(root, path)
        control = ensure_control(state)
        if control['status'] != 'active':
            raise ValueError('goal is '+control['status']+'; resolve that state before routing')
        if not safe_model_switch_boundary(state):
            raise ValueError('model switches require a safe boundary with no unresolved task operation')
        candidates = discover(root)
        current_selection = (current_model, current_effort)
        supported_current = (current_selection in PROFILE_SELECTIONS
                             and selection_is_advertised(candidates, current_model, current_effort))
        if not supported_current:
            raise ValueError('caller-reported current model/effort is not an advertised Kordra AMR profile')
        task = next_task(state)
        if task is None:
            result = projection(root, state)
            result['recommendation'] = {'action': 'none', 'reason': 'no eligible task',
                                        'selection_verification': 'unverified'}
            return result
        task = amr.validate_task(copy.deepcopy(task), root)
        classification = amr.classify(task)
        route = amr.resolve(classification, candidates)
        if route.get('action') == 'blocked':
            control.update(status='blocked', model_switch=None, pause={
                'reason': '; '.join(route.get('rationale', [])) or 'model route unavailable',
                'created_at': amr.now(), 'baseline_identity': current_identity(root),
                'next_action': 'Resolve model/effort availability and re-evaluate this task route.'})
            state['events'].append({'at': amr.now(), 'task_id': task['task_id'],
                                    'status': 'blocked', 'detail': control['pause']['reason']})
            atomic_save(path, state)
            result = projection(root, state)
            result['recommendation'] = {'action': 'blocked', 'route': route,
                                        'selection_verification': 'unverified'}
            return result

        target_model, target_effort = route.get('model'), route.get('effort')
        reason = None
        # v4 switches only between bounded Architect and Developer/Debugger tasks.
        if route.get('profile') is None:
            reason = 'the next task is deterministic; no model switch is needed'
            target_model, target_effort = current_model, current_effort
        elif current_model == target_model and current_effort == target_effort:
            reason = 'current model and effort already match the task route'
        else:
            reason = 'task route needs a different model or reasoning effort'

        if (target_model, target_effort) == current_selection:
            route = route_for_selection(route, current_model, current_effort, candidates)

        selection = {'model': current_model, 'effort': current_effort,
                     'task_id': task['task_id'],
                     'status': 'unverified', 'source': 'caller_reported', 'recorded_at': amr.now()}
        control['runtime_selection'] = selection
        if (target_model, target_effort) == (current_model, current_effort):
            state['events'].append({'at': amr.now(), 'task_id': task['task_id'],
                                    'status': 'active', 'detail': 'no model switch: '+reason})
            atomic_save(path, state)
            result = projection(root, state)
            result['recommendation'] = {'action': 'continue', 'task_id': task['task_id'],
                'current': selection, 'recommended': {'model': target_model, 'effort': target_effort},
                'reason': reason, 'route': route, 'selection_verification': 'unverified'}
            return result

        control.update(status='awaiting_model_switch', pause=None, model_switch={
            'task_id': task['task_id'], 'current_model': current_model,
            'current_effort': current_effort, 'current_selection_verification': 'unverified',
            'recommended_model': target_model, 'recommended_effort': target_effort,
            'reason': reason, 'route': route, 'baseline_identity': current_identity(root),
            'context_paths': sorted(set(task['paths']+task['consulted'])),
            'completed_tasks': sorted(tid for tid, record in state['tasks'].items()
                                      if record['status'] == 'completed'),
            'outstanding_operations': [],
            'next_action': 'Manually select '+target_model+'/'+target_effort
                           +' in Codex, explicitly resume GOAL, then revalidate this checkpoint.',
            'created_at': amr.now(), 'native_pause': 'not_available_in_local_cli'})
        state['events'].append({'at': amr.now(), 'task_id': task['task_id'],
                                'status': 'awaiting_model_switch', 'detail': reason})
        atomic_save(path, state)
        result = projection(root, state)
        result['recommendation'] = {'action': 'awaiting_model_switch',
            'task_id': task['task_id'], 'current': selection,
            'recommended': {'model': target_model, 'effort': target_effort},
            'reason': reason, 'selection_verification': 'unverified',
            'native_pause': 'not_available_in_local_cli'}
        return result


def pause(root, goal_id, status, reason):
    if status not in {'awaiting_approval', 'blocked'}:
        raise ValueError('manual pause state must be awaiting_approval or blocked')
    if not isinstance(reason, str) or not reason.strip():
        raise ValueError('pause requires a specific reason')
    with checkpoint(root, goal_id) as path:
        state = load(root, path)
        control = ensure_control(state)
        if control['status'] != 'active' or not safe_model_switch_boundary(state):
            raise ValueError('pause requires an active goal at a safe execution boundary')
        control.update(status=status, model_switch=None,
                       pause={'reason': reason.strip(), 'created_at': amr.now(),
                              'baseline_identity': current_identity(root),
                              'next_action': ('Obtain a human decision and record its evidence reference.'
                                  if status == 'awaiting_approval' else
                                  'Resolve the technical blocker, verify it, then explicitly resume.'),
                              'evidence_status': 'pending'})
        state['events'].append({'at': amr.now(), 'task_id': None,
                                'status': status, 'detail': reason.strip()})
        atomic_save(path, state)
        result = projection(root, state)
        result['native_goal_control'] = 'not_available_in_local_cli'
        return result


def resume(root, goal_id, *, model=None, effort=None, approval_evidence=None,
           resolution_evidence=None):
    with checkpoint(root, goal_id) as path:
        state = load(root, path)
        control = ensure_control(state)
        paused_status = control['status']
        if paused_status == 'active':
            raise ValueError('goal is already active; resume is never automatic')
        if paused_status == 'completed':
            raise ValueError('completed goal cannot be resumed; create a new plan')
        if not safe_model_switch_boundary(state):
            raise ValueError('unresolved or uncertain task state blocks resume; reconcile or create a new plan')

        if paused_status == 'awaiting_model_switch':
            switch = control['model_switch']
            current = current_identity(root)
            if current != switch['baseline_identity']:
                raise ValueError('Git revision or working tree changed while paused; revalidate and create a fresh plan')
            task = next_task(state)
            if task is None or task['task_id'] != switch['task_id']:
                raise ValueError('next task changed while paused; re-evaluate the model route')
            if (model, effort) != (switch['recommended_model'], switch['recommended_effort']):
                raise ValueError('selected model/effort does not match the recommendation; no silent fallback')
            capabilities = discover(root)
            if not any(x.get('model') == model and effort in x.get('efforts', [])
                       for x in capabilities.get('models', [])):
                raise ValueError('recommended model/effort is no longer advertised')
            switch['resumed_at'] = amr.now()
            switch['selection_verification'] = 'unverified'
            control.setdefault('switch_history', []).append(copy.deepcopy(switch))
            control['runtime_selection'] = {'model': model, 'effort': effort,
                'task_id': switch['task_id'], 'status': 'unverified',
                'source': 'manual_switch_claim', 'recorded_at': amr.now()}
        elif paused_status == 'awaiting_approval':
            if any(record['status'] == 'awaiting_triage' for record in state['tasks'].values()):
                raise ValueError('use goal accept after exact-content review triage')
            if not isinstance(approval_evidence, str) or not approval_evidence.strip():
                raise ValueError('approval evidence reference required; this CLI cannot authenticate approval')
            pause_record = control['pause']
            if pause_record.get('baseline_identity') != current_identity(root):
                raise ValueError('source changed while awaiting approval; create a fresh reviewed plan')
            pause_record.update(evidence_reference=approval_evidence.strip(), evidence_status='referenced')
        elif paused_status == 'blocked':
            if not isinstance(resolution_evidence, str) or not resolution_evidence.strip():
                raise ValueError('resolution evidence required to clear a technical block')
            if any(record['status'] in UNRESOLVED_TASK_STATES for record in state['tasks'].values()):
                raise ValueError('failed, uncertain or untriaged task requires reconciliation or a fresh plan')
            pause_record = control['pause']
            if pause_record.get('baseline_identity') != current_identity(root):
                raise ValueError('source changed while blocked; create a fresh reviewed plan')
            pause_record.update(evidence_reference=resolution_evidence.strip(), evidence_status='referenced')

        prior_status = paused_status
        if control.get('pause'):
            control['pause_history'].append(copy.deepcopy(control['pause']))
        control.update(status='active', pause=None, model_switch=None)
        state['events'].append({'at': amr.now(), 'task_id': None, 'status': 'active',
                                'detail': 'explicit resume from '+prior_status})
        atomic_save(path, state)
        result = projection(root, state)
        result['resumed_from'] = prior_status
        selection = control.get('runtime_selection')
        result['selection_verification'] = (selection.get('status') if isinstance(selection, dict)
                                            else 'not_applicable')
        result['native_goal_resume'] = 'coordinator_must_resume_native_goal_separately'
        return result


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


def protected_content_identity(files, write_paths):
    writable = set(write_paths)
    return digest({path: identity for path, identity in files.items() if path not in writable})


def verified_execution(record, route):
    execution = record.get('execution', {})
    profile = route.get('profile')
    policy = record.get('policy_version', '3')
    capabilities = (LEGACY_CAPABILITIES if policy == '3' else
                    V31_CAPABILITIES if policy == '3.1' else
                    PROFILE_CAPABILITIES if policy == amr.POLICY_VERSION else {})
    if policy == '3.1' and profile in {'ROUTINE', 'ENGINEERING'}:
        preferred = amr.PROFILES[profile]
        if (route.get('model'), route.get('effort')) != preferred:
            if route.get('retained_selection') != {'reason': 'preferred_unavailable',
                    'preferred_model': preferred[0], 'preferred_effort': preferred[1]}:
                return False
    if (record.get('policy_version') == amr.POLICY_VERSION and profile in amr.PROFILES
            and (route.get('model'), route.get('effort')) != amr.PROFILES[profile]):
        preferred_model, preferred_effort = amr.PROFILES[profile]
        if route.get('retained_selection') != {'reason': 'preferred_unavailable',
                'preferred_model': preferred_model, 'preferred_effort': preferred_effort}:
            return False
    return (profile in capabilities and route.get('action') == 'continue'
            and (route.get('model'), route.get('effort')) in capabilities[profile]
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
    task = dispatched_task(state, task['task_id'])
    if not verified_execution(record, record['route']):
        raise ValueError('no verified terminal execution; reconcile unknown work instead')
    git = amr.git_identity(root)
    if any(git[k] != record['input_identity']['git'][k] for k in ('head', 'branch')):
        raise ValueError('task Git HEAD/branch changed; uncommitted review cannot cover committed task edits')
    files = amr.tree_identity(root)['files']
    if (not record.get('protected_content_identity')
            or protected_content_identity(files, task['write_paths'])
            != record['protected_content_identity']):
        event(state, task['task_id'], 'failed',
              'source changed outside declared write paths; create a fresh reviewed task plan')
        atomic_save(path, state)
        return False
    if record['validation_attempts'] >= 4:
        raise ValueError('validation repair limit reached; contributor investigation required')
    record['validation_attempts'] += 1
    migrate_unfinished_review_policy(record)
    event(state, task['task_id'], 'running', 'validating completed work; execution will not replay')
    atomic_save(path, state)
    record['gates'] = amr.gates(root, state['plan']['base'])
    record['product_required'] = record['gates'].get('product_tests') == 'executed'
    record['validated_identity'] = current_identity(root)
    if record['gates']['local_status'] != 'passed' or record['gates'].get('source_mapping') != 'executed':
        event(state, task['task_id'], 'validation_failed', 'required gates failed')
        atomic_save(path, state); return
    if not review_required(state, task['task_id']):
        existing = record.get('review', {})
        if not (existing.get('status') == 'verified'
                and existing.get('content') == record['validated_identity']['content']
                and existing.get('source_stable') is True and verified_review(existing)):
            record['review'] = {'status': 'not_required', 'channel': 'pr_deferred',
                'independent': False, 'source_stable': True,
                'content': record['validated_identity']['content'],
                'reason': 'AMR v4: PR review is tracked separately; GOAL validation does not invoke an AI reviewer'}
        event(state, task['task_id'], 'awaiting_triage',
              'gates passed; inspect changes and findings; no independent task review claimed')
        atomic_save(path, state)
        return
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


def step(root, goal_id, design_review=None):
    with checkpoint(root, goal_id) as path:
        state = load(root, path)
        control = ensure_control(state)
        if control['status'] != 'active':
            raise ValueError('goal is '+control['status']+'; explicitly resolve or resume it first')
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
        candidates = discover(root)
        route = amr.resolve(amr.classify(task), candidates)
        selection = control.get('runtime_selection')
        if isinstance(selection, dict) and selection.get('task_id') == task['task_id']:
            route = route_for_selection(route, selection.get('model'), selection.get('effort'), candidates)
            if (route.get('action') == 'continue' and route.get('support') == 'advertised'
                    and (route.get('model'), route.get('effort')) !=
                        (selection.get('model'), selection.get('effort'))):
                raise ValueError('saved selection no longer eligible; run goal recommend at this safe boundary')
        record['route'] = route
        if route['action'] != 'continue' or route.get('support') != 'advertised':
            event(state, task['task_id'], 'failed', 'exact model unavailable or task requires deterministic execution')
            atomic_save(path, state); return projection(root, state)
        before = amr.tree_identity(root)
        if design_review is not None:
            raise ValueError('AMR v4 does not consume AI design-review receipts; use an Architect task and human approval')
        record.update(task_id=task['task_id'], attempts=record['attempts']+1, attempt_id=str(uuid.uuid4()),
                      policy_version=amr.POLICY_VERSION,
                      dispatch_task=copy.deepcopy(task), dispatch_task_digest=amr.task_identity(task),
                      input_identity={'content': before['digest'], 'git': amr.git_identity(root)},
                      protected_content_identity=protected_content_identity(before['files'], task['write_paths']),
                      context_paths=task['paths']+task['consulted'])
        event(state, task['task_id'], 'running', 'dispatch intent saved before native invocation')
        atomic_save(path, state)
        def started(thread_id):
            record['native_thread_id'] = thread_id
            atomic_save(path, state)
        # Informational dispatch facts, constructed only after the checks above
        # while checkpoint() still holds the checkout lock. Never derive proof
        # from objective/decision prose or substitute this summary for receipts.
        architect_task = amr.execution_role(task) == 'architect'
        design_facts = {'required': architect_task,
                        'status': 'human_approval_after_output' if architect_task else 'not_applicable'}
        pre_dispatch = {
            'source': 'amr_goal.step', 'policy_version': record['policy_version'],
            'goal_id': goal_id, 'task_id': task['task_id'],
            'task_digest': record['dispatch_task_digest'], 'content': before['digest'],
            'plan_digest': state['plan_digest'], 'attempt_id': record['attempt_id'],
            'route': copy.deepcopy(route),
            'execution_lock': {'owner': 'parent_coordinator', 'scope': 'checkout', 'held': True},
            'design_approval': design_facts,
        }
        try:
            record['execution'] = execute_task(root, task, route['model'], route['effort'],
                goal_context={'goal_id': goal_id, 'objective': state['plan']['objective'],
                              'milestone': state['plan']['milestone'],
                              'task_id': task['task_id'], 'depends_on': task['depends_on']},
                pre_dispatch=pre_dispatch,
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
        task = dispatched_task(state, task_id)
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
        migrate_unfinished_review_policy(record)
        ledger = dict(record['gates'], review=record['review'], findings=triage['findings'])
        errors = amr.completion_errors(ledger, identity['content'], critical=review_required(state, task_id),
                                      product_required=record.get('product_required') is True)
        if not verified_execution(record, record['route']):
            errors.append('verified native task execution required')
        if not valid_task_review(state, task_id):
            errors.append('verified native review identity and terminal output required')
        task = dispatched_task(state, task_id)
        if record.get('policy_version') == amr.POLICY_VERSION and amr.execution_role(task) == 'architect':
            approval = triage.get('human_design_approval')
            if not valid_human_design_approval(approval, identity['content'], task):
                errors.append('explicit human approval of the exact design task and content is required')
        elif record.get('policy_version') == amr.POLICY_VERSION and 'human_design_approval' in triage:
            errors.append('human design approval is valid only for an Architect task')
        if record.get('product_required') is not (record['gates'].get('product_tests') == 'executed'):
            errors.append('product requirement differs from saved gate receipt')
        if errors:
            raise ValueError('; '.join(errors))
        record['triage'] = triage
        if record.get('policy_version') == amr.POLICY_VERSION and amr.execution_role(task) == 'architect':
            record['human_design_approval'] = copy.deepcopy(triage['human_design_approval'])
        current_files = amr.tree_identity(root)['files']
        paths = set(record['context_paths']) | set(record.get('files_changed', []))
        record['output_artifacts'] = {name: current_files[name] for name in paths if name in current_files}
        event(state, task_id, 'completed', 'current gates and exact-content triage recorded; PR review remains a separate gate')
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
    for command in ('status', 'step', 'validate', 'accept', 'reconcile', 'recommend', 'pause', 'resume'):
        p = commands.add_parser(command); p.add_argument('--id', required=True)
        if command in {'validate', 'accept', 'reconcile'}:
            p.add_argument('--task-id', required=True)
        if command == 'accept':
            p.add_argument('--triage', required=True, type=Path)
        if command == 'step':
            p.add_argument('--design-review', type=Path)
        if command == 'reconcile':
            p.add_argument('--evidence', required=True)
        if command == 'recommend':
            p.add_argument('--current-model', required=True)
            p.add_argument('--current-effort', required=True)
        if command == 'pause':
            p.add_argument('--status', required=True, choices=('awaiting_approval', 'blocked'))
            p.add_argument('--reason', required=True)
        if command == 'resume':
            p.add_argument('--model')
            p.add_argument('--effort')
            p.add_argument('--approval-evidence')
            p.add_argument('--resolution-evidence')


def main(root, args):
    if args.goal_command == 'init':
        return init(root, json.loads(args.plan.read_text()))
    if args.goal_command == 'status':
        return status(root, args.id)
    if args.goal_command == 'step':
        return step(root, args.id, json.loads(args.design_review.read_text()) if args.design_review else None)
    if args.goal_command == 'validate':
        return validate(root, args.id, args.task_id)
    if args.goal_command == 'accept':
        return accept(root, args.id, args.task_id, json.loads(args.triage.read_text()))
    if args.goal_command == 'reconcile':
        return reconcile(root, args.id, args.task_id, args.evidence)
    if args.goal_command == 'recommend':
        return recommend(root, args.id, args.current_model, args.current_effort)
    if args.goal_command == 'pause':
        return pause(root, args.id, args.status, args.reason)
    return resume(root, args.id, model=args.model, effort=args.effort,
                  approval_evidence=args.approval_evidence,
                  resolution_evidence=args.resolution_evidence)
