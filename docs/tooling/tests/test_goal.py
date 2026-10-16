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
        return {'models': [{'model': 'gpt-6.1-sol', 'efforts': ['medium', 'high']}]}

    def execution(self, root, task, model, effort, **kwargs):
        kwargs['on_start']('native-thread-A')
        return dict(status='verified', turn_status='completed', rerouted=False,
                    execution='native_session', task_id=task['task_id'],
                    requested_model=model, requested_effort=effort,
                    thread_id='native-thread-A', turn_id='native-turn-A',
                    observed_model=model, observed_effort=effort,
                    evidence='.amr-local/native-task-A.json')

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

    def state(self):
        return json.loads((self.root/'.amr-local/goals/fixture.json').read_text())

    def triage(self):
        return dict(task_id='A', content=amr.tree_identity(self.root)['digest'],
                    reviewed_by='fixture-contributor', rationale='Inspected current native review', findings=[])

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
            with self.assertRaisesRegex(ValueError, 'no eligible task'):
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

    def test_review_requires_terminal_exact_selection_and_real_native_channel(self):
        for index, override in enumerate([
            {'channel': 'self_check'}, {'observed_model': 'gpt-6-sol'},
            {'observed_effort': 'medium'}, {'thread_id': ''}, {'turn_id': ''},
            {'turn_status': 'failed'}, {'rerouted': True}, {'review_output': []},
        ]):
            plan = copy.deepcopy(self.plan); plan['goal_id'] = 'review-'+str(index)
            goal.init(self.root, plan)
            def invalid(*args, **kwargs):
                review = self.review(); review.update(override); return review
            with self.subTest(override=override), self.boundaries(review=invalid):
                result = goal.step(self.root, plan['goal_id'])
                self.assertEqual(result['tasks']['A']['status'], 'validation_failed')
                with self.assertRaises(ValueError):
                    goal.accept(self.root, plan['goal_id'], 'A', self.triage())

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

    def test_independent_review_and_contributor_triage_required(self):
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

    def test_missing_native_review_evidence_cannot_complete(self):
        goal.init(self.root, self.plan)
        def missing(*args, **kwargs):
            review = self.review()
            for key in ('evidence', 'thread_id', 'turn_id'):
                review.pop(key)
            return review
        with self.boundaries(review=missing):
            goal.step(self.root, 'fixture')
            with self.assertRaises(ValueError):
                goal.accept(self.root, 'fixture', 'A', self.triage())

    def test_failed_review_does_not_advance(self):
        goal.init(self.root, self.plan)
        with self.boundaries(review=lambda *args, **kwargs: {'status': 'not_verified'}):
            result = goal.step(self.root, 'fixture')
        self.assertEqual(result['tasks']['A']['status'], 'validation_failed')
        self.assertIsNone(result['next_task'])

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
