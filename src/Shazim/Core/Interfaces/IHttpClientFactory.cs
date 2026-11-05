namespace Shazim.Core.Interfaces;

/// <summary>
/// Provides a shared HttpClient (one per app, as recommended) and allows test doubles.
/// </summary>
public interface IAppHttpClientFactory
{
    HttpClient CreateClient();
}
