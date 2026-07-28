using DualView.Shared.Services;

namespace DualView.Shared.Models;

public interface IEditableText
{
    public string CurrentText { get; set; }

    public string EditTitle { get; }

    public Task SaveChangesAsync(IClientDatabaseService databaseService, IBackendAPI backendAPI);
}

public class EditableTextWithCallback(string initialText)
    : IEditableText
{
    public string CurrentText { get; set; } = initialText;

    public Func<IClientDatabaseService, IBackendAPI, Task>? SaveCallback { get; set; }

    public string EditTitle { get; set; } = "Edit Text";

    public Task SaveChangesAsync(IClientDatabaseService databaseService, IBackendAPI backendAPI)
    {
        if (SaveCallback == null)
            throw new InvalidOperationException("No save callback set");

        return SaveCallback(databaseService, backendAPI);
    }
}
