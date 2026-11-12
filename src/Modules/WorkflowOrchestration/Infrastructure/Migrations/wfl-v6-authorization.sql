ALTER TABLE wfl.authority_bindings ADD command_hash character varying(64) NOT NULL;

ALTER TABLE wfl.authority_bindings ADD expected_version bigint NOT NULL;

ALTER TABLE wfl.authority_bindings ADD request_id uuid NOT NULL;

ALTER TABLE wfl.authority_bindings ADD resulting_version bigint NOT NULL;

ALTER TABLE wfl.authority_bindings ADD specification_hash character varying(64) NOT NULL;

CREATE UNIQUE INDEX "IX_authority_bindings_installation_id_tenant_id_instance_id_re~" ON wfl.authority_bindings (installation_id, tenant_id, instance_id, request_id);

ALTER TABLE wfl.authority_bindings ADD CONSTRAINT ck_authority_bindings_command_hash CHECK (command_hash ~ '^[0-9a-f]{64}$');

ALTER TABLE wfl.authority_bindings ADD CONSTRAINT ck_authority_bindings_request_id CHECK (request_id <> '00000000-0000-0000-0000-000000000000'::uuid);

ALTER TABLE wfl.authority_bindings ADD CONSTRAINT ck_authority_bindings_resulting_version CHECK (resulting_version > 0);

ALTER TABLE wfl.authority_bindings ADD CONSTRAINT ck_authority_bindings_specification_hash CHECK (specification_hash ~ '^[0-9a-f]{64}$');

ALTER TABLE wfl.authority_bindings ADD CONSTRAINT ck_authority_bindings_versions CHECK (expected_version > 0 AND expected_version < 9223372036854775807 AND resulting_version = expected_version + 1);
