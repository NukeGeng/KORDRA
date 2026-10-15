"""Offline acceptance/regression tests. Native calls are separate opt-in probes."""
import copy
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import contextlib
import io
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import amr
import amr_native
import changeguard
import doccheck


def task():
    return dict(task_id='AMR-test', objective='Review source', feature_id='FEAT-AMR-001',
                module_id='ENG-TOOLING', actor='contributor', scope='local-development',
                permission='repository.write', paths=['source.py'], consulted=['spec.md'],
                invariants=['No production access'], decisions=[], tests=[], findings=[],
                next_action='Run tests', kind='source', bounded=True, spec_verified=True,
                tests_known=True, signals=[])


class Routing(unittest.TestCase):
    def test_amr01_lookup_is_deterministic(self):
        t=task(); t['kind']='lookup'
        self.assertEqual(amr.classify(t)['action'], 'deterministic')

    def test_amr02_source_defaults_engineering_even_when_bounded(self):
        self.assertEqual(amr.classify(task())['profile'], 'ENGINEERING')

    def test_amr03_verified_narrow_task_may_use_luna(self):
        t=task(); t['kind']='crud'
        self.assertEqual(amr.classify(t)['profile'], 'ROUTINE')
        for flag in ('bounded','spec_verified','tests_known'):
            bad=dict(t); bad[flag]=False
            self.assertEqual(amr.classify(bad)['profile'], 'ENGINEERING')

    def test_amr04_messaging_contract_integration(self):
        t=task(); t['signals']=['messaging','contracts']; t['kind']='crud'
        self.assertEqual(amr.classify(t)['profile'], 'ENGINEERING')

    def test_amr05_06_security_reliability_escalate(self):
        for signal in sorted(amr.RISKS):
            with self.subTest(signal=signal):
                t=task(); t['signals']=[signal]; t['kind']='crud'
                self.assertEqual(amr.classify(t)['profile'], 'CRITICAL')

    def test_path_overrides_low_risk_claim(self):
        for path in ['src/Authorization.cs','src/TenantQuery.cs','.codex/config.toml',
                     'AGENTS.md','docs/reference-environments/lab-01/example.md']:
            t=task(); t['kind']='crud'; t['paths']=[path]
            self.assertEqual(amr.classify(t)['profile'], 'CRITICAL')

    def test_amr07_escalation_is_bounded(self):
        t=task(); t['failed_repairs']=2
        self.assertEqual(amr.classify(t)['profile'], 'CRITICAL')
        t['failed_repairs']=4
        self.assertEqual(amr.classify(t)['action'], 'blocked')

    def test_amr08_unavailable_model_or_effort_never_falls_back(self):
        route=amr.classify(task())
        for models in ([], [{'model':'gpt-6.1-sol','efforts':['low']}],
                       [{'model':'gpt-6-luna','efforts':['medium']}]):
            self.assertEqual(amr.resolve(route,{'models':models})['action'],'blocked')
        good=amr.resolve(route,{'models':[{'model':'gpt-6.1-sol','efforts':['medium']}]})
        self.assertEqual(good['support'],'advertised')

    def test_amr11_sensitive_scope_not_launched(self):
        t=task(); t['scope']='production'
        r=dict(action='continue',support='advertised',model='gpt-6.1-sol',effort='high')
        with self.assertRaises(ValueError): amr.launch_command(Path('.'),t,r)
        t['scope']='local-development'; t['signals']=['remote-execution']
        with self.assertRaises(ValueError): amr.launch_command(Path('.'),t,r)

    def test_launch_passes_real_model_effort_and_human_reviewer(self):
        r=dict(action='continue',support='advertised',model='gpt-6.1-sol',effort='medium')
        cmd=amr.launch_command(Path('.'),task(),r)
        self.assertIn('--model',cmd); self.assertIn('gpt-6.1-sol',cmd)
        self.assertIn('model_reasoning_effort="medium"',cmd)
        self.assertIn('approvals_reviewer="user"',cmd)
        self.assertNotIn('--dangerously-bypass-approvals-and-sandbox',cmd)


class Context(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.root=Path(self.tmp.name)
        (self.root/'source.py').write_text('x=1\n')
        (self.root/'spec.md').write_text('---\nfeature_id: FEAT-AMR-001\n'
            'module_id: ENG-TOOLING\nstatus: accepted\nsource_paths: ["source.py"]\n'
            'test_paths: []\n---\nspec\n')

    def tearDown(self): self.tmp.cleanup()

    def test_amr09_handoff_preserves_context_and_detects_changes(self):
        t=task(); h=amr.snapshot(self.root,t,amr.classify(t))
        amr.validate_snapshot(self.root,h)
        self.assertEqual(h['task']['invariants'],t['invariants'])
        self.assertIsNone(h['git']['head'])
        (self.root/'source.py').write_text('x=2\n')
        with self.assertRaisesRegex(ValueError,'stale'): amr.validate_snapshot(self.root,h)

    def test_new_contract_invalidates_handoff(self):
        h=amr.snapshot(self.root,task(),amr.classify(task()))
        (self.root/'new-contract.json').write_text('{}')
        with self.assertRaisesRegex(ValueError,'stale'): amr.validate_snapshot(self.root,h)

    def test_symlink_directory_addition_invalidates_handoff(self):
        h=amr.snapshot(self.root,task(),amr.classify(task()))
        (self.root/'linked').symlink_to(self.root.parent)
        with self.assertRaisesRegex(ValueError,'stale'):amr.validate_snapshot(self.root,h)

    def test_owner_mismatch_and_unmapped_input_rejected(self):
        t=task();t['module_id']='WRONG'
        with self.assertRaisesRegex(ValueError,'owner'):amr.validate_task(t,self.root)
        (self.root/'other.py').write_text('x=1')
        t=task();t['paths']=['other.py']
        with self.assertRaisesRegex(ValueError,'mapped'):amr.validate_task(t,self.root)

    def test_proposed_spec_cannot_be_claimed_verified(self):
        p=self.root/'spec.md';p.write_text(p.read_text().replace('accepted','proposed'))
        t=task();t['kind']='crud'
        self.assertEqual(amr.classify(amr.validate_task(t,self.root))['profile'],'ENGINEERING')

    def test_missing_test_mapping_cannot_be_claimed_known(self):
        t=task();t['kind']='crud'
        self.assertEqual(amr.classify(amr.validate_task(t,self.root))['profile'],'ENGINEERING')

    def test_private_receipts_do_not_self_invalidate(self):
        h=amr.snapshot(self.root,task(),amr.classify(task()))
        p=amr.save_private(self.root,'handoff.json',h)
        self.assertEqual(p.stat().st_mode & 0o777,0o600)
        amr.validate_snapshot(self.root,h)

    def test_escape_absolute_and_external_symlink_rejected(self):
        for p in ('../outside','/etc/passwd'):
            with self.assertRaises(ValueError): amr.safe_path(self.root,p)
        (self.root/'escape').symlink_to(self.root.parent)
        with self.assertRaises(ValueError): amr.safe_path(self.root,'escape/file')

    def test_receipt_symlink_is_not_followed(self):
        (self.root/'.amr-local').symlink_to(self.root.parent)
        with self.assertRaises(ValueError): amr.save_private(self.root,'x',{})

    def test_mapped_symlink_cannot_hide_referent_in_private_storage(self):
        private=self.root/'.amr-local';private.mkdir()
        (private/'hidden.py').write_text('x=1')
        (self.root/'source.py').unlink()
        (self.root/'source.py').symlink_to(private/'hidden.py')
        with self.assertRaisesRegex(ValueError,'excluded'):amr.validate_task(task(),self.root)
        with self.assertRaisesRegex(ValueError,'excluded'):amr.tree_identity(self.root)

    def test_amr16_missing_source_and_invalid_signal_rejected(self):
        for change in ({'paths':['missing']},{'signals':['imagined']},
                       {'bounded':'true'},{'failed_repairs':2},{'consulted':[]}):
            t=task(); t.update(change)
            with self.assertRaises(ValueError): amr.validate_task(t,self.root)
        self.assertEqual(amr.validate_task(task(),self.root)['task_id'],'AMR-test')

    def test_amr18_unknown_usage_is_not_invented(self):
        h=amr.snapshot(self.root,task(),amr.classify(task()))
        self.assertEqual(h['usage'],'unknown'); self.assertEqual(h['actual_model'],'unknown')


class ReviewLedger(unittest.TestCase):
    def ledger(self):
        return dict(schemaVersion=1,content='hash',local_status='passed',source_stable=True,
            checks=[dict(name=n,status='passed')
            for n in ('docs','scope','unit')], review=dict(status='verified',independent=True,
            content='hash',evidence='native receipt'),findings=[])

    def test_amr12_13_missing_stale_or_incomplete_review_blocks_critical(self):
        for review in ({}, {'status':'not_configured'},
                       dict(status='verified',independent=True,content='old',evidence='receipt')):
            r=self.ledger();r['review']=review
            self.assertTrue(amr.completion_errors(r,'hash',critical=True))
        self.assertEqual(amr.completion_errors(self.ledger(),'hash',True),[])

    def test_amr14_fix_requires_validation_and_new_review(self):
        r=self.ledger();r['findings']=[dict(id='BUG-1',state='fixed',evidence='test failure')]
        self.assertTrue(amr.completion_errors(r,'hash'))
        r['findings'][0].update(reproduction_passed=True,
            followup_review=dict(status='verified',content='hash',evidence='current review'))
        self.assertEqual(amr.completion_errors(r,'hash'),[])

    def test_false_string_and_stale_followup_review_are_rejected(self):
        r=self.ledger();r['findings']=[dict(id='BUG-1',state='fixed',evidence='repro',
            reproduction_passed='false',followup_review='old-review')]
        self.assertTrue(amr.completion_errors(r,'hash'))
        r['findings'][0].update(reproduction_passed=True,
            followup_review=dict(status='verified',content='old',evidence='stale'))
        self.assertTrue(amr.completion_errors(r,'hash'))

    def test_invalidated_gate_run_is_rejected_even_after_source_is_restored(self):
        for change in ({'local_status':'failed'},{'source_stable':False},{'source_stable':'true'}):
            r=self.ledger();r.update(change)
            self.assertTrue(amr.completion_errors(r,'hash'))

    def test_amr15_dismissal_requires_evidence_and_rationale(self):
        r=self.ledger();r['findings']=[dict(id='BUG-1',state='dismissed')]
        self.assertTrue(amr.completion_errors(r,'hash'))
        r['findings'][0].update(evidence='consumer handles optional field',rationale='compatible')
        self.assertEqual(amr.completion_errors(r,'hash'),[])

    def test_open_blocker_and_failed_or_missing_gates_block(self):
        r=self.ledger();r['findings']=[dict(id='BUG-1',state='open',evidence='repro')]
        self.assertTrue(amr.completion_errors(r,'hash'))
        for checks in ([],[dict(name='unit',status='passed')],
                       [dict(name='unit',status='failed')]):
            r=self.ledger();r['checks']=checks
            self.assertTrue(amr.completion_errors(r,'hash'))

    def test_amr11_17_native_review_is_not_human_production_approval(self):
        r=self.ledger();r['requires_human_approval']=True
        self.assertTrue(amr.completion_errors(r,'hash',True))

    def test_stale_receipt_rejected(self):
        self.assertTrue(amr.completion_errors(self.ledger(),'changed'))


class NativeContract(unittest.TestCase):
    def test_actual_selection_mismatch_rejected(self):
        response=dict(model='wrong',reasoningEffort='medium')
        fake=object.__new__(amr_native.Native);fake.cwd='.'
        with patch.object(fake,'request',return_value=response):
            with self.assertRaises(amr_native.NativeError):fake.start('gpt-6.1-sol','medium')

    def test_model_reroute_and_failed_turn_are_not_verified(self):
        start=dict(model='gpt-6.1-sol',reasoningEffort='medium')
        for turn,events in [(dict(status='failed'),[]),
                            (dict(status='completed'),[dict(method='model/rerouted')])]:
            self.assertEqual(amr_native.summarize(start,turn,events,'gpt-6.1-sol','medium')['status'],'failed')

    def test_client_never_grants_human_approval(self):
        import queue,time
        fake=object.__new__(amr_native.Native);fake.events=[];fake.pending={}
        fake.incoming=queue.Queue();fake.incoming.put(dict(id=1,method='item/commandExecution/requestApproval'))
        with patch.object(fake,'send') as send:
            fake.receive(time.monotonic()+1)
            self.assertIn('error',send.call_args.args[0])

    def test_timeout_and_crash_report_unknown_or_failed(self):
        import queue,time
        fake=object.__new__(amr_native.Native);fake.incoming=queue.Queue();fake.events=[]
        with self.assertRaisesRegex(amr_native.NativeError,'unknown'):fake.receive(time.monotonic())
        fake.incoming.put({'transportError':'native server closed'})
        with self.assertRaises(amr_native.NativeError):fake.receive(time.monotonic()+1)

    def test_native_exit_required_for_local_review(self):
        with patch('amr_native.Native') as cls:
            native=cls.return_value.__enter__.return_value
            native.start.return_value=dict(model='gpt-6.1-sol',reasoningEffort='high',thread={'id':'t'})
            native.request.return_value={'reviewThreadId':'t','turn':{'id':'current'}}
            native.wait_turn.return_value=dict(status='completed');native.events=[]
            result=amr_native.local_review(Path('.'),'gpt-6.1-sol','high','review')
            self.assertEqual(result['status'],'not_verified')

    def test_old_completed_turn_cannot_complete_a_new_turn(self):
        fake=object.__new__(amr_native.Native);fake.timeout=1
        fake.events=[dict(method='turn/completed',params=dict(threadId='t',
                          turn=dict(id='old',status='completed')))]
        with patch.object(fake,'request',return_value={'turn':{'id':'new'}}), patch.object(
                fake,'receive',side_effect=amr_native.NativeError('waited for new turn')):
            with self.assertRaisesRegex(amr_native.NativeError,'new turn'):
                fake.turn('t','second request','gpt-6.1-sol','high')

    def test_unrelated_review_exit_cannot_verify_current_review(self):
        with patch('amr_native.Native') as cls:
            native=cls.return_value.__enter__.return_value
            native.start.return_value=dict(model='gpt-6.1-sol',reasoningEffort='high',thread={'id':'t'})
            native.request.return_value={'reviewThreadId':'t','turn':{'id':'new'}}
            native.wait_turn.return_value=dict(id='new',status='completed')
            for thread,turn in [('other','new'),('t','old')]:
                native.events=[dict(method='item/completed',params=dict(threadId=thread,
                    turnId=turn,item=dict(type='exitedReviewMode',review='unrelated')))]
                result=amr_native.local_review(Path('.'),'gpt-6.1-sol','high','review')
                self.assertEqual(result['status'],'not_verified')


class Mapping(unittest.TestCase):
    def test_feature_must_be_updated_for_tooling_and_tests(self):
        root=amr.ROOT
        spec='docs/engineering/ai-orchestration/features/quality-first-amr.md'
        for path in ('docs/tooling/amr.py','docs/tooling/tests/test_amr.py',
                     '.codex/config.toml','.github/workflows/docs-ci.yml'):
            _,errors=changeguard.check_changes(root,{path})
            self.assertTrue(any('STALE' in e for e in errors))
            _,errors=changeguard.check_changes(root,{path,spec})
            self.assertEqual(errors,[])

    def test_unmapped_source_fails(self):
        _,errors=changeguard.check_changes(amr.ROOT,{'src/Unknown.cs'})
        self.assertTrue(any('UNMAPPED' in e for e in errors))

    def test_product_and_engineering_feature_roots_remain_checked(self):
        self.assertIn('docs/features/',doccheck.FEATURE_ROOTS)
        self.assertIn('docs/engineering/ai-orchestration/',doccheck.FEATURE_ROOTS)

    def test_real_git_range_enforces_stale_then_updated_spec(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            def git(*args):
                return subprocess.run(['git',*args],cwd=root,check=True,
                                      capture_output=True,text=True).stdout.strip()
            git('init','-q');git('config','user.name','AMR fixture')
            git('config','user.email','amr-fixture@example.invalid')
            source=root/'docs/tooling/example.py';source.parent.mkdir(parents=True)
            source.write_text('x=1\n')
            spec=root/'docs/engineering/ai-orchestration/features/test.md'
            spec.parent.mkdir(parents=True)
            spec.write_text('---\nfeature_id: TEST\nsource_paths: ["docs/tooling/example.py"]\n'
                            'test_paths: []\n---\n')
            git('add','.');git('commit','-qm','baseline');base=git('rev-parse','HEAD')
            source.write_text('x=2\n');git('add','.');git('commit','-qm','stale spec')
            with patch.object(changeguard,'ROOT',root),patch.object(sys,'argv',['guard','--base',base]):
                with contextlib.redirect_stdout(io.StringIO()):self.assertEqual(changeguard.main(),1)
            spec.write_text(spec.read_text()+'Updated spec\n')
            git('add','.');git('commit','-qm','update spec')
            with patch.object(changeguard,'ROOT',root),patch.object(sys,'argv',['guard','--base',base]):
                with contextlib.redirect_stdout(io.StringIO()):self.assertEqual(changeguard.main(),0)


class Gates(unittest.TestCase):
    def test_real_failing_commands_cannot_pass(self):
        with tempfile.TemporaryDirectory() as d:
            result=amr.gates(Path(d))
            self.assertEqual(result['local_status'],'failed')
            self.assertTrue(any(c['status']=='failed' for c in result['checks']))
            self.assertEqual(result['source_mapping'],'not_configured')

    def test_source_change_during_checks_invalidates_passing_results(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d)
            def mutate(*args,**kwargs):
                (root/'changed.py').write_text('changed during check')
                return subprocess.CompletedProcess(args[0],0,'passed','')
            with patch.object(amr.subprocess,'run',side_effect=mutate):
                result=amr.gates(root)
            self.assertEqual(result['local_status'],'failed')
            self.assertFalse(result['source_stable'])


if __name__=='__main__':unittest.main()
