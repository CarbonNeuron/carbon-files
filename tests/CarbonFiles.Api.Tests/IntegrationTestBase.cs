using Xunit;

namespace CarbonFiles.Api.Tests;

public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected TestFixture Fixture { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Fixture = new TestFixture();
        await Fixture.InitializeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Fixture.DisposeAsync();
    }
}
