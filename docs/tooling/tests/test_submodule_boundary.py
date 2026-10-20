"""FEAT-CLIENTS-001: actual Git isolation and negative topology regressions."""
import contextlib
import io
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import amr
import changeguard
import doccheck
import submodulecheck


class ClientsBoundary(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name) / 'product'
        self.root.mkdir()
        self.run_git(self.root, 'init', '-b', 'main')
        self.configure(self.root)
        (self.root / 'README.md').write_text('Product\n')
        self.run_git(self.root, 'add', 'README.md')
        self.run_git(self.root, 'commit', '-m', 'Product fixture')

    def run_git(self, cwd, *args):
        cp = subprocess.run(['git', *args], cwd=cwd, text=True, capture_output=True)
        self.assertEqual(cp.returncode, 0, cp.stderr)
        return cp.stdout.strip()

    def configure(self, root):
        self.run_git(root, 'config', 'user.name', 'Fixture')
        self.run_git(root, 'config', 'user.email', 'fixture@example.invalid')
        self.run_git(root, 'config', 'commit.gpgsign', 'false')

    def declaration(self, path='clients', url=submodulecheck.CLIENT_URL):
        (self.root / '.gitmodules').write_text(
            f'[submodule "clients"]\n\tpath = {path}\n\turl = {url}\n')

    def submodule(self):
        source = Path(self.temp.name) / 'client-source'
        source.mkdir()
        self.run_git(source, 'init', '-b', 'main')
        self.configure(source)
        (source / 'README.md').write_text('Frontend fixture\n')
        self.run_git(source, 'add', 'README.md')
        self.run_git(source, 'commit', '-m', 'Clients fixture')
        self.run_git(self.root, '-c', 'protocol.file.allow=always',
                     'submodule', 'add', str(source), 'clients')
        self.declaration()
        self.run_git(self.root, 'add', '.gitmodules', 'clients')
        self.configure(self.root / 'clients')
        return self.root / 'clients'

    def test_real_submodule_checkout_and_index_pin_match(self):
        clients = self.submodule()
        self.assertTrue((clients / '.git').is_file())
        result = submodulecheck.check(self.root, require_checkout=True)
        self.assertEqual(result['pin'], self.run_git(clients, 'rev-parse', 'HEAD'))

    def test_uninitialized_checkout_is_valid_only_for_pin_metadata_check(self):
        self.declaration()
        self.run_git(self.root, 'update-index', '--add', '--cacheinfo',
                     '160000,' + 'a' * 40 + ',clients')
        self.assertEqual(submodulecheck.check(self.root)['status'], 'passed')
        with self.assertRaisesRegex(ValueError, 'not initialized'):
            submodulecheck.check(self.root, require_checkout=True)

    def test_extra_or_escaping_boundaries_and_urls_are_rejected(self):
        for path in ('docs', 'src', '../clients', '/tmp/clients', 'clients/nested'):
            with self.subTest(path=path):
                self.declaration(path)
                with self.assertRaises(ValueError):
                    submodulecheck.configured_roots(self.root)
        self.declaration(url='https://example.invalid/other.git')
        with self.assertRaises(ValueError):
            submodulecheck.configured_roots(self.root)
        self.declaration()
        with (self.root / '.gitmodules').open('a') as stream:
            stream.write('[submodule "hidden"]\npath = docs\nurl = other\n')
        with self.assertRaises(ValueError):
            submodulecheck.configured_roots(self.root)

    def test_symlinks_and_malformed_config_cannot_hide_product_content(self):
        (self.root / 'declaration').write_text('not config')
        (self.root / '.gitmodules').symlink_to('declaration')
        with self.assertRaisesRegex(ValueError, 'symlink'):
            submodulecheck.configured_roots(self.root)
        (self.root / '.gitmodules').unlink()
        (self.root / '.gitmodules').write_text('not config')
        with patch.object(doccheck, 'ROOT', self.root), patch.object(sys, 'argv', ['doccheck']), \
                contextlib.redirect_stdout(io.StringIO()) as output:
            self.assertEqual(doccheck.main(), 1)
        self.assertIn('invalid .gitmodules', output.getvalue())
        self.declaration()
        (self.root / 'clients').symlink_to('README.md')
        with self.assertRaisesRegex(ValueError, 'symlink'):
            submodulecheck.configured_roots(self.root)

    def test_tracked_frontend_files_are_not_accepted_as_gitlink(self):
        self.declaration()
        (self.root / 'clients').mkdir()
        (self.root / 'clients/README.md').write_text('still Product-owned')
        self.run_git(self.root, 'add', 'clients')
        with self.assertRaisesRegex(ValueError, 'stage-0 gitlink'):
            submodulecheck.pinned_clients(self.root)

    def test_wrong_revision_and_dirty_ci_checkout_fail(self):
        clients = self.submodule()
        (clients / 'README.md').write_text('changed')
        with self.assertRaisesRegex(ValueError, 'must be clean'):
            submodulecheck.check(self.root, True)
        self.run_git(clients, 'add', 'README.md')
        self.run_git(clients, 'commit', '-m', 'Independent frontend change')
        with self.assertRaisesRegex(ValueError, 'does not match'):
            submodulecheck.check(self.root, True)
        self.run_git(self.root, 'add', 'clients')
        self.assertEqual(submodulecheck.check(self.root, True)['status'], 'passed')

    def test_product_snapshot_ignores_frontend_work_until_pin_is_staged(self):
        clients = self.submodule()
        before = amr.tree_identity(self.root)
        git_before = amr.git_identity(self.root)
        (clients / 'README.md').write_text('Independent work')
        (clients / 'new.md').write_text('No Product metadata')
        self.assertEqual(before, amr.tree_identity(self.root))
        self.assertEqual(git_before, amr.git_identity(self.root))
        self.run_git(clients, 'add', 'README.md', 'new.md')
        self.run_git(clients, 'commit', '-m', 'Independent frontend work')
        self.assertEqual(before, amr.tree_identity(self.root))
        self.assertEqual(git_before, amr.git_identity(self.root))
        self.run_git(self.root, 'add', 'clients')
        self.assertNotEqual(before, amr.tree_identity(self.root))
        self.assertNotEqual(git_before, amr.git_identity(self.root))
        after_pin = amr.tree_identity(self.root)
        (self.root / 'README.md').write_text('Product work')
        self.assertNotEqual(after_pin, amr.tree_identity(self.root))

    def test_only_registered_root_is_excluded_from_product_docs(self):
        clients = self.submodule()
        (clients / 'unowned.md').write_text('Frontend document')
        nested = self.root / 'docs/clients'
        nested.mkdir(parents=True)
        (nested / 'unowned.md').write_text('Product document')
        scanned = {p.relative_to(self.root).as_posix() for p in doccheck.markdown_files(self.root)}
        self.assertNotIn('clients/unowned.md', scanned)
        self.assertIn('docs/clients/unowned.md', scanned)
        (self.root / '.gitmodules').unlink()
        scanned = {p.relative_to(self.root).as_posix() for p in doccheck.markdown_files(self.root)}
        self.assertIn('clients/unowned.md', scanned)

    def test_pin_changes_require_owning_feature_document_update(self):
        feature = 'docs/engineering/ai-orchestration/features/clients-repository.md'
        for path in ('.gitmodules', 'clients'):
            _, errors = changeguard.check_changes(amr.ROOT, {path})
            self.assertTrue(any('STALE' in error for error in errors))
            _, errors = changeguard.check_changes(amr.ROOT, {path, feature})
            self.assertEqual(errors, [])


if __name__ == '__main__':
    unittest.main()
