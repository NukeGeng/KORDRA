ALTER TABLE wfl.authority_bindings DROP CONSTRAINT ck_authority_bindings_versions;

ALTER TABLE wfl.authority_bindings ADD CONSTRAINT ck_authority_bindings_versions CHECK ((generation = 1 AND expected_version = 0 AND resulting_version = 1) OR (expected_version > 0 AND expected_version < 9223372036854775807 AND resulting_version = expected_version + 1));
