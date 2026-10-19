namespace Kordra.Api;

public static class Program
{
    // Command-line arguments intentionally confer no configuration or binding authority.
    public static async Task Main()
    {
        await using var app = LocalApiHost.Create();
        await app.RunAsync();
    }
}
