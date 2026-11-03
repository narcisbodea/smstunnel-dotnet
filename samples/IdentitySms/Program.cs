// ASP.NET Core Identity with phone confirmation and two-factor sign-in codes sent through SMSTunnel.
//
//   dotnet user-secrets set "SmsTunnel:ApiKey" "<your key>" --project samples/IdentitySms
//   dotnet run --project samples/IdentitySms
//
// Then: POST /account/register, /account/login, /account/phone, /account/phone/confirm, /account/two-factor,
// /account/logout, and /account/login again: it answers {"twoFactorRequired":true} and texts a code for
// /account/login/code. The users live in memory; use your own database in an app.

using System.Security.Claims;
using System.Text.RegularExpressions;
using IdentitySms;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using SmsTunnel;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase("identity-sms-sample"));
builder.Services
    .AddIdentityCore<IdentityUser>()
    .AddSignInManager()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddDefaultTokenProviders();
builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
builder.Services.ConfigureApplicationCookie(options => options.Events.OnRedirectToLogin = context =>
{
    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
    return Task.CompletedTask;
});
builder.Services.AddAuthorization();

// SMSTunnel: reads SmsTunnel:ApiKey (user secrets, or the SmsTunnel__ApiKey environment variable).
builder.Services.AddSmsTunnel();
builder.Services.AddTransient<ISmsSender, SmsTunnelSmsSender>();

var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapPost("/account/register", async (Credentials body, UserManager<IdentityUser> users) =>
{
    var user = new IdentityUser { UserName = body.Email, Email = body.Email };
    IdentityResult result = await users.CreateAsync(user, body.Password);
    return result.Succeeded ? Results.Ok() : Results.ValidationProblem(Problems(result));
});

app.MapPost("/account/login", async (
    Credentials body, SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users, ISmsSender sms,
    ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    Microsoft.AspNetCore.Identity.SignInResult result = await signIn.PasswordSignInAsync(body.Email, body.Password, isPersistent: false, lockoutOnFailure: true);
    if (!result.RequiresTwoFactor)
    {
        return result.Succeeded ? Results.Ok(new { twoFactorRequired = false }) : Results.Unauthorized();
    }

    // The password was right: text the sign-in code to the confirmed phone number.
    IdentityUser user = (await users.FindByNameAsync(body.Email))!;
    string code = await users.GenerateTwoFactorTokenAsync(user, TokenOptions.DefaultPhoneProvider);
    return await SendCodeAsync(sms, user.PhoneNumber!, $"Your sign-in code is {code}", logger, cancellationToken)
        ?? Results.Ok(new { twoFactorRequired = true });
});

app.MapPost("/account/login/code", async (CodeSignIn body, SignInManager<IdentityUser> signIn) =>
{
    Microsoft.AspNetCore.Identity.SignInResult result = await signIn.TwoFactorSignInAsync(
        TokenOptions.DefaultPhoneProvider, body.Code, isPersistent: false, rememberClient: body.RememberThisDevice);
    return result.Succeeded ? Results.Ok() : Results.Unauthorized();
});

app.MapPost("/account/phone", async (
    PhoneNumberChange body, ClaimsPrincipal principal, UserManager<IdentityUser> users, ISmsSender sms,
    ILogger<Program> logger, CancellationToken cancellationToken) =>
{
    string? phoneNumber = InternationalNumber(body.PhoneNumber);
    if (phoneNumber is null)
    {
        return Results.BadRequest("Give the number in international format, for example +14155550123.");
    }

    IdentityUser user = (await users.GetUserAsync(principal))!;
    string code = await users.GenerateChangePhoneNumberTokenAsync(user, phoneNumber);
    return await SendCodeAsync(sms, phoneNumber, $"Your verification code is {code}", logger, cancellationToken)
        ?? Results.Accepted();
}).RequireAuthorization();

app.MapPost("/account/phone/confirm", async (
    PhoneNumberConfirmation body, ClaimsPrincipal principal, UserManager<IdentityUser> users, SignInManager<IdentityUser> signIn) =>
{
    IdentityUser user = (await users.GetUserAsync(principal))!;
    IdentityResult result = await users.ChangePhoneNumberAsync(user, InternationalNumber(body.PhoneNumber) ?? body.PhoneNumber, body.Code);
    if (!result.Succeeded)
    {
        return Results.ValidationProblem(Problems(result));
    }

    await signIn.RefreshSignInAsync(user);
    return Results.Ok();
}).RequireAuthorization();

app.MapPost("/account/two-factor", async (ClaimsPrincipal principal, UserManager<IdentityUser> users) =>
{
    IdentityUser user = (await users.GetUserAsync(principal))!;
    if (!await users.IsPhoneNumberConfirmedAsync(user))
    {
        return Results.BadRequest("Confirm a phone number first.");
    }

    await users.SetTwoFactorEnabledAsync(user, true);
    return Results.Ok();
}).RequireAuthorization();

app.MapPost("/account/logout", async (SignInManager<IdentityUser> signIn) =>
{
    await signIn.SignOutAsync();
    return Results.Ok();
});

app.Run();

// Null when the code went out, or the answer to give when it didn't.
static async Task<IResult?> SendCodeAsync(ISmsSender sms, string phoneNumber, string message, ILogger logger, CancellationToken cancellationToken)
{
    try
    {
        await sms.SendSmsAsync(phoneNumber, message, cancellationToken);
        return null;
    }
    catch (SmsTunnelRateLimitException error)
    {
        logger.LogWarning(error, "SMSTunnel: too many codes right now");
        return Results.Problem("Too many codes were sent. Try again in a minute.", statusCode: StatusCodes.Status429TooManyRequests);
    }
    catch (SmsTunnelException error)
    {
        // A wrong API key (401) or a missing permission (403) is a setup problem: log it, don't show it.
        logger.LogError(error, "SMSTunnel could not send the code");
        return Results.Problem("The code could not be sent. Try again later.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}

// "+1 415 555 0123" → "+14155550123"; null for anything that isn't an international number.
static string? InternationalNumber(string? text)
{
    string number = Regex.Replace(text ?? string.Empty, @"[\s.\-()]", string.Empty);
    return Regex.IsMatch(number, @"^\+[1-9][0-9]{6,14}$") ? number : null;
}

static Dictionary<string, string[]> Problems(IdentityResult result) =>
    result.Errors.GroupBy(e => e.Code).ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

internal sealed record Credentials(string Email, string Password);

internal sealed record PhoneNumberChange(string PhoneNumber);

internal sealed record PhoneNumberConfirmation(string PhoneNumber, string Code);

internal sealed record CodeSignIn(string Code, bool RememberThisDevice = false);

internal sealed class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<IdentityUser>(options);

public partial class Program;
