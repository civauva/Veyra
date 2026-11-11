using Npgsql;
using Veyra.Access.SecondFactor;

namespace Veyra.Access.Settings;

/// <summary>
/// The settings Admins change in the UI, in <c>access.setting</c>. Today that is only who must use a second factor
/// (ADR 0011).
/// </summary>
/// <param name="dataSource">The PostgreSQL connection pool.</param>
internal sealed class AccessSettingStore(NpgsqlDataSource dataSource)
{
    private const string _secondFactorPolicy = "second_factor_required_for";

    /// <summary>Reads who must use a second factor.</summary>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The policy; <see cref="SecondFactorPolicy.Nobody"/> until an Admin chose one.</returns>
    public async Task<SecondFactorPolicy> GetSecondFactorPolicyAsync(CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand("SELECT value FROM access.setting WHERE name = $1");
        command.Parameters.AddWithValue(_secondFactorPolicy);

        return await command.ExecuteScalarAsync(cancellationToken) is string value
            && Enum.TryParse<SecondFactorPolicy>(value, out var policy)
                ? policy
                : SecondFactorPolicy.Nobody;
    }

    /// <summary>Stores who must use a second factor.</summary>
    /// <param name="policy">The policy.</param>
    /// <param name="cancellationToken">Cancels the update.</param>
    /// <returns>A task that completes when the policy is stored.</returns>
    public async Task SetSecondFactorPolicyAsync(SecondFactorPolicy policy, CancellationToken cancellationToken)
    {
        await using var command = dataSource.CreateCommand(
            """
            INSERT INTO access.setting (name, value) VALUES ($1, $2)
            ON CONFLICT (name) DO UPDATE SET value = excluded.value
            """);
        command.Parameters.AddWithValue(_secondFactorPolicy);
        command.Parameters.AddWithValue(policy.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
