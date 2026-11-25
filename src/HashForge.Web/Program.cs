using HashForge.Bitcoin;
using HashForge.Mining;
using HashForge.Persistence;
using HashForge.Stratum;
using HashForge.Web;
using HashForge.Web.Admin;
using HashForge.Web.Api;
using HashForge.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Order matters for startup: storage is migrated first, then the mining engine restores its state
// and registers pools, and only then does the Stratum server start accepting miners.
builder.Services
    .AddHashForgeBitcoin()
    .AddHashForgePersistence()
    .AddHashForgeMining()
    .AddHashForgeStratum()
    .AddHashForgeWeb();

builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();

app.UseMiddleware<PathBaseHeaderMiddleware>();
app.UseForwardedHeaders();
app.UseExceptionHandler("/Error", createScopeForErrors: true);
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseMiddleware<AdminAccessMiddleware>();
app.UseMiddleware<AdminPasswordMiddleware>();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapGet("/healthz", () => Results.Ok("healthy"));
app.MapHashForgeApi();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

await app.RunAsync();
