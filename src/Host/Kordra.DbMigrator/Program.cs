using Kordra.DbMigrator;

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

Console.Error.WriteLine("Usage: Kordra.DbMigrator identity-access|audit-compliance|tenant-management");
return 2;
