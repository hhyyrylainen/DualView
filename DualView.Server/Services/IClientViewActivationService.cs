namespace DualView.Server.Services;

/// <summary>
///   Coordinates opening client views and starting a GUI when no client responds.
/// </summary>
public interface IClientViewActivationService
{
    // Requests on the server
    public void ShowImporter();

    // Clients send these info
    public void ClientConnected();
    public void ImporterShown(Guid requestId);
}
