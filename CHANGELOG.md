# Changelog

All notable changes to `SmsTunnel` and `SmsTunnel.Extensions.DependencyInjection` are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the packages use [Semantic Versioning](https://semver.org/).

## [1.0.0] - 2026-11-03

### Added

- `SmsTunnel`: `SmsTunnelClient` (and `ISmsTunnelClient`) with `SendSmsAsync`, `SendSignInCodeAsync`,
  `GetMessageStatusAsync`, `GetPhonesAsync`, `GetUsageAsync` and `GetReceivedTextsAsync`; a `CancellationToken` on
  every call; `SendOptions` with `DeviceId`, `DefaultCountryCode` and `IdempotencyKey` (`SendResult.Replayed` tells a
  stored answer); a client-wide `DefaultCountryCode` and `Timeout`.
- Exceptions: `SmsTunnelApiException` with the HTTP status, SMSTunnel's `ErrorCode`, `Permission`, `RetryAfter` and
  body, and its subclasses `SmsTunnelAuthenticationException` (401), `SmsTunnelPermissionException` (403),
  `SmsTunnelRateLimitException` (429) and `SmsTunnelServerException` (5xx); `SmsTunnelConnectionException` and
  `SmsTunnelTimeoutException` when no answer comes back. All derive from `SmsTunnelException`.
- Country codes and idempotency keys are checked with the server's rules before anything is sent; the client never
  follows redirects with its own connection pool and never retries by itself. No dependencies; trimming and AOT safe.
- `SmsTunnel.Extensions.DependencyInjection`: `services.AddSmsTunnel()`, with the settings read from the `SmsTunnel`
  configuration section (`ApiKey`, `DefaultCountryCode`, `Timeout`, `BaseUrl`), on `IHttpClientFactory`.
- A sample ASP.NET Core Identity app that texts phone confirmation and two-factor sign-in codes.
