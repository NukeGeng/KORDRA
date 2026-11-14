-- T12.3a IAM owner functions/ACL PoC (ENG-T12-3A-OWNER-FUNCTION-POC-SCOPE section 8). Offline reviewed artifact.
-- Role names are fixed placeholders; a disposable runner may only append its run suffix to them.
CREATE SCHEMA iam_workflow AUTHORIZATION kordra_iam_owner;
REVOKE ALL ON SCHEMA iam_workflow FROM PUBLIC;

CREATE TABLE iam_workflow.authority_fences (
    installation_id uuid PRIMARY KEY CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    epoch bigint NOT NULL CHECK (epoch >= 1),
    manifest_revision bigint NOT NULL CHECK (manifest_revision >= 1)
);

CREATE TABLE iam_workflow.service_logins (
    login_name name PRIMARY KEY,
    service_id uuid NOT NULL CHECK (service_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    generation bigint NOT NULL CHECK (generation >= 1),
    installation_id uuid NOT NULL REFERENCES iam_workflow.authority_fences (installation_id) ON DELETE RESTRICT,
    active boolean NOT NULL
);

CREATE TABLE iam_workflow.permits (
    permit_id uuid PRIMARY KEY CHECK (permit_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    service_id uuid NOT NULL,
    generation bigint NOT NULL CHECK (generation >= 1),
    installation_id uuid NOT NULL REFERENCES iam_workflow.authority_fences (installation_id) ON DELETE RESTRICT,
    tenant_id uuid NOT NULL CHECK (tenant_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    instance_id uuid NOT NULL CHECK (instance_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    operation_id uuid NOT NULL CHECK (operation_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    expected_version bigint NOT NULL CHECK (expected_version >= 1 AND expected_version < 9223372036854775807),
    specification_hash character varying(64) NOT NULL CHECK (specification_hash ~ '^[0-9a-f]{64}$'),
    transition_kind text NOT NULL CHECK (transition_kind IN ('admit')),
    authority_epoch bigint NOT NULL CHECK (authority_epoch >= 1),
    boot_id uuid NOT NULL CHECK (boot_id <> '00000000-0000-0000-0000-000000000000'::uuid),
    custody_epoch bigint NOT NULL CHECK (custody_epoch >= 0),
    issued_at timestamp with time zone NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    consumed_xact xid8 NULL,
    consumed_at timestamp with time zone NULL,
    CONSTRAINT ck_permits_ttl CHECK (expires_at > issued_at AND expires_at - issued_at <= interval '2 seconds'),
    CONSTRAINT ck_permits_consumed CHECK ((consumed_xact IS NULL) = (consumed_at IS NULL)),
    CONSTRAINT uq_permits_operation UNIQUE (installation_id, tenant_id, instance_id, operation_id)
);

CREATE FUNCTION iam_workflow.current_service(OUT service_id uuid, OUT generation bigint, OUT installation_id uuid)
LANGUAGE plpgsql STABLE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
BEGIN
    SELECT s.service_id, s.generation, s.installation_id INTO service_id, generation, installation_id
    FROM iam_workflow.service_logins AS s
    WHERE s.login_name = session_user AND s.active;
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
    IF p_installation_id IS DISTINCT FROM v_service.installation_id THEN
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

CREATE FUNCTION iam_workflow.consume_permit(p_permit_id uuid, p_operation_id uuid, p_instance_id uuid,
    p_expected_version bigint, p_specification_hash text, p_boot_id uuid, p_custody_epoch bigint,
    OUT tenant_id uuid, OUT consumed_xact xid8, OUT duplicate boolean)
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET lock_timeout = '1000ms' AS $fn$
DECLARE
    v_service record;
    v_epoch bigint;
    v_permit iam_workflow.permits%ROWTYPE;
BEGIN
    SELECT * INTO v_service FROM iam_workflow.current_service();
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
    UPDATE iam_workflow.permits AS p SET consumed_xact = pg_current_xact_id(), consumed_at = clock_timestamp()
    WHERE p.permit_id = p_permit_id;
    tenant_id := v_permit.tenant_id; consumed_xact := pg_current_xact_id(); duplicate := false;
END
$fn$;

CREATE FUNCTION iam_workflow.require_consumed(p_permit_id uuid, p_operation_id uuid, p_specification_hash text,
    OUT installation_id uuid, OUT tenant_id uuid, OUT instance_id uuid, OUT expected_version bigint,
    OUT transition_kind text, OUT service_id uuid)
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp AS $fn$
DECLARE
    v_service record;
BEGIN
    SELECT * INTO v_service FROM iam_workflow.current_service();
    SELECT p.installation_id, p.tenant_id, p.instance_id, p.expected_version, p.transition_kind, p.service_id
    INTO installation_id, tenant_id, instance_id, expected_version, transition_kind, service_id
    FROM iam_workflow.permits AS p
    WHERE p.permit_id = p_permit_id AND p.consumed_xact = pg_current_xact_id()
        AND p.service_id = v_service.service_id AND p.generation = v_service.generation
        AND p.operation_id = p_operation_id AND p.specification_hash = p_specification_hash;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'KD007', MESSAGE = 'permit_not_consumed_in_root';
    END IF;
    -- Final handoff recheck: an expired permit never admits, even inside its consuming root.
    IF clock_timestamp() >= (SELECT p.expires_at FROM iam_workflow.permits AS p WHERE p.permit_id = p_permit_id) THEN
        RAISE EXCEPTION USING ERRCODE = 'KD006', MESSAGE = 'permit_expired';
    END IF;
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

ALTER TABLE iam_workflow.authority_fences OWNER TO kordra_iam_owner;
ALTER TABLE iam_workflow.service_logins OWNER TO kordra_iam_owner;
ALTER TABLE iam_workflow.permits OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.current_service() OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.acquire_fence(uuid) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.advance_epoch(uuid, text) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.consume_permit(uuid, uuid, uuid, bigint, text, uuid, bigint) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.require_consumed(uuid, uuid, text) OWNER TO kordra_iam_owner;
ALTER FUNCTION iam_workflow.advance_floor(uuid, timestamp with time zone) OWNER TO kordra_iam_owner;

-- Existing IAM floor table keeps its migration owner; the IAM function owner receives exact rights only.
GRANT USAGE ON SCHEMA app TO kordra_iam_owner;
GRANT SELECT, INSERT, UPDATE (admitted_utc) ON TABLE app.iam_authority_clock_floors TO kordra_iam_owner;

REVOKE ALL ON FUNCTION iam_workflow.current_service() FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.acquire_fence(uuid) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.advance_epoch(uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.consume_permit(uuid, uuid, uuid, bigint, text, uuid, bigint) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.require_consumed(uuid, uuid, text) FROM PUBLIC;
REVOKE ALL ON FUNCTION iam_workflow.advance_floor(uuid, timestamp with time zone) FROM PUBLIC;

GRANT USAGE ON SCHEMA iam_workflow TO kordra_worker_runtime, kordra_api_runtime, kordra_wfl_owner, kordra_aud_owner;
GRANT EXECUTE ON FUNCTION iam_workflow.current_service() TO kordra_worker_runtime, kordra_api_runtime, kordra_wfl_owner, kordra_aud_owner;
GRANT EXECUTE ON FUNCTION iam_workflow.acquire_fence(uuid) TO kordra_worker_runtime, kordra_api_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.advance_epoch(uuid, text) TO kordra_api_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.consume_permit(uuid, uuid, uuid, bigint, text, uuid, bigint) TO kordra_worker_runtime;
GRANT EXECUTE ON FUNCTION iam_workflow.require_consumed(uuid, uuid, text) TO kordra_wfl_owner, kordra_aud_owner;
GRANT EXECUTE ON FUNCTION iam_workflow.advance_floor(uuid, timestamp with time zone) TO kordra_worker_runtime, kordra_api_runtime;
