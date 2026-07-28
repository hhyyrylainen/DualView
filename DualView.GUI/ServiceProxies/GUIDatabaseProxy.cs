using System;
using System.Net.Http;
using DualView.GUI.Services;
using DualView.Shared.Services;

namespace DualView.GUI.ServiceProxies;

public class GUIDatabaseProxy : HttpDatabaseAccessBase
{
    public GUIDatabaseProxy(IGuiConfigurationService guiConfigurationService) : base(new HttpClient
    {
        BaseAddress = guiConfigurationService.BackendUrl,
        Timeout = TimeSpan.FromSeconds(30),
    })
    {
    }
}
