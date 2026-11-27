namespace Veyra.Access.Views;

/// <summary>What became of a request to change or delete a saved view.</summary>
internal enum SavedViewChange
{
    /// <summary>The view is changed or deleted.</summary>
    Done,

    /// <summary>No view with that ID is visible to the person: none exists, or it is someone else's and private.</summary>
    NotFound,

    /// <summary>The person sees the view, because it is shared, but it is someone else's.</summary>
    NotOwner,
}
