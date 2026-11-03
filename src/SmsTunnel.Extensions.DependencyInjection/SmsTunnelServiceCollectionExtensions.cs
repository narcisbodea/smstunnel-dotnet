using System;
using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using SmsTunnel;
using SmsTunnel.Extensions.DependencyInjection;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers SMSTunnel in an <see cref="IServiceCollection"/>.</summary>
public static class SmsTunnelServiceCollectionExtensions
{
    /// <summary>The name of the <see cref="HttpClient"/> the client sends through (<c>IHttpClientFactory</c>).</summary>
    public const string HttpClientName = "SmsTunnel";

    private const string MissingKeyMessage =
        "The SMSTunnel API key is missing. Set SmsTunnel:ApiKey in your configuration: user secrets " +
        "(dotnet user-secrets set \"SmsTunnel:ApiKey\" \"<your key>\"), the SmsTunnel__ApiKey environment variable or your " +
        "secret store. Create a key in your SMSTunnel dashboard: Apps → Add an app (https://dashboard.smstunnel.io/apps).";

    /// <summary>
    /// Registers <see cref="ISmsTunnelClient"/>, with its settings read from the <c>SmsTunnel</c> section of the app's
    /// <see cref="IConfiguration"/>: <c>SmsTunnel:ApiKey</c> (required), <c>SmsTunnel:DefaultCountryCode</c>,
    /// <c>SmsTunnel:Timeout</c> (seconds or a time span) and <c>SmsTunnel:BaseUrl</c>.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <returns>
    /// The <see cref="IHttpClientBuilder"/> of the client's <see cref="HttpClient"/>, to add handlers to it. Its primary
    /// handler doesn't follow redirects, so the API key is never sent to another address. The client never retries a
    /// send by itself; if you add a retry handler, give every send an <see cref="SendOptions.IdempotencyKey"/>, or a
    /// retried send can go out twice.
    /// </returns>
    public static IHttpClientBuilder AddSmsTunnel(this IServiceCollection services)
    {
        return services.AddSmsTunnel(configureOptions: null);
    }

    /// <summary>
    /// Registers <see cref="ISmsTunnelClient"/> with the settings of the <c>SmsTunnel</c> configuration section, then
    /// <paramref name="configureOptions"/>, which can change any of them.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <param name="configureOptions">Changes the settings after they are read from configuration.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> of the client's <see cref="HttpClient"/>.</returns>
    public static IHttpClientBuilder AddSmsTunnel(this IServiceCollection services, Action<SmsTunnelClientOptions>? configureOptions)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<SmsTunnelClientOptions>()
            .Configure<IServiceProvider>(static (options, provider) =>
            {
                IConfiguration? configuration = provider.GetService<IConfiguration>();
                if (configuration is not null)
                {
                    SmsTunnelConfiguration.Apply(configuration.GetSection(SmsTunnelConfiguration.SectionName), options);
                }
            });

        return AddClient(services, configureOptions);
    }

    /// <summary>
    /// Registers <see cref="ISmsTunnelClient"/> with the settings of this configuration section (its <c>ApiKey</c>,
    /// <c>DefaultCountryCode</c>, <c>Timeout</c> and <c>BaseUrl</c>), then <paramref name="configureOptions"/>.
    /// </summary>
    /// <param name="services">The app's services.</param>
    /// <param name="configuration">The section to read, for example <c>builder.Configuration.GetSection("Sms")</c>.</param>
    /// <param name="configureOptions">Changes the settings after they are read from the section.</param>
    /// <returns>The <see cref="IHttpClientBuilder"/> of the client's <see cref="HttpClient"/>.</returns>
    public static IHttpClientBuilder AddSmsTunnel(this IServiceCollection services, IConfiguration configuration, Action<SmsTunnelClientOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<SmsTunnelClientOptions>()
            .Configure(options => SmsTunnelConfiguration.Apply(configuration, options));

        return AddClient(services, configureOptions);
    }

    private static IHttpClientBuilder AddClient(IServiceCollection services, Action<SmsTunnelClientOptions>? configureOptions)
    {
        if (configureOptions is not null)
        {
            services.Configure(configureOptions);
        }

        services.AddOptions<SmsTunnelClientOptions>()
            .Validate(static options => !string.IsNullOrWhiteSpace(options.ApiKey), MissingKeyMessage);

        IHttpClientBuilder builder = services.AddHttpClient<ISmsTunnelClient, SmsTunnelClient>(
            HttpClientName,
            static (http, provider) => new SmsTunnelClient(http, provider.GetRequiredService<IOptions<SmsTunnelClientOptions>>().Value));

        // SmsTunnelClientOptions.Timeout is the one timeout that applies.
        builder.ConfigureHttpClient(static http => http.Timeout = System.Threading.Timeout.InfiniteTimeSpan);
        builder.ConfigurePrimaryHttpMessageHandler(static () => new HttpClientHandler { AllowAutoRedirect = false });
        return builder;
    }
}
