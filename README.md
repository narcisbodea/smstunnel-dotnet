# SMSTunnel for .NET

`SmsTunnel`: send texts from your own .NET apps through **your own Android phone**, from **your own number**, with **[SMSTunnel](https://smstunnel.io)**.

Your app hands the text to your SMSTunnel account and your phone sends it from its own SIM, so recipients see a number they know and can reply to. You can cut an app off at any time by deleting its API key in your dashboard.

Your phone needs the SMSTunnel app from Google Play:

<a href="https://play.google.com/store/apps/details?id=io.smstunnel.app"><img src="https://smstunnel.io/google-play-badge.png" alt="Get it on Google Play" height="56"></a>

Get the app: [SMSTunnel on Google Play](https://play.google.com/store/apps/details?id=io.smstunnel.app) · Docs: [smstunnel.io/docs](https://smstunnel.io/docs/) · Keys: [dashboard.smstunnel.io/apps](https://dashboard.smstunnel.io/apps)

| Package | What it is |
| --- | --- |
| [`SmsTunnel`](https://www.nuget.org/packages/SmsTunnel) | The client: texts, sign-in codes, status, phones, usage, received texts. No dependencies. |
| [`SmsTunnel.Extensions.DependencyInjection`](https://www.nuget.org/packages/SmsTunnel.Extensions.DependencyInjection) | `services.AddSmsTunnel()` for ASP.NET Core and other apps with dependency injection: settings from configuration, `IHttpClientFactory`. |

Both need .NET 8 or newer (.NET 9 and .NET 10 too). The client is trimming- and Native AOT-safe.

## Install

```bash
dotnet add package SmsTunnel
# in ASP.NET Core, worker services and other apps with dependency injection:
dotnet add package SmsTunnel.Extensions.DependencyInjection
```

## Get your key

1. Install the [SMSTunnel app from Google Play](https://play.google.com/store/apps/details?id=io.smstunnel.app) on your Android phone and sign in. Sign in at [smstunnel.io](https://smstunnel.io) on your computer with the same account: the phone appears under **Phones** (you can also link it with the QR code from **Phones → Add phone**).
2. In your [SMSTunnel dashboard](https://dashboard.smstunnel.io/apps), open **Apps**.
3. Click **Add an app** and copy its key (`sk_live_…`). It can send texts and look up whether they went out (`sms:send` and `sms:read`). Reading received texts is a separate opt-in for each key: **Can also read received texts** (`sms:inbox`).

Keep the key out of your code: in user secrets, an environment variable or your secret store.

## Quick start

```csharp
using SmsTunnel;

using var sms = new SmsTunnelClient(Environment.GetEnvironmentVariable("SMSTUNNEL_API_KEY")!);

SendResult result = await sms.SendSmsAsync("+14155550123", "Your order #1042 has shipped.");

Console.WriteLine(result.MessageId); // keep it to look up the status later
```

Make one client and reuse it: it is thread-safe, and every client you create without your own `HttpClient` shares one connection pool.

## Usage

### Send a text

```csharp
SendResult result = await sms.SendSmsAsync(
    "+14155550123",                    // international format; one number per call
    "See you at 10:30.",               // up to 1600 characters
    new SendOptions
    {
        DeviceId = "6aea3248fedd7cccac9abf42", // the phone that sends it (Phones → Copy); leave it out to let SMSTunnel pick one of yours
        DefaultCountryCode = "+40",            // completes numbers written without + or 00, like 0721 123 456
        IdempotencyKey = "appointment-981-reminder", // makes a retry safe, see below
    },
    cancellationToken);

result.MessageId; // string
result.Queued;    // true when no phone of yours was online: the text waits and goes out when one is back
result.Branded;   // true when the Free plan added its short line to the text
result.Replayed;  // true when this is the stored answer to an earlier call with the same IdempotencyKey
```

One call sends one text to one number. To text several people, call `SendSmsAsync` once for each.

### Send a sign-in or verification code

```csharp
await sms.SendSignInCodeAsync("+14155550123", "Your sign-in code is 482913");
```

The text must contain the code as a number of its own, 4 to 10 digits (`482913` or `123-456`), and be at most 320 characters; otherwise SMSTunnel refuses it (400). Codes have their own daily allowance. Use it for codes only.

### Look up a status

```csharp
MessageStatus status = await sms.GetMessageStatusAsync(result.MessageId); // needs sms:read

status.State;        // Pending, Sent, Delivered, Failed or Unconfirmed
status.IsFinal;      // true once it won't change: Delivered, Failed or Unconfirmed
status.SentAt;       // DateTimeOffset?
status.ErrorMessage; // why it failed
```

`Unconfirmed` means the phone handed the text to the SIM but never got the confirmation: it most likely went out, and it is never resent automatically. Don't resend it yourself.

### Your phones and your usage

```csharp
foreach (Phone phone in await sms.GetPhonesAsync())
{
    Console.WriteLine($"{phone.Name}: {(phone.IsOnline ? "online" : "offline")} ({phone.Id})");
}

Usage usage = await sms.GetUsageAsync();
usage.Plan;                // "free", "starter", "standard", "pro" or "enterprise"
usage.Used.SmsClean;       // texts sent today
usage.Remaining.SmsClean;  // texts left today; null means no limit
usage.Remaining.TwoFaClean; // sign-in codes left today
```

### Read received texts

Needs a key you allowed to read received texts (**Can also read received texts**) and syncing turned on in the app. When it is on, only texts that arrive from then on are synced.

```csharp
ReceivedTextPage page = await sms.GetReceivedTextsAsync(new ReceivedTextsQuery
{
    Since = DateTimeOffset.UtcNow.AddDays(-1),
    Phone = "+14155550123", // only from this sender
    Limit = 20,             // 1 to 100
    Page = 1,
});

foreach (ReceivedText text in page.Texts) // newest first
{
    Console.WriteLine($"{text.ReceivedAt:t} {text.Sender}: {text.Message}");
}

page.HasMore; // ask for the next page
```

You can also have SMSTunnel post each received text to your own address: in the dashboard, **Apps** → the key → **Forward received texts**.

Results are records. `SendResult`, `MessageStatus`, `Usage`, `Phone` and `ReceivedText` also have a `Raw` property with SMSTunnel's answer as a `JsonElement`, for fields this version doesn't map yet.

## ASP.NET Core

Register the client once; it is read from the `SmsTunnel` configuration section:

```csharp
builder.Services.AddSmsTunnel();
```

```jsonc
// appsettings.json: everything except the key, which stays out of the repository
{
  "SmsTunnel": {
    "DefaultCountryCode": "+40", // optional
    "Timeout": "30"              // optional: seconds, or a time span such as 00:00:30
  }
}
```

```bash
dotnet user-secrets set "SmsTunnel:ApiKey" "<your key>"   # while developing
export SmsTunnel__ApiKey="<your key>"                      # on a server, or your host's secret store
```

Then inject `ISmsTunnelClient` wherever a text should go out:

```csharp
app.MapPost("/orders/{id:int}/shipped", async (int id, ShopDb db, ISmsTunnelClient sms, ILogger<Program> log, CancellationToken ct) =>
{
    Order order = await db.Orders.FindAsync([id], ct) ?? throw new BadHttpRequestException("No such order.");
    order.Status = OrderStatus.Shipped;
    await db.SaveChangesAsync(ct);

    try
    {
        await sms.SendSmsAsync(order.Phone, $"Your order #{order.Id} has shipped.",
            new SendOptions { IdempotencyKey = $"order-{order.Id}-shipped" }, ct);
    }
    catch (SmsTunnelException error)
    {
        log.LogWarning(error, "The shipping text for order {OrderId} did not go out", order.Id);
    }

    return Results.NoContent();
});
```

`AddSmsTunnel` also takes a callback (`services.AddSmsTunnel(o => o.DefaultCountryCode = "+49")`) or another section (`services.AddSmsTunnel(builder.Configuration.GetSection("Sms"))`). It returns the `IHttpClientBuilder` of the client's `HttpClient`, so you can add your own handlers. That `HttpClient` doesn't follow redirects, so the key is never sent to another address, and its only timeout is `SmsTunnel:Timeout`.

The same works in a worker service or a console app with the generic host. To text from a background job, inject `ISmsTunnelClient` into it, or create a scope from `IServiceScopeFactory` in a singleton.

## ASP.NET Core Identity: phone confirmation and two-factor codes

ASP.NET Core Identity makes the phone codes and leaves the sending to your app. Give it an SMS sender that uses SMSTunnel:

```csharp
public interface ISmsSender
{
    Task SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default);
}

public sealed class SmsTunnelSmsSender(ISmsTunnelClient smsTunnel) : ISmsSender
{
    // Identity's phone codes are 6 digits on their own: they go out as sign-in codes, with their own allowance.
    public Task SendSmsAsync(string phoneNumber, string message, CancellationToken cancellationToken = default) =>
        smsTunnel.SendSignInCodeAsync(phoneNumber, message, cancellationToken: cancellationToken);
}
```

```csharp
builder.Services.AddSmsTunnel();
builder.Services.AddTransient<ISmsSender, SmsTunnelSmsSender>();
```

Confirm a phone number:

```csharp
string code = await userManager.GenerateChangePhoneNumberTokenAsync(user, phoneNumber);
await smsSender.SendSmsAsync(phoneNumber, $"Your verification code is {code}");
// later, with the code the user typed:
IdentityResult result = await userManager.ChangePhoneNumberAsync(user, phoneNumber, typedCode);
```

Sign in with a texted code, once the phone is confirmed and two-factor sign-in is on (`userManager.SetTwoFactorEnabledAsync(user, true)`):

```csharp
SignInResult result = await signInManager.PasswordSignInAsync(email, password, isPersistent: false, lockoutOnFailure: true);
if (result.RequiresTwoFactor)
{
    IdentityUser user = (await userManager.FindByNameAsync(email))!;
    string code = await userManager.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider);
    await smsSender.SendSmsAsync(user.PhoneNumber!, $"Your sign-in code is {code}");
}
// next request, with the code the user typed:
SignInResult signedIn = await signInManager.TwoFactorSignInAsync(TokenOptions.DefaultPhoneProvider, typedCode, isPersistent: false, rememberClient: false);
```

The [IdentitySms sample](https://github.com/narcisbodea/smstunnel-dotnet/tree/main/samples/IdentitySms) is a small ASP.NET Core app with these endpoints: register, sign in, add and confirm a phone number, turn on two-factor sign-in, and sign in with the texted code.

## Errors

Every call throws an `SmsTunnelException` when it doesn't go through:

| Exception | When |
| --- | --- |
| `SmsTunnelAuthenticationException` (401) | The API key is wrong, was deleted or turned off, or its account is suspended. |
| `SmsTunnelPermissionException` (403) | The key lacks a permission (`ErrorCode` is `PERMISSION_REQUIRED` and `Permission` names it, e.g. `sms:inbox`), or the plan doesn't include the feature. |
| `SmsTunnelRateLimitException` (429) | Too many requests: `RetryAfter` says how long to wait. Without `RetryAfter`, the plan's limit for the day or month is reached and the message says when texts can go out again. |
| `SmsTunnelServerException` (5xx) | A problem on SMSTunnel's side. |
| `SmsTunnelApiException` | The base of the four above, and every other error answer: 400 (a number it can't read, a code text without a code), 404 (no such message), 409/422 (an `IdempotencyKey` still in progress, or reused for another text). |
| `SmsTunnelConnectionException` | No answer: DNS, TLS, a refused or broken connection. `SmsTunnelTimeoutException` when none came within `Timeout`. |

Each `SmsTunnelApiException` has the HTTP `StatusCode`, SMSTunnel's `ErrorCode` (see `SmsTunnelErrorCodes`), `Permission`, `RetryAfter`, `ResponseBody`, and SMSTunnel's own explanation as `Message`. Bad arguments (an empty number, two numbers in one, a country code that isn't one) throw `ArgumentException` before anything is sent; a cancelled `CancellationToken` throws `OperationCanceledException`.

```csharp
try
{
    await sms.SendSmsAsync(phoneNumber, text, new SendOptions { IdempotencyKey = textId }, ct);
}
catch (SmsTunnelRateLimitException error) when (error.RetryAfter is TimeSpan wait)
{
    await Task.Delay(wait, ct); // then try again, with the same IdempotencyKey
}
catch (SmsTunnelPermissionException error) when (error.ErrorCode == SmsTunnelErrorCodes.PermissionRequired)
{
    log.LogError("The SMSTunnel key lacks {Permission}", error.Permission);
}
catch (SmsTunnelException error)
{
    log.LogWarning(error, "The text did not go out");
}
```

A phone that is offline is not an error: the text waits on your account (`Queued` is `true`) and goes out when the phone is back, within 3 days (otherwise it is marked failed).

### Retrying a send safely

The client never retries on its own. After a timeout, a connection error or a 5xx answer, the text may already be on its way, so retry a send only with an `IdempotencyKey`: a second call with the same key within 24 hours gets the first answer back (`Replayed` is `true`) instead of sending a second text. Use a key that names the text (`$"order-{id}-shipped"`), or a GUID you store with it. If you add a retry handler to the `HttpClient` (Polly, `Microsoft.Extensions.Http.Resilience`), give every send an `IdempotencyKey`.

## Your own HttpClient, and tests

`new SmsTunnelClient(httpClient, options)` sends through your `HttpClient` (it doesn't dispose it). Build its handler with `AllowAutoRedirect = false`, so the key is never sent to another address. `SmsTunnelClientOptions.BaseUrl` points the client at a test server; the default is `https://smstunnel.io`.

In your own tests, mock `ISmsTunnelClient`, or give the client an `HttpClient` with a fake `HttpMessageHandler`. Results are records with `init` properties, so tests can build them: `new SendResult { MessageId = "test-1" }`.

## Notes and limits

- **Rate limit**: 30 sends a minute per key; the keys of one account together get three times that. Over it, `SmsTunnelRateLimitException` with `RetryAfter`.
- **Plans**: the Free plan has a daily and a monthly allowance; see [smstunnel.io/pricing](https://smstunnel.io/pricing/).
- **Length**: up to 1600 characters (10 joined SMS); sign-in codes up to 320.
- **Numbers**: use the international format (`+14155550123`). For local numbers, set `DefaultCountryCode` (`+49`, `49` and `0049` all work): the text's own wins over the client's, and either wins over the phone's country.
- **Offline phone**: the text waits (`Queued`) and goes out when the phone is back online.

## Build it with an AI assistant

Paste this into Claude, ChatGPT, Cursor or Copilot, and replace the part in brackets. Never paste your real API key into a prompt.

```text
I want my .NET app to send texts with SMSTunnel. SMSTunnel sends each text from my own Android phone, from my own number: the phone runs the SMSTunnel app and is linked to my SMSTunnel account.

Use the NuGet package SmsTunnel (dotnet add package SmsTunnel). In ASP.NET Core also add SmsTunnel.Extensions.DependencyInjection, call builder.Services.AddSmsTunnel() (it reads SmsTunnel:ApiKey from configuration) and inject ISmsTunnelClient. Without dependency injection: new SmsTunnelClient(apiKey).
API facts:
- SendSmsAsync(phoneNumber, message, new SendOptions { DeviceId, DefaultCountryCode, IdempotencyKey }, cancellationToken) calls POST https://smstunnel.io/api/v1/sms/send with the header X-API-Key and the JSON body {"to", "message", "deviceId"?, "defaultCountryCode"?}. It returns MessageId and Queued.
- One recipient per call. To text several people, call it once per number.
- SendSignInCodeAsync (POST /api/v1/sms/send-2fa) is only for sign-in codes: the text must contain a code of 4 to 10 digits standing on its own (e.g. 482913 or 123-456), at most 320 characters; it uses a separate allowance.
- Numbers in international format (+14155550123). DefaultCountryCode (e.g. "+40") completes numbers written without + or 00.
- Limits: 30 sends a minute per key, 1600 characters a text.
- Errors: SmsTunnelAuthenticationException (401, bad key), SmsTunnelPermissionException (403, missing permission or plan; ErrorCode such as PERMISSION_REQUIRED), SmsTunnelRateLimitException (429, RetryAfter), SmsTunnelServerException (5xx), SmsTunnelConnectionException / SmsTunnelTimeoutException (no answer). All derive from SmsTunnelException.
- The client never retries. Retry a send only with the same IdempotencyKey: a repeat within 24 h returns the first answer instead of sending twice.
- If the phone is offline, the text waits on the account (Queued is true) and goes out when the phone is back.

What I want: [describe what should send a text, to whom, and when]

Rules:
- Keep the API key in user secrets, an environment variable (SmsTunnel__ApiKey) or the platform's secret store. Never put it in source code, in the repository or in client-side code (Blazor WebAssembly, JavaScript).
- Validate phone numbers before sending, and handle every error above: log it clearly, don't retry 401/403, wait RetryAfter on 429.
- Only send texts people expect (alerts to myself, reminders, sign-in codes, order updates).
```

## Responsible use

Send only texts that people expect from you: alerts to yourself, appointment reminders, sign-in codes, order updates. Unsolicited messages from a personal SIM break most carriers' terms of service. Please make sure your usage complies with your carrier's terms and with local regulations (in the EU, that includes GDPR and ePrivacy rules on electronic communications).

## Development

```bash
dotnet build SmsTunnel.sln
dotnet test tests/SmsTunnel.Tests            # unit tests, HTTP mocked
```

The end-to-end tests (`tests/SmsTunnel.EndToEndTests`) send real texts, so they run only against a test server, never smstunnel.io; without one they are skipped. Point them at it with a JSON file `{"serverUrl": "http://127.0.0.1:3141", "apiKey": "...", "deviceId": "..."}`, or with `SMSTUNNEL_E2E_URL` and `SMSTUNNEL_E2E_API_KEY`:

```bash
SMSTUNNEL_E2E_KEY_FILE=/path/to/test-key.json dotnet test tests/SmsTunnel.EndToEndTests -f net8.0
```

## Links

- Documentation: <https://smstunnel.io/docs/> · API reference: <https://smstunnel.io/api-reference/>
- Dashboard (phones, keys): <https://dashboard.smstunnel.io/apps>
- Changelog: [CHANGELOG.md](https://github.com/narcisbodea/smstunnel-dotnet/blob/main/CHANGELOG.md)

SMSTunnel is not affiliated with, endorsed by, or sponsored by Microsoft. .NET, ASP.NET Core and NuGet are trademarks of Microsoft Corporation; Android and Google Play are trademarks of Google LLC.

## License

MIT, © NARBOWEB SRL. See [LICENSE](https://github.com/narcisbodea/smstunnel-dotnet/blob/main/LICENSE).
