using System.Security.Cryptography;
using System.Text;

namespace Kordra.Modules.Messaging.Infrastructure.Persistence;

/// <summary>Offline review artifact for Kordra-owned tables only. No execution or native Wolverine SQL.</summary>
public static class OwnedMessagingSchema
{
    public const string Revision = "msg-owned-v1";
    public static string CreationSql => "CREATE SCHEMA msg;\n" + TablesSql;
    public static string TablesSql => Receipt("publication_receipts", "message_id uuid NOT NULL, check (message_id <> '00000000-0000-0000-0000-000000000000'),") +
        Receipt("logical_receipts", "outcome text NOT NULL CHECK (outcome IN ('Completed','Denied')),") + """

        CREATE TABLE msg.quarantine_journal (
            record_id uuid PRIMARY KEY CHECK (record_id <> '00000000-0000-0000-0000-000000000000'),
            installation_id uuid NOT NULL CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'),
            reason text NOT NULL CHECK (reason IN ('InvalidEnvelope','UnknownType','ScopeMismatch','HashConflict','Expired','RetryExhausted','UnknownCommit')),
            observed_hash text NOT NULL CHECK (observed_hash ~ '^[0-9a-f]{64}$'),
            recorded_at_utc timestamptz NOT NULL
        );
        CREATE TABLE msg.recovery_journal (
            record_id uuid PRIMARY KEY CHECK (record_id <> '00000000-0000-0000-0000-000000000000'),
            installation_id uuid NOT NULL CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'),
            operation_id uuid NOT NULL CHECK (operation_id <> '00000000-0000-0000-0000-000000000000'),
            binding_hash text NOT NULL CHECK (binding_hash ~ '^[0-9a-f]{64}$'),
            disposition text NOT NULL CHECK (disposition IN ('InspectRequested','DurableFound','Conflict','StillUnknown')),
            recorded_at_utc timestamptz NOT NULL
        );
        CREATE TABLE msg.schema_manifest (
            revision integer PRIMARY KEY CHECK (revision = 1),
            manifest_sha256 text NOT NULL CHECK (manifest_sha256 ~ '^[0-9a-f]{64}$')
        );
        """;

    public static string Sha256 => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(CreationSql)));

    private static string Receipt(string table, string fields) => $$"""

        CREATE TABLE msg.{{table}} (
            installation_id uuid NOT NULL CHECK (installation_id <> '00000000-0000-0000-0000-000000000000'),
            scope_kind text NOT NULL CHECK (scope_kind IN ('tenant','system')),
            tenant_key uuid NOT NULL,
            operation_id uuid NOT NULL CHECK (operation_id <> '00000000-0000-0000-0000-000000000000'),
            step_id uuid NOT NULL CHECK (step_id <> '00000000-0000-0000-0000-000000000000'),
            step_version bigint NOT NULL CHECK (step_version > 0),
            type_id text NOT NULL CHECK (octet_length(type_id) BETWEEN 1 AND 256),
            type_schema_version integer NOT NULL CHECK (type_schema_version > 0),
            binding_hash text NOT NULL CHECK (binding_hash ~ '^[0-9a-f]{64}$'),
            original_deadline_utc timestamptz NOT NULL,
            {{fields}}
            recorded_at_utc timestamptz NOT NULL,
            CHECK ((scope_kind = 'system' AND tenant_key = '00000000-0000-0000-0000-000000000000') OR
                   (scope_kind = 'tenant' AND tenant_key <> '00000000-0000-0000-0000-000000000000')),
            PRIMARY KEY (installation_id, scope_kind, tenant_key, operation_id, step_id, step_version, type_id)
        );
        """;
}
