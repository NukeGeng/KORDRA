CREATE TABLE wfl.cancellation_intents (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    request_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    specification_hash character varying(64) NOT NULL,
    command_hash character varying(64) NOT NULL,
    expected_version bigint NOT NULL,
    resulting_version bigint NOT NULL,
    recorded_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_cancellation_intents" PRIMARY KEY (installation_id, tenant_id, request_id),
    CONSTRAINT ck_cancellation_intents_actor_id CHECK (actor_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_cancellation_intents_command_hash CHECK (command_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_cancellation_intents_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_cancellation_intents_instance_id CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_cancellation_intents_request_id CHECK (request_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_cancellation_intents_resulting_version CHECK (resulting_version > 0),
    CONSTRAINT ck_cancellation_intents_specification_hash CHECK (specification_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_cancellation_intents_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_cancellation_versions CHECK (expected_version > 0 AND expected_version < 9223372036854775807 AND resulting_version = expected_version + 1),
    CONSTRAINT "FK_cancellation_intents_instances_installation_id_tenant_id_in~" FOREIGN KEY (installation_id, tenant_id, instance_id) REFERENCES wfl.instances (installation_id, tenant_id, instance_id) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_cancellation_intents_installation_id_tenant_id_instance_id" ON wfl.cancellation_intents (installation_id, tenant_id, instance_id);
