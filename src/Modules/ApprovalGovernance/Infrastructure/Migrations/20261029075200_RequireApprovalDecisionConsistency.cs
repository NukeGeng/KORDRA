using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Kordra.Modules.ApprovalGovernance.Infrastructure.Migrations;

[DbContext(typeof(ApprovalDbContext))]
[Migration("20261029075200_RequireApprovalDecisionConsistency")]
public sealed class RequireApprovalDecisionConsistency : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        CREATE FUNCTION app.apr_require_decision_consistency() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE rid uuid; rec app.apr_requests%ROWTYPE; n integer; first_action text; second_action text; first_actor uuid;
        BEGIN
          rid := CASE WHEN TG_TABLE_NAME = 'apr_requests' THEN NEW.id ELSE NEW.request_id END;
          SELECT * INTO rec FROM app.apr_requests WHERE id = rid;
          IF NOT FOUND THEN RAISE EXCEPTION 'approval request missing'; END IF;
          SELECT count(*), max(action) FILTER (WHERE request_version = 2), max(action) FILTER (WHERE request_version = 3)
            INTO n, first_action, second_action FROM app.apr_decisions WHERE request_id = rid;
          IF rec.version = 1 AND rec.state = 'Pending' AND n = 0 THEN RETURN NULL; END IF;
          IF rec.version = 2 AND n = 1 AND ((rec.state = 'Granted' AND first_action = 'grant') OR
              (rec.state = 'Rejected' AND first_action = 'reject') OR (rec.state = 'Revoked' AND first_action = 'revoke')) THEN
            IF rec.state = 'Granted' THEN
              SELECT actor_principal_id INTO first_actor FROM app.apr_decisions WHERE request_id = rid AND request_version = 2;
              IF first_actor IS DISTINCT FROM rec.approver_principal_id THEN RAISE EXCEPTION 'approval actor mismatch'; END IF;
            END IF;
            RETURN NULL;
          END IF;
          IF rec.version = 3 AND rec.state = 'Revoked' AND n = 2 AND first_action = 'grant' AND second_action = 'revoke' THEN
            SELECT actor_principal_id INTO first_actor FROM app.apr_decisions WHERE request_id = rid AND request_version = 2;
            IF first_actor IS DISTINCT FROM rec.approver_principal_id THEN RAISE EXCEPTION 'approval actor mismatch'; END IF;
            RETURN NULL;
          END IF;
          RAISE EXCEPTION 'approval decision and request state mismatch';
        END $$;
        CREATE CONSTRAINT TRIGGER apr_request_decision_consistency AFTER INSERT OR UPDATE ON app.apr_requests
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.apr_require_decision_consistency();
        CREATE CONSTRAINT TRIGGER apr_decision_request_consistency AFTER INSERT ON app.apr_decisions
          DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION app.apr_require_decision_consistency();
        """);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new InvalidOperationException("Approval history consistency cannot be removed by migration rollback.");
}
