using Microsoft.AspNetCore.Components.Authorization;
using BlazorWebAppOidc;
using BlazorWebAppOidc.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddAuthentication("MicrosoftOidc")
    // For Microsoft Entra and Azure AD B2C, you can use AddMicrosoftIdentityWebApp from Microsoft.Identity.Web
    // which will add both the OIDC and Cookie authentication handlers with the appropriate defaults.
    // This is here to demonstrate how to configure the OIDC handler manually.
    .AddOpenIdConnect("MicrosoftOidc", oidcOptions =>
    {
        // The OIDC handler needs to use a SignInScheme that is capable of persisting user credentials across requests.
        // The following line is just for demonstration purposes. The default SignInScheme is already "Cookies".
        oidcOptions.SignInScheme = "Cookies";
        // The "openid" and "profile" scopes are also configured by default because they are required for the OIDC handler to work,
        // but these may need to be readded if scopes are included in the "Authentication:Schemes:MicrosoftOidc:Scope" configuration.
        oidcOptions.Scope.Add("openid");
        oidcOptions.Scope.Add("profile");

        // All the remaining options are being set to non-default values.
        // The offline_access scope is required for the refresh token.
        oidcOptions.SaveTokens = false;
        oidcOptions.Scope.Add("offline_access");
        // The "Weather.Get" scope is configured in the Azure or Entra portal under "Expose an API".
        // This is necessary for MinimalApiJwt to be able to validate the access token with AddBearerJwt.
        oidcOptions.Scope.Add("https://{directory-name}.onmicrosoft.com/{client-id}/Weather.Get");

        // The "common" authority should be used for multi-tenant applications. You can also use the common
        // authority for single-tenant applications, but that requires a custom IssuerValidator as shown in the comments below.
        //oidcOptions.Authority = "https://login.microsoftonline.com/common/v2.0/";
        oidcOptions.Authority = "https://login.microsoftonline.com/{tenant-id}/v2.0/";
        oidcOptions.ClientId = "{client-id}";

        // ClientSecret should not be compiled into the application assembly or checked into source control.
        // Instead consider user-secrets, Azure KeyVault and/or environment variables. Authentication scheme configuration
        // is automatically read from builder.Configuration["Authentication:Schemes:{SchemeName}:{PropertyName}"],
        // so ClientSecret will be read from "Authentication:Schemes:MicrosoftOidc:ClientSecret" configuration.
        //oidcOptions.ClientSecret = "xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";

        // This configures the OIDC handler to do authorization code flow only. Implicit grants and hybrid flows are unnecessary
        // in this mode. You do not need to check either box for the authorization endpoint to return access tokens or ID tokens.
        // The OIDC handler will automatically request the appropriate tokens using the code returned from the authorization endpoint.
        oidcOptions.ResponseType = "code";

        // Many OIDC servers use "name" and "role" rather than the SOAP/WS-Fed defaults in ClaimTypes.
        // If you do not use ClaimTypes, mapping inbound claims to ASP.NET Core's ClaimTypes isn't necessary.
        oidcOptions.MapInboundClaims = false;
        oidcOptions.TokenValidationParameters.NameClaimType = "name";
        oidcOptions.TokenValidationParameters.RoleClaimType = "role";

        // These paths must match the redirect and post logout redirect paths configured when registering the application
        // with the OIDC provider. On Azure, this is done through the "Authentication" blade of the application. Both the
        // signin and signout paths must be registered as redirect URIs. The default values are "/signin-oidc" and
        // "/signout-callback-oidc" which could be used instead.
        oidcOptions.CallbackPath = "/signin-microsoft";
        // Microsoft Identity currently only redirects back to the SignedOutCallbackPath if authority is
        // https://login.microsoftonline.com/{tenant-id}/v2.0/ as it is above. You can use the "common"
        // authority instead, and logout will redirect back to the blazor app.
        // See https://github.com/AzureAD/microsoft-authentication-library-for-js/issues/5783
        oidcOptions.SignedOutCallbackPath = "/signout-callback-microsoft";
        // This is the "Front-channel logout URL" for remote single sign-out. The default value is "/signout-oidc".
        oidcOptions.RemoteSignOutPath = "/signout-remote-microsoft";

        // Many OIDC provider work with the default issuer validator, but we need to account for the issuer parameterized
        // with "{tenant-id}" returned by https://login.microsoftonline.com/common/v2.0/.well-known/openid-configuration
        // See https://github.com/AzureAD/azure-activedirectory-identitymodel-extensions-for-dotnet/issues/1731
        //var microsoftIssuerValidator = AadIssuerValidator.GetAadIssuerValidator(oidcOptions.Authority);
        //oidcOptions.TokenValidationParameters.IssuerValidator = microsoftIssuerValidator.Validate;
    })
    .AddCookie("Cookies");

// This attaches a cookie OnValidatePrincipal callback to get a new access token when the current one expires, and
// reissue a cookie with the new access token saved inside. If the refresh fails, the user will be signed out.
builder.Services.ConfigureCookieOidcRefresh("Cookies", "MicrosoftOidc");

builder.Services.AddAuthorization();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddScoped<AuthenticationStateProvider, PersistingAuthenticationStateProvider>();

builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseAntiforgery();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
}).RequireAuthorization();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(BlazorWebAppOidc.Client._Imports).Assembly);

app.MapGroup("/authentication").MapLoginAndLogout();

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
