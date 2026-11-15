-- T12.3b IAM workflow authority (ENG-T12-3B-IAM-AUTHORITY-SCOPE §5.1). Offline reviewed artifact.
-- D1: complete replacement baseline for schema iam_workflow (v1 is the never-deployed 12.3a PoC). Same pattern as 12.3a §8.3.
-- Role names are fixed placeholders; a disposable runner may only append its run suffix to them.
CREATE SCHEMA iam_workflow AUTHORIZATION kordra_iam_owner;
REVOKE ALL ON SCHEMA iam_workflow FROM PUBLIC;

CREATE TABLE iam_workflow.authority_fences (
    installation_id uuid PRIMARY KEY CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    epoch bigint NOT NULL CHECK (epoch >= 1),
    manifest_revision bigint NOT NULL CHECK (manifest_revision >= 1)
);

CREATE TABLE iam_workflow.services (
    service_id uuid PRIMARY KEY CHECK (service_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    installation_id uuid NOT NULL REFERENCES iam_workflow.authority_fences (installation_id) ON DELETE RESTRICT,
    kind text NOT NULL CHECK (kind IN ('api', 'worker')),
    generation bigint NOT NULL CHECK (generation >= 1),
    status text NOT NULL CHECK (status IN ('active', 'retired', 'revoked')),
    login_name name NOT NULL UNIQUE,
    revoke_revision bigint NOT NULL CHECK (revoke_revision >= 0),
    created_at timestamp with time zone NOT NULL,
    changed_at timestamp with time zone NOT NULL,
    CONSTRAINT ck_services_times CHECK (changed_at >= created_at)
);

CREATE TABLE iam_workflow.service_bindings (
    service_id uuid NOT NULL REFERENCES iam_workflow.services (service_id) ON DELETE RESTRICT,
    generation bigint NOT NULL CHECK (generation >= 1),
    tenant_id uuid NOT NULL CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    environment_id uuid NOT NULL CHECK (environment_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    template_id text NOT NULL CHECK (template_id ~ '^[a-z][a-z0-9.-]{0,127}$'),
    template_version integer NOT NULL CHECK (template_version >= 1),
    transition_kind text NOT NULL CHECK (transition_kind IN ('admit', 'checkpoint', 'timer_due', 'cancel')),
    CONSTRAINT pk_service_bindings PRIMARY KEY (service_id, generation, tenant_id, environment_id, template_id, template_version, transition_kind)
);

CREATE TABLE iam_workflow.service_pins (
    service_id uuid NOT NULL REFERENCES iam_workflow.services (service_id) ON DELETE RESTRICT,
    generation bigint NOT NULL CHECK (generation >= 1),
    spki_sha256 character(64) NOT NULL CHECK (spki_sha256 ~ '^[0-9a-f]{64}$'),
    not_before timestamp with time zone NOT NULL,
    not_after timestamp with time zone NOT NULL,
    retired_at timestamp with time zone NULL,
    CONSTRAINT pk_service_pins PRIMARY KEY (service_id, spki_sha256),
    CONSTRAINT ck_service_pins_validity CHECK (not_after > not_before)
);

CREATE TABLE iam_workflow.service_manifests (
    manifest_sha256 character(64) PRIMARY KEY CHECK (manifest_sha256 ~ '^[0-9a-f]{64}$'),
    kind text NOT NULL CHECK (kind IN ('bootstrap', 'rotate', 'retire', 'revoke')),
    service_id uuid NOT NULL REFERENCES iam_workflow.services (service_id) ON DELETE RESTRICT,
    resulting_generation bigint NOT NULL CHECK (resulting_generation >= 1),
    resulting_epoch bigint NOT NULL CHECK (resulting_epoch >= 1),
    audit_intent_id uuid NOT NULL CHECK (audit_intent_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    applied_at timestamp with time zone NOT NULL
);

CREATE TABLE iam_workflow.delegations (
    delegation_id uuid PRIMARY KEY CHECK (delegation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    installation_id uuid NOT NULL REFERENCES iam_workflow.authority_fences (installation_id) ON DELETE RESTRICT,
    tenant_id uuid NOT NULL CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    instance_id uuid NOT NULL CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    generation bigint NOT NULL CHECK (generation >= 1),
    actor_id uuid NOT NULL CHECK (actor_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    session_id uuid NOT NULL CHECK (session_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    boot_generation uuid NOT NULL CHECK (boot_generation <> '00000000-0000-0000-0000-000000000000'::uuid),
    principal_revision bigint NOT NULL CHECK (principal_revision >= 1),
    project_id uuid NOT NULL CHECK (project_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    environment_id uuid NOT NULL CHECK (environment_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    release_id uuid NOT NULL CHECK (release_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    release_revision bigint NOT NULL CHECK (release_revision >= 1),
    release_hash character varying(64) NOT NULL CHECK (release_hash ~ '^[0-9a-f]{64}$'),
    template_id text NOT NULL CHECK (template_id ~ '^[a-z][a-z0-9.-]{0,127}$'),
    template_version integer NOT NULL CHECK (template_version >= 1),
    specification_hash character varying(64) NOT NULL CHECK (specification_hash ~ '^[0-9a-f]{64}$'),
    allowed_kinds text[] NOT NULL CHECK (cardinality(allowed_kinds) BETWEEN 1 AND 4
        AND allowed_kinds <@ ARRAY['admit', 'checkpoint', 'timer_due', 'cancel']::text[]),
    authority_evidence bytea NOT NULL CHECK (octet_length(authority_evidence) BETWEEN 2 AND 8192),
    authority_evidence_sha256 bytea NOT NULL CHECK (octet_length(authority_evidence_sha256) = 32),
    valid_until timestamp with time zone NOT NULL,
    created_at timestamp with time zone NOT NULL,
    revoked_at timestamp with time zone NULL,
    revoke_reason text NULL CHECK (revoke_reason IS NULL OR revoke_reason ~ '^[a-z][a-z0-9_.-]{0,63}$'),
    CONSTRAINT ck_delegations_validity CHECK (valid_until > created_at),
    CONSTRAINT ck_delegations_revocation CHECK ((revoked_at IS NULL) = (revoke_reason IS NULL)),
    CONSTRAINT uq_delegations_generation UNIQUE (installation_id, tenant_id, instance_id, generation)
);
-- At most one unrevoked generation per workflow instance.
CREATE UNIQUE INDEX ux_delegations_current ON iam_workflow.delegations (installation_id, tenant_id, instance_id) WHERE revoked_at IS NULL;

CREATE TABLE iam_workflow.permits (
    permit_id uuid PRIMARY KEY CHECK (permit_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    attempt integer NOT NULL CHECK (attempt BETWEEN 1 AND 1000),
    service_id uuid NOT NULL REFERENCES iam_workflow.services (service_id) ON DELETE RESTRICT,
    generation bigint NOT NULL CHECK (generation >= 1),
    installation_id uuid NOT NULL REFERENCES iam_workflow.authority_fences (installation_id) ON DELETE RESTRICT,
    tenant_id uuid NOT NULL CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    instance_id uuid NOT NULL CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    step_id uuid NOT NULL CHECK (step_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    operation_id uuid NOT NULL CHECK (operation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    delegation_id uuid NOT NULL REFERENCES iam_workflow.delegations (delegation_id) ON DELETE RESTRICT,
    expected_version bigint NOT NULL CHECK (expected_version >= 1 AND expected_version < 9223372036854775807),
    specification_hash character varying(64) NOT NULL CHECK (specification_hash ~ '^[0-9a-f]{64}$'),
    template_hash character varying(64) NOT NULL CHECK (template_hash ~ '^[0-9a-f]{64}$'),
    release_hash character varying(64) NOT NULL CHECK (release_hash ~ '^[0-9a-f]{64}$'),
    transition_kind text NOT NULL CHECK (transition_kind IN ('admit', 'checkpoint', 'timer_due', 'cancel')),
    authority_epoch bigint NOT NULL CHECK (authority_epoch >= 1),
    boot_id uuid NOT NULL CHECK (boot_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    custody_epoch bigint NOT NULL CHECK (custody_epoch >= 0),
    issued_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    consumed_xact xid8 NULL,
    consumed_at timestamp with time zone NULL,
    CONSTRAINT ck_permits_ttl CHECK (expires_at > issued_at AND expires_at - issued_at <= interval '2 seconds'),
    CONSTRAINT ck_permits_consumed CHECK ((consumed_xact IS NULL) = (consumed_at IS NULL)),
    CONSTRAINT uq_permits_attempt UNIQUE (installation_id, tenant_id, instance_id, operation_id, attempt)
);
-- D5: at most one consumed permit per logical operation.
CREATE UNIQUE INDEX ux_permits_consumed_operation ON iam_workflow.permits (installation_id, tenant_id, instance_id, operation_id)
    WHERE consumed_xact IS NOT NULL;

CREATE FUNCTION iam_workflow.current_service(OUT service_id uuid, OUT generation bigint, OUT installation_id uuid, OUT kind text)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
BEGIN
    SELECT s.service_id, s.generation, s.installation_id, s.kind INTO service_id, generation, installation_id, kind
    FROM iam_workflow.services AS s
    WHERE s.login_name = session_user AND s.status = 'active';
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'KD001', MESSAGE = 'service_unavailable';
    END IF;
END
$fn$;

CREATE FUNCTION iam_workflow.acquire_fence(p_installation_id uuid) RETURNS bigint
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET lock_timeout = '1000ms' AS $fn$
DECLARE
    v_service record;
    v_epoch bigint;
BEGIN
    SELECT * INTO v_service FROM iam_workflow.current_service();
    IF p_installation_id IS DISTINCT FROM v_service.installation_id THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    SELECT f.epoch INTO v_epoch FROM iam_workflow.authority_fences AS f
    WHERE f.installation_id = p_installation_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    RETURN v_epoch;
END
$fn$;

CREATE FUNCTION iam_workflow.advance_epoch(p_installation_id uuid, p_reason_code text) RETURNS bigint
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET lock_timeout = '1000ms' AS $fn$
DECLARE
    v_service record;
    v_epoch bigint;
BEGIN
    SELECT * INTO v_service FROM iam_workflow.current_service();
    IF p_installation_id IS DISTINCT FROM v_service.installation_id OR v_service.kind <> 'api' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    IF p_reason_code IS NULL OR p_reason_code !~ '^[a-z][a-z0-9_.-]{0,63}$' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD009', MESSAGE = 'invalid_reason';
    END IF;
    UPDATE iam_workflow.authority_fences AS f SET epoch = f.epoch + 1
    WHERE f.installation_id = p_installation_id AND f.epoch < 9223372036854775807
    RETURNING f.epoch INTO v_epoch;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'KD010', MESSAGE = 'epoch_unavailable';
    END IF;
    RETURN v_epoch;
END
$fn$;

CREATE FUNCTION iam_workflow.advance_floor(p_installation_id uuid, p_admitted_utc timestamp with time zone)
RETURNS timestamp with time zone
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
DECLARE
    v_service record;
    v_floor timestamp with time zone;
BEGIN
    SELECT * INTO v_service FROM iam_workflow.current_service();
    IF p_installation_id IS DISTINCT FROM v_service.installation_id THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    IF p_admitted_utc IS NULL OR p_admitted_utc > clock_timestamp() + interval '1 second' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD008', MESSAGE = 'clock_floor_rejected';
    END IF;
    INSERT INTO app.iam_authority_clock_floors AS c (installation_id, admitted_utc) VALUES (p_installation_id, p_admitted_utc)
    ON CONFLICT (installation_id) DO UPDATE SET admitted_utc = GREATEST(c.admitted_utc, EXCLUDED.admitted_utc)
    RETURNING c.admitted_utc INTO v_floor;
    RETURN v_floor;
END
$fn$;

-- D3: generation 1 in the start root; idempotent for the same exact content, never extends authority.
CREATE FUNCTION iam_workflow.create_delegation(p_delegation_id uuid, p_tenant_id uuid, p_instance_id uuid, p_actor_id uuid,
    p_session_id uuid, p_boot_generation uuid, p_principal_revision bigint, p_project_id uuid, p_environment_id uuid,
    p_release_id uuid, p_release_revision bigint, p_release_hash text, p_template_id text, p_template_version integer,
    p_specification_hash text, p_allowed_kinds text[], p_authority_evidence bytea, p_authority_evidence_sha256 bytea,
    p_valid_until timestamp with time zone, OUT delegation_id uuid, OUT generation bigint, OUT duplicate boolean)
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET lock_timeout = '1000ms' AS $fn$
DECLARE
    v_service record;
    v_existing iam_workflow.delegations%ROWTYPE;
BEGIN
    SELECT * INTO v_service FROM iam_workflow.current_service();
    IF v_service.kind <> 'api' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    IF p_authority_evidence IS NULL OR p_authority_evidence_sha256 IS NULL
        OR sha256(p_authority_evidence) <> p_authority_evidence_sha256 THEN
        RAISE EXCEPTION USING ERRCODE = 'KD011', MESSAGE = 'evidence_mismatch';
    END IF;
    SELECT * INTO v_existing FROM iam_workflow.delegations AS d
    WHERE d.installation_id = v_service.installation_id AND d.tenant_id = p_tenant_id AND d.instance_id = p_instance_id
    ORDER BY d.generation DESC LIMIT 1 FOR UPDATE;
    IF FOUND THEN
        IF v_existing.delegation_id = p_delegation_id AND v_existing.generation = 1 AND v_existing.actor_id = p_actor_id
            AND v_existing.session_id = p_session_id AND v_existing.boot_generation = p_boot_generation
            AND v_existing.principal_revision = p_principal_revision AND v_existing.project_id = p_project_id
            AND v_existing.environment_id = p_environment_id AND v_existing.release_id = p_release_id
            AND v_existing.release_revision = p_release_revision AND v_existing.release_hash = p_release_hash
            AND v_existing.template_id = p_template_id AND v_existing.template_version = p_template_version
            AND v_existing.specification_hash = p_specification_hash AND v_existing.allowed_kinds = p_allowed_kinds
            AND v_existing.authority_evidence_sha256 = p_authority_evidence_sha256 AND v_existing.valid_until = p_valid_until THEN
            delegation_id := v_existing.delegation_id; generation := 1; duplicate := true;
            RETURN;
        END IF;
        RAISE EXCEPTION USING ERRCODE = 'KD012', MESSAGE = 'delegation_conflict';
    END IF;
    IF p_valid_until IS NULL OR p_valid_until <= clock_timestamp() THEN
        RAISE EXCEPTION USING ERRCODE = 'KD013', MESSAGE = 'delegation_expired';
    END IF;
    INSERT INTO iam_workflow.delegations (delegation_id, installation_id, tenant_id, instance_id, generation, actor_id, session_id,
        boot_generation, principal_revision, project_id, environment_id, release_id, release_revision, release_hash, template_id,
        template_version, specification_hash, allowed_kinds, authority_evidence, authority_evidence_sha256, valid_until, created_at)
    VALUES (p_delegation_id, v_service.installation_id, p_tenant_id, p_instance_id, 1, p_actor_id, p_session_id,
        p_boot_generation, p_principal_revision, p_project_id, p_environment_id, p_release_id, p_release_revision, p_release_hash,
        p_template_id, p_template_version, p_specification_hash, p_allowed_kinds, p_authority_evidence,
        p_authority_evidence_sha256, p_valid_until, clock_timestamp());
    delegation_id := p_delegation_id; generation := 1; duplicate := false;
END
$fn$;

-- D4: invalidating writer. Same original actor and binding; revokes the current generation and advances the epoch.
CREATE FUNCTION iam_workflow.reauthorize_delegation(p_tenant_id uuid, p_instance_id uuid, p_expected_generation bigint,
    p_delegation_id uuid, p_actor_id uuid, p_session_id uuid, p_boot_generation uuid, p_principal_revision bigint,
    p_specification_hash text, p_authority_evidence bytea, p_authority_evidence_sha256 bytea,
    p_valid_until timestamp with time zone, p_reason_code text,
    OUT delegation_id uuid, OUT generation bigint, OUT authority_epoch bigint, OUT duplicate boolean)
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET lock_timeout = '1000ms' AS $fn$
DECLARE
    v_service record;
    v_epoch bigint;
    v_current iam_workflow.delegations%ROWTYPE;
    v_prior iam_workflow.delegations%ROWTYPE;
BEGIN
    SELECT * INTO v_service FROM iam_workflow.current_service();
    IF v_service.kind <> 'api' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    IF p_reason_code IS NULL OR p_reason_code !~ '^[a-z][a-z0-9_.-]{0,63}$' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD009', MESSAGE = 'invalid_reason';
    END IF;
    IF p_authority_evidence IS NULL OR p_authority_evidence_sha256 IS NULL
        OR sha256(p_authority_evidence) <> p_authority_evidence_sha256 THEN
        RAISE EXCEPTION USING ERRCODE = 'KD011', MESSAGE = 'evidence_mismatch';
    END IF;
    SELECT f.epoch INTO v_epoch FROM iam_workflow.authority_fences AS f
    WHERE f.installation_id = v_service.installation_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    SELECT * INTO v_prior FROM iam_workflow.delegations AS d WHERE d.delegation_id = p_delegation_id;
    IF FOUND THEN
        IF v_prior.installation_id = v_service.installation_id AND v_prior.tenant_id = p_tenant_id
            AND v_prior.instance_id = p_instance_id AND v_prior.generation = p_expected_generation + 1
            AND v_prior.actor_id = p_actor_id AND v_prior.session_id = p_session_id
            AND v_prior.boot_generation = p_boot_generation AND v_prior.principal_revision = p_principal_revision
            AND v_prior.specification_hash = p_specification_hash
            AND v_prior.authority_evidence_sha256 = p_authority_evidence_sha256 AND v_prior.valid_until = p_valid_until THEN
            delegation_id := v_prior.delegation_id; generation := v_prior.generation; authority_epoch := v_epoch; duplicate := true;
            RETURN;
        END IF;
        RAISE EXCEPTION USING ERRCODE = 'KD012', MESSAGE = 'delegation_conflict';
    END IF;
    SELECT * INTO v_current FROM iam_workflow.delegations AS d
    WHERE d.installation_id = v_service.installation_id AND d.tenant_id = p_tenant_id AND d.instance_id = p_instance_id
    ORDER BY d.generation DESC LIMIT 1 FOR UPDATE;
    IF NOT FOUND OR v_current.generation <> p_expected_generation THEN
        RAISE EXCEPTION USING ERRCODE = 'KD014', MESSAGE = 'generation_changed';
    END IF;
    -- No actor-source or binding change during reauthorization.
    IF v_current.actor_id <> p_actor_id OR v_current.specification_hash <> p_specification_hash THEN
        RAISE EXCEPTION USING ERRCODE = 'KD012', MESSAGE = 'delegation_conflict';
    END IF;
    IF p_valid_until IS NULL OR p_valid_until <= clock_timestamp() THEN
        RAISE EXCEPTION USING ERRCODE = 'KD013', MESSAGE = 'delegation_expired';
    END IF;
    IF v_current.generation >= 9223372036854775807 OR v_epoch >= 9223372036854775807 THEN
        RAISE EXCEPTION USING ERRCODE = 'KD010', MESSAGE = 'epoch_unavailable';
    END IF;
    IF v_current.revoked_at IS NULL THEN
        UPDATE iam_workflow.delegations AS d SET revoked_at = clock_timestamp(), revoke_reason = p_reason_code
        WHERE d.delegation_id = v_current.delegation_id;
    END IF;
    UPDATE iam_workflow.authority_fences AS f SET epoch = f.epoch + 1
    WHERE f.installation_id = v_service.installation_id RETURNING f.epoch INTO v_epoch;
    INSERT INTO iam_workflow.delegations (delegation_id, installation_id, tenant_id, instance_id, generation, actor_id, session_id,
        boot_generation, principal_revision, project_id, environment_id, release_id, release_revision, release_hash, template_id,
        template_version, specification_hash, allowed_kinds, authority_evidence, authority_evidence_sha256, valid_until, created_at)
    VALUES (p_delegation_id, v_service.installation_id, p_tenant_id, p_instance_id, v_current.generation + 1, p_actor_id,
        p_session_id, p_boot_generation, p_principal_revision, v_current.project_id, v_current.environment_id, v_current.release_id,
        v_current.release_revision, v_current.release_hash, v_current.template_id, v_current.template_version,
        v_current.specification_hash, v_current.allowed_kinds, p_authority_evidence, p_authority_evidence_sha256, p_valid_until,
        clock_timestamp());
    delegation_id := p_delegation_id; generation := v_current.generation + 1; authority_epoch := v_epoch; duplicate := false;
END
$fn$;

-- Invalidating writer primitive (12.3c attaches it to every writer). Idempotent for an already revoked delegation.
CREATE FUNCTION iam_workflow.revoke_delegation(p_delegation_id uuid, p_reason_code text,
    OUT authority_epoch bigint, OUT duplicate boolean)
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET lock_timeout = '1000ms' AS $fn$
DECLARE
    v_service record;
    v_epoch bigint;
    v_target iam_workflow.delegations%ROWTYPE;
BEGIN
    SELECT * INTO v_service FROM iam_workflow.current_service();
    IF v_service.kind <> 'api' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    IF p_reason_code IS NULL OR p_reason_code !~ '^[a-z][a-z0-9_.-]{0,63}$' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD009', MESSAGE = 'invalid_reason';
    END IF;
    SELECT f.epoch INTO v_epoch FROM iam_workflow.authority_fences AS f
    WHERE f.installation_id = v_service.installation_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    SELECT * INTO v_target FROM iam_workflow.delegations AS d
    WHERE d.delegation_id = p_delegation_id AND d.installation_id = v_service.installation_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'KD015', MESSAGE = 'delegation_unavailable';
    END IF;
    IF v_target.revoked_at IS NOT NULL THEN
        authority_epoch := v_epoch; duplicate := true;
        RETURN;
    END IF;
    IF v_epoch >= 9223372036854775807 THEN
        RAISE EXCEPTION USING ERRCODE = 'KD010', MESSAGE = 'epoch_unavailable';
    END IF;
    UPDATE iam_workflow.delegations AS d SET revoked_at = clock_timestamp(), revoke_reason = p_reason_code
    WHERE d.delegation_id = p_delegation_id;
    UPDATE iam_workflow.authority_fences AS f SET epoch = f.epoch + 1
    WHERE f.installation_id = v_service.installation_id RETURNING f.epoch INTO v_epoch;
    authority_epoch := v_epoch; duplicate := false;
END
$fn$;

-- Current generation for one instance (API issuer and reauthorization only). Returns no row when absent.
CREATE FUNCTION iam_workflow.read_current_delegation(p_tenant_id uuid, p_instance_id uuid)
RETURNS TABLE (delegation_id uuid, generation bigint, actor_id uuid, session_id uuid, boot_generation uuid,
    principal_revision bigint, project_id uuid, environment_id uuid, release_id uuid, release_revision bigint,
    release_hash text, template_id text, template_version integer, specification_hash text, allowed_kinds text[],
    authority_evidence bytea, valid_until timestamp with time zone, revoked boolean)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
DECLARE
    v_service record;
BEGIN
    SELECT * INTO v_service FROM iam_workflow.current_service();
    IF v_service.kind <> 'api' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    RETURN QUERY SELECT d.delegation_id, d.generation, d.actor_id, d.session_id, d.boot_generation, d.principal_revision,
        d.project_id, d.environment_id, d.release_id, d.release_revision, d.release_hash::text, d.template_id, d.template_version,
        d.specification_hash::text, d.allowed_kinds, d.authority_evidence, d.valid_until, d.revoked_at IS NOT NULL
    FROM iam_workflow.delegations AS d
    WHERE d.installation_id = v_service.installation_id AND d.tenant_id = p_tenant_id AND d.instance_id = p_instance_id
    ORDER BY d.generation DESC LIMIT 1;
END
$fn$;

-- D5: the API issuer calls this under the fence after re-evaluating current owner facts. TTL never exceeds 2 s or the delegation.
CREATE FUNCTION iam_workflow.issue_permit(p_permit_id uuid, p_service_id uuid, p_tenant_id uuid, p_instance_id uuid,
    p_step_id uuid, p_operation_id uuid, p_expected_version bigint, p_specification_hash text, p_template_hash text,
    p_transition_kind text, p_boot_id uuid, p_custody_epoch bigint,
    OUT permit_id uuid, OUT attempt integer, OUT authority_epoch bigint, OUT issued_at timestamp with time zone,
    OUT expires_at timestamp with time zone, OUT duplicate boolean)
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET lock_timeout = '1000ms' AS $fn$
DECLARE
    v_caller record;
    v_epoch bigint;
    v_target iam_workflow.services%ROWTYPE;
    v_delegation iam_workflow.delegations%ROWTYPE;
    v_prior iam_workflow.permits%ROWTYPE;
    v_attempt integer;
    v_now timestamp with time zone;
BEGIN
    SELECT * INTO v_caller FROM iam_workflow.current_service();
    IF v_caller.kind <> 'api' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    SELECT f.epoch INTO v_epoch FROM iam_workflow.authority_fences AS f
    WHERE f.installation_id = v_caller.installation_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    SELECT * INTO v_prior FROM iam_workflow.permits AS p WHERE p.permit_id = p_permit_id;
    IF FOUND THEN
        IF v_prior.installation_id = v_caller.installation_id AND v_prior.service_id = p_service_id
            AND v_prior.tenant_id = p_tenant_id AND v_prior.instance_id = p_instance_id AND v_prior.step_id = p_step_id
            AND v_prior.operation_id = p_operation_id AND v_prior.expected_version = p_expected_version
            AND v_prior.specification_hash = p_specification_hash AND v_prior.template_hash = p_template_hash
            AND v_prior.transition_kind = p_transition_kind AND v_prior.boot_id = p_boot_id
            AND v_prior.custody_epoch = p_custody_epoch THEN
            permit_id := v_prior.permit_id; attempt := v_prior.attempt; authority_epoch := v_prior.authority_epoch;
            issued_at := v_prior.issued_at; expires_at := v_prior.expires_at; duplicate := true;
            RETURN;
        END IF;
        RAISE EXCEPTION USING ERRCODE = 'KD016', MESSAGE = 'permit_conflict';
    END IF;
    SELECT * INTO v_target FROM iam_workflow.services AS s
    WHERE s.service_id = p_service_id AND s.installation_id = v_caller.installation_id;
    IF NOT FOUND OR v_target.status <> 'active' OR v_target.kind <> 'worker' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD017', MESSAGE = 'service_not_admitted';
    END IF;
    v_now := clock_timestamp();
    SELECT * INTO v_delegation FROM iam_workflow.delegations AS d
    WHERE d.installation_id = v_caller.installation_id AND d.tenant_id = p_tenant_id AND d.instance_id = p_instance_id
    ORDER BY d.generation DESC LIMIT 1;
    IF NOT FOUND OR v_delegation.revoked_at IS NOT NULL OR v_delegation.valid_until <= v_now
        OR v_delegation.specification_hash <> p_specification_hash OR NOT (p_transition_kind = ANY (v_delegation.allowed_kinds)) THEN
        RAISE EXCEPTION USING ERRCODE = 'KD018', MESSAGE = 'delegation_not_current';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM iam_workflow.service_bindings AS b WHERE b.service_id = v_target.service_id
            AND b.generation = v_target.generation AND b.tenant_id = p_tenant_id AND b.environment_id = v_delegation.environment_id
            AND b.template_id = v_delegation.template_id AND b.template_version = v_delegation.template_version
            AND b.transition_kind = p_transition_kind) THEN
        RAISE EXCEPTION USING ERRCODE = 'KD017', MESSAGE = 'service_not_admitted';
    END IF;
    IF EXISTS (SELECT 1 FROM iam_workflow.permits AS p WHERE p.installation_id = v_caller.installation_id
            AND p.tenant_id = p_tenant_id AND p.instance_id = p_instance_id AND p.operation_id = p_operation_id
            AND p.consumed_xact IS NOT NULL) THEN
        RAISE EXCEPTION USING ERRCODE = 'KD019', MESSAGE = 'operation_already_consumed';
    END IF;
    SELECT COALESCE(max(p.attempt), 0) + 1 INTO v_attempt FROM iam_workflow.permits AS p
    WHERE p.installation_id = v_caller.installation_id AND p.tenant_id = p_tenant_id AND p.instance_id = p_instance_id
        AND p.operation_id = p_operation_id;
    IF v_attempt > 1000 THEN
        RAISE EXCEPTION USING ERRCODE = 'KD020', MESSAGE = 'attempts_exhausted';
    END IF;
    INSERT INTO iam_workflow.permits (permit_id, attempt, service_id, generation, installation_id, tenant_id, instance_id, step_id,
        operation_id, delegation_id, expected_version, specification_hash, template_hash, release_hash, transition_kind,
        authority_epoch, boot_id, custody_epoch, issued_at, expires_at)
    VALUES (p_permit_id, v_attempt, v_target.service_id, v_target.generation, v_caller.installation_id, p_tenant_id, p_instance_id,
        p_step_id, p_operation_id, v_delegation.delegation_id, p_expected_version, p_specification_hash, p_template_hash,
        v_delegation.release_hash, p_transition_kind, v_epoch, p_boot_id, p_custody_epoch, v_now,
        LEAST(v_now + interval '2 seconds', v_delegation.valid_until));
    permit_id := p_permit_id; attempt := v_attempt; authority_epoch := v_epoch; issued_at := v_now;
    expires_at := LEAST(v_now + interval '2 seconds', v_delegation.valid_until); duplicate := false;
END
$fn$;

CREATE FUNCTION iam_workflow.consume_permit(p_permit_id uuid, p_operation_id uuid, p_instance_id uuid,
    p_expected_version bigint, p_specification_hash text, p_boot_id uuid, p_custody_epoch bigint,
    OUT tenant_id uuid, OUT consumed_xact xid8, OUT duplicate boolean)
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET lock_timeout = '1000ms' AS $fn$
DECLARE
    v_service record;
    v_epoch bigint;
    v_permit iam_workflow.permits%ROWTYPE;
    v_delegation iam_workflow.delegations%ROWTYPE;
BEGIN
    SELECT * INTO v_service FROM iam_workflow.current_service();
    IF v_service.kind <> 'worker' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    -- The global fence is held by this root before any authorization fact is read (re-entrant FOR UPDATE).
    SELECT f.epoch INTO v_epoch FROM iam_workflow.authority_fences AS f
    WHERE f.installation_id = v_service.installation_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    SELECT * INTO v_permit FROM iam_workflow.permits AS p WHERE p.permit_id = p_permit_id FOR UPDATE;
    IF NOT FOUND OR v_permit.service_id <> v_service.service_id OR v_permit.generation <> v_service.generation
        OR v_permit.installation_id <> v_service.installation_id OR v_permit.operation_id IS DISTINCT FROM p_operation_id
        OR v_permit.instance_id IS DISTINCT FROM p_instance_id OR v_permit.expected_version IS DISTINCT FROM p_expected_version
        OR v_permit.specification_hash IS DISTINCT FROM p_specification_hash OR v_permit.boot_id IS DISTINCT FROM p_boot_id
        OR v_permit.custody_epoch IS DISTINCT FROM p_custody_epoch THEN
        RAISE EXCEPTION USING ERRCODE = 'KD003', MESSAGE = 'permit_mismatch';
    END IF;
    IF v_permit.consumed_xact IS NOT NULL THEN
        IF v_permit.consumed_xact = pg_current_xact_id() THEN
            tenant_id := v_permit.tenant_id; consumed_xact := v_permit.consumed_xact; duplicate := true;
            RETURN;
        END IF;
        RAISE EXCEPTION USING ERRCODE = 'KD004', MESSAGE = 'permit_consumed';
    END IF;
    IF v_permit.authority_epoch <> v_epoch THEN
        RAISE EXCEPTION USING ERRCODE = 'KD005', MESSAGE = 'epoch_changed';
    END IF;
    IF clock_timestamp() >= v_permit.expires_at THEN
        RAISE EXCEPTION USING ERRCODE = 'KD006', MESSAGE = 'permit_expired';
    END IF;
    SELECT * INTO v_delegation FROM iam_workflow.delegations AS d WHERE d.delegation_id = v_permit.delegation_id;
    IF NOT FOUND OR v_delegation.revoked_at IS NOT NULL OR v_delegation.valid_until <= clock_timestamp() THEN
        RAISE EXCEPTION USING ERRCODE = 'KD018', MESSAGE = 'delegation_not_current';
    END IF;
    UPDATE iam_workflow.permits AS p SET consumed_xact = pg_current_xact_id(), consumed_at = clock_timestamp()
    WHERE p.permit_id = p_permit_id;
    tenant_id := v_permit.tenant_id; consumed_xact := pg_current_xact_id(); duplicate := false;
END
$fn$;

CREATE FUNCTION iam_workflow.require_consumed(p_permit_id uuid, p_operation_id uuid, p_specification_hash text,
    OUT installation_id uuid, OUT tenant_id uuid, OUT instance_id uuid, OUT expected_version bigint,
    OUT transition_kind text, OUT service_id uuid, OUT delegation_id uuid, OUT delegation_generation bigint, OUT step_id uuid)
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
DECLARE
    v_service record;
    v_expires timestamp with time zone;
BEGIN
    SELECT * INTO v_service FROM iam_workflow.current_service();
    SELECT p.installation_id, p.tenant_id, p.instance_id, p.expected_version, p.transition_kind, p.service_id, p.delegation_id,
        d.generation, p.step_id, p.expires_at
    INTO installation_id, tenant_id, instance_id, expected_version, transition_kind, service_id, delegation_id,
        delegation_generation, step_id, v_expires
    FROM iam_workflow.permits AS p JOIN iam_workflow.delegations AS d ON d.delegation_id = p.delegation_id
    WHERE p.permit_id = p_permit_id AND p.consumed_xact = pg_current_xact_id()
        AND p.service_id = v_service.service_id AND p.generation = v_service.generation
        AND p.operation_id = p_operation_id AND p.specification_hash = p_specification_hash
        AND d.revoked_at IS NULL;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'KD007', MESSAGE = 'permit_not_consumed_in_root';
    END IF;
    -- Final handoff recheck: an expired permit never admits, even inside its consuming root.
    IF clock_timestamp() >= v_expires THEN
        RAISE EXCEPTION USING ERRCODE = 'KD006', MESSAGE = 'permit_expired';
    END IF;
END
$fn$;

-- D6: offline owner manifest only (no runtime login has EXECUTE). The caller stages the matching AUD intent on the same root.
CREATE FUNCTION iam_workflow.apply_service_manifest(p_manifest_sha256 text, p_kind text, p_installation_id uuid,
    p_service_id uuid, p_service_kind text, p_login_name name, p_spki_sha256 text, p_pin_not_before timestamp with time zone,
    p_pin_not_after timestamp with time zone, p_bindings jsonb, p_audit_intent_id uuid,
    OUT generation bigint, OUT authority_epoch bigint, OUT duplicate boolean)
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET lock_timeout = '1000ms' AS $fn$
DECLARE
    v_prior iam_workflow.service_manifests%ROWTYPE;
    v_service iam_workflow.services%ROWTYPE;
    v_epoch bigint;
    v_now timestamp with time zone := clock_timestamp();
    v_generation bigint;
    v_exists boolean;
BEGIN
    IF p_manifest_sha256 IS NULL OR p_manifest_sha256 !~ '^[0-9a-f]{64}$' OR p_kind NOT IN ('bootstrap', 'rotate', 'retire', 'revoke')
        OR p_installation_id IS NULL OR p_service_id IS NULL OR p_audit_intent_id IS NULL THEN
        RAISE EXCEPTION USING ERRCODE = 'KD021', MESSAGE = 'manifest_invalid';
    END IF;
    SELECT * INTO v_prior FROM iam_workflow.service_manifests AS m WHERE m.manifest_sha256 = p_manifest_sha256;
    IF FOUND THEN
        IF v_prior.kind = p_kind AND v_prior.service_id = p_service_id AND v_prior.audit_intent_id = p_audit_intent_id THEN
            generation := v_prior.resulting_generation; authority_epoch := v_prior.resulting_epoch; duplicate := true;
            RETURN;
        END IF;
        RAISE EXCEPTION USING ERRCODE = 'KD022', MESSAGE = 'manifest_conflict';
    END IF;
    IF p_kind = 'bootstrap' THEN
        INSERT INTO iam_workflow.authority_fences (installation_id, epoch, manifest_revision) VALUES (p_installation_id, 1, 1)
        ON CONFLICT (installation_id) DO NOTHING;
    END IF;
    SELECT f.epoch INTO v_epoch FROM iam_workflow.authority_fences AS f WHERE f.installation_id = p_installation_id FOR UPDATE;
    IF NOT FOUND OR v_epoch >= 9223372036854775807 THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    SELECT * INTO v_service FROM iam_workflow.services AS s WHERE s.service_id = p_service_id FOR UPDATE;
    v_exists := FOUND;
    IF v_exists AND v_service.installation_id <> p_installation_id THEN
        RAISE EXCEPTION USING ERRCODE = 'KD002', MESSAGE = 'scope_denied';
    END IF;
    IF p_kind = 'bootstrap' THEN
        IF p_service_kind NOT IN ('api', 'worker') OR p_login_name IS NULL OR p_spki_sha256 IS NULL
            OR p_spki_sha256 !~ '^[0-9a-f]{64}$' OR p_pin_not_before IS NULL OR p_pin_not_after IS NULL
            OR p_pin_not_after <= GREATEST(p_pin_not_before, v_now) THEN
            RAISE EXCEPTION USING ERRCODE = 'KD021', MESSAGE = 'manifest_invalid';
        END IF;
        IF v_exists AND v_service.status = 'active' THEN
            RAISE EXCEPTION USING ERRCODE = 'KD022', MESSAGE = 'manifest_conflict';
        END IF;
        IF v_exists THEN
            IF v_service.kind <> p_service_kind OR v_service.login_name <> p_login_name THEN
                RAISE EXCEPTION USING ERRCODE = 'KD022', MESSAGE = 'manifest_conflict';
            END IF;
            v_generation := v_service.generation + 1;
            UPDATE iam_workflow.services AS s SET generation = v_generation, status = 'active', changed_at = v_now
            WHERE s.service_id = p_service_id;
        ELSE
            v_generation := 1;
            INSERT INTO iam_workflow.services (service_id, installation_id, kind, generation, status, login_name, revoke_revision,
                created_at, changed_at)
            VALUES (p_service_id, p_installation_id, p_service_kind, 1, 'active', p_login_name, 0, v_now, v_now);
        END IF;
        INSERT INTO iam_workflow.service_pins (service_id, generation, spki_sha256, not_before, not_after)
        VALUES (p_service_id, v_generation, p_spki_sha256, p_pin_not_before, p_pin_not_after);
        INSERT INTO iam_workflow.service_bindings (service_id, generation, tenant_id, environment_id, template_id, template_version,
            transition_kind)
        SELECT p_service_id, v_generation, b.tenant_id, b.environment_id, b.template_id, b.template_version, b.transition_kind
        FROM jsonb_to_recordset(COALESCE(p_bindings, '[]'::jsonb))
            AS b (tenant_id uuid, environment_id uuid, template_id text, template_version integer, transition_kind text);
    ELSIF NOT v_exists THEN
        RAISE EXCEPTION USING ERRCODE = 'KD017', MESSAGE = 'service_not_admitted';
    ELSIF p_kind = 'rotate' THEN
        IF v_service.status <> 'active' OR p_spki_sha256 IS NULL OR p_spki_sha256 !~ '^[0-9a-f]{64}$'
            OR p_pin_not_before IS NULL OR p_pin_not_after IS NULL OR p_pin_not_after <= GREATEST(p_pin_not_before, v_now) THEN
            RAISE EXCEPTION USING ERRCODE = 'KD021', MESSAGE = 'manifest_invalid';
        END IF;
        v_generation := v_service.generation;
        -- Older pins of this generation retire within 10 minutes; at most two pins stay valid meanwhile.
        UPDATE iam_workflow.service_pins AS p SET retired_at = LEAST(v_now + interval '10 minutes', p.not_after)
        WHERE p.service_id = p_service_id AND (p.retired_at IS NULL OR p.retired_at > v_now + interval '10 minutes');
        INSERT INTO iam_workflow.service_pins (service_id, generation, spki_sha256, not_before, not_after)
        VALUES (p_service_id, v_generation, p_spki_sha256, p_pin_not_before, p_pin_not_after);
        IF (SELECT count(*) FROM iam_workflow.service_pins AS p WHERE p.service_id = p_service_id
                AND (p.retired_at IS NULL OR p.retired_at > v_now) AND p.not_after > v_now) > 2 THEN
            RAISE EXCEPTION USING ERRCODE = 'KD023', MESSAGE = 'too_many_pins';
        END IF;
        UPDATE iam_workflow.services AS s SET changed_at = v_now WHERE s.service_id = p_service_id;
    ELSE
        v_generation := v_service.generation;
        UPDATE iam_workflow.services AS s SET status = CASE WHEN p_kind = 'retire' THEN 'retired' ELSE 'revoked' END,
            revoke_revision = s.revoke_revision + CASE WHEN p_kind = 'revoke' THEN 1 ELSE 0 END, changed_at = v_now
        WHERE s.service_id = p_service_id;
        UPDATE iam_workflow.service_pins AS p SET retired_at = v_now
        WHERE p.service_id = p_service_id AND (p.retired_at IS NULL OR p.retired_at > v_now);
    END IF;
    UPDATE iam_workflow.authority_fences AS f SET epoch = f.epoch + 1, manifest_revision = f.manifest_revision + 1
    WHERE f.installation_id = p_installation_id RETURNING f.epoch INTO v_epoch;
    INSERT INTO iam_workflow.service_manifests (manifest_sha256, kind, service_id, resulting_generation, resulting_epoch,
        audit_intent_id, applied_at)
    VALUES (p_manifest_sha256, p_kind, p_service_id, v_generation, v_epoch, p_audit_intent_id, v_now);
    generation := v_generation; authority_epoch := v_epoch; duplicate := false;
END
$fn$;

ALTER TABLE iam_workflow.authority_fences OWNER TO kordra_iam_owner;
ALTER TABLE iam_workflow.services OWNER TO kordra_iam_owner;
ALTER TABLE iam_workflow.service_bindings OWNER TO kordra_iam_owner;
ALTER TABLE iam_workflow.service_pins OWNER TO kordra_iam_owner;
ALTER TABLE iam_workflow.service_manifests OWNER TO kordra_iam_owner;
ALTER TABLE iam_workflow.delegations OWNER TO kordra_iam_owner;
ALTER TABLE iam_workflow.permits OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.current_service() OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.acquire_fence(uuid) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.advance_epoch(uuid, text) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.advance_floor(uuid, timestamp with time zone) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.create_delegation(uuid, uuid, uuid, uuid, uuid, uuid, bigint, uuid, uuid, uuid, bigint, text, text, integer, text, text[], bytea, bytea, timestamp with time zone) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.reauthorize_delegation(uuid, uuid, bigint, uuid, uuid, uuid, uuid, bigint, text, bytea, bytea, timestamp with time zone, text) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.revoke_delegation(uuid, text) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.read_current_delegation(uuid, uuid) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.issue_permit(uuid, uuid, uuid, uuid, uuid, uuid, bigint, text, text, text, uuid, bigint) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.consume_permit(uuid, uuid, uuid, bigint, text, uuid, bigint) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.require_consumed(uuid, uuid, text) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.apply_service_manifest(text, text, uuid, uuid, text, name, text, timestamp with time zone, timestamp with time zone, jsonb, uuid) OWNER TO kordra_iam_owner;

-- Existing IAM floor table keeps its migration owner; the IAM function owner receives exact rights only.
GRANT USAGE ON SCHEMA app TO kordra_iam_owner;
GRANT SELECT, INSERT, UPDATE (admitted_utc) ON TABLE app.iam_authority_clock_floors TO kordra_iam_owner;

REVOKE ALL ON FUNCTION iam_workflow.current_service() FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.acquire_fence(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.advance_epoch(uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.advance_floor(uuid, timestamp with time zone) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.create_delegation(uuid, uuid, uuid, uuid, uuid, uuid, bigint, uuid, uuid, uuid, bigint, text, text, integer, text, text[], bytea, bytea, timestamp with time zone) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.reauthorize_delegation(uuid, uuid, bigint, uuid, uuid, uuid, uuid, bigint, text, bytea, bytea, timestamp with time zone, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.revoke_delegation(uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.read_current_delegation(uuid, uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.issue_permit(uuid, uuid, uuid, uuid, uuid, uuid, bigint, text, text, text, uuid, bigint) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.consume_permit(uuid, uuid, uuid, bigint, text, uuid, bigint) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.require_consumed(uuid, uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.apply_service_manifest(text, text, uuid, uuid, text, name, text, timestamp with time zone, timestamp with time zone, jsonb, uuid) FROM PUBLIC;

GRANT USAGE ON SCHEMA iam_workflow TO kordra_worker_runtime, kordra_api_runtime, kordra_wfl_owner, kordra_aud_owner;
GRANT EXECUTE ON FUNCTION iam_workflow.current_service() TO kordra_worker_runtime, kordra_api_runtime, kordra_wfl_owner, kordra_aud_owner;
GRANT EXECUTE ON FUNCTION iam_workflow.acquire_fence(uuid) TO kordra_worker_runtime, kordra_api_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.advance_epoch(uuid, text) TO kordra_api_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.advance_floor(uuid, timestamp with time zone) TO kordra_worker_runtime, kordra_api_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.create_delegation(uuid, uuid, uuid, uuid, uuid, uuid, bigint, uuid, uuid, uuid, bigint, text, text, integer, text, text[], bytea, bytea, timestamp with time zone) TO kordra_api_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.reauthorize_delegation(uuid, uuid, bigint, uuid, uuid, uuid, uuid, bigint, text, bytea, bytea, timestamp with time zone, text) TO kordra_api_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.revoke_delegation(uuid, text) TO kordra_api_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.read_current_delegation(uuid, uuid) TO kordra_api_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.issue_permit(uuid, uuid, uuid, uuid, uuid, uuid, bigint, text, text, text, uuid, bigint) TO kordra_api_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.consume_permit(uuid, uuid, uuid, bigint, text, uuid, bigint) TO kordra_worker_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.require_consumed(uuid, uuid, text) TO kordra_wfl_owner, kordra_aud_owner;
