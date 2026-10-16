"""Native task execution contracts; no live model or infrastructure calls."""
import json
from pathlib import Path
import queue
import sys
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import amr_native


class DeadlineContract(unittest.TestCase):
    def native(self):
        native = object.__new__(amr_native.Native)
        native.timeout = 1
        native.counter = 0
        native.pending = {}
        native.events = []
        native.incoming = queue.Queue()
        for _ in range(10):
            native.incoming.put({'method': 'notification', 'params': {}})
        return native

    def test_expired_deadline_rejects_even_a_ready_notification(self):
        native = self.native()
        with patch('amr_native.time.monotonic', return_value=2):
            with self.assertRaisesRegex(amr_native.NativeError, 'timed out; outcome unknown'):
                native.receive(1)
        self.assertEqual(native.incoming.qsize(), 10)

    def test_notification_flood_cannot_extend_request_deadline(self):
        native = self.native()
        with patch.object(native, 'send'), patch('amr_native.time.monotonic',
                side_effect=[0, 0, 0, 2]):
            with self.assertRaisesRegex(amr_native.NativeError, 'outcome unknown'):
                native.request('example', {})
        self.assertEqual(len(native.events), 1)
        self.assertEqual(native.incoming.qsize(), 9)

    def test_notification_flood_cannot_extend_turn_deadline(self):
        native = self.native()
        with patch('amr_native.time.monotonic', side_effect=[0, 0, 0, 2]):
            with self.assertRaisesRegex(amr_native.NativeError, 'outcome unknown'):
                native.wait_turn('thread', 'turn')
        self.assertEqual(len(native.events), 1)


class NativeTaskContract(unittest.TestCase):
    def task(self):
        return {'task_id': 'GOAL-task', 'objective': 'Implement the local domain parser',
                'feature_id': 'FEAT-AMR-001', 'module_id': 'ENG-TOOLING',
                'actor': 'contributor', 'permission': 'repository.write',
                'scope': 'local-development', 'paths': ['source.py'],
                'consulted': ['spec.md'], 'invariants': ['Approval remains external'],
                'tests': ['parser regression'], 'next_action': 'Run local tests'}

    def configure(self, cls):
        native = cls.return_value.__enter__.return_value
        native.start.return_value = {
            'model': 'gpt-6.1-sol', 'reasoningEffort': 'high',
            'thread': {'id': 'task-thread'},
            'sandbox': {'type': 'readOnly', 'networkAccess': False}}
        native.turn.return_value = {'id': 'task-turn', 'status': 'completed'}
        native.events = []
        return native

    def test_actual_task_context_and_runtime_identity_are_retained(self):
        with patch('amr_native.Native') as cls:
            native = self.configure(cls)
            result = amr_native.execute_task(Path('.'), self.task(), 'gpt-6.1-sol', 'high')
            cls.assert_called_once_with(Path('.'), timeout=600)
            native.start.assert_called_once_with('gpt-6.1-sol', 'high', sandbox='read-only')
            thread, prompt, model, effort = native.turn.call_args.args
        self.assertEqual((thread, model, effort), ('task-thread', 'gpt-6.1-sol', 'high'))
        self.assertIn('AGENTS.md and WORKFLOW.md', prompt)
        self.assertIn('Do not delegate or create nested agents', prompt)
        self.assertIn('or access infrastructure', prompt)
        context = json.loads(prompt.split('Structured task:\n', 1)[1])['task']
        for key in ('objective', 'scope', 'consulted', 'invariants', 'task_id'):
            self.assertEqual(context[key], self.task()[key])
        self.assertEqual(result['status'], 'verified')
        self.assertEqual(result['execution'], 'native_session')
        self.assertEqual(result['task_id'], 'GOAL-task')
        self.assertEqual(result['thread_id'], 'task-thread')
        self.assertEqual(result['turn_id'], 'task-turn')

    def test_goal_objective_is_carried_forward(self):
        with patch('amr_native.Native') as cls:
            native = self.configure(cls)
            amr_native.execute_task(Path('.'), self.task(), 'gpt-6.1-sol', 'high',
                goal_context={'goal_id':'active','objective':'Continue milestone M1','milestone':'M1'})
            context = json.loads(native.turn.call_args.args[1].split('Structured task:\n',1)[1])
        self.assertEqual(context['goal']['objective'],'Continue milestone M1')

    def test_start_callback_is_durable_boundary_before_turn(self):
        observed = []
        with patch('amr_native.Native') as cls:
            native = self.configure(cls)
            def turn(*args):
                self.assertEqual(observed, ['task-thread'])
                return {'id': 'task-turn', 'status': 'completed'}
            native.turn.side_effect = turn
            amr_native.execute_task(Path('.'), self.task(), 'gpt-6.1-sol', 'high',
                                    sandbox='workspace-write', timeout=7,
                                    on_start=observed.append)
            cls.assert_called_once_with(Path('.'), timeout=7)
            native.start.assert_called_once_with('gpt-6.1-sol', 'high', sandbox='workspace-write')

    def test_callback_failure_prevents_dispatch(self):
        with patch('amr_native.Native') as cls:
            native = self.configure(cls)
            def unavailable(_thread):
                raise OSError('state persistence failed')
            with self.assertRaisesRegex(OSError, 'persistence failed'):
                amr_native.execute_task(Path('.'), self.task(), 'gpt-6.1-sol', 'high',
                                        on_start=unavailable)
            native.turn.assert_not_called()

    def test_timeout_is_not_replayed(self):
        with patch('amr_native.Native') as cls:
            native = self.configure(cls)
            native.turn.side_effect = amr_native.NativeError('outcome unknown')
            with self.assertRaisesRegex(amr_native.NativeError, 'unknown'):
                amr_native.execute_task(Path('.'), self.task(), 'gpt-6.1-sol', 'high')
            self.assertEqual(native.turn.call_count, 1)

    def test_remote_scope_or_network_enabled_sandbox_blocks_dispatch(self):
        for changes in ({'scope': 'production'}, {'signals': ['remote-execution']},
                        {'signals': ['secrets']}):
            task = self.task(); task.update(changes)
            with patch('amr_native.Native') as cls:
                with self.assertRaises(amr_native.NativeError):
                    amr_native.execute_task(Path('.'), task, 'gpt-6.1-sol', 'high')
                cls.assert_not_called()
        with patch('amr_native.Native') as cls:
            native = self.configure(cls)
            native.start.return_value['sandbox']['networkAccess'] = True
            with self.assertRaisesRegex(amr_native.NativeError, 'network access'):
                amr_native.execute_task(Path('.'), self.task(), 'gpt-6.1-sol', 'high')
            native.turn.assert_not_called()


class GoalReviewContract(unittest.TestCase):
    def test_start_encodes_optional_developer_context_without_changing_defaults(self):
        native = object.__new__(amr_native.Native)
        native.cwd = '.'
        response = {'model': 'gpt-6.1-sol', 'reasoningEffort': 'high',
                    'approvalsReviewer': 'user', 'approvalPolicy': 'on-request',
                    'sandbox': {'type': 'readOnly'}}
        for instructions in (None, '', 'Review only task GOAL-task and its invariants'):
            with patch.object(native, 'request', return_value=response) as request:
                native.start('gpt-6.1-sol', 'high', developer_instructions=instructions)
                config = request.call_args.args[1]['config']
            self.assertEqual(config['model_reasoning_effort'], 'high')
            if instructions:
                self.assertEqual(config['developer_instructions'], instructions)
            else:
                self.assertNotIn('developer_instructions', config)

    def test_review_target_and_review_thread_identity_are_exact(self):
        target = {'type': 'uncommittedChanges'}
        with patch('amr_native.Native') as cls:
            native = cls.return_value.__enter__.return_value
            native.start.return_value = {'thread': {'id': 'start-thread'}}
            native.request.return_value = {'reviewThreadId': 'review-thread',
                                           'turn': {'id': 'review-turn'}}
            native.wait_turn.return_value = {'id': 'review-turn', 'status': 'completed'}
            native.events = [{'method': 'item/completed', 'params': {
                'threadId': 'review-thread', 'turnId': 'review-turn',
                'item': {'type': 'exitedReviewMode', 'review': 'No defects'}}}]
            result = amr_native.local_review(Path('.'), 'gpt-6.1-sol', 'high',
                                             'Review task', target=target)
            native.start.assert_called_once_with('gpt-6.1-sol', 'high',
                                                 developer_instructions='Review task')
            native.request.assert_called_once_with('review/start', {
                'threadId': 'start-thread', 'delivery': 'inline', 'target': target})
            native.wait_turn.assert_called_once_with('review-thread', 'review-turn')
        self.assertEqual(result['status'], 'verified')
        self.assertEqual((result['thread_id'], result['turn_id']),
                         ('review-thread', 'review-turn'))

    def test_empty_review_context_preserves_start_call(self):
        with patch('amr_native.Native') as cls:
            native = cls.return_value.__enter__.return_value
            native.start.return_value = {'thread': {'id': 'thread'}}
            native.request.return_value = {'reviewThreadId': 'thread', 'turn': {'id': 'turn'}}
            native.wait_turn.return_value = {'id': 'turn', 'status': 'completed'}
            native.events = []
            amr_native.local_review(Path('.'), 'gpt-6.1-sol', 'high', '')
            native.start.assert_called_once_with('gpt-6.1-sol', 'high')


if __name__ == '__main__':
    unittest.main()
