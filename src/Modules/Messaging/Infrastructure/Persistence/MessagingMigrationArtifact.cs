using System.Security.Cryptography;
using System.Text;
using Weasel.Core.Migrations;

namespace Kordra.Modules.Messaging.Infrastructure.Persistence;

/// <summary>Offline immutable review bundle only. Never executes DDL, creates roles or grants authority.</summary>
public sealed class MessagingMigrationArtifact
{
    public string SchemaRevision => OwnedMessagingSchema.Revision;
    public string WolverineVersion => "6.59.0";
    public string PackageGraphSha256 { get; }
    public string CreationSql { get; }
    public string CreationSqlSha256 { get; }
    public string OwnedAclSql => MessagingAclReview.OwnedObjectSql;
    public string OwnedAclSha256 { get; }
    public NativeMessagingInventory NativeInventory { get; }
    public string NativeAclSha256 { get; }
    public string ReviewManifestSha256 { get; }
    public bool NativeAclComplete => NativeInventory.IsReviewedEmptyCatalogProfile;
    public bool ReadyToApply => false;

    public MessagingMigrationArtifact(IDatabase nativeDatabase, string packageGraphSha256)
    {
        ArgumentNullException.ThrowIfNull(nativeDatabase);
        if (packageGraphSha256 is not { Length: 64 } || !packageGraphSha256.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new ArgumentException("Exact lowercase package graph fingerprint required.", nameof(packageGraphSha256));
        var schemas = nativeDatabase.AllSchemaNames().ToArray();
        if (!schemas.Contains(MessagingSchemaProfile.EnvelopeSchema, StringComparer.Ordinal) ||
            schemas.Any(s => s is not (MessagingSchemaProfile.EnvelopeSchema or MessagingSchemaProfile.TransportSchema)))
            throw new InvalidOperationException("Native schema inventory is outside the accepted MSG profile.");
        PackageGraphSha256 = packageGraphSha256;
        NativeInventory = new NativeMessagingInventory(nativeDatabase);
        CreationSql = nativeDatabase.ToDatabaseScript() + "\nCREATE SCHEMA IF NOT EXISTS msg_transport;\n" + OwnedMessagingSchema.TablesSql;
        CreationSqlSha256 = Hash(CreationSql); OwnedAclSha256 = Hash(OwnedAclSql);
        NativeAclSha256 = Hash(NativeInventory.ReviewSql);
        ReviewManifestSha256 = Hash(string.Join('\n', SchemaRevision, WolverineVersion, PackageGraphSha256,
            CreationSqlSha256, OwnedAclSha256, NativeAclSha256));
    }

    private static string Hash(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public override string ToString() => "MessagingMigrationArtifact [review-only; not operational approval]";
}
