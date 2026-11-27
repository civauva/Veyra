namespace Veyra.Access.Views;

/// <summary>
/// Saved views of a screen (ADR 0024): a person keeps their own, sees those others shared, and changes only their
/// own.
/// </summary>
/// <param name="store">The views.</param>
/// <param name="clock">The clock for when a view was saved and changed.</param>
internal sealed class SavedViewService(SavedViewStore store, TimeProvider clock)
{
    /// <summary>The most views one person keeps, so a script cannot fill the table.</summary>
    public const int MaxViewsPerPerson = 100;

    /// <summary>The longest name, so it fits the list of views.</summary>
    public const int MaxNameLength = 80;

    /// <summary>The longest query, as long as the logs endpoints read one.</summary>
    public const int MaxQueryLength = 2_000;

    /// <summary>Lists the views of a screen the person sees: their own first, then those others shared.</summary>
    /// <param name="userId">The signed-in person.</param>
    /// <param name="screen">The screen, such as <c>logs</c>.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    /// <returns>The views.</returns>
    /// <exception cref="AccessValidationException">The screen has no saved views.</exception>
    public Task<IReadOnlyList<SavedView>> ListAsync(Guid userId, string? screen, CancellationToken cancellationToken)
    {
        CheckScreen(screen);

        return store.ListAsync(userId, screen!, cancellationToken);
    }

    /// <summary>Saves a new view of the person's.</summary>
    /// <param name="ownerId">The signed-in person, who owns the view.</param>
    /// <param name="screen">The screen it belongs to.</param>
    /// <param name="name">Its name; spaces around it are dropped.</param>
    /// <param name="query">The query text; <see langword="null"/> is an empty query.</param>
    /// <param name="shared">Whether everyone sees it.</param>
    /// <param name="cancellationToken">Cancels the save.</param>
    /// <returns>The saved view.</returns>
    /// <exception cref="AccessValidationException">The screen, name, or query is not acceptable.</exception>
    /// <exception cref="AccessConflictException">The person keeps as many views as allowed.</exception>
    public async Task<SavedView> CreateAsync(
        Guid ownerId,
        string? screen,
        string? name,
        string? query,
        bool shared,
        CancellationToken cancellationToken)
    {
        CheckScreen(screen);

        var now = clock.GetUtcNow();
        var view = new SavedView(
            Guid.CreateVersion7(now),
            ownerId,
            string.Empty,
            screen!,
            CleanName(name),
            CleanQuery(query),
            shared,
            now,
            now);

        if (!await store.InsertAsync(view, MaxViewsPerPerson, cancellationToken))
        {
            throw new AccessConflictException(
                $"You keep {MaxViewsPerPerson} saved views, the most there may be. Delete one to save another.");
        }

        return (await store.FindAsync(view.Id, cancellationToken))!;
    }

    /// <summary>Changes the name, query, and sharing of one of the person's views.</summary>
    /// <param name="userId">The signed-in person.</param>
    /// <param name="id">The view's ID.</param>
    /// <param name="name">The new name.</param>
    /// <param name="query">The new query text.</param>
    /// <param name="shared">Whether everyone sees it now.</param>
    /// <param name="cancellationToken">Cancels the change.</param>
    /// <returns>What became of it, and the view when it changed.</returns>
    /// <exception cref="AccessValidationException">The name or query is not acceptable.</exception>
    public async Task<(SavedViewChange Change, SavedView? View)> UpdateAsync(
        Guid userId,
        Guid id,
        string? name,
        string? query,
        bool shared,
        CancellationToken cancellationToken)
    {
        var cleanName = CleanName(name);
        var cleanQuery = CleanQuery(query);
        var (change, view) = await OwnedAsync(userId, id, cancellationToken);

        if (change != SavedViewChange.Done)
        {
            return (change, null);
        }

        var changed = view! with
        {
            Name = cleanName,
            Query = cleanQuery,
            Shared = shared,
            UpdatedAt = clock.GetUtcNow(),
        };

        return await store.UpdateAsync(changed, cancellationToken)
            ? (SavedViewChange.Done, changed)
            : (SavedViewChange.NotFound, null);
    }

    /// <summary>Deletes one of the person's views.</summary>
    /// <param name="userId">The signed-in person.</param>
    /// <param name="id">The view's ID.</param>
    /// <param name="cancellationToken">Cancels the delete.</param>
    /// <returns>What became of it.</returns>
    public async Task<SavedViewChange> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var (change, _) = await OwnedAsync(userId, id, cancellationToken);

        if (change != SavedViewChange.Done)
        {
            return change;
        }

        return await store.DeleteAsync(id, userId, cancellationToken) ? SavedViewChange.Done : SavedViewChange.NotFound;
    }

    /// <summary>
    /// Finds a view the person may change. Someone else's private view is not found, so its existence stays private;
    /// a shared one is seen, so changing it is refused as not theirs.
    /// </summary>
    private async Task<(SavedViewChange Change, SavedView? View)> OwnedAsync(
        Guid userId,
        Guid id,
        CancellationToken cancellationToken)
    {
        var view = await store.FindAsync(id, cancellationToken);

        if (view is null || (view.OwnerId != userId && !view.Shared))
        {
            return (SavedViewChange.NotFound, null);
        }

        return view.OwnerId == userId ? (SavedViewChange.Done, view) : (SavedViewChange.NotOwner, null);
    }

    private static void CheckScreen(string? screen)
    {
        if (!SavedViewScreens.IsKnown(screen))
        {
            throw new AccessValidationException(SavedViewScreens.Unknown);
        }
    }

    private static string CleanName(string? name)
    {
        var clean = name?.Trim() ?? string.Empty;

        if (clean.Length is 0 or > MaxNameLength)
        {
            throw new AccessValidationException($"A view's name must be 1 to {MaxNameLength} characters.");
        }

        return clean;
    }

    private static string CleanQuery(string? query)
    {
        var clean = query ?? string.Empty;

        if (clean.Length > MaxQueryLength)
        {
            throw new AccessValidationException($"A view's query must be at most {MaxQueryLength} characters.");
        }

        return clean;
    }
}
