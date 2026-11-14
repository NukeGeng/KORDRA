-- T12.3a WFL owner function/ACL PoC (ENG-T12-3A-OWNER-FUNCTION-POC-SCOPE section 8). Offline reviewed artifact.
-- Requires WFL schema revision 6 and iam-workflow-authority-v1. Role names are fixed placeholders.
CREATE TABLE wfl.state_edges (
    from_state integer NOT NULL CHECK (from_state BETWEEN 1 AND 10),
    to_state integer NOT NULL CHECK (to_state BETWEEN 1 AND 10),
    worker_kind text NULL CHECK (worker_kind IS NULL OR worker_kind IN ('admit')),
    CONSTRAINT pk_state_edges PRIMARY KEY (from_state, to_state)
);

-- Generated from WorkflowStateRules.CanChangeState; parity is an offline test. Only Pending -> Running is worker-admitted in 12.3a.
INSERT INTO wfl.state_edges (from_state, to_state, worker_kind) VALUES
    (1, 2, 'admit'), (1, 4, NULL), (1, 9, NULL), (1, 10, NULL),
    (2, 3, NULL), (2, 4, NULL), (2, 5, NULL), (2, 6, NULL), (2, 7, NULL), (2, 8, NULL),
    (3, 2, NULL), (3, 4, NULL), (3, 5, NULL), (3, 8, NULL), (3, 10, NULL),
    (4, 2, NULL), (4, 5, NULL), (4, 10, NULL),
    (5, 6, NULL), (5, 9, NULL);

CREATE FUNCTION wfl.stage_transition(p_permit_id uuid, p_installation_id uuid, p_tenant_id uuid, p_instance_id uuid,
    p_step_id uuid, p_operation_id uuid, p_specification_hash text, p_expected_version bigint, p_next_state integer,
    OUT resulting_version bigint, OUT duplicate boolean)
LANGUAGE plpgsql VOLATILE SECURITY DEFINER SET search_path = pg_catalog, pg_temp SET lock_timeout = '1000ms' AS $fn$
DECLARE
    v_binding record;
    v_instance record;
    v_prior record;
BEGIN
    SELECT * INTO v_binding FROM iam_workflow.require_consumed(p_permit_id, p_operation_id, p_specification_hash);
    IF v_binding.installation_id IS DISTINCT FROM p_installation_id OR v_binding.tenant_id IS DISTINCT FROM p_tenant_id
        OR v_binding.instance_id IS DISTINCT FROM p_instance_id OR v_binding.expected_version IS DISTINCT FROM p_expected_version THEN
        RAISE EXCEPTION USING ERRCODE = 'KD101', MESSAGE = 'scope_mismatch';
    END IF;
    IF p_step_id IS NULL OR p_next_state IS NULL THEN
        RAISE EXCEPTION USING ERRCODE = 'KD101', MESSAGE = 'scope_mismatch';
    END IF;
    SELECT i.state, i.version, i.specification_hash INTO v_instance FROM wfl.instances AS i
    WHERE i.installation_id = p_installation_id AND i.tenant_id = p_tenant_id AND i.instance_id = p_instance_id FOR UPDATE;
    IF NOT FOUND THEN
        RAISE EXCEPTION USING ERRCODE = 'KD102', MESSAGE = 'workflow_unavailable';
    END IF;
    SELECT t.step_id, t.resulting_version, t.resulting_state, t.specification_hash INTO v_prior FROM wfl.transitions AS t
    WHERE t.installation_id = p_installation_id AND t.tenant_id = p_tenant_id AND t.instance_id = p_instance_id
        AND t.operation_id = p_operation_id;
    IF FOUND THEN
        IF v_prior.specification_hash <> p_specification_hash OR v_prior.step_id <> p_step_id
            OR v_prior.resulting_state <> p_next_state OR v_prior.resulting_version <> p_expected_version + 1 THEN
            RAISE EXCEPTION USING ERRCODE = 'KD103', MESSAGE = 'operation_conflict';
        END IF;
        resulting_version := v_prior.resulting_version; duplicate := true;
        RETURN;
    END IF;
    IF EXISTS (SELECT 1 FROM wfl.cancellation_intents AS c WHERE c.installation_id = p_installation_id
            AND c.tenant_id = p_tenant_id AND c.instance_id = p_instance_id)
        AND p_next_state NOT IN (5, 6, 9) THEN
        RAISE EXCEPTION USING ERRCODE = 'KD104', MESSAGE = 'cancellation_pending';
    END IF;
    IF p_next_state IN (7, 8, 9, 10) AND (
        EXISTS (SELECT 1 FROM wfl.operation_custody AS o WHERE o.installation_id = p_installation_id
            AND o.tenant_id = p_tenant_id AND o.instance_id = p_instance_id AND o.phase <> 3)
        OR EXISTS (SELECT 1 FROM wfl.steps AS s WHERE s.installation_id = p_installation_id
            AND s.tenant_id = p_tenant_id AND s.instance_id = p_instance_id AND s.state = 6)
        OR EXISTS (SELECT 1 FROM wfl.timers AS m WHERE m.installation_id = p_installation_id
            AND m.tenant_id = p_tenant_id AND m.instance_id = p_instance_id AND m.status = 2)) THEN
        RAISE EXCEPTION USING ERRCODE = 'KD105', MESSAGE = 'reconciliation_required';
    END IF;
    IF v_instance.specification_hash <> p_specification_hash THEN
        RAISE EXCEPTION USING ERRCODE = 'KD106', MESSAGE = 'binding_conflict';
    END IF;
    IF v_instance.version <> p_expected_version THEN
        RAISE EXCEPTION USING ERRCODE = 'KD107', MESSAGE = 'version_changed';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM wfl.state_edges AS e WHERE e.from_state = v_instance.state
            AND e.to_state = p_next_state AND e.worker_kind = v_binding.transition_kind) THEN
        RAISE EXCEPTION USING ERRCODE = 'KD108', MESSAGE = 'state_conflict';
    END IF;
    UPDATE wfl.instances AS i SET version = i.version + 1, state = p_next_state
    WHERE i.installation_id = p_installation_id AND i.tenant_id = p_tenant_id AND i.instance_id = p_instance_id;
    INSERT INTO wfl.transitions (installation_id, tenant_id, instance_id, operation_id, step_id, resulting_version,
        resulting_state, specification_hash, recorded_at_utc)
    VALUES (p_installation_id, p_tenant_id, p_instance_id, p_operation_id, p_step_id, p_expected_version + 1,
        p_next_state, p_specification_hash, clock_timestamp());
    resulting_version := p_expected_version + 1; duplicate := false;
END
$fn$;

-- A new owner needs CREATE on the schema only for the ownership transfer; it is revoked immediately after.
GRANT CREATE ON SCHEMA wfl TO kordra_wfl_owner;
ALTER TABLE wfl.state_edges OWNER TO kordra_wfl_owner;
ALTER FUNCTION wfl.stage_transition(uuid, uuid, uuid, uuid, uuid, uuid, text, bigint, integer) OWNER TO kordra_wfl_owner;
REVOKE CREATE ON SCHEMA wfl FROM kordra_wfl_owner;

-- Existing WFL tables keep their migration owner; the WFL function owner receives exact rights only.
GRANT USAGE ON SCHEMA wfl TO kordra_wfl_owner;
GRANT SELECT ON TABLE wfl.instances, wfl.transitions, wfl.cancellation_intents, wfl.operation_custody, wfl.steps, wfl.timers
    TO kordra_wfl_owner;
GRANT UPDATE (version, state) ON TABLE wfl.instances TO kordra_wfl_owner;
GRANT INSERT ON TABLE wfl.transitions TO kordra_wfl_owner;

REVOKE ALL ON FUNCTION wfl.stage_transition(uuid, uuid, uuid, uuid, uuid, uuid, text, bigint, integer) FROM PUBLIC;
GRANT USAGE ON SCHEMA wfl TO kordra_worker_runtime;
GRANT EXECUTE ON FUNCTION wfl.stage_transition(uuid, uuid, uuid, uuid, uuid, uuid, text, bigint, integer) TO kordra_worker_runtime;
