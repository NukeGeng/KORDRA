-- T12.3a AUD owner function/ACL PoC (ENG-T12-3A-OWNER-FUNCTION-POC-SCOPE section 8). Offline reviewed artifact.
-- C# AuditIntentCanonicalizer stays the only canonicalizer (D2): SQL verifies the hash of the exact canonical text and
-- binds the extracted columns. Requires iam-workflow-authority-v1. Role names are fixed placeholders.
CREATE FUNCTION app.aud_append_bound_intent(p_permit_id uuid, p_operation_id uuid, p_specification_hash text,
    p_canonical_intent text, p_canonical_hash bytea, OUT record_id uuid, OUT duplicate boolean)
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET lock_timeout = '1000ms' AS $fn$
DECLARE
    v_binding record;
    v_json jsonb;
    v_intent_id uuid;
    v_existing record;
    v_record_id uuid;
BEGIN
    SELECT * INTO v_binding FROM iam_workflow.require_consumed(p_permit_id, p_operation_id, p_specification_hash);
    IF p_canonical_intent IS NULL OR p_canonical_hash IS NULL OR octet_length(p_canonical_hash) <> 32
        OR octet_length(convert_to(p_canonical_intent, 'UTF8')) > 8192
        OR sha256(convert_to(p_canonical_intent, 'UTF8')) <> p_canonical_hash THEN
        RAISE EXCEPTION USING ERRCODE = 'KD201', MESSAGE = 'canonical_hash_mismatch';
    END IF;
    -- Exactly the canonical member set, no duplicate members (json keeps duplicates; jsonb would hide them).
    IF json_typeof(p_canonical_intent::json) <> 'object'
        OR (SELECT count(*) FROM json_object_keys(p_canonical_intent::json)) <> 18
        OR (SELECT array_agg(k ORDER BY k COLLATE "C") FROM jsonb_object_keys(p_canonical_intent::jsonb) AS k) <> ARRAY[
            'actionOutcome', 'authorityRevision', 'correlationId', 'intentId', 'invocationId', 'occurredAtUtc', 'permission',
            'policyRevision', 'resourceRef', 'resultDigest', 'resultSizeBytes', 'safeReason', 'schemaVersion', 'scopeId',
            'scopeKind', 'stage', 'subjectRef', 'tenantRef']::text[] THEN
        RAISE EXCEPTION USING ERRCODE = 'KD202', MESSAGE = 'canonical_shape';
    END IF;
    v_json := p_canonical_intent::jsonb;
    IF (v_json->>'schemaVersion') IS DISTINCT FROM '1' OR (v_json->>'stage') NOT IN ('mutation', 'outcome')
        OR (v_json->>'tenantRef')::uuid IS DISTINCT FROM v_binding.tenant_id
        OR (v_json->>'resourceRef')::uuid IS DISTINCT FROM v_binding.instance_id
        OR (v_json->>'occurredAtUtc') !~ '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{7}Z$' THEN
        RAISE EXCEPTION USING ERRCODE = 'KD204', MESSAGE = 'intent_scope_mismatch';
    END IF;
    v_intent_id := (v_json->>'intentId')::uuid;
    SELECT a.record_id, a.canonical_intent_hash INTO v_existing FROM app.aud_intents AS a WHERE a.intent_id = v_intent_id;
    IF FOUND THEN
        IF v_existing.canonical_intent_hash <> p_canonical_hash THEN
            RAISE EXCEPTION USING ERRCODE = 'KD203', MESSAGE = 'intent_conflict';
        END IF;
        record_id := v_existing.record_id; duplicate := true;
        RETURN;
    END IF;
    v_record_id := gen_random_uuid();
    INSERT INTO app.aud_intents (record_id, schema_version, intent_id, invocation_id, stage, subject_ref, tenant_ref,
        resource_ref, permission, scope_kind, scope_id, policy_revision, authority_revision, safe_reason, occurred_at_utc,
        correlation_id, result_digest, result_size_bytes, canonical_intent, canonical_intent_hash, recorded_at_utc)
    VALUES (v_record_id, 1, v_intent_id, (v_json->>'invocationId')::uuid, v_json->>'stage', (v_json->>'subjectRef')::uuid,
        (v_json->>'tenantRef')::uuid, (v_json->>'resourceRef')::uuid, v_json->>'permission', v_json->>'scopeKind',
        (v_json->>'scopeId')::uuid, (v_json->>'policyRevision')::bigint, (v_json->>'authorityRevision')::bigint,
        v_json->>'safeReason',
        -- Npgsql truncates 100 ns ticks to microseconds; truncate the seventh digit the same way.
        regexp_replace(v_json->>'occurredAtUtc', '^(.*\.\d{6})\dZ$', '\1Z')::timestamp with time zone,
        (v_json->>'correlationId')::uuid, v_json->>'resultDigest', (v_json->>'resultSizeBytes')::bigint,
        p_canonical_intent, p_canonical_hash, date_trunc('microseconds', clock_timestamp()));
    record_id := v_record_id; duplicate := false;
END
$fn$;

-- A new owner needs CREATE on the schema only for the ownership transfer; it is revoked immediately after.
GRANT CREATE ON SCHEMA app TO kordra_aud_owner;
ALTER FUNCTION app.aud_append_bound_intent(uuid, uuid, text, text, bytea) OWNER TO kordra_aud_owner;
REVOKE CREATE ON SCHEMA app FROM kordra_aud_owner;

-- Existing AUD table keeps its migration owner; the AUD function owner may only read and append (no UPDATE/DELETE).
GRANT USAGE ON SCHEMA app TO kordra_aud_owner;
GRANT SELECT, INSERT ON TABLE app.aud_intents TO kordra_aud_owner;

REVOKE ALL ON FUNCTION app.aud_append_bound_intent(uuid, uuid, text, text, bytea) FROM PUBLIC;
GRANT USAGE ON SCHEMA app TO kordra_worker_runtime;
GRANT EXECUTE ON FUNCTION app.aud_append_bound_intent(uuid, uuid, text, text, bytea) TO kordra_worker_runtime;
