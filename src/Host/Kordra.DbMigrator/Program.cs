using Kordra.DbMigrator;

if (args.Length != 1 || args[0] is not ("identity-access" or "audit-compliance" or "tenant-management" or "project-environment" or "infrastructure-inventory" or "connection-management" or "artifact-management"))
{
    Console.Error.WriteLine("Usage: Kordra.DbMigrator identity-access|audit-compliance|tenant-management|project-environment|infrastructure-inventory|connection-management|artifact-management");
    return 2;
}
MigrationInstallationCustody custody;
try { custody = MigrationInstallationCustody.AcquireInstalled(); }
catch { Console.Error.WriteLine("Offline migration requires private canonical installation custody with API stopped."); return 2; }
using var heldInstallation = custody;

if (args is ["identity-access"])
{
    return await IdentityAccessMigrationCommand.RunAsync(args);
}

if (args is ["audit-compliance"])
{
    return await AuditComplianceMigrationCommand.RunAsync(args);
}

if (args is ["tenant-management"])
{
    return await TenantManagementMigrationCommand.RunAsync(args);
}

if (args is ["project-environment"]) return await ProjectEnvironmentMigrationCommand.RunAsync(args);
if (args is ["connection-management"]) return await ConnectionManagementMigrationCommand.RunAsync(args);
if (args is ["artifact-management"]) return await ArtifactManagementMigrationCommand.RunAsync(args);
if (args is ["infrastructure-inventory"]) return await InfrastructureInventoryMigrationCommand.RunAsync(args);

Console.Error.WriteLine("Usage: Kordra.DbMigrator identity-access|audit-compliance|tenant-management|project-environment|infrastructure-inventory|connection-management|artifact-management");
return 2;
