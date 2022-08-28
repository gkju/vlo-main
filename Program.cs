using Mcrio.Configuration.Provider.Docker.Secrets;
using vlo_main;

public class Program
{
    public static void Main(string[] args)
    {
        CreateHostBuilder(args).Build().Run();
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureWebHostDefaults(webBuilder =>
            {
                webBuilder.UseStartup<Startup>();
                webBuilder.ConfigureAppConfiguration(c =>
                {
                    c.AddDockerSecrets();
                    c.AddJsonFile("appsettings.Secret.json");
                });
                webBuilder.UseSentry();
            });
}