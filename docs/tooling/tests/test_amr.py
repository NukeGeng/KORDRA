"""Offline acceptance/regression tests. Native calls are separate opt-in probes."""
import copy
import datetime
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
    def test_project_defaults_disable_subagent_creation(self):
        import tomllib
        config = tomllib.loads((amr.ROOT/'.codex/config.toml').read_text())
        self.assertIs(config['agents']['enabled'], False)
        self.assertEqual((config['model'], config['model_reasoning_effort']), ('gpt-6-luna', 'max'))

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
                route=amr.classify(t)
                self.assertEqual(route['risk'], 'CRITICAL')
                expected = 'CRITICAL' if signal in {'architecture', 'critical-design'} else 'ENGINEERING'
                self.assertEqual(route['profile'], expected)

    def test_path_overrides_low_risk_claim(self):
        for path in ['src/Authorization.cs','src/TenantQuery.cs','.codex/config.toml',
                     'AGENTS.md','docs/reference-environments/lab-01/example.md']:
            t=task(); t['kind']='crud'; t['paths']=[path]
            self.assertEqual(amr.classify(t)['profile'], 'ENGINEERING')

    def test_amr07_escalation_is_bounded(self):
        t=task(); t['failed_repairs']=2; t['failure_evidence']='two Luna attempts';
        t['reported_defect']={'finding_id':'BUG-1','summary':'still failing',
                              'evidence':'reproduction failed','affected_paths':['source.py']}
        self.assertEqual(amr.classify(t)['profile'], 'DEBUGGER_MEDIUM')
        t['failed_repairs']=4
        self.assertEqual(amr.classify(t)['action'], 'blocked')

    def test_amr08_unavailable_model_or_effort_never_falls_back(self):
        route=amr.classify(task())
        for models in ([], [{'model':'gpt-6.1-sol','efforts':['low']}],
                       [{'model':'gpt-6-luna','efforts':['medium']}]):
            self.assertEqual(amr.resolve(route,{'models':models})['action'],'blocked')
        good=amr.resolve(route,{'models':[{'model':'gpt-6-luna','efforts':['max']}]})
        self.assertEqual(good['support'],'advertised')

    def test_profiles_use_luna_for_implementation_and_exact_specialist_routes(self):
        cases=[(task(), 'gpt-6-luna', 'max'),
               (dict(task(),kind='architecture'), 'gpt-6.1-sol', 'high'),
               (dict(task(),failed_repairs=2,failure_evidence='two failed Luna repairs',
                     reported_defect={'finding_id':'BUG-1','summary':'still failing',
                                      'evidence':'reproduction','affected_paths':['source.py']}),
                'gpt-6.1-sol','medium'),
               (dict(task(),signals=['auth']), 'gpt-6-luna','max')]
        for t, model, effort in cases:
            with self.subTest(effort=effort):
                route=amr.classify(t)
                older={'models':[{'model':'gpt-6-sol','efforts':['medium','high']}]}
                self.assertEqual(amr.resolve(route,older)['action'],'blocked')
                exact={'models':[{'model':model,'efforts':[effort]}]}
                resolved=amr.resolve(route,exact)
                self.assertEqual(resolved['model'],model)
                self.assertEqual(resolved['effort'],effort)
                self.assertEqual(resolved['support'],'advertised')
                self.assertNotEqual(resolved['action'],'blocked')

    def test_amr11_sensitive_scope_not_launched(self):
        t=task(); t['scope']='production'
        r=dict(action='continue',support='advertised',model='gpt-6.1-sol',effort='high')
        with self.assertRaises(ValueError): amr.launch_command(Path('.'),t,r)
        t['scope']='local-development'; t['signals']=['remote-execution']
        with self.assertRaises(ValueError): amr.launch_command(Path('.'),t,r)

    def test_architect_and_escalation_debugger_routes_are_role_based(self):
        for kind in ('source', 'tests', 'integration', 'bugfix', 'docs'):
            t=task();t['kind']=kind
            route=amr.resolve(amr.classify(t),{'models':[{'model':'gpt-6-luna','efforts':['max']}]})
            self.assertEqual((route['model'],route['effort']),('gpt-6-luna','max'))
        t=task();t['kind']='architecture'
        self.assertEqual(amr.classify(t)['profile'],'CRITICAL')
        self.assertEqual(amr.execution_role(t), 'architect')
        t=task();t['kind']='complex-debugging'
        self.assertEqual(amr.classify(t)['action'],'blocked')
        t.update(failed_repairs=2,failure_evidence='two Luna attempts',reported_defect={
            'finding_id':'BUG-2','summary':'repeatable failure','evidence':'test reproduction',
            'affected_paths':['source.py']})
        self.assertEqual(amr.classify(t)['profile'],'DEBUGGER_MEDIUM')
        t['signals']=['concurrency']
        self.assertEqual(amr.classify(t)['profile'],'CRITICAL')
        self.assertEqual(amr.execution_role(dict(task(),kind='architecture')), 'architect')
        self.assertTrue(amr.design_review_required(dict(task(),signals=['critical-design'])))
        self.assertFalse(amr.design_review_required(task()))

    def test_model_probe_reuses_shared_selection_even_after_native_failure(self):
        for native_result in ({'status':'verified'}, amr.NativeError('outcome unknown')):
            with self.subTest(result=native_result), patch.object(sys,'argv',['amr','probe-models']), \
                    patch.object(amr,'probe_profile',side_effect=[native_result,{'status':'verified'},
                        {'status':'verified'}]) as probe, \
                    patch.object(amr,'save_private'),contextlib.redirect_stdout(io.StringIO()):
                code=amr.main()
            self.assertEqual(probe.call_count,3)
            self.assertEqual([c.args for c in probe.call_args_list],
                             [('gpt-6-luna','max'),('gpt-6.1-sol','high'),
                              ('gpt-6.1-sol','medium')])
            self.assertEqual(code,1 if isinstance(native_result,Exception) else 0)

    def test_launch_passes_real_model_effort_and_human_reviewer(self):
        r=dict(action='continue',support='advertised',model='gpt-6.1-sol',effort='medium')
        cmd=amr.launch_command(Path('.'),task(),r)
        self.assertIn('--model',cmd); self.assertIn('gpt-6.1-sol',cmd)
        self.assertIn('model_reasoning_effort="medium"',cmd)
        self.assertIn('agents.enabled=false',cmd)
        self.assertIn('approvals_reviewer="user"',cmd)
        self.assertNotIn('--dangerously-bypass-approvals-and-sandbox',cmd)


class Context(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(); self.root=Path(self.tmp.name)
        (self.root/'source.py').write_text('x=1\n')
        (self.root/'spec.md').write_text('---\nfeature_id: FEAT-AMR-001\n'
            'module_id: ENG-TOOLING\nstatus: accepted\nsource_paths: ["source.py"]\n'
            'test_paths: []\nplanned_source_paths: ["generated.py"]\n'
            'planned_test_paths: ["generated_test.py"]\n'
            'planned_doc_paths: ["planned.md", "spec.md", "other.md"]\n'
            '---\nspec\n')

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

    def test_explicit_owner_mapped_outputs_allow_new_and_existing_files(self):
        t=task(); t.update(output_paths=['generated.py', 'planned.md'],
                           write_paths=['generated.py', 'planned.md'])
        amr.validate_task(t,self.root)
        self.assertFalse((self.root/'generated.py').exists())
        (self.root/'generated.py').write_text('value = 2\n')
        (self.root/'planned.md').write_text('planned output\n')
        self.assertEqual(amr.validate_task(t,self.root)['output_paths'],['generated.py','planned.md'])

    def test_matching_owner_feature_can_be_updated_to_map_created_source_and_tests(self):
        t=task(); outputs=['generated.py', 'generated_test.py', 'spec.md']
        t.update(output_paths=outputs, write_paths=outputs)
        amr.validate_task(t,self.root)
        (self.root/'generated.py').write_text('value = 2\n')
        (self.root/'generated_test.py').write_text('assert True\n')
        spec=self.root/'spec.md'
        spec.write_text(spec.read_text().replace(
            'source_paths: ["source.py"]',
            'source_paths: ["source.py", "generated.py"]').replace(
            'test_paths: []', 'test_paths: ["generated_test.py"]'))
        normalized=amr.validate_task(t,self.root)
        self.assertIn('test_paths: ["generated_test.py"]', spec.read_text())
        self.assertFalse(normalized['tests_known'])

    def test_only_matching_owner_feature_may_overlap_consulted_and_output_paths(self):
        (self.root/'other.md').write_text('Other consulted document\n')
        t=task(); t['consulted'].append('other.md')
        t.update(output_paths=['other.md'], write_paths=['other.md'])
        with self.assertRaisesRegex(ValueError, 'only the matching owner feature'):
            amr.validate_task(t,self.root)

    def test_unmapped_duplicate_escaping_and_symlink_outputs_rejected(self):
        for outputs in (['unmapped.py'], ['source.py'], ['../outside.py'],
                        ['.amr-local/receipt.json'], ['/tmp/out.py']):
            t=task(); t['output_paths']=outputs
            with self.subTest(outputs=outputs), self.assertRaises(ValueError):
                amr.validate_task(t,self.root)
        (self.root/'linked').symlink_to(self.root.parent)
        t=task(); t['output_paths']=['linked/new.py']
        with self.assertRaisesRegex(ValueError,'symlink'):
            amr.validate_task(t,self.root)

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

    def test_generated_dotnet_outputs_do_not_replace_source_identity(self):
        before=amr.tree_identity(self.root)
        for directory in ('bin','obj','TestResults'):
            output=self.root/directory;output.mkdir()
            (output/'generated').write_text('derived output')
            with self.assertRaisesRegex(ValueError,'excluded'):
                amr.safe_path(self.root,directory+'/generated')
        self.assertEqual(before,amr.tree_identity(self.root))

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

    def test_debugger_requires_two_failed_repairs_and_restricts_writes_to_reported_defect(self):
        t=task(); t.update(kind='complex-debugging', failed_repairs=2,
            failure_evidence='two Luna repair attempts with test output',
            reported_defect={'finding_id':'BUG-17','summary':'parser drops a value',
                'evidence':'test_source.py reproduces it','affected_paths':['source.py']},
            write_paths=['source.py'])
        self.assertEqual(amr.execution_role(t), 'debugger')
        self.assertEqual(amr.classify(t)['profile'], 'DEBUGGER_MEDIUM')
        self.assertEqual(amr.validate_task(t,self.root)['task_id'],'AMR-test')
        t['execution_role']='developer'
        with self.assertRaisesRegex(ValueError, 'cannot downgrade'):
            amr.validate_task(t,self.root)
        t.pop('execution_role')
        t['write_paths']=['test_source.py']
        with self.assertRaisesRegex(ValueError, 'writes must stay within the reported defect'):
            amr.validate_task(t,self.root)

    def test_amr18_unknown_usage_is_not_invented(self):
        h=amr.snapshot(self.root,task(),amr.classify(task()))
        self.assertEqual(h['usage'],'unknown'); self.assertEqual(h['actual_model'],'unknown')

    def test_staged_index_content_changes_are_part_of_git_identity(self):
        def git(*args):
            return subprocess.run(['git',*args],cwd=self.root,capture_output=True,
                                  text=True,check=True).stdout.strip()
        git('init','-q'); git('config','user.name','AMR fixture')
        git('config','user.email','amr@example.invalid'); git('add','.')
        git('commit','-qm','baseline')
        (self.root/'source.py').write_text('x=2\n'); git('add','source.py')
        first=amr.git_identity(self.root)
        (self.root/'source.py').write_text('x=3\n'); git('add','source.py')
        second=amr.git_identity(self.root)
        self.assertEqual(first['status'],second['status'])
        self.assertNotEqual(first['index'],second['index'])


class Changeguard(unittest.TestCase):
    def test_working_tree_paths_includes_unstaged_staged_and_untracked(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            for name in ('staged.py','unstaged.py','tracked.py'):
                (root/name).write_text('value = 1\n')
            def git(*args):
                return subprocess.run(['git',*args],cwd=root,capture_output=True,
                                      text=True,check=True).stdout.strip()
            git('init','-q'); git('config','user.name','AMR fixture')
            git('config','user.email','amr@example.invalid'); git('add','.')
            git('commit','-qm','baseline')
            (root/'staged.py').write_text('value = 2\n'); git('add','staged.py')
            (root/'unstaged.py').write_text('value = 2\n')
            (root/'untracked.py').write_text('value = 1\n')
            self.assertTrue({'staged.py','unstaged.py','untracked.py'}
                            <= changeguard.working_tree_paths(root))


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

    def test_product_solution_requires_product_checks_in_completion_ledger(self):
        r=self.ledger()
        self.assertTrue(amr.completion_errors(r,'hash',product_required=True))
        r['product_tests']='executed'
        r['checks'] += [dict(name=n,status='passed') for n in
                       ('product-restore','product-build','product-test')]
        self.assertEqual(amr.completion_errors(r,'hash',product_required=True),[])


class BoundedIncrementalReview(unittest.TestCase):
    def ledger(self, count=1):
        shas = [str(i)*40 for i in range(1, 5)]
        rounds = []
        for i in range(1, count+1):
            initiator = (dict(trigger='automatic', role='provider_automation',
                              evidence='Codex summary identifies PR-open trigger') if i == 1 else
                         dict(trigger='manual', role='luna_goal_coordinator',
                              evidence='authorized Luna follow-up request',
                              session_evidence='native Luna task/session reference'))
            rounds.append(dict(number=i, channel='codex_auto_code_review',
                scope='full' if i == 1 else 'incremental', base_sha=shas[i-1], head_sha=shas[i],
                status='verified', request_evidence='provider request '+str(i), initiator=initiator,
                review_id='provider-review-'+str(i), completion_evidence='terminal '+str(i),
                context='Surrounding callers, owner contracts and prior findings',
                new_findings=[], tracked_findings=[]))
        return dict(schemaVersion=2, policyVersion='4',
                    pr=dict(repository='https://github.com/example/repo', number=7),
                    base_sha=shas[0], head_sha=shas[count], rounds=rounds, findings=[])

    def result(self, ledger):
        return amr.bir_result(ledger, ledger['head_sha'])

    def add_finding(self, ledger, state='open', blocking=True):
        finding = dict(id='BUG-1', category='correctness', root_cause='stale receipt',
                       bug_key='accept/current-head', state=state, blocking=blocking,
                       evidence='regression reproduction')
        ledger['findings'].append(finding)
        ledger['rounds'][0]['new_findings'] = ['BUG-1']
        for review in ledger['rounds'][1:]:
            review['tracked_findings'] = ['BUG-1']
        return finding

    def test_full_then_incremental_rounds_and_no_implicit_merge_approval(self):
        for count in (1, 2, 3):
            with self.subTest(count=count):
                result = self.result(self.ledger(count))
                self.assertEqual(result['status'], 'ledger_valid', result)
                self.assertEqual(result['rounds_remaining'], 3-count)
                self.assertFalse(result['merge_approval'])
                self.assertEqual(result['native_hard_cap'], 'unverified')
        self.assertEqual(result['next_action'], 'stop_automatic_review')

    def test_historical_v31_ledger_is_compatible_and_unchanged_sha_cannot_be_reviewed_twice(self):
        ledger = self.ledger()
        ledger['schemaVersion'] = 1
        ledger['policyVersion'] = '3.1'
        ledger['rounds'][0].pop('initiator')
        self.assertEqual(self.result(ledger)['status'], 'ledger_valid')
        ledger = self.ledger(2)
        ledger['head_sha'] = ledger['rounds'][0]['head_sha']
        ledger['rounds'][1].update(base_sha=ledger['head_sha'], head_sha=ledger['head_sha'])
        result = self.result(ledger)
        self.assertEqual(result['status'], 'blocked')
        self.assertTrue(any('distinct immutable diff SHAs' in error for error in result['errors']))

    def test_v4_requires_versioned_authorized_initiator_and_bound_evidence(self):
        ledger = self.ledger()
        ledger['schemaVersion'] = 1
        result = self.result(ledger)
        self.assertTrue(any('schemaVersion 2' in error for error in result['errors']))

        ledger = self.ledger()
        ledger['rounds'][0].pop('initiator')
        self.assertTrue(any('initiator and evidence' in error for error in self.result(ledger)['errors']))

        ledger = self.ledger()
        ledger['rounds'][0]['initiator'].update(trigger='manual', role='sol_architect',
                                                evidence='claimed session')
        result = self.result(ledger)
        self.assertTrue(any('unauthorized initiator role' in error for error in result['errors']))

        ledger = self.ledger()
        ledger['rounds'][0]['initiator'].update(trigger='manual', role=['sol_architect'],
                                                evidence='malformed role value')
        self.assertEqual(self.result(ledger)['status'], 'blocked')

        ledger = self.ledger()
        ledger['rounds'][0]['initiator'].update(trigger='automatic', role='human_maintainer')
        self.assertTrue(any('provider_automation' in error for error in self.result(ledger)['errors']))

        ledger = self.ledger()
        ledger['rounds'][0]['initiator']['evidence'] = ''
        self.assertTrue(any('initiator requires evidence' in error for error in self.result(ledger)['errors']))

        ledger = self.ledger()
        ledger['rounds'][0]['initiator'].update(trigger='manual', role='luna_goal_coordinator')
        ledger['rounds'][0]['initiator'].pop('session_evidence', None)
        self.assertTrue(any('session-bound evidence' in error for error in self.result(ledger)['errors']))

        ledger = self.ledger()
        ledger['rounds'][0]['initiator'].update(trigger='manual', role='human_maintainer')
        self.assertTrue(any('authorization evidence' in error for error in self.result(ledger)['errors']))

        ledger = self.ledger()
        ledger['rounds'][0]['initiator'].update(trigger='manual', role='human_maintainer',
                                                authorization_evidence='explicit maintainer approval')
        self.assertEqual(self.result(ledger)['status'], 'ledger_valid')

    def test_no_fourth_round_and_exhaustion_never_approves(self):
        ledger = self.ledger(3)
        self.add_finding(ledger)
        result = self.result(ledger)
        self.assertEqual(result['status'], 'blocked')
        self.assertEqual(result['next_action'], 'escalate_to_human_maintainer')
        self.assertTrue(any('escalation record' in e for e in result['errors']))
        ledger['escalation'] = dict(target='human_maintainer', reason='blocking BUG-1', evidence='handoff')
        self.assertEqual(self.result(ledger)['status'], 'blocked')
        ledger['rounds'].append(copy.deepcopy(ledger['rounds'][-1]))
        self.assertTrue(any('fourth' in e for e in self.result(ledger)['errors']))

    def test_requests_unknowns_and_failures_consume_budget_without_faking_completion(self):
        for state in ('requested', 'unknown', 'failed'):
            ledger = self.ledger(3); ledger['rounds'][-1]['status'] = state
            result = self.result(ledger)
            self.assertEqual(result['rounds_used'], 3)
            self.assertEqual(result['status'], 'blocked')
            self.assertEqual(result['next_action'], 'escalate_to_human_maintainer')
        ledger = self.ledger(2); ledger['rounds'][0]['status'] = 'unknown'
        self.assertTrue(any('last verified' in e for e in self.result(ledger)['errors']))

    def test_wrong_scope_baseline_stale_head_and_reused_provider_identity_fail(self):
        variants = [dict(scope='full'), dict(base_sha='1'*40), dict(number=3),
                    dict(context=''), dict(review_id='provider-review-1'),
                    dict(completion_evidence=''), dict(channel='native_local_review')]
        for override in variants:
            ledger = self.ledger(2); ledger['rounds'][1].update(override)
            self.assertEqual(self.result(ledger)['status'], 'blocked', override)
        ledger = self.ledger()
        self.assertEqual(amr.bir_result(ledger,'f'*40)['status'], 'blocked')

    def test_dedup_and_tracking_do_not_repost_or_drop_existing_findings(self):
        ledger = self.ledger(2)
        finding = self.add_finding(ledger, blocking=False)
        self.assertEqual(self.result(ledger)['status'], 'ledger_valid')
        ledger['rounds'][1]['new_findings'] = ['BUG-1']
        self.assertTrue(any('repost' in e for e in self.result(ledger)['errors']))
        ledger['rounds'][1]['new_findings'] = []
        ledger['rounds'][1]['tracked_findings'] = []
        self.assertTrue(any('carry all' in e for e in self.result(ledger)['errors']))
        ledger['rounds'][1]['tracked_findings'] = ['BUG-1']
        ledger['findings'].append(dict(finding, id='BUG-2'))
        ledger['rounds'][1]['new_findings'] = ['BUG-2']
        self.assertTrue(any('fingerprint' in e for e in self.result(ledger)['errors']))
        ledger = self.ledger(); self.add_finding(ledger); ledger['findings'] = []
        self.assertTrue(any('canonical ledger' in e for e in self.result(ledger)['errors']))

    def test_fixes_need_reproduction_and_exact_head_followup_after_budget_exhaustion(self):
        ledger = self.ledger(3); finding = self.add_finding(ledger, state='fixed')
        self.assertEqual(self.result(ledger)['status'], 'blocked')
        finding.update(reproduction_passed=True, followup_review=dict(status='verified',
            head_sha=ledger['head_sha'], evidence='human verified change', reviewer='human_maintainer'))
        self.assertEqual(self.result(ledger)['status'], 'ledger_valid')
        finding['followup_review'].update(reviewer='sol_high', observed_model='gpt-6.1-sol', observed_effort='high')
        self.assertEqual(self.result(ledger)['status'], 'blocked')
        legacy = self.ledger(3); legacy['schemaVersion'] = 1; legacy['policyVersion'] = '3.1'
        for review in legacy['rounds']:
            review.pop('initiator', None)
        legacy_finding = self.add_finding(legacy, state='fixed')
        legacy_finding.update(reproduction_passed=True, followup_review=dict(status='verified',
            head_sha=legacy['head_sha'], evidence='historical Sol follow-up', reviewer='sol_high',
            observed_model='gpt-6.1-sol', observed_effort='high'))
        self.assertEqual(self.result(legacy)['status'], 'ledger_valid')
        finding['followup_review'].update(reviewer='human_maintainer')
        finding['followup_review'].update(reviewer='codex_auto_code_review', review_id='provider-review-1')
        self.assertEqual(self.result(ledger)['status'], 'blocked')
        finding['followup_review']['review_id'] = 'provider-review-3'
        self.assertEqual(self.result(ledger)['status'], 'ledger_valid')
        finding['followup_review']['head_sha'] = '2'*40
        self.assertEqual(self.result(ledger)['status'], 'blocked')

    def test_dismissal_requires_current_evidence_and_no_unknown_or_malformed_states(self):
        ledger = self.ledger(); finding = self.add_finding(ledger, state='dismissed')
        self.assertEqual(self.result(ledger)['status'], 'blocked')
        finding.update(rationale='consumer regression proves compatibility', validated_sha=ledger['head_sha'])
        self.assertEqual(self.result(ledger)['status'], 'ledger_valid')
        for value in ([], {'schemaVersion':2}, {'rounds':{},'findings':[]},
                      dict(ledger, rounds=[None]), dict(ledger,findings=[None])):
            self.assertEqual(amr.bir_result(value,'2'*40)['status'], 'blocked')

    def test_completed_escalation_resolves_new_head_without_fourth_provider_round(self):
        ledger=self.ledger(3);finding=self.add_finding(ledger,state='fixed')
        ledger['head_sha']='5'*40
        review=dict(status='verified',reviewer='human_maintainer',head_sha=ledger['head_sha'],
                    evidence='maintainer verified current head',scope='full')
        ledger['escalation']=dict(target='human_maintainer',reason='round-three blocker fixed',
                                  evidence='maintainer handoff',review=review)
        finding.update(reproduction_passed=True,followup_review=copy.deepcopy(review))
        result=self.result(ledger)
        self.assertEqual(result['status'],'ledger_valid',result)
        self.assertFalse(result['provider_review_on_current_head'])
        self.assertEqual(result['resolution_channel'],'human_maintainer')
        self.assertEqual(result['rounds_used'],3)
        for override in ({'status':'unknown'},{'head_sha':'4'*40},{'reviewer':'sol_high'},
                         {'scope':'incremental','base_sha':'2'*40},{'scope':'incremental','base_sha':'4'*40,'context':''}):
            bad=copy.deepcopy(ledger);bad['escalation']['review'].update(override)
            self.assertEqual(self.result(bad)['status'],'blocked',override)

    def test_real_git_commits_are_required_and_rebased_history_is_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            root=Path(directory)
            def git(*args):
                return subprocess.run(['git',*args],cwd=root,check=True,capture_output=True,text=True).stdout.strip()
            git('init','-q');git('config','user.name','BIR fixture');git('config','user.email','bir@example.invalid')
            (root/'source.txt').write_text('base\n');git('add','source.txt');git('commit','-qm','base')
            base=git('rev-parse','HEAD')
            (root/'source.txt').write_text('changed\n');git('add','source.txt');git('commit','-qm','head')
            head=git('rev-parse','HEAD')
            ledger=self.ledger();ledger.update(base_sha=base,head_sha=head)
            ledger['rounds'][0].update(base_sha=base,head_sha=head)
            self.assertEqual(amr.bir_result(ledger,head,root)['status'],'ledger_valid')
            git('commit','--allow-empty','-qm','empty PR revision'); empty_head=git('rev-parse','HEAD')
            unchanged=self.ledger(2);unchanged.update(base_sha=base,head_sha=empty_head)
            unchanged['rounds'][0].update(base_sha=base,head_sha=head)
            unchanged['rounds'][1].update(base_sha=head,head_sha=empty_head)
            self.assertTrue(any('no source-tree changes' in error
                                for error in amr.bir_result(unchanged,empty_head,root)['errors']))
            ledger['base_sha']='e'*40;ledger['rounds'][0]['base_sha']='e'*40
            self.assertEqual(amr.bir_result(ledger,head,root)['status'],'blocked')
            git('checkout','--orphan','unrelated');git('commit','--allow-empty','-qm','other')
            unrelated=git('rev-parse','HEAD');ledger['base_sha']=unrelated
            ledger['rounds'][0]['base_sha']=unrelated
            self.assertTrue(any('ancestral' in e for e in amr.bir_result(ledger,head,root)['errors']))


class NativeContract(unittest.TestCase):
    def test_native_app_server_explicitly_disables_subagents(self):
        with patch('amr_native.subprocess.Popen') as popen, \
                patch('amr_native.threading.Thread') as thread, \
                patch.object(amr_native.Native, 'request', return_value={}), \
                patch.object(amr_native.Native, 'send'):
            amr_native.Native(Path('.'))
        command = popen.call_args.args[0]
        self.assertIn('-c', command)
        self.assertIn('agents.enabled=false', command)
        thread.return_value.start.assert_called_once()

    def test_discovery_reports_effective_subagent_setting(self):
        from types import SimpleNamespace
        with patch('amr_native.shutil.which', return_value='/usr/bin/codex'), \
                patch('amr_native.subprocess.run',
                      return_value=SimpleNamespace(stdout='codex test\n')), \
                patch('amr_native.Native') as cls:
            cls.return_value.__enter__.return_value.request.side_effect = [
                {'config': {'agents': {'enabled': False, 'max_concurrent_threads_per_session': None}},
                 'layers': [{'name': {'type': 'project'}}]},
                {'data': [{'model': 'gpt-6.1-sol', 'supportedReasoningEfforts': [
                    {'reasoningEffort': 'medium'}, {'reasoningEffort': 'high'}]}]}]
            result = amr_native.discover(Path('.'))
        self.assertEqual(result['agent_defaults'], {
            'subagents_enabled': False, 'max_concurrent_threads_per_session': None})

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


class DocumentationInputs(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        for path in ('AGENTS.md','GOAL.md','WORKFLOW.md','docs/README.md',
                     'docs/governance/source-of-truth.md','docs/contracts/permissions.md'):
            self.document(path,path)

    def document(self,path,identifier,body=''):
        target=self.root/path;target.parent.mkdir(parents=True,exist_ok=True)
        target.write_text(f'---\nid: {identifier}\nstatus: proposed\nowner: fixture\n'
                          f'last_reviewed: 2026-10-18\n---\n{body}\n')

    def check(self):
        output=io.StringIO()
        with patch.object(doccheck,'ROOT',self.root),patch.object(sys,'argv',
                ['doccheck','--strict-sources']),contextlib.redirect_stdout(output):
            code=doccheck.main()
        return code,output.getvalue()

    def test_import_pack_and_private_backups_do_not_pollute_canonical_docs(self):
        self.document('docs/imported.md','IMPORTED')
        for folder in ('.temp/pack','.amr-local/preview','.git/fixtures','docs/.temp/pack'):
            self.document(f'{folder}/copy.md','IMPORTED','[draft link](missing.md)')
        code,output=self.check()
        self.assertEqual(code,0,output)

    def test_untracked_docs_are_checked_even_if_gitignore_hides_them(self):
        (self.root/'.gitignore').write_text('docs/\n')
        self.document('docs/untracked.md','UNTRACKED','[broken](missing.md)')
        code,output=self.check()
        self.assertEqual(code,1)
        self.assertIn('docs/untracked.md: broken local link',output)

    def test_duplicates_in_maintained_and_other_hidden_docs_still_fail(self):
        self.document('docs/first.md','DUPLICATE')
        self.document('.notes/second.md','DUPLICATE')
        code,output=self.check()
        self.assertEqual(code,1)
        self.assertIn('duplicate id DUPLICATE',output)

    def test_linked_directory_outside_checkout_is_not_scanned(self):
        with tempfile.TemporaryDirectory() as external:
            (Path(external)/'outside.md').write_text('No repository metadata\n')
            (self.root/'linked').symlink_to(external,target_is_directory=True)
            code,output=self.check()
        self.assertEqual(code,0,output)


class Mapping(unittest.TestCase):
    def test_feature_must_be_updated_for_tooling_and_tests(self):
        root=amr.ROOT
        spec='docs/engineering/ai-orchestration/features/quality-first-amr.md'
        clients_spec='docs/engineering/ai-orchestration/features/clients-repository.md'
        shared={'docs/tooling/amr.py','.github/workflows/docs-ci.yml'}
        for path in ('docs/tooling/amr.py','docs/tooling/tests/test_amr.py',
                     '.codex/config.toml','.github/workflows/docs-ci.yml'):
            _,errors=changeguard.check_changes(root,{path})
            self.assertTrue(any('STALE' in e and spec in e for e in errors))
            _,errors=changeguard.check_changes(root,{path,spec})
            if path in shared:
                self.assertTrue(any('STALE' in e and clients_spec in e for e in errors))
                _,errors=changeguard.check_changes(root,{path,clients_spec})
                self.assertTrue(any('STALE' in e and spec in e for e in errors))
                _,errors=changeguard.check_changes(root,{path,spec,clients_spec})
            self.assertEqual(errors,[])

    def test_unmapped_source_fails(self):
        _,errors=changeguard.check_changes(amr.ROOT,{'src/Unknown.cs'})
        self.assertTrue(any('UNMAPPED' in e for e in errors))

    def test_product_tests_and_bootstrap_files_need_feature_ownership(self):
        paths={'global.json','Kordra.slnx','tests/ArtifactManagement.Domain.Tests/OCIImageDigestTests.cs',
               '.github/workflows/product-ci.yml'}
        # Shared bootstrap files require every owner, including ADR-0010's Inventory owner.
        specs=list((amr.ROOT/'docs/features').glob('*/features/*.md'))
        specs+=list((amr.ROOT/'docs/engineering/ai-orchestration/features').glob('*.md'))
        owners={}
        for spec in sorted(specs):
            meta=changeguard.frontmatter(spec.read_text('utf-8'))
            mappings=json.loads(meta.get('source_paths','[]'))+json.loads(meta.get('test_paths','[]'))
            owned={path for path in paths if any(changeguard.matches(mapping,path) for mapping in mappings)}
            if owned:
                owners[spec.relative_to(amr.ROOT).as_posix()]=(meta['feature_id'],owned)
        expected_owners={
            'docs/features/artifact-management/features/immutable-artifact.md':'FEAT-ART-001',
            'docs/features/infrastructure-inventory/features/register-target.md':'FEAT-INV-001',
        }
        for spec,feature_id in expected_owners.items():
            with self.subTest(expected_owner=feature_id):
                self.assertIn(spec,owners)
                self.assertEqual(owners[spec][0],feature_id)
                self.assertTrue({'Kordra.slnx','.github/workflows/product-ci.yml'} <= owners[spec][1])
        _,errors=changeguard.check_changes(amr.ROOT,paths|set(owners))
        self.assertEqual(errors,[])
        for spec,(feature_id,owned) in sorted(owners.items()):
            with self.subTest(omitted_owner=feature_id):
                _,errors=changeguard.check_changes(amr.ROOT,paths|(set(owners)-{spec}))
                self.assertCountEqual(errors,[
                    f'STALE {path} belongs to {feature_id} ({spec}) but feature doc not updated in PR'
                    for path in sorted(owned)
                ])
        _,errors=changeguard.check_changes(amr.ROOT,{'tests/UnownedTests.cs'})
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
    def cached_fixture(self):
        tmp=tempfile.TemporaryDirectory(); self.addCleanup(tmp.cleanup)
        root=Path(tmp.name)
        for name,text in {'Kordra.slnx':'<Solution />','Source.cs':'class Source {}',
                          'Tests.cs':'class Tests {}','AGENTS.md':'Rules',
                          'docs/spec.md':'Feature spec','NuGet.Config':'<configuration />'}.items():
            p=root/name;p.parent.mkdir(parents=True,exist_ok=True);p.write_text(text)
        def command(cmd,**kwargs):
            return subprocess.CompletedProcess(cmd,0,'stable SDK' if cmd==['dotnet','--info'] else 'passed','')
        self.run=self.enterContext(patch.object(amr.subprocess,'run',side_effect=command))
        return root,amr.gates(root,'base',validation_context='fixture-epoch-1')

    def test_docs_only_change_reuses_product_but_runs_docs_mapping_and_tooling_tests(self):
        root,previous=self.cached_fixture();(root/'docs/spec.md').write_text('Updated behavior evidence')
        result=amr.gates(root,'base',reuse=previous,validation_context='fixture-epoch-1')
        self.assertEqual(result['local_status'],'passed')
        self.assertNotEqual(previous['content'],result['content'])
        self.assertEqual({c['name'] for c in result['checks'] if c['execution']=='reused'},
                         {'product-restore','product-build','product-test'})
        for c in result['checks']:
            if c['execution']=='reused':self.assertEqual(c['reuse']['content'],previous['content'])

    def test_unchanged_inputs_reuse_unit_and_product_with_original_timestamp(self):
        root,previous=self.cached_fixture()
        second=amr.gates(root,'base',reuse=previous,validation_context='fixture-epoch-1')
        third=amr.gates(root,'base',reuse=second,validation_context='fixture-epoch-1')
        for old,new in zip(previous['checks'],third['checks']):
            if old['name'] in {'unit','product-restore','product-build','product-test'}:
                self.assertEqual(new['execution'],'reused')
                self.assertEqual(new['reuse']['executed_at'],old['executed_at'])
            else:self.assertEqual(new['execution'],'executed')

    def test_source_tests_config_and_instruction_changes_invalidate_cached_checks(self):
        root,previous=self.cached_fixture()
        for name in ('Source.cs','Tests.cs','NuGet.Config','AGENTS.md'):
            path=root/name;old=path.read_text();path.write_text(old+' changed')
            with self.subTest(path=name):
                result=amr.gates(root,'base',reuse=previous,validation_context='fixture-epoch-1')
                self.assertFalse(any(c['execution']=='reused' for c in result['checks']))
            path.write_text(old)

    def test_environment_toolchain_and_fixture_epoch_changes_invalidate_reuse(self):
        root,previous=self.cached_fixture()
        with patch.dict('os.environ',{'AMR_TEST_INPUT_CHANGED':'yes'}):
            changed=amr.gates(root,'base',reuse=previous,validation_context='fixture-epoch-1')
        self.assertFalse(any(c['execution']=='reused' for c in changed['checks']))
        changed=amr.gates(root,'base',reuse=previous,validation_context='fixture-epoch-2')
        self.assertFalse(any(c['execution']=='reused' for c in changed['checks']))
        self.run.side_effect=lambda cmd,**kw:subprocess.CompletedProcess(cmd,0,'new SDK','')
        changed=amr.gates(root,'base',reuse=previous,validation_context='fixture-epoch-1')
        self.assertTrue(all(c['execution']=='executed' for c in changed['checks'] if c['name'].startswith('product-')))

    def test_no_fixture_epoch_always_executes_integration_tests(self):
        root,_=self.cached_fixture();previous=amr.gates(root,'base')
        current=amr.gates(root,'base',reuse=previous)
        checks={c['name']:c for c in current['checks']}
        self.assertEqual(checks['product-build']['execution'],'reused')
        self.assertEqual(checks['product-test']['execution'],'executed')

    def test_native_tool_call_correlation_is_not_a_build_or_test_input(self):
        with patch.dict('os.environ',{'CODEX_TOOL_CALL_ID':'first-call'}):
            root,previous=self.cached_fixture()
        with patch.dict('os.environ',{'CODEX_TOOL_CALL_ID':'next-call'}):
            current=amr.gates(root,'base',reuse=previous,validation_context='fixture-epoch-1')
        self.assertEqual({c['name'] for c in current['checks'] if c['execution']=='reused'},
                         {'unit','product-restore','product-build','product-test'})

    def test_malformed_failed_stale_and_legacy_receipts_never_supply_reused_gate(self):
        _,previous=self.cached_fixture();old=next(c for c in previous['checks'] if c['name']=='unit')
        cases=[None,{},dict(previous,schemaVersion=99),dict(previous,local_status='failed'),dict(previous,source_stable=False),
               dict(previous,checks=[None]),dict(previous,checks=[old,old])]
        for delta in ({'execution':'unknown'},{'executed_at':None},{'executed_at':'invalid'}, {'execution':'reused','reuse':[]},
                      {'executed_at':(datetime.datetime.now(datetime.timezone.utc)-datetime.timedelta(hours=2)).isoformat()},
                      {'executed_at':(datetime.datetime.now(datetime.timezone.utc)+datetime.timedelta(hours=2)).isoformat()},
                      {'exit_code':1},{'status':'failed'},{'command':['wrong']},{'input_digest':None}):
            cases.append(dict(previous,checks=[dict(old,**delta)]))
        for receipt in cases:
            with self.subTest(receipt=receipt):
                self.assertIsNone(amr.reusable_gate(receipt,'unit',old['command'],old['input_digest'],'fixture-epoch-1'))

    def test_environment_changes_during_run_invalidate_passing_checks(self):
        root,_=self.cached_fixture()
        def command(cmd,**kw):
            if cmd[0]!='dotnet':amr.os.environ['AMR_DURING_RUN']='changed'
            return subprocess.CompletedProcess(cmd,0,'stable SDK' if cmd==['dotnet','--info'] else 'passed','')
        with patch.dict('os.environ',{}),patch.object(amr.subprocess,'run',side_effect=command):
            result=amr.gates(root,'base')
        self.assertFalse(result['source_stable']);self.assertEqual(result['local_status'],'failed')

    def test_serial_product_suite_has_bounded_room_without_extending_other_gates(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'Kordra.slnx').write_text('<Solution />')
            def command(cmd,**kwargs):
                if cmd[:2]==['dotnet','test']:
                    # The expanded serial fixture suite legitimately exceeds the old 90s cap.
                    if kwargs['timeout'] < 120:raise subprocess.TimeoutExpired(cmd,kwargs['timeout'])
                    self.assertLessEqual(kwargs['timeout'],300)
                elif cmd!=['dotnet','--info']:
                    self.assertEqual(kwargs['timeout'],90)
                return subprocess.CompletedProcess(cmd,0,'stable SDK' if cmd==['dotnet','--info'] else 'passed','')
            with patch.object(amr.subprocess,'run',side_effect=command):result=amr.gates(root)
            self.assertEqual(result['local_status'],'passed')

    def test_failed_product_restore_blocks_build_and_stale_binary_tests(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'Kordra.slnx').write_text('<Solution />')
            def command(cmd,**kwargs):
                return subprocess.CompletedProcess(cmd,1 if cmd[0]=='dotnet' else 0,'','')
            with patch.object(amr.subprocess,'run',side_effect=command) as run:
                result=amr.gates(root)
            stages=[x['status'] for x in result['checks'] if x['name'].startswith('product-')]
            self.assertEqual(stages,['failed','blocked','blocked'])
            self.assertEqual([x.args[0][1] for x in run.call_args_list
                              if x.args[0][0]=='dotnet' and x.args[0][1]!='--info'],['restore'])
            self.assertEqual(result['local_status'],'failed')

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
