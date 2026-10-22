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
UNRESOLVED_TASK_STATES = {'running', 'in_progress', 'unknown', 'validation_failed', 'awaiting_triage', 'failed'}
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
        allowed_writes = set(task['paths']) | set(task.get('output_paths', []))
        for rel in task['write_paths']:
            if rel not in allowed_writes:
                raise ValueError('write paths must be declared task inputs or owner-mapped outputs')
            if rel in task.get('output_paths', []):
                amr.safe_output_path(root, rel)
            else:
                path = amr.safe_path(root, rel)
                if require_inputs and not path.is_file():
                    raise ValueError('write input paths must exist')
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
        if record.get('status') not in {'pending', 'running', 'in_progress', 'unknown', 'validation_failed',
                                        'awaiting_triage', 'completed', 'failed'}:
            raise ValueError('unknown checkpoint task state')
        for counter in ('attempts', 'validation_attempts'):
            if type(record.get(counter)) is not int or record[counter] < 0:
                raise ValueError('invalid checkpoint attempt counter')
        if 'protected_file_identities' in record:
            manifest = record['protected_file_identities']
            if (not isinstance(manifest, dict)
                    or any(not isinstance(k, str) or not isinstance(v, str) or not v
                           for k, v in manifest.items())
                    or digest(manifest) != record.get('protected_content_identity')
                    or preparation_identity(record) != record.get('preparation_digest')):
                raise ValueError('invalid protected-file manifest or preparation binding')
        if 'task_outcome' in record and record.get('status') != 'completed':
            try:
                task = dispatched_task(state, task_id)
                outcome_error = task_outcome_error(record, task)
            except (KeyError, TypeError, AttributeError, ValueError) as exc:
                raise ValueError('invalid task outcome receipt') from exc
            if outcome_error:
                raise ValueError('invalid task outcome receipt: '+outcome_error)
        if record['status'] == 'completed' and record.get('completion_kind') == 'coordinator_execution':
            validate_coordinator_record(root, state, task_id)
        elif record['status'] == 'completed' and record.get('completion_kind') in {'evidence_recovery', 'coordinator_continuation'}:
            validate_recovered_record(root, state, task_id)
        elif record['status'] == 'completed':
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
                outcome_valid = ('task_outcome' not in record or not task_outcome_error(
                    record, task, output_artifacts=record.get('output_artifacts')))
                valid = (verified_execution(record, record['route']) and valid_task_review(state, task_id)
                         and design_valid and outcome_valid
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
    elif status in {'running', 'in_progress'}:
        control.update(status='active', pause=None, model_switch=None)


def next_task(state):
    # Never advance around unresolved work, even if another task is independent.
    unresolved = UNRESOLVED_TASK_STATES
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
            'active_task': next((key for key, value in state['tasks'].items()
                                 if value['status'] == 'in_progress'), None),
            'status': status,
            'control': control,
            'tasks': state['tasks'], 'events': state['events'],
            'coordinator_diagnostics': {
                key: coordinator_diagnostics(root, record)
                for key, record in state['tasks'].items()
                if record.get('status') == 'in_progress'
                and record.get('execution_mode') == 'current_session'},
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
        persisted = json.loads(path.read_text())
        state = load(root, path)
        # Holding the lock proves no executor still owns this running claim.
        for task_id, record in state['tasks'].items():
            if record['status'] == 'running':
                event(state, task_id, 'unknown', 'executor stopped before durable terminal checkpoint; no automatic replay')
        if state != persisted:
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


def legacy_design_valid(record, task):
    policy = record.get('policy_version', '3')
    if policy not in {'3', '3.1'} or not amr.design_review_required(task):
        return True
    input_content = record.get('input_identity', {}).get('content')
    return valid_design_review(record.get('design_review'), input_content, task)


def task_outcome_error(record, task, *, current_files=None, output_artifacts=None):
    """Validate the structured terminal outcome and its observed artifact receipt."""
    outcome = record.get('task_outcome')
    if not isinstance(outcome, dict):
        execution = record.get('execution', {})
        return execution.get('task_outcome_error') or 'structured native task outcome is required'
    required = {'schemaVersion', 'task_id', 'task_digest', 'input_content', 'status',
                'summary', 'artifacts', 'no_change_reason', 'artifact_evidence'}
    if set(outcome) != required or type(outcome.get('schemaVersion')) is not int \
            or outcome.get('schemaVersion') != 1:
        return 'task outcome receipt has an unsupported shape'
    if (outcome.get('task_id') != record.get('task_id')
            or outcome.get('task_digest') != record.get('dispatch_task_digest')
            or outcome.get('input_content') != record.get('input_identity', {}).get('content')):
        return 'task outcome is stale or bound to a different task/content'
    claim = record.get('execution', {}).get('task_outcome')
    if not isinstance(claim, dict) or record.get('execution', {}).get('task_outcome_error'):
        return 'task outcome is not bound to a parsed native marker'
    claim_fields = {'schemaVersion', 'task_id', 'task_digest', 'input_content', 'status',
                    'summary', 'artifacts', 'no_change_reason'}
    if set(claim) != claim_fields or type(claim.get('schemaVersion')) is not int \
            or claim.get('schemaVersion') != 1:
        return 'inspected native task marker has an unsupported shape'
    for key in ('schemaVersion', 'task_id', 'task_digest', 'input_content', 'status',
                'summary', 'no_change_reason'):
        if outcome.get(key) != claim.get(key):
            return 'task outcome differs from the inspected native marker'
    if (not isinstance(claim.get('artifacts'), list)
            or set(outcome.get('artifacts', [])) != set(claim['artifacts'])):
        return 'task outcome artifacts differ from the inspected native marker'
    if outcome.get('status') != 'completed':
        return 'native task reported a blocked or unsuccessful outcome'
    if not isinstance(outcome.get('summary'), str) or not outcome['summary'].strip():
        return 'task outcome requires an inspected summary'
    artifacts = outcome.get('artifacts')
    write_paths = set(task.get('write_paths', []))
    if (not isinstance(artifacts, list) or any(not isinstance(path, str) or not path for path in artifacts)
            or len(artifacts) != len(set(artifacts)) or set(artifacts) - write_paths):
        return 'task outcome artifacts must be unique declared write paths'
    if set(artifacts) != set(record.get('files_changed', [])):
        return 'task outcome artifacts do not match observed changed files'
    no_change_reason = outcome.get('no_change_reason')
    if no_change_reason is not None and (not isinstance(no_change_reason, str)
                                         or not no_change_reason.strip()):
        return 'task outcome no_change_reason must be null or nonempty text'
    if write_paths and not artifacts and not no_change_reason:
        return 'unexplained no-op write task outcome'
    if artifacts and no_change_reason is not None:
        return 'task outcome cannot declare both changed artifacts and a no-change reason'
    evidence = outcome.get('artifact_evidence')
    if not isinstance(evidence, dict) or set(evidence) != set(artifacts):
        return 'task outcome lacks actual declared artifact evidence'
    if any(value is not None and (not isinstance(value, str) or not value)
           for value in evidence.values()):
        return 'task outcome artifact evidence is malformed'
    for rel, identity in evidence.items():
        if current_files is not None and current_files.get(rel) != identity:
            return 'task outcome artifact evidence is stale'
        if output_artifacts is not None and output_artifacts.get(rel) != identity:
            return 'task outcome artifact evidence differs from accepted artifacts'
    return None


def bind_task_outcome(record, task, changed, files):
    execution = record.get('execution', {})
    claim = execution.get('task_outcome')
    if not isinstance(claim, dict):
        detail = execution.get('task_outcome_error') or 'structured native task outcome is required'
        raise ValueError(detail)
    claim_fields = {'schemaVersion', 'task_id', 'task_digest', 'input_content', 'status',
                    'summary', 'artifacts', 'no_change_reason'}
    if set(claim) != claim_fields:
        raise ValueError('native task outcome marker has an unsupported shape')
    if claim.get('status') == 'blocked':
        raise ValueError('native task reported blocked outcome: '+str(claim.get('summary', '')).strip())
    if (type(claim.get('schemaVersion')) is not int or claim.get('schemaVersion') != 1
            or claim.get('task_id') != record.get('task_id')
            or claim.get('task_digest') != record.get('dispatch_task_digest')
            or claim.get('input_content') != record.get('input_identity', {}).get('content')):
        raise ValueError('native task outcome is stale or bound to a different task/content')
    artifacts = claim.get('artifacts')
    write_paths = set(task.get('write_paths', []))
    if (not isinstance(artifacts, list) or any(not isinstance(path, str) or not path for path in artifacts)
            or len(artifacts) != len(set(artifacts)) or set(artifacts) - write_paths):
        raise ValueError('native task outcome artifacts must be unique declared write paths')
    if set(artifacts) != set(changed):
        raise ValueError('native task outcome artifacts do not match observed changed files')
    reason = claim.get('no_change_reason')
    if reason is not None and (not isinstance(reason, str) or not reason.strip()):
        raise ValueError('native task outcome no_change_reason must be null or nonempty text')
    if write_paths and not artifacts and not reason:
        raise ValueError('unexplained no-op write task outcome')
    if artifacts and reason is not None:
        raise ValueError('native task outcome cannot declare changed artifacts and a no-change reason')
    if (not isinstance(claim.get('summary'), str) or not claim['summary'].strip()
            or claim.get('status') != 'completed'):
        raise ValueError('native task outcome is not a completed inspected result')
    record['task_outcome'] = {
        'schemaVersion': 1, 'task_id': record['task_id'],
        'task_digest': record['dispatch_task_digest'],
        'input_content': record['input_identity']['content'],
        'status': 'completed', 'summary': claim['summary'],
        'artifacts': sorted(artifacts), 'no_change_reason': reason,
        'artifact_evidence': {rel: files.get(rel) for rel in artifacts},
    }


def validate_work(root, state, task, path):
    record = state['tasks'][task['task_id']]
    task = dispatched_task(state, task['task_id'])
    if not verified_execution(record, record['route']):
        raise ValueError('no verified terminal execution; reconcile unknown work instead')
    if not legacy_design_valid(record, task):
        event(state, task['task_id'], 'failed',
              'legacy design-review evidence is missing, stale or not bound to the dispatched task')
        atomic_save(path, state)
        return False
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
    outcome_error = task_outcome_error(record, task)
    if outcome_error:
        event(state, task['task_id'], 'failed', outcome_error)
        atomic_save(path, state)
        return False
    current_files = amr.tree_identity(root)['files']
    record['task_outcome']['artifact_evidence'] = {
        rel: current_files.get(rel) for rel in record['task_outcome']['artifacts']}
    outcome_error = task_outcome_error(record, task, current_files=current_files)
    if outcome_error:
        event(state, task['task_id'], 'failed', outcome_error)
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


def local_developer_only(task):
    if (task.get('scope') != 'local-development' or amr.execution_role(task) != 'developer'
            or amr.design_review_required(task)
            or set(task.get('signals', [])) & {'production', 'secrets', 'remote-execution'}):
        raise ValueError('coordinator completion supports approved local Developer repository work only')


def coordinator_claim(root, goal_id):
    # The filesystem edits occur between CLI calls. Retain one checkout claim
    # even though the short-lived atomic checkpoint writer lock is released.
    for path in private_directory(root).glob('*.json'):
        if path.stem == goal_id:
            continue
        if path.is_symlink():
            raise ValueError('checkpoint cannot be a symlink')
        other = json.loads(path.read_text())
        if any(t.get('status') == 'in_progress' for t in other.get('tasks', {}).values()):
            raise ValueError('another coordinator task owns this checkout')


def preparation_identity(record):
    value = {key: record.get(key) for key in (
        'dispatch_task_digest', 'input_identity', 'input_file_identities',
        'protected_content_identity', 'coordinator_context', 'scope_basis')}
    # Omit the additive field for historical receipts: preserve their digests.
    if 'protected_file_identities' in record:
        value['protected_file_identities'] = record['protected_file_identities']
    return digest(value)


def coordinator_diagnostics(root, record, *, files=None, git=None):
    """Read-only drift evidence; never widen writes, reset a baseline or replay."""
    files = amr.tree_identity(root)['files'] if files is None else files
    git = amr.git_identity(root) if git is None else git
    task = record['dispatch_task']; writable = set(task['write_paths'])
    protected = {p: h for p, h in files.items() if p not in writable}
    baseline = record.get('protected_file_identities')
    changes = (None if baseline is None else {
        p: {'before': baseline.get(p), 'after': protected.get(p)}
        for p in sorted(set(baseline) | set(protected))
        if baseline.get(p) != protected.get(p)})
    return {
        'protected_content_changed': digest(protected) != record['protected_content_identity'],
        'protected_manifest': ('available' if baseline is not None
                               else 'unavailable_historical_checkpoint'),
        'protected_changes': changes,
        'git_head_or_branch_changed': any(
            git.get(k) != record['input_identity']['git'].get(k) for k in ('head', 'branch')),
        'declared_write_changes': [p for p, h in record['input_file_identities'].items()
                                   if files.get(p) != h],
        'next_action': 'Inspect drift; use goal reconcile with evidence for stopped work. Do not replay or reset the baseline.'}


def step(root, goal_id, design_review=None, *, native_runner=False, context=None):
    """Default to this chat; optional serial runner is an explicit operation."""
    if native_runner:
        return native_step(root, goal_id, design_review)
    if design_review is not None:
        raise ValueError('AMR v4 does not consume AI design-review receipts')
    with checkpoint(root, goal_id) as path:
        state = load(root, path)
        current = next((r for r in state['tasks'].values() if r['status'] == 'in_progress'), None)
        if current:
            result = projection(root, state)
            result['execution_mode'] = 'current_session'
            result['instruction'] = 'Continue the prepared task; finish with current evidence. Do not start it again.'
            return result
        if ensure_control(state)['status'] != 'active':
            raise ValueError('goal is '+state['control']['status']+'; resolve the existing task first')
        coordinator_claim(root, goal_id)
        task = next_task(state)
        if task is None:
            raise ValueError('no eligible task')
        task = amr.validate_task(copy.deepcopy(task), root)
        local_developer_only(task)
        if (not isinstance(context, dict) or not isinstance(context.get('scope_basis'), str)
                or not context['scope_basis'].strip()):
            raise ValueError('current-session execution requires --context with native_context and approved scope_basis')
        native = native_context_evidence(context.get('native_context'), current=True)
        route = amr.resolve(amr.classify(task), discover(root))
        if (route.get('action') != 'continue' or route.get('support') != 'advertised'
                or (native['model'], native['effort']) != (route.get('model'), route.get('effort'))):
            raise ValueError('actual coordinator model/effort must match the advertised Developer route')
        before = amr.tree_identity(root)
        for dependency in task['depends_on']:
            artifacts = state['tasks'][dependency].get('output_artifacts', {})
            if not artifacts or any(before['files'].get(p) != h for p, h in artifacts.items()):
                raise ValueError('completed dependency inputs changed; reconcile the task plan')
        record = state['tasks'][task['task_id']]
        record.update(policy_version='4', execution_mode='current_session', route=route,
            dispatch_task=task, dispatch_task_digest=amr.task_identity(task),
            input_identity=current_identity(root),
            input_file_identities={p: before['files'].get(p) for p in task['write_paths']},
            protected_content_identity=protected_content_identity(before['files'], task['write_paths']),
            protected_file_identities={p: h for p, h in before['files'].items()
                                       if p not in set(task['write_paths'])},
            coordinator_context=native, scope_basis=context['scope_basis'],
            context_paths=task['paths']+task['consulted'])
        record['preparation_digest'] = preparation_identity(record)
        event(state, task['task_id'], 'in_progress', 'Prepared for current coordinator; no AI dispatch')
        atomic_save(path, state)
        result = projection(root, state)
        result.update(execution_mode='current_session', task=task)
        return result


def validate_local_gates(gates, state, triage, content, product_required):
    if not isinstance(gates, dict) or not isinstance(triage, dict):
        raise ValueError('gate and contributor evidence must be JSON objects')
    checks = gates.get('checks', [])
    if not isinstance(checks, list) or any(not isinstance(c, dict) for c in checks):
        raise ValueError('gate checks must be an array of objects')
    errors = amr.completion_errors(dict(gates, findings=triage.get('findings')),
                                  content, product_required=product_required)
    mapping = next((c for c in checks if c.get('name') == 'mapping'), {})
    if (gates.get('source_mapping') != 'executed'
            or len({c.get('name') for c in checks}) != len(checks)
            or any(c.get('exit_code') != 0 for c in checks)
            or not isinstance(mapping.get('command'), list)
            or mapping['command'][1:] != ['docs/tooling/changeguard.py', '--base',
                                          state['plan']['base'], '--working-tree']
            or product_required is not (gates.get('product_tests') == 'executed')):
        errors.append('successful applicable gates and exact base mapping are required')
    if errors:
        raise ValueError('; '.join(errors))


def local_gate_evidence(root, state, evidence, identity):
    _, gates = private_evidence(root, evidence.get('gates'))
    validate_local_gates(gates, state, evidence.get('triage'), identity['content'],
                         (root/'Kordra.slnx').is_file())
    return gates


def contributor_triage(task, triage, content, previous_findings=()):
    if (not isinstance(triage, dict) or triage.get('task_id') != task['task_id']
            or triage.get('content') != content
            or not isinstance(triage.get('reviewed_by'), str) or not triage['reviewed_by'].strip()
            or not isinstance(triage.get('rationale'), str) or not triage['rationale'].strip()
            or not isinstance(triage.get('findings'), list)):
        raise ValueError('exact-content contributor triage is required')
    known = task.get('findings', []) + list(previous_findings)
    if (any(not isinstance(f, dict) or not f.get('id') for f in known)
            or not {f['id'] for f in known} <= {f.get('id') for f in triage['findings'] if isinstance(f, dict)}):
        raise ValueError('continuation cannot omit previously reported findings')
    errors = amr.finding_errors(triage['findings'], content)
    if errors:
        raise ValueError('; '.join(errors))


def validate_coordinator_record(root, state, task_id):
    try:
        _validate_coordinator_record(root, state, task_id)
    except (KeyError, TypeError, AttributeError) as exc:
        raise ValueError('incomplete or invalid coordinator completion receipt') from exc


def _validate_coordinator_record(root, state, task_id):
    record = state['tasks'][task_id]
    task = dispatched_task(state, task_id)
    local_developer_only(task)
    receipt = record.get('coordinator_receipt', {})
    if (record.get('policy_version') != '4' or record.get('execution_mode') != 'current_session'
            or record.get('attempts') != 0 or record.get('validation_attempts') != 0
            or any(key in record for key in ('execution', 'task_outcome', 'native_thread_id'))
            or record.get('preparation_digest') != preparation_identity(record)
            or record.get('coordinator_receipt_digest') != digest(receipt)
            or receipt.get('preparation_digest') != record['preparation_digest']
            or receipt.get('task_digest') != amr.task_identity(task)
            or receipt.get('content') != record.get('validated_identity', {}).get('content')
            or receipt.get('native_context', {}).get('observation') != 'turn_context_only_not_terminal_execution'
            or (receipt['native_context'].get('model'), receipt['native_context'].get('effort')) != amr.PROFILES['ENGINEERING']
            or receipt['native_context'].get('thread_id') != record['coordinator_context'].get('thread_id')
            or not isinstance(receipt.get('summary'), str) or not receipt['summary'].strip()):
        raise ValueError('invalid coordinator completion receipt; native execution is not claimed')
    content = record['validated_identity']['content']
    contributor_triage(task, record.get('triage'), content)
    validate_local_gates(record['gates'], state, record['triage'], content, record['product_required'])
    outputs = record.get('output_artifacts', {})
    expected_paths = set(task['paths']+task['consulted']+task['write_paths'])
    changed = {p: outputs.get(p) for p, old in record['input_file_identities'].items()
               if outputs.get(p) != old}
    if (set(outputs) != expected_paths or set(record['input_file_identities']) != set(task['write_paths'])
            or receipt.get('artifacts') != changed
            or (task['write_paths'] and not changed and (
                not isinstance(receipt.get('no_change_reason'), str)
                or not receipt['no_change_reason'].strip()))):
        raise ValueError('coordinator artifact evidence is incomplete or outside declared writes')


def finish(root, goal_id, task_id, evidence):
    """Record current-chat work; no native terminal marker or human decision is synthesized."""
    if not isinstance(evidence, dict):
        raise ValueError('completion evidence must be a JSON object')
    with checkpoint(root, goal_id) as path:
        state = load(root, path); record = state['tasks'][task_id]
        if record['status'] != 'in_progress' or record.get('execution_mode') != 'current_session':
            raise ValueError('task is not a prepared current-session task')
        task = dispatched_task(state, task_id); local_developer_only(task)
        files = amr.tree_identity(root)['files']; identity = current_identity(root)
        if (evidence.get('identity') != identity or evidence.get('task_id') != task_id
                or evidence.get('task_digest') != amr.task_identity(task)
                or record.get('preparation_digest') != preparation_identity(record)):
            raise ValueError('stale coordinator evidence or invalid preparation binding')
        diagnostics = coordinator_diagnostics(root, record, files=files, git=identity['git'])
        if diagnostics['protected_content_changed']:
            paths = diagnostics['protected_changes']
            detail = (', '.join(sorted(paths)) if paths is not None
                      else 'historical checkpoint has no protected-file manifest')
            raise ValueError('changes outside prepared task scope: '+detail+'; inspect and reconcile without replay')
        if diagnostics['git_head_or_branch_changed']:
            raise ValueError('Git HEAD/branch changed since preparation; inspect and reconcile without replay')
        native = native_context_evidence(evidence.get('native_context'), current=True)
        if ((native['model'], native['effort']) != amr.PROFILES['ENGINEERING']
                or native['thread_id'] != record['coordinator_context']['thread_id']):
            raise ValueError('coordinator session/model changed; reconcile before completion')
        changed = {p: files.get(p) for p, old in record['input_file_identities'].items() if files.get(p) != old}
        if evidence.get('artifacts') != changed:
            raise ValueError('coordinator artifacts must exactly match observed declared changes')
        contributor_triage(task, evidence.get('triage'), identity['content'])
        gates = local_gate_evidence(root, state, evidence, identity)
        receipt = {'task_digest': amr.task_identity(task), 'content': identity['content'],
            'preparation_digest': record['preparation_digest'], 'native_context': native,
            'summary': evidence.get('summary'), 'no_change_reason': evidence.get('no_change_reason'),
            'artifacts': changed, 'created_at': amr.now()}
        record.update(completion_kind='coordinator_execution', coordinator_receipt=receipt,
            coordinator_receipt_digest=digest(receipt), validated_identity=identity,
            gates=gates, product_required=(root/'Kordra.slnx').is_file(), triage=evidence['triage'],
            output_artifacts={p: files.get(p) for p in set(task['paths']+task['consulted']+task['write_paths'])})
        validate_coordinator_record(root, state, task_id)
        if identity != current_identity(root):
            raise ValueError('source changed during coordinator completion')
        event(state, task_id, 'completed', 'Current-session contributor evidence recorded; PR review remains separate')
        atomic_save(path, state)
        return projection(root, state)


def native_step(root, goal_id, design_review=None):
    with checkpoint(root, goal_id) as path:
        state = load(root, path)
        coordinator_claim(root, goal_id)
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
        def turn_started(turn_id):
            record['native_turn_id'] = turn_id
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
                sandbox='workspace-write' if task['write_paths'] else 'read-only', on_start=started,
                on_turn_started=turn_started)
            after = amr.tree_identity(root)
            changed = sorted(p for p in before['files'].keys() | after['files'].keys()
                             if before['files'].get(p) != after['files'].get(p))
            record['files_changed'] = changed
            if set(changed) - set(task['write_paths']):
                event(state, task['task_id'], 'failed', 'execution changed undeclared files; inspect and recover manually')
            elif not verified_execution(record, route):
                event(state, task['task_id'], 'unknown', 'native execution not verified; no automatic replay')
            else:
                try:
                    bind_task_outcome(record, task, changed, after['files'])
                except ValueError as exc:
                    event(state, task['task_id'], 'failed', str(exc))
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
        task = dispatched_task(state, task_id)
        if not legacy_design_valid(record, task):
            raise ValueError('legacy design-review evidence is missing, stale or not bound to the dispatched task')
        outcome_error = task_outcome_error(record, task,
            current_files=amr.tree_identity(root)['files'])
        if outcome_error:
            raise ValueError(outcome_error)
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
    if not isinstance(evidence, str) or not evidence.strip():
        raise ValueError('reconciliation requires nonempty contributor evidence')
    findings = []
    if evidence.startswith('.amr-local/'):
        _, value = private_evidence(root, evidence)
        findings = value.get('findings', [])
        if (not isinstance(findings, list)
                or any(not isinstance(f, dict) or not isinstance(f.get('id'), str)
                       or not f['id'].strip() for f in findings)
                or len({f['id'] for f in findings}) != len(findings)):
            raise ValueError('reconciliation findings require unique nonempty IDs')
    with checkpoint(root, goal_id) as path:
        state = load(root, path); record = state['tasks'][task_id]
        if record['status'] not in {'running', 'unknown', 'in_progress'}:
            raise ValueError('only interrupted/unknown execution needs reconciliation')
        raw = path.read_bytes()
        directory = path.parent.parent/'reconciliation'
        if directory.is_symlink():
            raise ValueError('reconciliation snapshots cannot traverse symlinks')
        directory.mkdir(mode=0o700, exist_ok=True); directory.chmod(0o700)
        snapshot = directory/(identifier(goal_id)+'-before-'+str(uuid.uuid4())+'.json')
        fd = os.open(snapshot, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
        with os.fdopen(fd, 'wb') as stream:
            stream.write(raw); stream.flush(); os.fsync(stream.fileno())
        record['reconciliation'] = {
            'evidence': evidence, 'identity': current_identity(root),
            'checkpoint_before': {'path': snapshot.relative_to(root).as_posix(),
                                  'sha256': hashlib.sha256(raw).hexdigest()},
            'findings': copy.deepcopy(findings)}
        if record.get('execution_mode') == 'current_session':
            record['reconciliation']['diagnostics'] = coordinator_diagnostics(root, record)
        # Do not synthesize success from prose; actual execution receipt is mandatory.
        target = 'validation_failed' if verified_execution(record, record['route']) else 'failed'
        event(state, task_id, target, 'reconciled without replay; new work needs an explicit new task/plan')
        atomic_save(path, state)
        return projection(root, state)


def private_evidence(root, relative):
    """Read local evidence without following symlinks or arbitrary paths."""
    if not isinstance(relative, str) or Path(relative).is_absolute():
        raise ValueError('evidence must be a private repository-relative file')
    parts = Path(relative).parts
    if len(parts) < 2 or parts[0] != '.amr-local' or '..' in parts:
        raise ValueError('evidence must remain inside .amr-local')
    path = root
    for part in parts:
        path = path/part
        if path.is_symlink():
            raise ValueError('evidence cannot traverse symlinks')
    if not path.is_file():
        raise ValueError('evidence file is missing')
    value = json.loads(path.read_text())
    if not isinstance(value, dict):
        raise ValueError('evidence must be a JSON object')
    return path, value


def native_context_evidence(context, *, current=False):
    """Observe a real local turn context; never claim terminal task execution."""
    if not isinstance(context, dict) or not all(
            isinstance(context.get(k), str) and context[k].strip()
            for k in ('source', 'thread_id', 'turn_id', 'model', 'effort')):
        raise ValueError('native turn context identity is required')
    path = Path(context['source'])
    sessions = Path(os.environ.get('CODEX_HOME', Path.home()/'.codex'))/'sessions'
    if not path.is_absolute() or not path.is_relative_to(sessions.absolute()):
        raise ValueError('native context must be an actual local session log')
    relative = path.relative_to(sessions.absolute())
    if (not path.is_absolute() or not path.resolve().is_relative_to(sessions.resolve())
            or path.suffix != '.jsonl' or not path.is_file()
            or any((sessions/Path(*relative.parts[:i])).is_symlink()
                   for i in range(len(relative.parts)+1))):
        raise ValueError('native context must be an actual local session log')
    session_matches = False
    observations = []
    latest_turn = None
    with path.open() as stream:
        for line in stream:
            try:
                row = json.loads(line)
            except ValueError:
                continue
            payload = row.get('payload', {})
            if row.get('type') == 'session_meta' and payload.get('id') == context['thread_id']:
                session_matches = True
            if row.get('type') == 'turn_context':
                latest_turn = payload.get('turn_id')
            if row.get('type') == 'turn_context' and payload.get('turn_id') == context['turn_id']:
                observations.append((payload.get('model'), payload.get('effort')))
    if (current and latest_turn != context['turn_id']):
        raise ValueError('current coordinator requires the latest native model/effort turn context')
    if not session_matches or not observations or any(
            pair != (context['model'], context['effort']) for pair in observations):
        raise ValueError('native model/effort does not match the referenced thread/turn')
    return dict(context, observation='turn_context_only_not_terminal_execution')


def validate_recovered_record(root, state, task_id):
    """Separate maintainer evidence adoption from native execution acceptance."""
    try:
        _validate_recovered_record(root, state, task_id)
    except (KeyError, TypeError, AttributeError, OSError) as exc:
        raise ValueError('incomplete or invalid evidence-recovery checkpoint') from exc


def _validate_recovered_record(root, state, task_id):
    record = state['tasks'][task_id]
    recovery = record.get('recovery', {})
    if (not recovery or record.get('recovery_digest') != digest(recovery)
            or record.get('policy_version') != '4'
            or record.get('attempts') != 0 or record.get('validation_attempts') != 0
            or any(k in record for k in ('execution', 'task_outcome', 'native_thread_id'))):
        raise ValueError('invalid evidence-recovery checkpoint')
    origin = recovery['origin']
    path, previous = private_evidence(root, '.amr-local/goals/'+identifier(origin['goal_id'])+'.json')
    if hashlib.sha256(path.read_bytes()).hexdigest() != origin['checkpoint_sha256']:
        raise ValueError('recovery origin checkpoint changed')
    old_record = previous['tasks'][task_id]
    task = dispatched_task(state, task_id)
    expected_plan = copy.deepcopy(previous['plan'])
    expected_plan['goal_id'] = state['plan']['goal_id']
    if (len(previous['tasks']) != 1 or origin['task_id'] != task_id
            or state['plan'] != expected_plan
            or old_record['status'] != 'failed' or old_record.get('policy_version') != '4'
            or origin['task_digest'] != amr.task_identity(task)
            or old_record.get('dispatch_task_digest') != amr.task_identity(task)
            or task['scope'] != 'local-development' or amr.execution_role(task) != 'developer'
            or set(task.get('signals', [])) & {'production', 'secrets', 'remote-execution'}):
        raise ValueError('recovery requires one failed v4 local Developer task')
    content = record['validated_identity']['content']
    continued = record.get('completion_kind') == 'coordinator_continuation'
    authorization = recovery.get('authorization', {})
    if continued:
        local_developer_only(task)
        continuation = recovery.get('continuation', {})
        if (continuation.get('execution_stopped') is not True
                or continuation.get('effects') != 'repository_files_only'
                or continuation.get('external_operations') != []
                or any(not isinstance(continuation.get(k), str) or not continuation[k].strip()
                       for k in ('scope_basis', 'inspection_evidence'))):
            raise ValueError('continuation requires stopped execution, approved scope and inspected repository-only effects')
    # Historical human-authorized recovery keeps its exact original requirements.
    elif (authorization.get('purpose') != 'checkpoint_recovery'
            or not valid_human_design_approval(authorization, content, task)):
        raise ValueError('explicit task/content-bound maintainer recovery approval is required')
    native = recovery['native_context']
    if native.get('observation') != 'turn_context_only_not_terminal_execution':
        raise ValueError('recovery must not claim native terminal execution')
    if (native.get('model'), native.get('effort')) != amr.PROFILES['ENGINEERING']:
        if continued:
            raise ValueError('coordinator continuation requires exact default Developer model/effort')
        override = authorization.get('model_override', {})
        if ((override.get('model'), override.get('effort')) != (native.get('model'), native.get('effort'))
                or not isinstance(override.get('decision_evidence'), str)
                or not override['decision_evidence'].strip()):
            raise ValueError('non-default native model requires explicit task-local approval')
    triage = record['triage']
    if (triage.get('task_id') != task_id or triage.get('content') != content
            or not isinstance(triage.get('reviewed_by'), str) or not triage['reviewed_by'].strip()
            or not isinstance(triage.get('rationale'), str) or not triage['rationale'].strip()):
        raise ValueError('recovery requires exact-content contributor triage')
    known = (task.get('findings', []) + old_record.get('triage', {}).get('findings', [])
             + old_record.get('reconciliation', {}).get('findings', []))
    if (any(not isinstance(f, dict) or not f.get('id') for f in known)
            or not {f['id'] for f in known} <= {
                f.get('id') for f in triage.get('findings', []) if isinstance(f, dict)}):
        raise ValueError('recovery cannot omit previously reported findings')
    gates = record['gates']
    errors = amr.completion_errors(dict(gates, findings=triage.get('findings')), content,
                                   product_required=record['product_required'])
    checks = gates.get('checks', [])
    names = [c.get('name') for c in checks]
    mapping = next((c for c in checks if c.get('name') == 'mapping'), {})
    if (gates.get('source_mapping') != 'executed' or len(names) != len(set(names))
            or any(c.get('exit_code') != 0 for c in checks)
            or mapping.get('command', [])[1:] != ['docs/tooling/changeguard.py', '--base',
                                                   state['plan']['base'], '--working-tree']
            or record['product_required'] is not (gates.get('product_tests') == 'executed')):
        errors.append('recovery requires successful applicable gates and exact base mapping')
    expected = set(task['paths']+task['consulted']+(task['write_paths'] if continued else []))
    artifacts = record.get('output_artifacts', {})
    if (set(artifacts) != expected or recovery.get('artifacts') != artifacts
            or any(not isinstance(value, str) or not value for value in artifacts.values())):
        errors.append('recovery requires complete task/context artifact hashes')
    if errors:
        raise ValueError('; '.join(errors))


def recover(root, goal_id, task_id, successor_id, evidence_path, *, coordinator=False):
    """Adopt current evidence into a new checkpoint; never rewrite/replay failure."""
    identifier(goal_id); identifier(task_id); identifier(successor_id)
    if goal_id == successor_id:
        raise ValueError('recovery requires a new successor goal ID')
    with checkpoint(root, successor_id) as path:
        if path.exists():
            raise ValueError('successor already exists; inspect it instead of repeating recovery')
        origin_path, _ = private_evidence(root, '.amr-local/goals/'+goal_id+'.json')
        original = load(root, origin_path)
        if len(original['tasks']) != 1 or task_id not in original['tasks']:
            raise ValueError('bounded recovery supports a single-task origin only')
        task = dispatched_task(original, task_id)
        _, evidence = private_evidence(root, evidence_path)
        identity = current_identity(root)
        expected_origin = dict(goal_id=goal_id, task_id=task_id,
            checkpoint_sha256=hashlib.sha256(origin_path.read_bytes()).hexdigest(),
            task_digest=amr.task_identity(task))
        decision_field = 'continuation' if coordinator else 'authorization'
        if (evidence.get('schemaVersion') != (2 if coordinator else 1)
                or not {'origin', 'identity', 'gates', 'artifacts', decision_field, 'triage',
                        'native_context'} <= evidence.keys()
                or evidence.get('origin') != expected_origin
                or evidence.get('identity') != identity):
            raise ValueError('recovery evidence is stale or bound to another task/checkpoint')
        _, gates = private_evidence(root, evidence.get('gates'))
        files = amr.tree_identity(root)['files']
        expected = set(task['paths']+task['consulted']+(task['write_paths'] if coordinator else []))
        if (not isinstance(evidence.get('artifacts'), dict)
                or set(evidence['artifacts']) != expected
                or any(files.get(name) != value for name, value in evidence['artifacts'].items())):
            raise ValueError('recovery artifact hashes are missing or stale')
        plan = copy.deepcopy(original['plan']); plan['goal_id'] = successor_id
        recovery = dict(origin=expected_origin,
                        native_context=native_context_evidence(evidence['native_context']),
                        artifacts=copy.deepcopy(evidence['artifacts']), created_at=amr.now())
        recovery[decision_field] = copy.deepcopy(evidence[decision_field])
        record = dict(task_id=task_id, status='completed',
            completion_kind='coordinator_continuation' if coordinator else 'evidence_recovery',
            attempts=0, validation_attempts=0, policy_version='4',
            dispatch_task=copy.deepcopy(task), dispatch_task_digest=amr.task_identity(task),
            validated_identity=identity, gates=gates, product_required=(root/'Kordra.slnx').is_file(),
            triage=evidence['triage'], output_artifacts=copy.deepcopy(evidence['artifacts']),
            recovery=recovery, recovery_digest=digest(recovery))
        state = dict(schemaVersion=1, plan=plan, plan_digest=digest(plan),
            objective_hash=original['objective_hash'], created_at=amr.now(),
            tasks={task_id: record}, events=[dict(at=amr.now(), task_id=task_id,
            status='completed', detail=('Inspected local coordinator continuation; original failure retained; no AI dispatch'
                if coordinator else 'Maintainer-authorized evidence recovery; original failure retained; no AI dispatch'))])
        state['control'] = default_control(state)
        validate_recovered_record(root, state, task_id)
        if identity != current_identity(root):
            raise ValueError('source/Git changed during recovery')
        atomic_save(path, state)
        return projection(root, load(root, path))


def add_parser(sub):
    parser = sub.add_parser('goal', help='persistent local goal execution')
    commands = parser.add_subparsers(dest='goal_command', required=True)
    p = commands.add_parser('init'); p.add_argument('--plan', required=True, type=Path)
    p = commands.add_parser('recover', help='adopt approved current evidence into a new checkpoint; no AI dispatch')
    p.add_argument('--id', required=True)
    p.add_argument('--task-id', required=True)
    p.add_argument('--as-goal', required=True)
    p.add_argument('--evidence', required=True)
    p = commands.add_parser('continue', help='record inspected local Developer continuation; no replay or new approval')
    p.add_argument('--id', required=True); p.add_argument('--task-id', required=True)
    p.add_argument('--as-goal', required=True); p.add_argument('--evidence', required=True)
    p = commands.add_parser('finish', help='record current-session completion without a native terminal receipt')
    p.add_argument('--id', required=True); p.add_argument('--task-id', required=True)
    p.add_argument('--evidence', required=True)
    for command in ('status', 'step', 'validate', 'accept', 'reconcile', 'recommend', 'pause', 'resume'):
        p = commands.add_parser(command); p.add_argument('--id', required=True)
        if command in {'validate', 'accept', 'reconcile'}:
            p.add_argument('--task-id', required=True)
        if command == 'accept':
            p.add_argument('--triage', required=True, type=Path)
        if command == 'step':
            p.add_argument('--design-review', type=Path)
            p.add_argument('--native-runner', action='store_true', help='explicit optional serial AI session')
            p.add_argument('--context', help='private native turn context and approved scope basis')
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
    if args.goal_command == 'recover':
        return recover(root, args.id, args.task_id, args.as_goal, args.evidence)
    if args.goal_command == 'continue':
        return recover(root, args.id, args.task_id, args.as_goal, args.evidence, coordinator=True)
    if args.goal_command == 'finish':
        return finish(root, args.id, args.task_id, private_evidence(root, args.evidence)[1])
    if args.goal_command == 'status':
        return status(root, args.id)
    if args.goal_command == 'step':
        return step(root, args.id, json.loads(args.design_review.read_text()) if args.design_review else None,
                    native_runner=args.native_runner,
                    context=private_evidence(root, args.context)[1] if args.context else None)
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
