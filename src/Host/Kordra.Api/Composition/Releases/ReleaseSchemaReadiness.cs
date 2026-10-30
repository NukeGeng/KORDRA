using Kordra.Modules.ReleaseManagement.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Kordra.Api;

public interface IReleaseSchemaReadiness { Task<bool> CheckAsync(CancellationToken ct = default); }

/// <summary>Metadata-only minimum REL/APR schema, immutable guards and runtime privilege probe. No DDL.</summary>
public sealed class ReleaseSchemaReadiness(Guid installationId, IDbContextFactory<ReleaseDbContext> releases) : IReleaseSchemaReadiness
{
    public async Task<bool> CheckAsync(CancellationToken ct = default)
    {
        try
        {
            await using var db = await releases.CreateDbContextAsync(ct);
            if (installationId == Guid.Empty) return false;
            var schema = await db.Database.SqlQueryRaw<bool>("""
                SELECT CASE WHEN to_regclass('app.rel_lineages') IS NULL OR to_regclass('app.rel_revisions') IS NULL OR
                    to_regclass('app.rel_commands') IS NULL OR to_regclass('app.apr_policy_heads') IS NULL OR
                    to_regclass('app.apr_policy_revisions') IS NULL OR to_regclass('app.apr_policy_bindings') IS NULL OR
                    to_regclass('app.apr_policy_binding_history') IS NULL OR to_regclass('app.apr_policy_commands') IS NULL OR
                    NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'kordra_app') THEN FALSE ELSE
                  (SELECT count(*) FROM pg_trigger t JOIN pg_class c ON c.oid = t.tgrelid
                     JOIN pg_namespace n ON n.oid = c.relnamespace
                     WHERE n.nspname = 'app' AND t.tgenabled IN ('O','A') AND NOT t.tgisinternal AND (c.relname,t.tgname) IN
                       (('rel_lineages','rel_lineages_immutable'), ('rel_lineages','rel_lineages_no_truncate'),
                        ('rel_revisions','rel_revision_guard'), ('rel_revisions','rel_revisions_no_truncate'),
                        ('rel_commands','rel_commands_immutable'), ('rel_commands','rel_commands_no_truncate'),
                        ('rel_commands','rel_command_recovery_identity'), ('apr_policy_heads','apr_policy_head_guard'),
                        ('apr_policy_revisions','apr_revisions_immutable'), ('apr_policy_bindings','apr_policy_binding_guard'),
                        ('apr_policy_revisions','apr_revisions_no_truncate'), ('apr_policy_heads','apr_heads_no_truncate'),
                        ('apr_policy_bindings','apr_bindings_no_truncate'), ('apr_policy_binding_history','apr_binding_history_immutable'),
                        ('apr_policy_binding_history','apr_binding_history_no_truncate'), ('apr_policy_commands','apr_policy_commands_immutable'),
                        ('apr_policy_commands','apr_commands_no_truncate'))) = 17
                  AND (SELECT count(*) FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
                       WHERE n.nspname = 'app' AND c.convalidated AND c.conname IN
                         ('ck_rel_revision_state','ck_rel_revision_ids','ck_rel_command')) = 3
                  AND (SELECT count(*) FROM pg_index i JOIN pg_class c ON c.oid = i.indexrelid
                       JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'app' AND i.indisunique AND i.indisvalid AND c.relname IN
                         ('IX_rel_revisions_release_id','IX_rel_revisions_release_id_revision_number',
                          'IX_rel_commands_installation_id_tenant_id_principal_id_action_~')) = 3
                  AND (SELECT count(*) FROM information_schema.columns WHERE table_schema = 'app' AND table_name = 'rel_commands'
                       AND column_name IN ('boot_id','invocation_id')) = 2
                  AND has_table_privilege('kordra_app','app.rel_lineages','SELECT') AND has_table_privilege('kordra_app','app.rel_lineages','INSERT')
                  AND has_table_privilege('kordra_app','app.rel_revisions','SELECT') AND has_table_privilege('kordra_app','app.rel_revisions','INSERT')
                  AND has_table_privilege('kordra_app','app.rel_commands','SELECT') AND has_table_privilege('kordra_app','app.rel_commands','INSERT')
                  AND NOT has_table_privilege('kordra_app','app.rel_revisions','DELETE,TRUNCATE')
                  AND NOT has_column_privilege('kordra_app','app.rel_revisions','tenant_id','UPDATE')
                  AND has_column_privilege('kordra_app','app.rel_revisions','state','UPDATE')
                  AND has_table_privilege('kordra_app','app.apr_policy_heads','SELECT')
                  AND has_table_privilege('kordra_app','app.apr_policy_revisions','SELECT')
                  AND has_table_privilege('kordra_app','app.apr_policy_bindings','SELECT') END AS "Value"
                """).SingleAsync(ct);
            if (!schema) return false;
            var reasonPrefix = "release-m-" + installationId.ToString("N") + "-";
            return await db.Database.SqlQuery<bool>($"""
                SELECT NOT EXISTS (
                    SELECT 1 FROM app.rel_commands c LEFT JOIN app.aud_intents a ON a.intent_id = c.audit_intent_id
                    WHERE c.installation_id = {installationId} AND
                      (c.boot_id IS NULL OR c.invocation_id IS NULL OR a.intent_id IS NULL OR
                       a.stage IS DISTINCT FROM 'mutation' OR a.subject_ref IS DISTINCT FROM c.principal_id OR
                       a.tenant_ref IS DISTINCT FROM c.tenant_id OR a.resource_ref IS DISTINCT FROM c.release_revision_id OR
                       a.scope_kind IS DISTINCT FROM 'project' OR a.scope_id IS DISTINCT FROM c.project_id OR
                       a.invocation_id IS DISTINCT FROM c.invocation_id OR a.canonical_intent_hash IS DISTINCT FROM c.audit_intent_hash OR
                       a.canonical_intent_hash IS DISTINCT FROM sha256(convert_to(a.canonical_intent, 'UTF8')) OR
                       a.permission IS DISTINCT FROM CASE WHEN c.action = 'retire' THEN 'release.retire' ELSE 'release.create' END OR
                       a.safe_reason IS DISTINCT FROM ({reasonPrefix} || encode(sha256(convert_to(
                           'release-audit-binding-v1' || chr(10) || encode(c.scoped_key_hash, 'hex') || chr(10) ||
                           encode(c.business_hash, 'hex'), 'UTF8')), 'hex')))
                ) AND NOT EXISTS (
                    SELECT 1 FROM app.aud_intents a LEFT JOIN app.rel_commands c
                      ON c.audit_intent_id = a.intent_id AND c.installation_id = {installationId}
                    WHERE a.stage = 'mutation' AND a.safe_reason LIKE {reasonPrefix + "%"} AND c.id IS NULL
                ) AS "Value"
                """).SingleAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return false; }
    }
}
