DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM pg_namespace WHERE nspname = 'wfl') THEN
        CREATE SCHEMA wfl;
    END IF;
END $EF$;


CREATE TABLE wfl.clock_floor (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    floor_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_clock_floor" PRIMARY KEY (installation_id, tenant_id),
    CONSTRAINT ck_clock_floor_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_clock_floor_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid)
);


CREATE TABLE wfl.instances (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    request_id uuid NOT NULL,
    specification_hash character varying(64) NOT NULL,
    specification bytea NOT NULL,
    state integer NOT NULL,
    version bigint NOT NULL,
    created_at_utc timestamp with time zone NOT NULL,
    deadline_utc timestamp with time zone NOT NULL,
    deadline_exceeded boolean NOT NULL,
    CONSTRAINT "PK_instances" PRIMARY KEY (installation_id, tenant_id, instance_id),
    CONSTRAINT "AK_instances_installation_id_tenant_id_instance_id_request_id" UNIQUE (installation_id, tenant_id, instance_id, request_id),
    CONSTRAINT ck_instances_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_instances_instance_id CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_instances_lifetime CHECK (deadline_utc > created_at_utc AND deadline_utc <= created_at_utc + interval '24 hours'),
    CONSTRAINT ck_instances_request_id CHECK (request_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_instances_spec_size CHECK (octet_length(specification) BETWEEN 2 AND 65536),
    CONSTRAINT ck_instances_specification_hash CHECK (specification_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_instances_state CHECK (state BETWEEN 1 AND 10),
    CONSTRAINT ck_instances_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_instances_version CHECK (version > 0)
);


CREATE TABLE wfl.authority_bindings (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    binding_id uuid NOT NULL,
    delegation_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    generation bigint NOT NULL,
    recorded_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_authority_bindings" PRIMARY KEY (installation_id, tenant_id, instance_id, binding_id),
    CONSTRAINT ck_authority_bindings_actor_id CHECK (actor_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_authority_bindings_binding_id CHECK (binding_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_authority_bindings_delegation_id CHECK (delegation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_authority_bindings_generation CHECK (generation > 0),
    CONSTRAINT ck_authority_bindings_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_authority_bindings_instance_id CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_authority_bindings_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_authority_bindings_instances_installation_id_tenant_id_inst~" FOREIGN KEY (installation_id, tenant_id, instance_id) REFERENCES wfl.instances (installation_id, tenant_id, instance_id) ON DELETE RESTRICT
);


CREATE TABLE wfl.request_receipts (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    request_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    content_hash character varying(64) NOT NULL,
    result_json jsonb NOT NULL,
    recorded_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_request_receipts" PRIMARY KEY (installation_id, tenant_id, request_id),
    CONSTRAINT ck_receipt_size CHECK (octet_length(result_json::text) <= 16384),
    CONSTRAINT ck_request_receipts_content_hash CHECK (content_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_request_receipts_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_request_receipts_instance_id CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_request_receipts_request_id CHECK (request_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_request_receipts_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_request_receipts_instances_installation_id_tenant_id_instan~" FOREIGN KEY (installation_id, tenant_id, instance_id, request_id) REFERENCES wfl.instances (installation_id, tenant_id, instance_id, request_id) ON DELETE RESTRICT
);


CREATE TABLE wfl.steps (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    step_id uuid NOT NULL,
    ordinal integer NOT NULL,
    state integer NOT NULL,
    version bigint NOT NULL,
    CONSTRAINT "PK_steps" PRIMARY KEY (installation_id, tenant_id, instance_id, step_id),
    CONSTRAINT ck_steps_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_steps_instance_id CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_steps_ordinal CHECK (ordinal BETWEEN 0 AND 31),
    CONSTRAINT ck_steps_state CHECK (state BETWEEN 1 AND 6),
    CONSTRAINT ck_steps_step_id CHECK (step_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_steps_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_steps_version CHECK (version > 0),
    CONSTRAINT "FK_steps_instances_installation_id_tenant_id_instance_id" FOREIGN KEY (installation_id, tenant_id, instance_id) REFERENCES wfl.instances (installation_id, tenant_id, instance_id) ON DELETE RESTRICT
);


CREATE TABLE wfl.operation_custody (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    step_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    specification_hash character varying(64) NOT NULL,
    worker_id uuid NOT NULL,
    worker_boot_id uuid NOT NULL,
    epoch bigint NOT NULL,
    version bigint NOT NULL,
    started_at_utc timestamp with time zone NOT NULL,
    heartbeat_at_utc timestamp with time zone NOT NULL,
    phase integer NOT NULL,
    last_commit_receipt_id uuid,
    CONSTRAINT "PK_operation_custody" PRIMARY KEY (installation_id, tenant_id, instance_id, step_id, operation_id),
    CONSTRAINT ck_custody_phase CHECK (phase BETWEEN 1 AND 4),
    CONSTRAINT ck_custody_times CHECK (heartbeat_at_utc >= started_at_utc),
    CONSTRAINT ck_operation_custody_epoch CHECK (epoch > 0),
    CONSTRAINT ck_operation_custody_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_operation_custody_instance_id CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_operation_custody_operation_id CHECK (operation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_operation_custody_specification_hash CHECK (specification_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_operation_custody_step_id CHECK (step_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_operation_custody_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_operation_custody_version CHECK (version > 0),
    CONSTRAINT ck_operation_custody_worker_boot_id CHECK (worker_boot_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_operation_custody_worker_id CHECK (worker_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_operation_custody_steps_installation_id_tenant_id_instance_~" FOREIGN KEY (installation_id, tenant_id, instance_id, step_id) REFERENCES wfl.steps (installation_id, tenant_id, instance_id, step_id) ON DELETE RESTRICT
);


CREATE TABLE wfl.timers (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    step_id uuid NOT NULL,
    timer_id uuid NOT NULL,
    generation bigint NOT NULL,
    version bigint NOT NULL,
    status integer NOT NULL,
    due_at_utc timestamp with time zone NOT NULL,
    scheduler_attempts integer NOT NULL,
    CONSTRAINT "PK_timers" PRIMARY KEY (installation_id, tenant_id, instance_id, step_id, timer_id, generation),
    CONSTRAINT ck_timers_budget CHECK (scheduler_attempts BETWEEN 0 AND 10),
    CONSTRAINT ck_timers_generation CHECK (generation > 0),
    CONSTRAINT ck_timers_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_timers_instance_id CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_timers_status CHECK (status BETWEEN 1 AND 3),
    CONSTRAINT ck_timers_step_id CHECK (step_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_timers_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_timers_timer_id CHECK (timer_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_timers_version CHECK (version > 0),
    CONSTRAINT "FK_timers_steps_installation_id_tenant_id_instance_id_step_id" FOREIGN KEY (installation_id, tenant_id, instance_id, step_id) REFERENCES wfl.steps (installation_id, tenant_id, instance_id, step_id) ON DELETE RESTRICT
);


CREATE TABLE wfl.transitions (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    step_id uuid NOT NULL,
    resulting_version bigint NOT NULL,
    resulting_state integer NOT NULL,
    specification_hash character varying(64) NOT NULL,
    recorded_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_transitions" PRIMARY KEY (installation_id, tenant_id, instance_id, operation_id),
    CONSTRAINT ck_transitions_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_transitions_instance_id CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_transitions_operation_id CHECK (operation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_transitions_resulting_version CHECK (resulting_version > 0),
    CONSTRAINT ck_transitions_specification_hash CHECK (specification_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_transitions_state CHECK (resulting_state BETWEEN 1 AND 10),
    CONSTRAINT ck_transitions_step_id CHECK (step_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_transitions_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_transitions_steps_installation_id_tenant_id_instance_id_ste~" FOREIGN KEY (installation_id, tenant_id, instance_id, step_id) REFERENCES wfl.steps (installation_id, tenant_id, instance_id, step_id) ON DELETE RESTRICT
);


CREATE TABLE wfl.recovery_decisions (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    decision_id uuid NOT NULL,
    step_id uuid NOT NULL,
    operation_id uuid NOT NULL,
    kind integer NOT NULL,
    evidence_hash character varying(64) NOT NULL,
    specification_hash character varying(64) NOT NULL,
    resulting_version bigint NOT NULL,
    recorded_at_utc timestamp with time zone NOT NULL,
    CONSTRAINT "PK_recovery_decisions" PRIMARY KEY (installation_id, tenant_id, instance_id, decision_id),
    CONSTRAINT ck_recovery_decisions_decision_id CHECK (decision_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_recovery_decisions_evidence_hash CHECK (evidence_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_recovery_decisions_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_recovery_decisions_instance_id CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_recovery_decisions_operation_id CHECK (operation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_recovery_decisions_resulting_version CHECK (resulting_version > 0),
    CONSTRAINT ck_recovery_decisions_specification_hash CHECK (specification_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_recovery_decisions_step_id CHECK (step_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_recovery_decisions_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_recovery_kind CHECK (kind BETWEEN 1 AND 2),
    CONSTRAINT "FK_recovery_decisions_operation_custody_installation_id_tenant~" FOREIGN KEY (installation_id, tenant_id, instance_id, step_id, operation_id) REFERENCES wfl.operation_custody (installation_id, tenant_id, instance_id, step_id, operation_id) ON DELETE RESTRICT
);


CREATE UNIQUE INDEX "IX_authority_bindings_installation_id_tenant_id_instance_id_ge~" ON wfl.authority_bindings (installation_id, tenant_id, instance_id, generation);


CREATE UNIQUE INDEX "IX_instances_installation_id_tenant_id_request_id" ON wfl.instances (installation_id, tenant_id, request_id);


CREATE INDEX "IX_recovery_decisions_installation_id_tenant_id_instance_id_st~" ON wfl.recovery_decisions (installation_id, tenant_id, instance_id, step_id, operation_id);


CREATE INDEX "IX_request_receipts_installation_id_tenant_id_instance_id_requ~" ON wfl.request_receipts (installation_id, tenant_id, instance_id, request_id);


CREATE UNIQUE INDEX "IX_steps_installation_id_tenant_id_instance_id_ordinal" ON wfl.steps (installation_id, tenant_id, instance_id, ordinal);


CREATE INDEX "IX_timers_installation_id_tenant_id_status_due_at_utc_timer_id" ON wfl.timers (installation_id, tenant_id, status, due_at_utc, timer_id);


CREATE UNIQUE INDEX "IX_transitions_installation_id_tenant_id_instance_id_resulting~" ON wfl.transitions (installation_id, tenant_id, instance_id, resulting_version);


CREATE INDEX "IX_transitions_installation_id_tenant_id_instance_id_step_id" ON wfl.transitions (installation_id, tenant_id, instance_id, step_id);


