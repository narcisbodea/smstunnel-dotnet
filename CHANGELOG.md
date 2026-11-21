# Changelog

All notable changes to `SmsTunnel` and `SmsTunnel.Extensions.DependencyInjection` are documented here. The format
follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the packages use [Semantic Versioning](https://semver.org/).

## [1.1.1] - 2026-11-21

### Fixed

- `MessageStatus.IsFinal` is now `true` for `Sent`. The SMSTunnel app gets no delivery reports, so a text that went out
  stays `Sent`, and code waiting "until final" never stopped. `Sent` is the last good status today; `Delivered` is
  reserved and still counts as final. `Pending` and `Unknown` are still not final.
- The docs no longer promise a delivery confirmation.

### Compatibility

- Only `IsFinal` changes. If your code waits for `IsFinal` and then looks for `Delivered`, treat `Sent` as success too.

## [1.1.0] - 2026-11-15

### Added

- Scheduled texts. `ScheduleSmsAsync(phoneNumber, message, sendAt, options)` gives SMSTunnel a text to send later, 1
  minute to 30 days ahead (`POST /api/v1/sms/send` with `sendAt`), and returns a `ScheduledSendResult`:
  `ScheduledId`, `SendAt`, `Status`, the `ScheduledText` and `Replayed`. At that time SMSTunnel sends it as if it were
  sent then with the same key. `sendAt` is a `DateTimeOffset`, sent with its offset in whole seconds; a `DateTime`
  overload takes `Kind` `Utc` or `Local` and refuses `Unspecified` instead of guessing. The `SendOptions` are the same
  as for `SendSmsAsync`, `IdempotencyKey` included.
- `GetScheduledTextsAsync` (a `ScheduledTextsQuery` with `Scope` `Waiting` or `Done`, `Page` and `Limit`; a
  `ScheduledTextPage` with `Waiting` and the plan's `Max`), `GetScheduledTextAsync` and `CancelScheduledTextAsync`,
  for the texts the key scheduled. `ScheduledText` has the number, the text, `SendAt`, `Status`, `DeviceId`,
  `MessageId` once it went out (for `GetMessageStatusAsync`), `Error` and `CreatedAt`.
- `SmsTunnelErrorCodes`: the `SCHEDULE_*` codes. `SmsTunnelApiException.Max`: with `SCHEDULE_LIMIT_REACHED`, how many
  texts the plan lets wait at once.

### Compatibility

- The new members of `ISmsTunnelClient` have default implementations that throw `NotSupportedException`, so classes
  written against 1.0.0 still compile. `SendSmsAsync`, `SendOptions` and `SendResult` are unchanged.

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
