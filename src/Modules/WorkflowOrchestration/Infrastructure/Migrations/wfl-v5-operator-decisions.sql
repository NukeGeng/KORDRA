
CREATE TABLE wfl.operator_decisions (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    request_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    kind integer NOT NULL,
    expected_version bigint NOT NULL,
    resulting_version bigint NOT NULL,
    command_hash character varying(64) NOT NULL,
    evidence_hash character varying(64) NOT NULL,
    specification_hash character varying(64) NOT NULL,
    recorded_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_operator_decisions" PRIMARY KEY (installation_id, tenant_id, request_id),
    CONSTRAINT ck_operator_decision_kind CHECK (kind IN (1,2)),
    CONSTRAINT ck_operator_decision_versions CHECK (expected_version > 0 AND expected_version < 9223372036854775807 AND resulting_version = expected_version + 1),
    CONSTRAINT ck_operator_decisions_actor_id CHECK (actor_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_operator_decisions_command_hash CHECK (command_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_operator_decisions_evidence_hash CHECK (evidence_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_operator_decisions_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_operator_decisions_instance_id CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_operator_decisions_operation_id CHECK (operation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_operator_decisions_request_id CHECK (request_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_operator_decisions_resulting_version CHECK (resulting_version > 0),
    CONSTRAINT ck_operator_decisions_specification_hash CHECK (specification_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_operator_decisions_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_operator_decisions_instances_installation_id_tenant_id_inst~" FOREIGN KEY (installation_id, tenant_id, instance_id) REFERENCES wfl.instances (installation_id, tenant_id, instance_id) ON DELETE RESTRICT
);


CREATE UNIQUE INDEX "IX_operator_decisions_installation_id_tenant_id_instance_id_op~" ON wfl.operator_decisions (installation_id, tenant_id, instance_id, operation_id, kind);
