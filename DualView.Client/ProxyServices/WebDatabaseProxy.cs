using DualView.Shared.Services;

namespace DualView.Client.ProxyServices;

public class WebDatabaseProxy : HttpDatabaseAccessBase
{
    public WebDatabaseProxy(HttpClient httpClient) : base(httpClient)
    {
    }
}
