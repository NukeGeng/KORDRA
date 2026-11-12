CREATE TABLE wfl.wake_operations (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    instance_id uuid NOT NULL,
    step_id uuid NOT NULL,
    timer_id uuid NOT NULL,
    generation bigint NOT NULL,
    scheduler_attempt integer NOT NULL,
    custody_epoch bigint NOT NULL,
    scheduler_operation_id uuid NOT NULL,
    wake_operation_id uuid NOT NULL,
    message_id uuid NOT NULL,
    descriptor_hash character varying(64) NOT NULL,
    descriptor bytea NOT NULL,
    CONSTRAINT "PK_wake_operations" PRIMARY KEY (installation_id, tenant_id, instance_id, step_id, timer_id, generation),
    CONSTRAINT ck_wake_operations_budget CHECK (scheduler_attempt BETWEEN 1 AND 10),
    CONSTRAINT ck_wake_operations_descriptor CHECK (octet_length(descriptor) BETWEEN 2 AND 16384),
    CONSTRAINT ck_wake_operations_descriptor_hash CHECK (descriptor_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_wake_operations_epoch CHECK (custody_epoch > 0),
    CONSTRAINT ck_wake_operations_generation CHECK (generation > 0),
    CONSTRAINT ck_wake_operations_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_wake_operations_instance_id CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_wake_operations_message_id CHECK (message_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_wake_operations_operations CHECK (scheduler_operation_id <> wake_operation_id),
    CONSTRAINT ck_wake_operations_scheduler_operation_id CHECK (scheduler_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_wake_operations_step_id CHECK (step_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_wake_operations_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_wake_operations_timer_id CHECK (timer_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_wake_operations_wake_operation_id CHECK (wake_operation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_wake_operations_timers_installation_id_tenant_id_instance_i~" FOREIGN KEY (installation_id, tenant_id, instance_id, step_id, timer_id, generation) REFERENCES wfl.timers (installation_id, tenant_id, instance_id, step_id, timer_id, generation) ON DELETE RESTRICT
);

CREATE UNIQUE INDEX "IX_wake_operations_installation_id_tenant_id_message_id" ON wfl.wake_operations (installation_id, tenant_id, message_id);

CREATE UNIQUE INDEX "IX_wake_operations_installation_id_tenant_id_scheduler_operati~" ON wfl.wake_operations (installation_id, tenant_id, scheduler_operation_id);

CREATE UNIQUE INDEX "IX_wake_operations_installation_id_tenant_id_wake_operation_id" ON wfl.wake_operations (installation_id, tenant_id, wake_operation_id);
