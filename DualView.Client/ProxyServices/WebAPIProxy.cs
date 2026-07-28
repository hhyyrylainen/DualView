using DualView.Shared.Services;

namespace DualView.Client.ProxyServices;

public class WebAPIProxy : HttpBackendAPI
{
    public WebAPIProxy(HttpClient httpClient) : base(httpClient)
    {
    }
}
