using Veyra.Testing;

namespace Veyra.Alerting.Tests;

/// <summary>Shares one PostgreSQL container across the tests of this project.</summary>
[CollectionDefinition(PostgresGroup.Name)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresFixture>;
