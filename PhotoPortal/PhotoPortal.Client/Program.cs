using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using PhotoPortal.Client.Services;

namespace PhotoPortal.Client
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            var builder = WebAssemblyHostBuilder.CreateDefault(args);

            builder.Services.AddScoped((sp) => new HttpClient()
            {
                BaseAddress = new Uri(builder.HostEnvironment.BaseAddress)
            });

            builder.Services.AddAuthorizationCore();

            builder.Services.AddTransient<AuthenticationStateProvider, AuthService>();
            builder.Services.AddTransient((sp) => sp.GetRequiredService<AuthenticationStateProvider>() as AuthService);

            await builder.Build().RunAsync();
        }
    }
}
