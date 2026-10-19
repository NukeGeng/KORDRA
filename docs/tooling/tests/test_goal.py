"""Offline goal integration tests; only native execution/check boundaries are mocked."""
import copy
import json
from pathlib import Path
import stat
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import amr
import amr_goal as goal


class GoalIntegration(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        for name, content in {
            '.gitignore': '.amr-local/\n', 'GOAL.md': 'Local development goal\n',
            'progress.md': 'Milestone one\n', 'source.py': 'value = 1\n',
            'test_source.py': 'assert True\n',
            'feature.md': ('---\nfeature_id: FEAT-FIXTURE-001\nmodule_id: FIXTURE\n'
                           'status: implemented\nsource_paths: ["source.py"]\n'
                           'test_paths: ["test_source.py"]\n---\nFixture owner\n'),
        }.items():
            (self.root/name).write_text(content)
        self.git('init', '-q')
        self.git('config', 'user.name', 'Goal fixture')
        self.git('config', 'user.email', 'goal@example.invalid')
        self.git('add', '.')
        self.git('commit', '-qm', 'Fixture baseline')
        task = dict(task_id='A', objective='Inspect fixture source', feature_id='FEAT-FIXTURE-001',
                    module_id='FIXTURE', actor='contributor', scope='local-development',
                    permission='repository.write', paths=['source.py', 'test_source.py'],
                    consulted=['feature.md'], invariants=['Local files only'], decisions=[],
                    tests=['python test_source.py'], findings=[], next_action='Validate fixture',
                    kind='source', signals=[], milestone='M1', depends_on=[], write_paths=[])
        second = dict(copy.deepcopy(task), task_id='B', depends_on=['A'])
        self.plan = dict(schemaVersion=1, goal_id='fixture', milestone='M1',
                         objective='Continue the fixture goal safely.',
                         base=self.git('rev-parse', 'HEAD'), objective_path='GOAL.md',
                         progress_path='progress.md', tasks=[task, second])

    def git(self, *args):
        return subprocess.run(['git', *args], cwd=self.root, capture_output=True,
                              text=True, check=True).stdout.strip()

    def capabilities(self, root):
        return {'models': [
            {'model': 'gpt-6.1-sol', 'efforts': ['medium', 'high']},
            {'model': 'gpt-6-luna', 'efforts': ['high', 'max']}]}

    def execution(self, root, task, model, effort, *, mutate=True, outcome_status='completed',
                  no_change_reason=None, claimed_artifacts=None, **kwargs):
        kwargs['on_start']('native-thread-A')
        artifacts = []
        if mutate:
            for rel in task.get('write_paths', []):
                target = root/rel
                target.write_text(target.read_text()+'\n# bounded task output\n')
                artifacts.append(rel)
        pre_dispatch = kwargs.get('pre_dispatch') or {}
        outcome = dict(schemaVersion=1, task_id=task['task_id'],
                       task_digest=pre_dispatch.get('task_digest', amr.task_identity(task)),
                       input_content=pre_dispatch.get('content', 'fixture-input-content'),
                       status=outcome_status, summary='Inspected and completed the bounded task',
                       artifacts=artifacts if claimed_artifacts is None else claimed_artifacts,
                       no_change_reason=no_change_reason)
        return dict(status='verified', turn_status='completed', rerouted=False,
                    execution='native_session', task_id=task['task_id'],
                    requested_model=model, requested_effort=effort,
                    thread_id='native-thread-A', turn_id='native-turn-A',
                    observed_model=model, observed_effort=effort,
                    evidence='.amr-local/native-task-A.json', task_outcome=outcome)

    def gates(self, root, base):
        self.assertEqual(base, self.plan['base'])
        return dict(schemaVersion=1, content=amr.tree_identity(root)['digest'],
                    product_tests='not_configured: no product solution',
                    source_stable=True, source_mapping='executed', local_status='passed',
                    checks=[dict(name=name, status='passed') for name in ('docs', 'scope', 'unit', 'mapping')])

    def review(self, *args, **kwargs):
        return dict(status='verified', evidence='.amr-local/native-review-A.json',
                    channel='native_local_review', observed_model='gpt-6.1-sol',
                    observed_effort='high', turn_status='completed', rerouted=False,
                    thread_id='review-thread-A', turn_id='review-turn-A',
                    review_output=[{'review': 'No actionable findings'}])

    def boundaries(self, execution=None, gates=None, review=None, capabilities=None):
        from contextlib import ExitStack
        stack = ExitStack()
        stack.enter_context(patch.object(goal, 'discover', side_effect=capabilities or self.capabilities))
        self.execute_mock = stack.enter_context(patch.object(goal, 'execute_task', side_effect=execution or self.execution))
        stack.enter_context(patch.object(amr, 'gates', side_effect=gates or self.gates))
        self.review_mock = stack.enter_context(patch.object(goal, 'local_review', side_effect=review or self.review))
        return stack

    def state(self, goal_id='fixture'):
        return json.loads((self.root/f'.amr-local/goals/{goal_id}.json').read_text())

    def triage(self):
        return dict(task_id='A', content=amr.tree_identity(self.root)['digest'],
                    reviewed_by='fixture-contributor', rationale='Inspected current native review', findings=[])

    def architect_task(self):
        task = self.plan['tasks'][0]
        task.update(kind='architecture', objective='Design the bounded fixture change',
                    paths=['feature.md'], write_paths=['feature.md'],
                    decisions=['Do not implement source code in the design task'])
        return task

    def design_approval(self, task_id='A', *, content=None, task=None, **overrides):
        task = task or goal.dispatched_task(self.state(), task_id)
        approval = dict(decision='approved', task_id=task_id,
                        task_digest=amr.task_identity(task),
                        content=content or amr.tree_identity(self.root)['digest'],
                        approved_by='fixture-maintainer', scope='fixture design document only',
                        decision_evidence='explicit maintainer approval in test fixture')
        approval.update(overrides)
        return approval

    def legacy_design_review(self, record, task, **overrides):
        content = record['input_identity']['content']
        task_digest = amr.task_identity(task)
        review = self.review()
        review.update(independent=True, source_stable=True, purpose='design',
                      task_id=task['task_id'], task_digest=task_digest, content=content,
                      evidence='native:legacy-design-review',
                      triage={'task_id': task['task_id'], 'task_digest': task_digest,
                              'content': content, 'reviewed_by': 'fixture-architect',
                              'rationale': 'Reviewed the original design input', 'findings': []})
        review.update(overrides)
        return review

    def legacy_architect_checkpoint(self, *, goal_id='fixture', review_overrides=None):
        self.plan = copy.deepcopy(self.plan)
        self.plan['goal_id'] = goal_id
        self.architect_task()
        goal.init(self.root, self.plan)
        with self.boundaries():
            goal.step(self.root, goal_id)
        state = self.state(goal_id)
        record = state['tasks']['A']
        record['policy_version'] = '3.1'
        task = goal.dispatched_task(state, 'A')
        record['design_review'] = self.legacy_design_review(
            record, task, **(review_overrides or {}))
        path = self.root/f'.amr-local/goals/{goal_id}.json'
        path.write_text(json.dumps(state))
        return path

    def task_outcome_receipt(self, record, *, task_id='A', task_digest=None,
                             input_content=None, artifacts=None, artifact_evidence=None):
        task = goal.dispatched_task(self.state(), task_id)
        return dict(schemaVersion=1, task_id=task_id,
                    task_digest=task_digest or amr.task_identity(task),
                    input_content=input_content or record['input_identity']['content'],
                    status='completed', summary='Inspected and completed the bounded task',
                    artifacts=artifacts or [], no_change_reason=None,
                    artifact_evidence=artifact_evidence or {})

    def test_persistent_status_atomic_checkpoint_and_current_triage_advance(self):
        first = goal.init(self.root, self.plan)
        self.assertEqual(first['next_task'], 'A')
        checkpoint = self.root/'.amr-local/goals/fixture.json'
        self.assertEqual(stat.S_IMODE(checkpoint.stat().st_mode), 0o600)
        self.assertEqual(stat.S_IMODE(checkpoint.parent.stat().st_mode), 0o700)
        self.assertEqual(self.state()['plan']['base'], self.plan['base'])
        with self.boundaries():
            result = goal.step(self.root, 'fixture')
            self.assertEqual(result['tasks']['A']['status'], 'awaiting_triage')
            self.assertIsNone(result['next_task'])
            self.assertEqual(goal.status(self.root, 'fixture')['tasks']['A']['status'], 'awaiting_triage')
            accepted = goal.accept(self.root, 'fixture', 'A', self.triage())
        self.assertEqual(accepted['next_task'], 'B')
        self.assertEqual(self.state()['tasks']['A']['attempts'], 1)
        self.assertEqual(list(checkpoint.parent.glob('.checkpoint-*')), [])

    def test_developer_tasks_never_invoke_sol_for_critical_or_final_milestone_review(self):
        goal.init(self.root, self.plan)
        with self.boundaries(review=lambda *args, **kwargs: self.fail('v4 must not invoke local AI review')):
            first = goal.step(self.root, 'fixture')
            self.assertEqual(first['tasks']['A']['policy_version'], '4')
            self.assertEqual(first['tasks']['A']['route']['model'], 'gpt-6-luna')
            self.assertEqual(first['tasks']['A']['review']['status'], 'not_required')
            goal.accept(self.root, 'fixture', 'A', self.triage())
            second = goal.step(self.root, 'fixture')
            self.assertEqual(second['tasks']['B']['review']['status'], 'not_required')
            triage = self.triage(); triage['task_id'] = 'B'
            goal.accept(self.root, 'fixture', 'B', triage)
        self.assertEqual(goal.status(self.root,'fixture')['status'],'completed')

    def test_dispatch_forwards_source_bound_facts_while_parent_holds_lock(self):
        self.plan['tasks'][0]['decisions'] = ['Caller claims design review and routing passed']
        goal.init(self.root, self.plan)
        before = amr.tree_identity(self.root)['digest']
        def inspect(root, task, model, effort, **kwargs):
            state = self.state()
            record = state['tasks']['A']
            self.assertEqual(record['status'], 'running')
            facts = kwargs['pre_dispatch']
            self.assertEqual(facts, {
                'source': 'amr_goal.step', 'policy_version': '4', 'goal_id': 'fixture',
                'task_id': 'A', 'task_digest': amr.task_identity(task), 'content': before,
                'plan_digest': state['plan_digest'], 'attempt_id': record['attempt_id'],
                'route': record['route'],
                'execution_lock': {'owner': 'parent_coordinator', 'scope': 'checkout', 'held': True},
                'design_approval': {'required': False, 'status': 'not_applicable'},
            })
            self.assertEqual(facts['route']['support'], 'advertised')
            self.assertEqual((facts['route']['model'], facts['route']['effort']), (model, effort))
            self.assertEqual(kwargs['goal_context'], {
                'goal_id': 'fixture', 'objective': self.plan['objective'], 'milestone': 'M1',
                'task_id': 'A', 'depends_on': [],
            })
            with self.assertRaisesRegex(ValueError, 'another goal executor'):
                with goal.checkpoint(root, 'other'):
                    self.fail('parent lock was released during dispatch')
            return self.execution(root, task, model, effort, **kwargs)
        with self.boundaries(execution=inspect):
            goal.step(self.root, 'fixture')
            self.review_mock.assert_not_called()

    def test_architect_design_requires_exact_human_approval_after_sol_output(self):
        self.architect_task()
        goal.init(self.root, self.plan)
        with self.boundaries():
            result = goal.step(self.root, 'fixture')
            self.review_mock.assert_not_called()
            self.assertEqual(result['tasks']['A']['route']['model'], 'gpt-6.1-sol')
            self.assertEqual(result['tasks']['A']['route']['effort'], 'high')
            facts = self.execute_mock.call_args.kwargs['pre_dispatch']
            self.assertEqual(facts['design_approval'],
                             {'required': True, 'status': 'human_approval_after_output'})
            with self.assertRaisesRegex(ValueError, 'explicit human approval'):
                goal.accept(self.root, 'fixture', 'A', self.triage())
            stale = self.triage()
            stale['human_design_approval'] = self.design_approval(content='stale')
            with self.assertRaisesRegex(ValueError, 'explicit human approval'):
                goal.accept(self.root, 'fixture', 'A', stale)
            approved = self.triage()
            approved['human_design_approval'] = self.design_approval()
            completed = goal.accept(self.root, 'fixture', 'A', approved)
        self.assertEqual(completed['tasks']['A']['status'], 'completed')
        self.review_mock.assert_not_called()

    def test_legacy_design_evidence_is_rechecked_at_acceptance(self):
        corruptions = {
            'missing': None,
            'corrupt': {'turn_status': 'failed'},
            'wrong_task': {'task_id': 'different-task'},
            'stale_content': {'content': 'stale-design-input'},
        }
        for label, overrides in corruptions.items():
            with self.subTest(evidence=label):
                goal_id = 'legacy-'+label
                path = self.legacy_architect_checkpoint(goal_id=goal_id)
                state = self.state(goal_id)
                record = state['tasks']['A']
                if overrides is None:
                    record.pop('design_review')
                else:
                    review = record['design_review']
                    review.update(overrides)
                    if label == 'wrong_task':
                        review['triage']['task_id'] = 'different-task'
                path.write_text(json.dumps(state))
                with self.assertRaisesRegex(ValueError, 'legacy design-review evidence'):
                    goal.accept(self.root, goal_id, 'A', self.triage())

    def test_legacy_design_evidence_is_checked_before_revalidation_gates(self):
        path = self.legacy_architect_checkpoint()
        state = self.state()
        state['tasks']['A'].pop('design_review')
        path.write_text(json.dumps(state))
        with self.boundaries():
            result = goal.validate(self.root, 'fixture', 'A')
            self.assertEqual(result['tasks']['A']['status'], 'failed')
            self.review_mock.assert_not_called()

    def test_valid_legacy_design_migrates_and_completed_checkpoint_reloads(self):
        path = self.legacy_architect_checkpoint()
        with self.boundaries():
            goal.validate(self.root, 'fixture', 'A')
            result = goal.accept(self.root, 'fixture', 'A', self.triage())
            self.assertEqual(result['tasks']['A']['review_policy_version'], '4')
            self.review_mock.assert_not_called()
        loaded = goal.status(self.root, 'fixture')
        self.assertEqual(loaded['tasks']['A']['status'], 'completed')
        self.assertEqual(loaded['tasks']['A']['policy_version'], '3.1')

    def test_blocked_native_outcome_rejects_even_when_a_declared_file_changed(self):
        plan = copy.deepcopy(self.plan)
        plan['tasks'] = [plan['tasks'][0]]
        plan['tasks'][0]['write_paths'] = ['source.py']
        goal.init(self.root, plan)
        with self.boundaries(execution=lambda *args, **kwargs: self.execution(
                *args, outcome_status='blocked', **kwargs)):
            result = goal.step(self.root, 'fixture')
        self.assertEqual(result['tasks']['A']['status'], 'failed')
        self.assertIn('blocked', result['control']['pause']['reason'])

    def test_completed_transport_without_structured_outcome_cannot_reach_gates(self):
        goal.init(self.root, self.plan)
        gate_calls = []
        def no_outcome(*args, **kwargs):
            receipt = self.execution(*args, **kwargs)
            receipt.pop('task_outcome')
            return receipt
        def gates(root, base):
            gate_calls.append((root, base))
            return self.gates(root, base)
        with self.boundaries(execution=no_outcome, gates=gates):
            result = goal.step(self.root, 'fixture')
        self.assertEqual(result['tasks']['A']['status'], 'failed')
        self.assertEqual(gate_calls, [])

    def test_unexplained_noop_write_outcome_is_rejected(self):
        plan = copy.deepcopy(self.plan)
        plan['tasks'] = [plan['tasks'][0]]
        plan['tasks'][0]['write_paths'] = ['source.py']
        goal.init(self.root, plan)
        with self.boundaries(execution=lambda *args, **kwargs: self.execution(
                *args, mutate=False, **kwargs)):
            result = goal.step(self.root, 'fixture')
        self.assertEqual(result['tasks']['A']['status'], 'failed')
        self.assertIn('no-op', result['control']['pause']['reason'])

    def test_explained_noop_write_can_complete_with_inspection_evidence(self):
        plan = copy.deepcopy(self.plan)
        plan['tasks'] = [plan['tasks'][0]]
        plan['tasks'][0]['write_paths'] = ['source.py']
        goal.init(self.root, plan)
        with self.boundaries(execution=lambda *args, **kwargs: self.execution(
                *args, mutate=False,
                no_change_reason='Inspected the source; no modification was required.', **kwargs)):
            result = goal.step(self.root, 'fixture')
            self.assertEqual(result['tasks']['A']['status'], 'awaiting_triage')
            goal.accept(self.root, 'fixture', 'A', self.triage())
        self.assertEqual(goal.status(self.root, 'fixture')['tasks']['A']['status'], 'completed')

    def test_successful_write_persists_task_outcome_and_actual_artifact_evidence(self):
        plan = copy.deepcopy(self.plan)
        plan['tasks'] = [plan['tasks'][0]]
        plan['tasks'][0]['write_paths'] = ['source.py']
        goal.init(self.root, plan)
        with self.boundaries():
            result = goal.step(self.root, 'fixture')
            record = self.state()['tasks']['A']
            self.assertEqual(result['tasks']['A']['status'], 'awaiting_triage')
            self.assertEqual(record['task_outcome']['artifacts'], ['source.py'])
            self.assertEqual(record['task_outcome']['artifact_evidence']['source.py'],
                             amr.tree_identity(self.root)['files']['source.py'])
            goal.accept(self.root, 'fixture', 'A', self.triage())
        self.assertEqual(goal.status(self.root, 'fixture')['tasks']['A']['status'], 'completed')

    def test_read_only_inspection_can_complete_with_explicit_empty_artifacts(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            result = goal.step(self.root, 'fixture')
            self.assertEqual(result['tasks']['A']['status'], 'awaiting_triage')
            self.assertEqual(self.state()['tasks']['A']['task_outcome']['artifacts'], [])
            goal.accept(self.root, 'fixture', 'A', self.triage())
        self.assertEqual(goal.status(self.root, 'fixture')['tasks']['A']['status'], 'completed')

    def test_task_outcome_reload_rejects_stale_or_forged_bindings(self):
        plan = copy.deepcopy(self.plan)
        plan['tasks'] = [plan['tasks'][0]]
        plan['tasks'][0]['write_paths'] = ['source.py']
        goal.init(self.root, plan)
        with self.boundaries():
            goal.step(self.root, 'fixture')
            goal.accept(self.root, 'fixture', 'A', self.triage())
        original = self.state()
        record = original['tasks']['A']
        outcome = self.task_outcome_receipt(
            record, artifacts=['source.py'],
            artifact_evidence={'source.py': amr.tree_identity(self.root)['files']['source.py']})
        path = self.root/'.amr-local/goals/fixture.json'
        for field, value in (('task_id', 'forged-task'),
                             ('task_digest', 'forged-task-digest'),
                             ('input_content', 'stale-input-content'),
                             ('artifact_evidence', {'source.py': 'forged-artifact'})):
            corrupted = copy.deepcopy(original)
            corrupted['tasks']['A']['task_outcome'] = copy.deepcopy(outcome)
            corrupted['tasks']['A']['task_outcome'][field] = value
            path.write_text(json.dumps(corrupted))
            with self.subTest(field=field), self.assertRaisesRegex(
                    ValueError, 'completed checkpoint lacks'):
                goal.status(self.root, 'fixture')

    def test_completed_prefix_v4_checkpoint_without_outcome_remains_readable(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            goal.step(self.root, 'fixture')
            goal.accept(self.root, 'fixture', 'A', self.triage())
        path = self.root/'.amr-local/goals/fixture.json'
        state = self.state()
        state['tasks']['A'].pop('task_outcome', None)
        path.write_text(json.dumps(state))
        self.assertEqual(goal.status(self.root, 'fixture')['tasks']['A']['status'], 'completed')

    def test_critical_developer_task_uses_luna_and_separate_pr_review(self):
        self.plan['tasks'][0]['signals']=['auth']
        goal.init(self.root,self.plan)
        with self.boundaries(review=lambda *args, **kwargs: self.fail('PR review is a separate workflow')):
            result = goal.step(self.root, 'fixture')
            self.assertEqual(result['tasks']['A']['route']['model'], 'gpt-6-luna')
            self.assertFalse(goal.review_required(self.state(), 'A'))
            goal.accept(self.root, 'fixture', 'A', self.triage())
            self.review_mock.assert_not_called()

    def test_final_milestone_selection_uses_dependency_schedule_not_array_tail(self):
        self.plan['tasks'].reverse()  # B still depends on A, so B must finish last.
        goal.init(self.root,self.plan)
        state=self.state()
        for record in state['tasks'].values():record['policy_version']='3.1'
        self.assertFalse(goal.review_required(state,'A'))
        self.assertTrue(goal.review_required(state,'B'))

    def test_architect_session_returns_after_one_design_and_requires_human_approval(self):
        self.architect_task()
        goal.init(self.root,self.plan)
        with self.boundaries():
            with self.assertRaisesRegex(ValueError, 'does not consume AI design-review'):
                goal.step(self.root, 'fixture', {'status': 'verified'})
            self.execute_mock.assert_not_called()
            result=goal.step(self.root,'fixture')
            self.assertEqual(result['tasks']['A']['status'],'awaiting_triage')
            self.assertEqual(self.execute_mock.call_count, 1)
            self.review_mock.assert_not_called()
            self.assertEqual(result['next_task'], None)
            triage = self.triage()
            triage['human_design_approval'] = self.design_approval()
            goal.accept(self.root,'fixture','A',triage)

    def test_legacy_completed_receipts_keep_original_selection_and_review_obligations(self):
        goal.init(self.root,self.plan)
        with self.boundaries():
            goal.step(self.root,'fixture')
            goal.accept(self.root,'fixture','A',self.triage())
        state=self.state();record=state['tasks']['A'];record.pop('policy_version')
        record.pop('task_outcome', None)
        record['route'].update(model='gpt-6.1-sol',effort='medium')
        record['execution'].update(requested_model='gpt-6.1-sol',observed_model='gpt-6.1-sol',
                                   requested_effort='medium',observed_effort='medium')
        record['review']=dict(self.review(),content=record['gates']['content'],
                              independent=True,source_stable=True)
        path=self.root/'.amr-local/goals/fixture.json';path.write_text(json.dumps(state))
        self.assertEqual(goal.status(self.root,'fixture')['tasks']['A'],record)
        with self.boundaries():
            result=goal.step(self.root,'fixture')
            self.assertEqual(result['tasks']['B']['route']['model'],'gpt-6-luna')
            self.assertEqual(result['tasks']['B']['route']['effort'],'max')
            self.assertEqual(result['tasks']['B']['policy_version'],'4')
        state=self.state();state['tasks']['A']['review']['status']='not_required'
        path.write_text(json.dumps(state))
        with self.assertRaisesRegex(ValueError,'completed checkpoint lacks'):goal.status(self.root,'fixture')

    def test_human_design_approval_cannot_be_reused_for_a_different_task_definition(self):
        self.architect_task()
        goal.init(self.root,self.plan)
        with self.boundaries():
            goal.step(self.root, 'fixture')
        approval = self.design_approval()
        other=copy.deepcopy(self.plan);other['goal_id']='other'
        other['tasks'][0]['objective']='A different architectural decision'
        goal.init(self.root,other)
        with self.boundaries():
            goal.step(self.root, 'other')
            triage = self.triage(); triage['human_design_approval'] = approval
            with self.assertRaisesRegex(ValueError, 'explicit human approval'):
                goal.accept(self.root, 'other', 'A', triage)

    def test_v4_blocks_without_luna_then_switches_from_sol_when_luna_is_available(self):
        goal.init(self.root,self.plan)
        with self.boundaries(capabilities=lambda root:{'models':[{'model':'gpt-6.1-sol','efforts':['high']}]}):
            blocked = goal.recommend(self.root,'fixture','gpt-6.1-sol','high')
            self.assertEqual(blocked['status'], 'blocked')
            self.execute_mock.assert_not_called()
            self.assertEqual(self.state()['tasks']['A']['attempts'],0)
        with self.boundaries():
            goal.resume(self.root, 'fixture', resolution_evidence='Luna Max catalog entry available')
            result=goal.recommend(self.root,'fixture','gpt-6.1-sol','high')
            self.assertEqual(result['status'],'awaiting_model_switch')

    def test_architect_approval_binds_to_dispatch_normalization_after_owner_metadata_change(self):
        task = self.architect_task()
        task.update(spec_verified=True, tests_known=True)
        goal.init(self.root,self.plan)
        feature=self.root/'feature.md'
        feature.write_text(feature.read_text().replace('status: implemented','status: proposed'))
        normalized=amr.validate_task(copy.deepcopy(self.state()['plan']['tasks'][0]),self.root)
        self.assertIs(normalized['spec_verified'],False)
        with self.boundaries():
            result=goal.step(self.root,'fixture')
            self.assertEqual(result['tasks']['A']['status'],'awaiting_triage')
            triage = self.triage()
            triage['human_design_approval'] = self.design_approval(task=normalized)
            goal.accept(self.root,'fixture','A',triage)
            self.review_mock.assert_not_called()
        state=goal.status(self.root,'fixture')
        self.assertEqual(state['tasks']['A']['status'],'completed')
        self.assertIs(state['tasks']['A']['dispatch_task']['spec_verified'],False)

    def test_developer_recovery_preserves_dispatch_tests_known_downgrade(self):
        self.plan['tasks'][0].update(kind='source',paths=['source.py'],tests_known=True)
        self.plan['tasks'][1]['paths']=['source.py']
        goal.init(self.root,self.plan)
        feature=self.root/'feature.md'
        feature.write_text(feature.read_text().replace('test_paths: ["test_source.py"]','test_paths: []'))
        normalized=amr.validate_task(copy.deepcopy(self.state()['plan']['tasks'][0]),self.root)
        self.assertIs(normalized['tests_known'],False)
        with self.boundaries():
            goal.step(self.root,'fixture')
            goal.validate(self.root,'fixture','A')
            goal.accept(self.root,'fixture','A',self.triage())
        self.assertEqual(goal.status(self.root,'fixture')['tasks']['A']['status'],'completed')

    def test_dispatch_snapshot_cannot_change_plan_or_promote_source_claims(self):
        self.plan['tasks'][0]['spec_verified']=False
        goal.init(self.root,self.plan)
        with self.boundaries():
            goal.step(self.root,'fixture')
            goal.accept(self.root,'fixture','A',self.triage())
        original=self.state();path=self.root/'.amr-local/goals/fixture.json'
        for override in ({'objective':'unreviewed work'},{'scope':'production'},
                         {'spec_verified':True},{'extra':'unplanned'}):
            state=copy.deepcopy(original);record=state['tasks']['A']
            record['dispatch_task'].update(override)
            record['dispatch_task_digest']=goal.digest(record['dispatch_task'])
            path.write_text(json.dumps(state))
            with self.subTest(override=override),self.assertRaisesRegex(ValueError,'dispatch task differs'):
                goal.status(self.root,'fixture')

    def test_unknown_execution_policy_cannot_be_loaded(self):
        goal.init(self.root,self.plan)
        state=self.state();state['tasks']['A']['policy_version']='future'
        (self.root/'.amr-local/goals/fixture.json').write_text(json.dumps(state))
        with self.assertRaisesRegex(ValueError,'policy version'):goal.status(self.root,'fixture')

    def test_v31_completed_sol_retention_checkpoint_remains_readable_and_bound(self):
        goal.init(self.root,self.plan)
        with self.boundaries():
            goal.step(self.root,'fixture')
            goal.accept(self.root,'fixture','A',self.triage())
        state=self.state()
        record=state['tasks']['A']
        record['policy_version']='3.1'
        record['route'].update(model='gpt-6.1-sol',effort='high',retained_selection={
            'reason':'preferred_unavailable','preferred_model':'gpt-6-luna','preferred_effort':'max'})
        record['execution'].update(requested_model='gpt-6.1-sol',observed_model='gpt-6.1-sol',
                                   requested_effort='high',observed_effort='high')
        path=self.root/'.amr-local/goals/fixture.json'
        path.write_text(json.dumps(state))
        self.assertEqual(goal.status(self.root,'fixture')['tasks']['A'],record)
        state['tasks']['A']['route'].pop('retained_selection')
        path.write_text(json.dumps(state))
        with self.assertRaisesRegex(ValueError,'completed checkpoint lacks'):goal.status(self.root,'fixture')

    def test_old_checkpoint_without_control_is_read_as_active_without_replay(self):
        goal.init(self.root, self.plan)
        path = self.root/'.amr-local/goals/fixture.json'
        state = self.state()
        state.pop('control')
        path.write_text(json.dumps(state))
        result = goal.status(self.root, 'fixture')
        self.assertEqual(result['status'], 'active')
        self.assertIn('control', self.state())
        self.assertEqual(self.state()['tasks']['A']['attempts'], 0)

    def test_historical_v31_architect_checkpoint_keeps_its_pinned_source_paths(self):
        goal.init(self.root, self.plan)
        path = self.root/'.amr-local/goals/fixture.json'
        state = self.state()
        task = state['plan']['tasks'][0]
        task.update(kind='architecture', paths=['feature.md', 'source.py'],
                    write_paths=['feature.md'])
        state['plan_digest'] = goal.digest(state['plan'])
        state['tasks']['A']['policy_version'] = '3.1'
        before = json.dumps(state, indent=2, ensure_ascii=False).encode()+b'\n'
        path.write_bytes(before)

        loaded = goal.load(self.root, path)

        self.assertEqual(loaded['tasks']['A']['policy_version'], '3.1')
        self.assertEqual(loaded['plan']['tasks'][0]['paths'], ['feature.md', 'source.py'])
        self.assertEqual(loaded['tasks']['A']['status'], 'pending')
        self.assertEqual(path.read_bytes(), before)

    def test_forged_completed_control_state_cannot_claim_pending_goal_complete(self):
        goal.init(self.root, self.plan)
        path = self.root/'.amr-local/goals/fixture.json'
        state = self.state()
        state['control']['status'] = 'completed'
        path.write_text(json.dumps(state))
        with self.assertRaisesRegex(ValueError, 'unfinished tasks'):
            goal.status(self.root, 'fixture')

    def test_routine_task_keeps_current_luna_max_without_interrupt(self):
        self.plan['tasks'][0].update(kind='docs', bounded=True, spec_verified=True, tests_known=True)
        goal.init(self.root, self.plan)
        with self.boundaries():
            result = goal.recommend(self.root, 'fixture', 'gpt-6-luna', 'max')
        self.assertEqual(result['status'], 'active')
        self.assertEqual(result['recommendation']['action'], 'continue')
        self.assertEqual(result['recommendation']['recommended'], {'model':'gpt-6-luna','effort':'max'})

    def test_unavailable_luna_max_blocks_developer_without_sol_fallback(self):
        goal.init(self.root, self.plan)
        with self.boundaries(capabilities=lambda root: {
            'models': [{'model': 'gpt-6.1-sol', 'efforts': ['high']}]}):
            result = goal.recommend(self.root, 'fixture', 'gpt-6.1-sol', 'high')
        self.assertEqual(result['status'], 'blocked')
        self.assertEqual(result['recommendation']['action'], 'blocked')
        self.assertEqual(result['recommendation']['route']['model'], 'gpt-6-luna')
        self.assertEqual(result['recommendation']['route']['support'], 'unsupported')
        self.assertEqual(self.state()['tasks']['A']['attempts'], 0)

    def test_even_short_standard_task_returns_from_high_to_luna_at_safe_boundary(self):
        self.plan['tasks'] = [self.plan['tasks'][0]]
        goal.init(self.root, self.plan)
        with self.boundaries():
            recommendation = goal.recommend(self.root, 'fixture', 'gpt-6.1-sol', 'high')
            self.assertEqual(recommendation['recommendation']['action'], 'awaiting_model_switch')
            with self.assertRaisesRegex(ValueError, 'awaiting_model_switch'):
                goal.step(self.root, 'fixture')
            goal.resume(self.root, 'fixture', model='gpt-6-luna', effort='max')
            result = goal.step(self.root, 'fixture')
        self.assertEqual(result['tasks']['A']['route']['model'], 'gpt-6-luna')
        self.assertEqual(result['tasks']['A']['route']['effort'], 'max')
        self.assertEqual(result['tasks']['A']['status'], 'awaiting_triage')

    def test_unavailable_critical_target_still_blocks_luna_max(self):
        self.plan['tasks'][0].update(kind='complex-debugging', signals=['concurrency'],
            failed_repairs=2, failure_evidence='two failed Luna fixes with reproduction',
            reported_defect={'finding_id':'BUG-CONC-1','summary':'concurrent update lost',
                'evidence':'repeatable concurrent test failure','affected_paths':['source.py']})
        goal.init(self.root, self.plan)
        with self.boundaries(capabilities=lambda root: {
            'models': [{'model': 'gpt-6-luna', 'efforts': ['max']}]}):
            result = goal.recommend(self.root, 'fixture', 'gpt-6-luna', 'max')
        self.assertEqual(result['status'], 'blocked')
        self.assertEqual(result['recommendation']['action'], 'blocked')
        self.assertEqual(self.execute_mock.call_count, 0)

    def test_luna_high_cannot_satisfy_luna_max_floor(self):
        goal.init(self.root, self.plan)
        with self.boundaries(capabilities=lambda root: {
            'models': [{'model': 'gpt-6-luna', 'efforts': ['high']}]}):
            with self.assertRaisesRegex(ValueError, 'not an advertised Kordra AMR profile'):
                goal.recommend(self.root, 'fixture', 'gpt-6-luna', 'high')
            self.assertEqual(self.execute_mock.call_count, 0)

    def test_luna_to_luna_continues_and_manual_selection_remains_unverified(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            result = goal.recommend(self.root, 'fixture', 'gpt-6-luna', 'max')
        self.assertEqual(result['status'], 'active')
        self.assertEqual(result['recommendation']['action'], 'continue')
        self.assertEqual(result['recommendation']['selection_verification'], 'unverified')
        self.assertIsNone(result['control']['model_switch'])
        self.assertEqual(result['control']['runtime_selection']['status'], 'unverified')
        self.assertEqual(result['native_goal_control'], 'not_available_in_local_cli')
        self.assertEqual(self.execute_mock.call_count, 0)

    def test_luna_to_high_persists_switch_checkpoint_and_blocks_dispatch(self):
        self.plan['tasks'][0].update(kind='complex-debugging', signals=['concurrency'],
            failed_repairs=2, failure_evidence='two failed Luna fixes with reproduction',
            reported_defect={'finding_id':'BUG-CONC-1','summary':'concurrent update lost',
                'evidence':'repeatable concurrent test failure','affected_paths':['source.py']})
        goal.init(self.root, self.plan)
        with self.boundaries():
            result = goal.recommend(self.root, 'fixture', 'gpt-6-luna', 'max')
            with self.assertRaisesRegex(ValueError, 'awaiting_model_switch'):
                goal.step(self.root, 'fixture')
        self.assertEqual(result['status'], 'awaiting_model_switch')
        self.assertEqual(result['recommendation']['recommended'], {'model':'gpt-6.1-sol','effort':'high'})
        self.assertEqual(self.state()['control']['model_switch']['task_id'], 'A')
        self.assertEqual(self.state()['control']['model_switch']['current_selection_verification'], 'unverified')
        self.assertIn('Manually select', self.state()['control']['model_switch']['next_action'])
        self.assertEqual(self.execute_mock.call_count, 0)

    def test_legacy_medium_session_is_recommended_luna_max_not_silently_reused(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            result = goal.recommend(self.root, 'fixture', 'gpt-6.1-sol', 'medium')
        self.assertEqual(result['status'], 'awaiting_model_switch')
        self.assertEqual(result['recommendation']['recommended'], {'model':'gpt-6-luna','effort':'max'})

    def test_manual_resume_is_explicit_and_never_claims_runtime_selection_verified(self):
        plan = copy.deepcopy(self.plan)
        plan['tasks'][0].update(kind='complex-debugging', signals=['concurrency'],
            failed_repairs=2, failure_evidence='two failed Luna fixes with reproduction',
            reported_defect={'finding_id':'BUG-CONC-1','summary':'concurrent update lost',
                'evidence':'repeatable concurrent test failure','affected_paths':['source.py']})
        goal.init(self.root, plan)
        with self.boundaries():
            goal.recommend(self.root, 'fixture', 'gpt-6.1-sol', 'medium')
            with self.assertRaisesRegex(ValueError, 'does not match the recommendation'):
                goal.resume(self.root, 'fixture', model='gpt-6.1-sol', effort='medium')
            resumed = goal.resume(self.root, 'fixture', model='gpt-6.1-sol', effort='high')
        self.assertEqual(resumed['status'], 'active')
        self.assertEqual(resumed['selection_verification'], 'unverified')
        self.assertEqual(resumed['control']['runtime_selection']['source'], 'manual_switch_claim')
        self.assertEqual(len(resumed['control']['switch_history']), 1)
        self.assertIn('Manually select', resumed['control']['switch_history'][0]['next_action'])
        self.assertEqual(resumed['native_goal_resume'], 'coordinator_must_resume_native_goal_separately')

    def test_switch_resume_rejects_source_or_git_revision_change(self):
        plan = copy.deepcopy(self.plan)
        plan['tasks'][0].update(kind='complex-debugging', signals=['concurrency'],
            failed_repairs=2, failure_evidence='two failed Luna fixes with reproduction',
            reported_defect={'finding_id':'BUG-CONC-1','summary':'concurrent update lost',
                'evidence':'repeatable concurrent test failure','affected_paths':['source.py']})
        goal.init(self.root, plan)
        with self.boundaries():
            goal.recommend(self.root, 'fixture', 'gpt-6.1-sol', 'medium')
            (self.root/'source.py').write_text('value = 9\n')
            with self.assertRaisesRegex(ValueError, 'working tree changed'):
                goal.resume(self.root, 'fixture', model='gpt-6.1-sol', effort='high')
        self.assertEqual(self.state()['control']['status'], 'awaiting_model_switch')

    def test_approval_and_blocked_are_distinct_and_require_separate_evidence(self):
        goal.init(self.root, self.plan)
        approval = goal.pause(self.root, 'fixture', 'awaiting_approval', 'ADR decision required')
        self.assertEqual(approval['status'], 'awaiting_approval')
        with self.assertRaisesRegex(ValueError, 'approval evidence reference'):
            goal.resume(self.root, 'fixture')
        resumed = goal.resume(self.root, 'fixture', approval_evidence='ADR-0009 accepted by owner')
        self.assertEqual(resumed['status'], 'active')
        self.assertEqual(resumed['control']['pause_history'][-1]['evidence_status'], 'referenced')

        blocked = goal.pause(self.root, 'fixture', 'blocked', 'SDK dependency is missing')
        self.assertEqual(blocked['status'], 'blocked')
        with self.assertRaisesRegex(ValueError, 'resolution evidence'):
            goal.resume(self.root, 'fixture')
        unblocked = goal.resume(self.root, 'fixture', resolution_evidence='SDK installed and verified')
        self.assertEqual(unblocked['status'], 'active')
        self.assertEqual(unblocked['resumed_from'], 'blocked')

    def test_unsupported_native_pause_remains_local_and_does_not_claim_goal_control(self):
        goal.init(self.root, self.plan)
        result = goal.pause(self.root, 'fixture', 'awaiting_approval', 'Human decision needed')
        self.assertEqual(result['native_goal_control'], 'not_available_in_local_cli')
        self.assertIn('native GOAL control', result['limitation'])
        self.assertEqual(result['status'], 'awaiting_approval')

    def test_dag_rejects_cycles_duplicates_missing_and_invalid_dependencies(self):
        variants = []
        p = copy.deepcopy(self.plan); p['tasks'][0]['depends_on'] = ['B']; variants.append(p)
        p = copy.deepcopy(self.plan); p['tasks'][1]['task_id'] = 'A'; variants.append(p)
        p = copy.deepcopy(self.plan); p['tasks'][1]['depends_on'] = ['missing']; variants.append(p)
        p = copy.deepcopy(self.plan); p['tasks'][1]['depends_on'] = ['A', 'A']; variants.append(p)
        p = copy.deepcopy(self.plan); p['tasks'][0]['task_id'] = '../escape'; variants.append(p)
        p = copy.deepcopy(self.plan); p['tasks'][0]['depends_on'] = 'A'; variants.append(p)
        for plan in variants:
            with self.subTest(tasks=plan['tasks']), self.assertRaises(ValueError):
                goal.validate_plan(self.root, plan)

    def test_untrusted_objective_drift_blocks_reuse(self):
        goal.init(self.root, self.plan)
        (self.root/'GOAL.md').write_text('Different objective\n')
        with self.assertRaisesRegex(ValueError, 'objective changed'):
            goal.status(self.root, 'fixture')

    def test_exact_model_unavailable_never_executes_or_advances(self):
        goal.init(self.root, self.plan)
        with self.boundaries(capabilities=lambda root: {'models': [{'model': 'gpt-6-sol', 'efforts': ['medium']}] }):
            result = goal.step(self.root, 'fixture')
            self.execute_mock.assert_not_called()
        self.assertEqual(result['tasks']['A']['status'], 'failed')
        self.assertIsNone(result['next_task'])

    def test_callback_crash_preserves_unknown_without_replay_or_dependency_advance(self):
        goal.init(self.root, self.plan)
        def crash(root, task, model, effort, **kwargs):
            kwargs['on_start']('accepted-before-crash')
            raise RuntimeError('executor process died')
        with self.boundaries(execution=crash):
            with self.assertRaisesRegex(RuntimeError, 'process died'):
                goal.step(self.root, 'fixture')
            self.assertEqual(self.state()['tasks']['A']['native_thread_id'], 'accepted-before-crash')
            result = goal.status(self.root, 'fixture')
            self.assertEqual(result['tasks']['A']['status'], 'unknown')
            self.assertIsNone(result['next_task'])
            with self.assertRaisesRegex(ValueError, 'goal is blocked'):
                goal.step(self.root, 'fixture')
            self.assertEqual(self.execute_mock.call_count, 1)

    def test_source_scope_violation_blocks_gates_and_review(self):
        goal.init(self.root, self.plan)
        def changed(*args, **kwargs):
            receipt = self.execution(*args, **kwargs)
            (self.root/'source.py').write_text('value = 2\n')
            return receipt
        with self.boundaries(execution=changed):
            result = goal.step(self.root, 'fixture')
            self.review_mock.assert_not_called()
        self.assertEqual(result['tasks']['A']['status'], 'failed')
        self.assertEqual(result['tasks']['A']['files_changed'], ['source.py'])

    def test_failed_gates_block_progress_and_validation_never_reexecutes(self):
        goal.init(self.root, self.plan)
        def failed(root, base):
            result = self.gates(root, base); result['local_status'] = 'failed'; return result
        with self.boundaries(gates=failed):
            result = goal.step(self.root, 'fixture')
            self.assertEqual(result['tasks']['A']['status'], 'validation_failed')
            self.assertIsNone(result['next_task'])
            self.review_mock.assert_not_called()
            goal.validate(self.root, 'fixture', 'A')
            self.assertEqual(self.execute_mock.call_count, 1)

    def test_validate_rejects_source_drift_outside_write_paths_after_execution(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            result = goal.step(self.root, 'fixture')
            self.assertEqual(result['tasks']['A']['status'], 'awaiting_triage')
            review_count = self.review_mock.call_count
            (self.root/'source.py').write_text('value = 7\n')
            result = goal.validate(self.root, 'fixture', 'A')
            self.assertEqual(self.review_mock.call_count, review_count)
            with self.assertRaisesRegex(ValueError, 'task not reviewed or source/Git changed'):
                goal.accept(self.root, 'fixture', 'A', self.triage())
        self.assertEqual(result['tasks']['A']['status'], 'failed')
        self.assertEqual(result['status'], 'blocked')
        self.assertIn('outside declared write paths', result['control']['pause']['reason'])

    def test_revalidation_allows_repairs_within_declared_write_paths(self):
        plan = copy.deepcopy(self.plan)
        plan['tasks'][0]['write_paths'] = ['source.py']
        goal.init(self.root, plan)
        calls = {'count': 0}
        def first_gate_fails(root, base):
            calls['count'] += 1
            result = self.gates(root, base)
            if calls['count'] == 1:
                result['local_status'] = 'failed'
            return result
        with self.boundaries(gates=first_gate_fails):
            result = goal.step(self.root, 'fixture')
            self.assertEqual(result['tasks']['A']['status'], 'validation_failed')
            (self.root/'source.py').write_text('value = 2\n')
            result = goal.validate(self.root, 'fixture', 'A')
        self.assertEqual(result['tasks']['A']['status'], 'awaiting_triage')
        self.assertEqual(self.execute_mock.call_count, 1)

    def test_declared_write_path_deletion_survives_checkpoint_reload(self):
        plan = copy.deepcopy(self.plan)
        plan['tasks'] = [plan['tasks'][0]]
        plan['tasks'][0]['write_paths'] = ['source.py']
        goal.init(self.root, plan)
        def delete_source(root, task, model, effort, **kwargs):
            receipt = self.execution(root, task, model, effort, **kwargs)
            (self.root/'source.py').unlink()
            return receipt
        with self.boundaries(execution=delete_source):
            result = goal.step(self.root, 'fixture')
            self.assertEqual(result['tasks']['A']['status'], 'awaiting_triage')
            self.assertFalse((self.root/'source.py').exists())
            reloaded = goal.status(self.root, 'fixture')
            self.assertEqual(reloaded['tasks']['A']['status'], 'awaiting_triage')
            accepted = goal.accept(self.root, 'fixture', 'A', self.triage())
        self.assertEqual(accepted['status'], 'completed')

    def test_coordinator_only_edits_cannot_validate_or_accept_without_execution_receipt(self):
        goal.init(self.root, self.plan)
        (self.root/'source.py').write_text('value = 8\n')
        with self.boundaries():
            with self.assertRaisesRegex(ValueError, 'task is not awaiting revalidation'):
                goal.validate(self.root, 'fixture', 'A')
            with self.assertRaisesRegex(ValueError, 'task not reviewed or source/Git changed'):
                goal.accept(self.root, 'fixture', 'A', self.triage())
            self.execute_mock.assert_not_called()
            self.review_mock.assert_not_called()
        self.assertEqual(goal.status(self.root, 'fixture')['tasks']['A']['status'], 'pending')

    def test_terminal_model_mismatch_is_unknown_not_success(self):
        goal.init(self.root, self.plan)
        def mismatch(*args, **kwargs):
            result = self.execution(*args, **kwargs); result['observed_model'] = 'gpt-6-sol'; return result
        with self.boundaries(execution=mismatch):
            result = goal.step(self.root, 'fixture')
            self.review_mock.assert_not_called()
        self.assertEqual(result['tasks']['A']['status'], 'unknown')
        self.assertIsNone(result['next_task'])

    def test_execution_receipts_cannot_reuse_probe_other_task_or_thread(self):
        for index, override in enumerate([
            {'execution': 'probe'}, {'task_id': 'unrelated-task'},
            {'thread_id': 'unrelated-thread'}, {'turn_id': ''},
            {'observed_effort': 'high'}, {'rerouted': True},
        ]):
            plan = copy.deepcopy(self.plan); plan['goal_id'] = 'receipt-'+str(index)
            goal.init(self.root, plan)
            def invalid(*args, **kwargs):
                receipt = self.execution(*args, **kwargs); receipt.update(override); return receipt
            with self.subTest(override=override), self.boundaries(execution=invalid):
                result = goal.step(self.root, plan['goal_id'])
                self.assertEqual(result['tasks']['A']['status'], 'unknown')
                self.review_mock.assert_not_called()
                self.assertIsNone(result['next_task'])

    def test_local_sol_review_receipt_cannot_substitute_for_pr_review_evidence(self):
        goal.init(self.root, self.plan)
        with self.boundaries(review=lambda *args, **kwargs: self.fail('v4 does not invoke local AI review')):
            result = goal.step(self.root, 'fixture')
            self.assertEqual(result['tasks']['A']['status'], 'awaiting_triage')
            state = self.state()
            state['tasks']['A']['review'] = self.review()
            (self.root/'.amr-local/goals/fixture.json').write_text(json.dumps(state))
            with self.assertRaisesRegex(ValueError, 'verified native review identity'):
                goal.accept(self.root, 'fixture', 'A', self.triage())

    def test_native_execution_commit_cannot_escape_uncommitted_review(self):
        goal.init(self.root, self.plan)
        def commits(*args, **kwargs):
            receipt = self.execution(*args, **kwargs)
            self.git('commit', '--allow-empty', '-qm', 'Unexpected executor commit')
            return receipt
        with self.boundaries(execution=commits):
            result = goal.step(self.root, 'fixture')
            self.review_mock.assert_not_called()
        self.assertNotEqual(result['tasks']['A']['status'], 'awaiting_triage')
        self.assertIsNone(result['next_task'])

    def test_checkpoint_invalid_states_and_counters_are_rejected(self):
        goal.init(self.root, self.plan)
        path = self.root/'.amr-local/goals/fixture.json'
        original = self.state()
        for field, value in [('status', 'made-up'), ('attempts', -1),
                             ('attempts', True), ('validation_attempts', '0')]:
            corrupted = copy.deepcopy(original)
            corrupted['tasks']['A'][field] = value
            path.write_text(json.dumps(corrupted))
            with self.subTest(field=field, value=value), self.assertRaises(ValueError):
                goal.status(self.root, 'fixture')

    def test_forged_completed_pending_checkpoint_cannot_advance_dependency(self):
        goal.init(self.root, self.plan)
        path = self.root/'.amr-local/goals/fixture.json'
        forged = self.state()
        forged['tasks']['A']['status'] = 'completed'
        path.write_text(json.dumps(forged))
        with self.boundaries():
            with self.assertRaisesRegex(ValueError, 'completed checkpoint lacks'):
                goal.status(self.root, 'fixture')
            with self.assertRaisesRegex(ValueError, 'completed checkpoint lacks'):
                goal.step(self.root, 'fixture')
            self.execute_mock.assert_not_called()

    def test_completed_checkpoint_cannot_rebind_task_or_drop_terminal_evidence(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            goal.step(self.root, 'fixture')
            goal.accept(self.root, 'fixture', 'A', self.triage())
        path = self.root/'.amr-local/goals/fixture.json'
        original = self.state()
        for mutation in ('record_task', 'execution_task', 'review', 'gates', 'triage'):
            corrupted = copy.deepcopy(original)
            record = corrupted['tasks']['A']
            if mutation == 'record_task':
                record['task_id'] = 'B'
            elif mutation == 'execution_task':
                record['execution']['task_id'] = 'B'
            else:
                record.pop(mutation)
            path.write_text(json.dumps(corrupted))
            with self.subTest(mutation=mutation), self.assertRaises(ValueError):
                goal.status(self.root, 'fixture')

    def test_historical_completed_receipt_allows_next_task_after_unrelated_progress_change(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            goal.step(self.root, 'fixture')
            goal.accept(self.root, 'fixture', 'A', self.triage())
            historical = copy.deepcopy(self.state()['tasks']['A'])
            (self.root/'progress.md').write_text('Milestone one progress updated\n')
            resumed = goal.status(self.root, 'fixture')
            self.assertEqual(resumed['next_task'], 'B')
            self.assertEqual(resumed['tasks']['A'], historical)
            result = goal.step(self.root, 'fixture')
            self.assertEqual(result['tasks']['B']['status'], 'awaiting_triage')
            self.assertEqual(self.execute_mock.call_count, 2)
            self.assertEqual(self.execute_mock.call_args.args[1]['task_id'], 'B')
            triage = self.triage(); triage['task_id'] = 'B'
            goal.accept(self.root, 'fixture', 'B', triage)
            complete = goal.status(self.root, 'fixture')
        self.assertEqual(complete['status'], 'completed')
        self.assertIsNone(complete['next_task'])
        self.assertEqual(complete['tasks']['A'], historical)

    def test_completed_dependency_source_or_consulted_feature_drift_blocks_dispatch(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            goal.step(self.root, 'fixture')
            goal.accept(self.root, 'fixture', 'A', self.triage())
            for relative in ('source.py', 'feature.md'):
                path = self.root/relative
                original = path.read_text()
                path.write_text(original+'\nChanged dependency context\n')
                with self.subTest(relative=relative), self.assertRaisesRegex(ValueError, 'dependency inputs changed'):
                    goal.step(self.root, 'fixture')
                path.write_text(original)
            self.assertEqual(self.execute_mock.call_count, 1)
        self.assertEqual(self.state()['tasks']['B']['attempts'], 0)

    def test_completed_route_cannot_null_or_mismatch_profile_model_effort(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            goal.step(self.root, 'fixture')
            goal.accept(self.root, 'fixture', 'A', self.triage())
        path = self.root/'.amr-local/goals/fixture.json'
        original = self.state()
        variants = [dict(profile=None, model=None, effort=None),
                    dict(profile='CRITICAL'), dict(model='gpt-6-sol'), dict(effort='high')]
        for override in variants:
            corrupted = copy.deepcopy(original)
            corrupted['tasks']['A']['route'].update(override)
            path.write_text(json.dumps(corrupted))
            with self.subTest(override=override), self.assertRaisesRegex(ValueError, 'completed checkpoint lacks'):
                goal.status(self.root, 'fixture')

    def test_new_product_solution_does_not_retroactively_invalidate_historical_completion(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            goal.step(self.root, 'fixture')
            goal.accept(self.root, 'fixture', 'A', self.triage())
        historical = copy.deepcopy(self.state()['tasks']['A'])
        self.assertFalse(historical['product_required'])
        self.assertTrue(historical['gates']['product_tests'].startswith('not_configured'))
        (self.root/'Kordra.slnx').write_text('<Solution />\n')
        result = goal.status(self.root, 'fixture')
        self.assertEqual(result['tasks']['A'], historical)
        self.assertEqual(result['next_task'], 'B')

    def test_completed_product_requirement_missing_forged_or_inconsistent_blocks(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            goal.step(self.root, 'fixture')
            goal.accept(self.root, 'fixture', 'A', self.triage())
        path = self.root/'.amr-local/goals/fixture.json'
        original = self.state()
        for mutation in ('missing', 'true', 'string', 'executed_without_checks'):
            corrupted = copy.deepcopy(original)
            record = corrupted['tasks']['A']
            if mutation == 'missing':
                record.pop('product_required')
            elif mutation == 'true':
                record['product_required'] = True
            elif mutation == 'string':
                record['product_required'] = 'false'
            else:
                record['product_required'] = True
                record['gates']['product_tests'] = 'executed'
            path.write_text(json.dumps(corrupted))
            with self.subTest(mutation=mutation), self.assertRaisesRegex(ValueError, 'completed checkpoint lacks'):
                goal.status(self.root, 'fixture')

    def test_exact_content_contributor_triage_and_blocking_findings_are_required(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            goal.step(self.root, 'fixture')
            for key in ('reviewed_by', 'rationale', 'findings'):
                invalid = self.triage(); invalid.pop(key)
                with self.subTest(key=key), self.assertRaises(ValueError):
                    goal.accept(self.root, 'fixture', 'A', invalid)
            invalid = self.triage()
            invalid['findings'] = [dict(id='F1', state='open', blocking=True, evidence='current source reproduction')]
            with self.assertRaisesRegex(ValueError, 'blocking'):
                goal.accept(self.root, 'fixture', 'A', invalid)
        self.assertEqual(self.state()['tasks']['A']['status'], 'awaiting_triage')

    def test_v4_critical_developer_validation_does_not_invoke_native_reviewer(self):
        self.plan['tasks'][0]['signals'] = ['auth']
        goal.init(self.root, self.plan)
        with self.boundaries(review=lambda *args, **kwargs: self.fail('GOAL must not invoke an AI reviewer')):
            goal.step(self.root, 'fixture')
            goal.accept(self.root, 'fixture', 'A', self.triage())
            self.review_mock.assert_not_called()

    def test_historical_v31_critical_completion_still_requires_exact_native_review(self):
        self.plan['tasks'][0]['signals'] = ['auth']
        goal.init(self.root, self.plan)
        with self.boundaries():
            result = goal.step(self.root, 'fixture')
            goal.accept(self.root, 'fixture', 'A', self.triage())
        state = self.state()
        record = state['tasks']['A']
        record['policy_version'] = '3.1'
        record['route'].update(profile='CRITICAL', model='gpt-6.1-sol', effort='high')
        record['execution'].update(requested_model='gpt-6.1-sol', observed_model='gpt-6.1-sol',
                                   requested_effort='high', observed_effort='high')
        valid = self.review(); valid.update(content=record['gates']['content'],
            source_stable=True, independent=True)
        record['review'] = valid
        record.pop('task_outcome', None)
        path = self.root/'.amr-local/goals/fixture.json'
        path.write_text(json.dumps(state))
        self.assertEqual(goal.status(self.root, 'fixture')['tasks']['A']['status'], 'completed')
        for override in ({'channel':'self_check'}, {'observed_model':'gpt-6-sol'},
                         {'observed_effort':'medium'}, {'thread_id':''}, {'turn_id':''},
                         {'turn_status':'failed'}, {'rerouted':True}, {'review_output':[]}):
            corrupted = copy.deepcopy(state)
            corrupted['tasks']['A']['review'].update(override)
            path.write_text(json.dumps(corrupted))
            with self.subTest(override=override), self.assertRaisesRegex(
                    ValueError, 'completed checkpoint lacks'):
                goal.status(self.root, 'fixture')

    def test_unfinished_v31_task_migrates_review_obligation_without_reinvoking_sol(self):
        self.plan['tasks'][0]['signals'] = ['auth']
        goal.init(self.root, self.plan)
        with self.boundaries(review=lambda *args, **kwargs: self.fail('legacy review must not repeat')):
            goal.step(self.root, 'fixture')
            state = self.state()
            state['tasks']['A']['policy_version'] = '3.1'
            (self.root/'.amr-local/goals/fixture.json').write_text(json.dumps(state))
            goal.validate(self.root, 'fixture', 'A')
            triage = self.triage()
            goal.accept(self.root, 'fixture', 'A', triage)
            self.review_mock.assert_not_called()
        record = goal.status(self.root, 'fixture')['tasks']['A']
        self.assertEqual(record['policy_version'], '3.1')
        self.assertEqual(record['review_policy_version'], '4')
        self.assertEqual(record['review_policy_migration']['from'], '3.1')

    def test_stale_content_accept_rejected_even_with_fresh_triage(self):
        goal.init(self.root, self.plan)
        with self.boundaries():
            goal.step(self.root, 'fixture')
            (self.root/'source.py').write_text('value = 3\n')
            with self.assertRaisesRegex(ValueError, 'source/Git changed'):
                goal.accept(self.root, 'fixture', 'A', self.triage())

    def test_executor_lock_covers_all_goal_ids(self):
        goal.init(self.root, self.plan)
        with goal.checkpoint(self.root, 'fixture'):
            with self.assertRaisesRegex(ValueError, 'another goal executor'):
                with goal.checkpoint(self.root, 'different-goal'):
                    self.fail('second executor acquired checkout lock')

    def test_symlink_storage_rejected(self):
        outside = self.root/'outside'; outside.mkdir()
        (self.root/'.amr-local').symlink_to(outside, target_is_directory=True)
        with self.assertRaisesRegex(ValueError, 'symlink'):
            goal.init(self.root, self.plan)

    def test_symlink_checkpoint_rejected(self):
        directory = goal.private_directory(self.root)
        (directory/'fixture.json').symlink_to(self.root/'GOAL.md')
        with self.assertRaisesRegex(ValueError, 'symlink'):
            goal.status(self.root, 'fixture')

    def test_bounded_validation_retries_do_not_replay_native_execution(self):
        goal.init(self.root, self.plan)
        def failed(root, base):
            result = self.gates(root, base); result['local_status'] = 'failed'; return result
        with self.boundaries(gates=failed):
            goal.step(self.root, 'fixture')
            for _ in range(3):
                goal.validate(self.root, 'fixture', 'A')
            with self.assertRaisesRegex(ValueError, 'repair limit'):
                goal.validate(self.root, 'fixture', 'A')
            self.assertEqual(self.execute_mock.call_count, 1)
        self.assertEqual(self.state()['tasks']['A']['validation_attempts'], 4)
        self.assertIsNone(goal.status(self.root, 'fixture')['next_task'])


if __name__ == '__main__':
    unittest.main()
