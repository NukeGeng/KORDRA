import hashlib
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch

import prepare_ci_fixtures as fixtures


class CiFixturePreparationTests(unittest.TestCase):
    def setUp(self):
        self.temporary = tempfile.TemporaryDirectory()
        self.addCleanup(self.temporary.cleanup)
        self.root = Path(self.temporary.name)
        self.output = self.root / 'environment'
        self.output.touch()
        self.env = dict(GITHUB_ACTIONS='true', RUNNER_ENVIRONMENT='github-hosted', RUNNER_OS='Linux',
                        GITHUB_REPOSITORY='NukeGeng/KORDRA', GITHUB_EVENT_NAME='pull_request',
                        GITHUB_RUN_ID='123', GITHUB_RUN_ATTEMPT='1', GITHUB_WORKSPACE=str(self.root),
                        RUNNER_TEMP=str(self.root), GITHUB_ENV=str(self.output))

    def test_manifests_are_distinct_bounded_synthetic_and_hash_exact_offline_bytes(self):
        calls = []

        def ddl(command, case):
            calls.append((command, case))
            if command == 'lifecycle-cleanup-sql':
                return ('delete from \"t12_' + case + '\".wolverine_incoming_envelopes fixture-only').encode()
            return ('CREATE SCHEMA t12_' + case + ';\n').encode()

        exported = fixtures.prepare(self.root, self.env, ddl)
        self.assertEqual(8, len(calls))
        compatibility = json.loads(Path(exported['KORDRA_MSG_COMPAT_FIXTURE_MANIFEST']).read_text())
        schemas = compatibility['schemas']
        self.assertEqual(8, len(set(schemas)))
        for name in schemas:
            self.assertRegex(name, r'^t12_[0-9a-f]{32}$')
        for variable in ('KORDRA_MSG_RECEIPT_FIXTURE_MANIFEST', 'KORDRA_MSG_INGRESS_FIXTURE_MANIFEST', 'KORDRA_MSG_TIMER_FIXTURE_MANIFEST', 'KORDRA_MSG_RETRY_FIXTURE_MANIFEST', 'KORDRA_MSG_METADATA_FIXTURE_MANIFEST', 'KORDRA_MSG_EXHAUSTION_FIXTURE_MANIFEST', 'KORDRA_MSG_LIFECYCLE_FIXTURE_MANIFEST'):
            path = Path(exported[variable])
            document = json.loads(path.read_text())
            self.assertFalse(document['localOperationalPermission'])
            self.assertEqual('kordra_test', document['identity'])
            sql = path.with_suffix('.sql').read_bytes()
            self.assertEqual(hashlib.sha256(sql).hexdigest(), document['creationSha256'])
            if variable == 'KORDRA_MSG_LIFECYCLE_FIXTURE_MANIFEST':
                native_cleanup = path.with_name('T12.1e-lifecycle-native-cleanup.sql').read_bytes()
                self.assertEqual(hashlib.sha256(native_cleanup).hexdigest(), document['nativeCleanupSha256'])
            schemas.append('t12_' + document['caseId'].replace('-', ''))
        self.assertEqual(15, len(set(schemas)))
        self.assertEqual(9, len(self.output.read_text().splitlines()))
        self.assertNotIn('PASSWORD', self.output.read_text())

    def test_local_self_hosted_wrong_repository_or_missing_context_cannot_generate_or_export(self):
        for key, bad in [('GITHUB_ACTIONS', 'false'), ('RUNNER_ENVIRONMENT', 'self-hosted'),
                         ('GITHUB_REPOSITORY', 'NukeGeng/KORDRA-Clients'), ('GITHUB_WORKSPACE', '/elsewhere'),
                         ('RUNNER_OS', 'macOS'), ('GITHUB_EVENT_NAME', 'workflow_dispatch'),
                         ('GITHUB_RUN_ID', '123\nINJECT=value'), ('GITHUB_ENV', str(self.output) + '\ninject')]:
            with self.subTest(key=key):
                changed = dict(self.env, **{key: bad})
                with patch.object(fixtures.subprocess, 'run', side_effect=AssertionError('No subprocess allowed')):
                    with self.assertRaises(ValueError):
                        fixtures.prepare(self.root, changed, lambda *_: self.fail('No generation allowed'))
                self.assertEqual('', self.output.read_text())

    def test_generation_failure_does_not_export_partial_manifests(self):
        with self.assertRaises(ValueError):
            fixtures.prepare(self.root, self.env, lambda *_: b'wrong schema')
        self.assertEqual('', self.output.read_text())

    def test_existing_file_or_symlink_is_never_overwritten(self):
        existing = self.root / 'existing.json'
        existing.write_text('preserve')
        with self.assertRaises(FileExistsError):
            fixtures.write_manifest(existing, {'status': 'new'})
        self.assertEqual('preserve', existing.read_text())
        link = self.root / 'symlink.json'
        link.symlink_to(existing)
        with self.assertRaises(FileExistsError):
            fixtures.write_manifest(link, {'status': 'new'})
        self.assertEqual('preserve', existing.read_text())

    def test_environment_output_cannot_be_a_symlink_or_outside_runner_temp(self):
        link = self.root / 'env-link'
        link.symlink_to(self.output)
        with self.assertRaises(ValueError):
            fixtures.require_hosted_ci(dict(self.env, GITHUB_ENV=str(link)), self.root)
        nested = self.root / 'nested'
        nested.mkdir()
        with self.assertRaises(ValueError):
            fixtures.require_hosted_ci(dict(self.env, RUNNER_TEMP=str(nested)), self.root)


if __name__ == '__main__':
    unittest.main()
