"""Prepare synthetic MSG manifests for the existing ephemeral GitHub-hosted CI service.

No SQL execution, credential lookup, role/HBA change or local operational approval.
Environment declarations are a misuse guard, not authenticated infrastructure identity.
"""
from __future__ import annotations

import hashlib
import json
import os
from pathlib import Path
import subprocess
import uuid


def require_hosted_ci(environment: dict[str, str], repository: Path) -> tuple[Path, Path]:
    if (environment.get('GITHUB_ACTIONS') != 'true'
            or environment.get('RUNNER_ENVIRONMENT') != 'github-hosted'
            or environment.get('GITHUB_REPOSITORY') != 'NukeGeng/KORDRA'
            or environment.get('RUNNER_OS') != 'Linux'
            or environment.get('GITHUB_EVENT_NAME') not in {'pull_request', 'push'}
            or not environment.get('GITHUB_RUN_ID', '').isdigit()
            or not environment.get('GITHUB_RUN_ATTEMPT', '').isdigit()):
        raise ValueError('Ephemeral Product GitHub-hosted CI context required; no local approval is granted.')
    for key in ('GITHUB_WORKSPACE', 'RUNNER_TEMP', 'GITHUB_ENV'):
        if not environment.get(key) or any(c in environment[key] for c in '\r\n\0'):
            raise ValueError('Valid CI workspace and temporary-file paths required.')
    if Path(environment['GITHUB_WORKSPACE']).resolve() != repository.resolve():
        raise ValueError('CI workspace must match this Product repository.')
    temporary = Path(environment['RUNNER_TEMP']).resolve(strict=True)
    exported = Path(environment['GITHUB_ENV'])
    if (exported.is_symlink() or not exported.is_file()
            or not exported.resolve(strict=True).is_relative_to(temporary)):
        raise ValueError('CI environment output must be an existing file within runner temporary storage.')
    return temporary, exported


def write_manifest(path: Path, document: dict) -> None:
    # Refuse overwrites, including a pre-existing symlink. All data here is nonsecret synthetic identity.
    with path.open('x', encoding='utf-8') as output:
        json.dump(document, output, indent=2)
        output.write('\n')
    path.chmod(0o600)


def prepare(repository: Path, environment: dict[str, str], generate_ddl) -> dict[str, str]:
    temporary, env_output = require_hosted_ci(environment, repository)
    directory = temporary / ('kordra-msg-' + uuid.uuid4().hex)
    directory.mkdir(mode=0o700)
    common = {
        'status': 'approved_for_execution',
        'authorizationBasis': 'repository Product CI: isolated synthetic ephemeral PostgreSQL service only',
        'target': '127.0.0.1:5432/kordra_test',
        'identity': 'kordra_test',
        'runId': environment['GITHUB_RUN_ID'],
        'runAttempt': environment['GITHUB_RUN_ATTEMPT'],
        'localOperationalPermission': False,
    }
    compatibility = directory / 'compatibility.json'
    write_manifest(compatibility, dict(common, purpose='T12.1a-compatibility',
                                     schemas=['t12_' + uuid.uuid4().hex for _ in range(8)]))
    exports = {'KORDRA_MSG_COMPAT_FIXTURE_MANIFEST': str(compatibility)}
    for purpose, command, variable in [
        ('T12.1d-receipts', 'receipt-fixture-ddl', 'KORDRA_MSG_RECEIPT_FIXTURE_MANIFEST'),
        ('T12.1e-ingress', 'ingress-fixture-ddl', 'KORDRA_MSG_INGRESS_FIXTURE_MANIFEST'),
        ('T12.1e-timer', 'ingress-fixture-ddl', 'KORDRA_MSG_TIMER_FIXTURE_MANIFEST'),
        ('T12.1e-retry', 'retry-fixture-ddl', 'KORDRA_MSG_RETRY_FIXTURE_MANIFEST'),
        ('T12.1e-metadata', 'ingress-fixture-ddl', 'KORDRA_MSG_METADATA_FIXTURE_MANIFEST'),
        ('T12.1e-exhaustion', 'exhaustion-fixture-ddl', 'KORDRA_MSG_EXHAUSTION_FIXTURE_MANIFEST'),
        ('T12.1e-lifecycle', 'ingress-fixture-ddl', 'KORDRA_MSG_LIFECYCLE_FIXTURE_MANIFEST'),
    ]:
        case = uuid.uuid4()
        sql = generate_ddl(command, case.hex)
        if not isinstance(sql, bytes) or not sql.startswith(('CREATE SCHEMA t12_' + case.hex + ';').encode()):
            raise ValueError('Offline native fixture DDL must begin with the exact fresh UUID schema.')
        sql_path = directory / (purpose + '.sql')
        with sql_path.open('xb') as output:
            output.write(sql)
        sql_path.chmod(0o600)
        manifest = directory / (purpose + '.json')
        additional = {}
        if purpose == 'T12.1e-lifecycle':
            cleanup = generate_ddl('lifecycle-cleanup-sql', case.hex)
            expected = ('delete from \"t12_' + case.hex + '\".wolverine_incoming_envelopes').encode()
            if not isinstance(cleanup, bytes) or not cleanup.startswith(expected):
                raise ValueError('Exact lifecycle native cleanup SQL required.')
            cleanup_path = directory / (purpose + '-native-cleanup.sql')
            with cleanup_path.open('xb') as output:
                output.write(cleanup)
            cleanup_path.chmod(0o600)
            additional['nativeCleanupSha256'] = hashlib.sha256(cleanup).hexdigest()
        write_manifest(manifest, dict(common, purpose=purpose, caseId=str(case),
                                    creationSha256=hashlib.sha256(sql).hexdigest(), **additional))
        exports[variable] = str(manifest)
    # Do not export child stdout/stderr to public artifacts: only tests and nonsecret manifests are required.
    exports['KORDRA_MSG_COMPAT_PROCESS_LOG_ROOT'] = str(directory)
    with env_output.open('a', encoding='utf-8') as output:
        for key, value in exports.items():
            output.write(key + '=' + value + '\n')
    return exports


def main() -> None:
    repository = Path(__file__).resolve().parents[2]
    # Validate before spawning even a review-only executable.
    require_hosted_ci(dict(os.environ), repository)
    assembly = repository / 'tests/Messaging.Tests/bin/Release/net10.0/Messaging.Tests.dll'
    if not assembly.is_file() or assembly.is_symlink():
        raise ValueError('Build the pinned Release MSG test assembly before CI fixture preparation.')

    def generate_ddl(command: str, case: str) -> bytes:
        result = subprocess.run(['dotnet', str(assembly), command, case], cwd=repository,
                                capture_output=True, timeout=30, check=False)
        if result.returncode:
            raise RuntimeError('Offline fixture generation failed; no database operation was performed.')
        return result.stdout

    exports = prepare(repository, dict(os.environ), generate_ddl)
    print('Prepared', len(exports) - 1, 'nonsecret MSG CI manifests; no database operation performed.')


if __name__ == '__main__':
    main()
