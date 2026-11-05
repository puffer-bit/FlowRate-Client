using Xunit;

namespace Client.Infrastructure.Tests;

// Database setup, encryption changes, and cleanup clear process-wide SQLite connection pools.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DatabaseTestCollection
{
    public const string Name = "SQLite database";
}
