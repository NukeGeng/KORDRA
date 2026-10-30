using Kordra.Modules.ApprovalGovernance.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

/// <summary>Read-only APR schema/privilege and installation receipt/history integrity checks. Never runs migrations or repairs.</summary>
public sealed class ApprovalSchemaReadiness(Guid installation, IDbContextFactory<ApprovalDbContext> approvals,
    IReleaseSchemaReadiness releases) : IApprovalSchemaReadiness
{
    public async Task<bool> CheckAsync(CancellationToken ct)
    {
        if (installation == Guid.Empty || !await releases.CheckAsync(ct)) return false;
        try
        {
            await using var db = await approvals.CreateDbContextAsync(ct);
            var schema = await db.Database.SqlQueryRaw<bool>("""
                SELECT CASE WHEN to_regclass('app.apr_requests') IS NULL OR to_regclass('app.apr_decisions') IS NULL OR
                    to_regclass('app.apr_request_commands') IS NULL THEN FALSE ELSE
                  (SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                   WHERE n.nspname='app' AND t.tgenabled IN ('O','A') AND NOT t.tgisinternal AND (c.relname,t.tgname) IN
                     (('apr_requests','apr_request_guard'),('apr_requests','apr_requests_no_truncate'),
                      ('apr_decisions','apr_decisions_immutable'),('apr_decisions','apr_decisions_no_truncate'),
                      ('apr_request_commands','apr_request_commands_immutable'),('apr_request_commands','apr_request_commands_no_truncate'),
                      ('apr_requests','apr_request_decision_consistency'),('apr_decisions','apr_decision_request_consistency'))) = 8
                  AND (SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid=t.tgrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                       WHERE n.nspname='app' AND t.tgdeferrable AND t.tginitdeferred AND (c.relname,t.tgname) IN
                         (('apr_requests','apr_request_decision_consistency'),('apr_decisions','apr_decision_request_consistency'))) = 2
                  AND (SELECT count(*) FROM pg_constraint c JOIN pg_class r ON r.oid=c.conrelid JOIN pg_namespace n ON n.oid=r.relnamespace
                       WHERE n.nspname='app' AND c.convalidated AND (r.relname,c.conname) IN
                         (('apr_requests','ck_apr_request'),('apr_decisions','ck_apr_decision'),('apr_request_commands','ck_apr_request_command'))) = 3
                  AND (SELECT count(*) FROM pg_index i JOIN pg_class c ON c.oid=i.indexrelid JOIN pg_namespace n ON n.oid=c.relnamespace
                       WHERE n.nspname='app' AND i.indisunique AND i.indisvalid AND i.indpred IS NULL AND i.indexprs IS NULL AND c.relname IN
                         ('IX_apr_requests_installation_id_release_revision_id','IX_apr_decisions_request_id_request_version',
                          'IX_apr_request_commands_installation_id_tenant_id_principal_id~')) = 3
                  AND (SELECT count(*) FROM pg_constraint c JOIN pg_class r ON r.oid=c.conrelid JOIN pg_namespace n ON n.oid=r.relnamespace
                       WHERE n.nspname='app' AND c.convalidated AND c.contype='f' AND r.relname IN ('apr_decisions','apr_request_commands')) = 2
                  AND has_table_privilege('kordra_app','app.apr_requests','SELECT')
                  AND has_table_privilege('kordra_app','app.apr_requests','INSERT')
                  AND NOT has_table_privilege('kordra_app','app.apr_requests','DELETE,TRUNCATE')
                  AND has_table_privilege('kordra_app','app.apr_decisions','SELECT')
                  AND has_table_privilege('kordra_app','app.apr_decisions','INSERT')
                  AND NOT has_table_privilege('kordra_app','app.apr_decisions','UPDATE,DELETE,TRUNCATE')
                  AND has_table_privilege('kordra_app','app.apr_request_commands','SELECT')
                  AND has_table_privilege('kordra_app','app.apr_request_commands','INSERT')
                  AND NOT has_table_privilege('kordra_app','app.apr_request_commands','UPDATE,DELETE,TRUNCATE')
                  AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema='app' AND table_name='apr_requests'
                      AND has_column_privilege('kordra_app','app.apr_requests',column_name,'UPDATE') IS DISTINCT FROM
                        (column_name IN ('version','state','granted_at_utc','grant_expires_at_utc','approver_principal_id','approver_evidence')))
                  END AS "Value"
                """).SingleAsync(ct);
            if (!schema) return false;
            var prefix = "approval-m-" + installation.ToString("N") + "-";
            return await db.Database.SqlQuery<bool>($"""
                SELECT NOT EXISTS (
                    SELECT 1 FROM app.apr_request_commands c
                    LEFT JOIN app.apr_requests r ON r.id=c.request_id AND r.installation_id=c.installation_id AND r.tenant_id=c.tenant_id
                    LEFT JOIN app.aud_intents a ON a.intent_id=c.audit_intent_id
                    WHERE c.installation_id={installation} AND (r.id IS NULL OR a.intent_id IS NULL OR
                        a.stage IS DISTINCT FROM 'mutation' OR a.subject_ref IS DISTINCT FROM c.principal_id OR
                        a.tenant_ref IS DISTINCT FROM c.tenant_id OR a.resource_ref IS DISTINCT FROM c.request_id OR
                        a.scope_kind IS DISTINCT FROM 'environment' OR a.scope_id IS DISTINCT FROM r.environment_id OR
                        a.invocation_id IS DISTINCT FROM c.invocation_id OR a.canonical_intent_hash IS DISTINCT FROM c.audit_intent_hash OR
                        a.canonical_intent_hash IS DISTINCT FROM sha256(convert_to(a.canonical_intent,'UTF8')) OR
                        a.permission IS DISTINCT FROM CASE c.action WHEN 'request' THEN 'release.request-approval'
                          WHEN 'revoke' THEN 'release.approval.revoke' ELSE 'release.approve' END OR
                        a.safe_reason IS DISTINCT FROM ({prefix} || encode(sha256(convert_to('release-audit-binding-v1' || chr(10) ||
                          encode(c.scoped_key_hash,'hex') || chr(10) || encode(c.business_hash,'hex'),'UTF8')),'hex')) OR
                        (c.action='request' AND (c.principal_id<>r.requester_principal_id OR c.created_at_utc<>r.created_at_utc)) OR
                        (c.action<>'request' AND NOT EXISTS (SELECT 1 FROM app.apr_decisions d WHERE d.request_id=r.id AND
                          d.installation_id=c.installation_id AND d.tenant_id=c.tenant_id AND d.action=c.action AND
                          d.actor_principal_id=c.principal_id AND d.occurred_at_utc=c.created_at_utc)))
                ) AND NOT EXISTS (
                    SELECT 1 FROM app.aud_intents a LEFT JOIN app.apr_request_commands c
                      ON c.audit_intent_id=a.intent_id AND c.installation_id={installation}
                    WHERE a.stage='mutation' AND a.safe_reason LIKE {prefix + "%"} AND c.id IS NULL
                ) AND NOT EXISTS (
                    SELECT 1 FROM app.apr_requests r WHERE r.installation_id={installation} AND (
                      (SELECT count(*) FROM app.apr_request_commands c WHERE c.request_id=r.id AND c.installation_id=r.installation_id AND c.tenant_id=r.tenant_id)<>r.version OR
                      (SELECT count(*) FROM app.apr_request_commands c WHERE c.request_id=r.id AND c.installation_id=r.installation_id AND c.tenant_id=r.tenant_id AND c.action='request')<>1 OR
                      (SELECT count(*) FROM app.apr_decisions d WHERE d.request_id=r.id AND d.installation_id=r.installation_id AND d.tenant_id=r.tenant_id)<>r.version-1)
                ) AS "Value"
                """).SingleAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; }
    }
}
