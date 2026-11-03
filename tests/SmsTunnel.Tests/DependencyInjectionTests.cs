using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace SmsTunnel.Tests;

public class DependencyInjectionTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings, Action<IServiceCollection>? register = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
        if (register is null)
        {
            services.AddSmsTunnel();
        }
        else
        {
            register(services);
        }

        return services.BuildServiceProvider();
    }

    [Fact]
    public void Reads_the_SmsTunnel_section()
    {
        using ServiceProvider provider = Build(new()
        {
            ["SmsTunnel:ApiKey"] = Clients.Key,
            ["SmsTunnel:BaseUrl"] = "http://127.0.0.1:3141",
            ["SmsTunnel:DefaultCountryCode"] = "40",
            ["SmsTunnel:Timeout"] = "45",
        });

        SmsTunnelClientOptions options = provider.GetRequiredService<IOptions<SmsTunnelClientOptions>>().Value;
        Assert.Equal(Clients.Key, options.ApiKey);
        Assert.Equal(new Uri("http://127.0.0.1:3141"), options.BaseUrl);
        Assert.Equal("40", options.DefaultCountryCode);
        Assert.Equal(TimeSpan.FromSeconds(45), options.Timeout);

        var client = Assert.IsType<SmsTunnelClient>(provider.GetRequiredService<ISmsTunnelClient>());
        Assert.Equal(new Uri("http://127.0.0.1:3141/"), client.BaseUrl);
    }

    [Fact]
    public async Task Sends_through_the_http_client_factory()
    {
        FakeHandler handler = FakeHandler.Answering(HttpStatusCode.OK, Clients.SendAnswer);
        using ServiceProvider provider = Build(
            new() { ["SmsTunnel:ApiKey"] = Clients.Key, ["SmsTunnel:DefaultCountryCode"] = "0040" },
            services => services.AddSmsTunnel().ConfigurePrimaryHttpMessageHandler(() => handler));

        ISmsTunnelClient sms = provider.GetRequiredService<ISmsTunnelClient>();
        SendResult result = await sms.SendSmsAsync("0721123456", "Hi");

        Assert.Equal("6aea38f9b38ed3bed4f8569f", result.MessageId);
        Assert.Equal(Clients.Key, handler.Single.Header("X-API-Key"));
        Assert.Equal("""{"to":"0721123456","message":"Hi","defaultCountryCode":"+40"}""", handler.Single.Body);
        Assert.Equal("https://smstunnel.io/api/v1/sms/send", handler.Single.Uri.ToString());
    }

    [Fact]
    public void A_missing_key_says_where_to_put_it()
    {
        using ServiceProvider provider = Build(new());

        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<ISmsTunnelClient>());

        Assert.Contains("SmsTunnel:ApiKey", error.Message, StringComparison.Ordinal);
        Assert.Contains("Apps → Add an app", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_callback_wins_over_configuration()
    {
        using ServiceProvider provider = Build(
            new() { ["SmsTunnel:ApiKey"] = "sk_live_from_config", ["SmsTunnel:DefaultCountryCode"] = "40" },
            services => services.AddSmsTunnel(o =>
            {
                o.ApiKey = Clients.Key;
                o.DefaultCountryCode = "+49";
            }));

        SmsTunnelClientOptions options = provider.GetRequiredService<IOptions<SmsTunnelClientOptions>>().Value;
        Assert.Equal(Clients.Key, options.ApiKey);
        Assert.Equal("+49", options.DefaultCountryCode);
    }

    [Fact]
    public void Works_without_configuration()
    {
        var services = new ServiceCollection();
        services.AddSmsTunnel(o => o.ApiKey = Clients.Key);
        using ServiceProvider provider = services.BuildServiceProvider();

        Assert.IsType<SmsTunnelClient>(provider.GetRequiredService<ISmsTunnelClient>());
    }

    [Fact]
    public void Reads_a_section_of_your_choice()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Texts:Sms:ApiKey"] = Clients.Key, ["Texts:Sms:Timeout"] = "00:01:30" })
            .Build();
        var services = new ServiceCollection();
        services.AddSmsTunnel(configuration.GetSection("Texts:Sms"));
        using ServiceProvider provider = services.BuildServiceProvider();

        SmsTunnelClientOptions options = provider.GetRequiredService<IOptions<SmsTunnelClientOptions>>().Value;
        Assert.Equal(Clients.Key, options.ApiKey);
        Assert.Equal(TimeSpan.FromSeconds(90), options.Timeout);
    }

    [Theory]
    [InlineData("30", 30_000)]
    [InlineData("2.5", 2_500)]
    [InlineData("00:00:10", 10_000)]
    [InlineData("infinite", -1)]
    public void Reads_the_timeout_as_seconds_or_a_time_span(string text, int milliseconds)
    {
        using ServiceProvider provider = Build(new() { ["SmsTunnel:ApiKey"] = Clients.Key, ["SmsTunnel:Timeout"] = text });

        Assert.Equal(TimeSpan.FromMilliseconds(milliseconds), provider.GetRequiredService<IOptions<SmsTunnelClientOptions>>().Value.Timeout);
    }

    [Theory]
    [InlineData("SmsTunnel:Timeout", "soon")]
    [InlineData("SmsTunnel:Timeout", "0")]
    [InlineData("SmsTunnel:BaseUrl", "smstunnel")]
    public void Refuses_settings_it_cant_read(string key, string value)
    {
        using ServiceProvider provider = Build(new() { ["SmsTunnel:ApiKey"] = Clients.Key, [key] = value });

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IOptions<SmsTunnelClientOptions>>().Value);

        Assert.Contains(key, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_http_client_never_follows_redirects_and_leaves_the_timeout_to_the_options()
    {
        using ServiceProvider provider = Build(new() { ["SmsTunnel:ApiKey"] = Clients.Key });

        HttpMessageHandler handler = provider.GetRequiredService<IHttpMessageHandlerFactory>().CreateHandler(SmsTunnelServiceCollectionExtensions.HttpClientName);
        while (handler is DelegatingHandler delegating)
        {
            handler = delegating.InnerHandler!;
        }

        Assert.False(Assert.IsType<HttpClientHandler>(handler).AllowAutoRedirect);
        Assert.Equal(Timeout.InfiniteTimeSpan, provider.GetRequiredService<IHttpClientFactory>().CreateClient(SmsTunnelServiceCollectionExtensions.HttpClientName).Timeout);
    }
}
