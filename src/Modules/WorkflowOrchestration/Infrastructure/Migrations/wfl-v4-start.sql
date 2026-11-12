
CREATE TABLE wfl.start_receipts (
    installation_id uuid NOT NULL,
    tenant_id uuid NOT NULL,
    actor_id uuid NOT NULL,
    scoped_key_hash character varying(64) NOT NULL,
    request_id uuid NOT NULL,
    business_hash character varying(64) NOT NULL,
    CONSTRAINT "PK_start_receipts" PRIMARY KEY (installation_id, tenant_id, actor_id, scoped_key_hash),
    CONSTRAINT ck_start_receipts_actor_id CHECK (actor_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_start_receipts_business_hash CHECK (business_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_start_receipts_installation_id CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_start_receipts_request_id CHECK (request_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT ck_start_receipts_scoped_key_hash CHECK (scoped_key_hash ~ '^[0-9a-f]{64}$'),
    CONSTRAINT ck_start_receipts_tenant_id CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    CONSTRAINT "FK_start_receipts_request_receipts_installation_id_tenant_id_r~" FOREIGN KEY (installation_id, tenant_id, request_id) REFERENCES wfl.request_receipts (installation_id, tenant_id, request_id) ON DELETE RESTRICT
);


CREATE UNIQUE INDEX "IX_start_receipts_installation_id_tenant_id_request_id" ON wfl.start_receipts (installation_id, tenant_id, request_id);
