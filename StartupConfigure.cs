using AccountsData.Models.DataModels;
using Amazon.S3;
using CanonicalEmails;
using Microsoft.Net.Http.Headers;
using SameSiteMode = Microsoft.AspNetCore.Http.SameSiteMode;

namespace vlo_main;

public partial class Startup {
    /// This method configures the app
    public void Configure(IApplicationBuilder app, IWebHostEnvironment env, AmazonS3Client minioClient, MinioConfig minioConfig)
    {
        app.UseForwardedHeaders();
        
        if (env.IsDevelopment())
        {
            app.UseCookiePolicy(new CookiePolicyOptions
            {
                MinimumSameSitePolicy = SameSiteMode.None
            });
        }
        else
        {
            app.UseCookiePolicy(new CookiePolicyOptions
            {
                MinimumSameSitePolicy = SameSiteMode.Strict
            });
        }
        
        EnsureBucketsExits(minioClient, minioConfig).Wait();

        if (env.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
            
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("v1/swagger.json", "VLO Boards API v1");
            });
            app.UseReDoc(c =>
            {
                c.DocumentTitle = "Dokumentacja API Vlo Boards, dostępna również pod /swagger/";
            });
        }
        else
        {
            app.UseExceptionHandler("/Error");
            // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
            app.UseHsts();
        }

        Normalizer.ConfigureDefaults(new NormalizerSettings
        {
            RemoveDots = true,
            RemoveTags = true,
            LowerCase = true,
            NormalizeHost = true
        });
        
        app.UseRouting();
        app.UseCors("DefaultExternalOrigins");
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseStaticFiles(new StaticFileOptions
        {
            OnPrepareResponse = ctx =>
            {
                    
                if (ctx.Context.Request.Path.StartsWithSegments("/static"))
                {
                    var headers = ctx.Context.Response.GetTypedHeaders();
                    headers.CacheControl = new CacheControlHeaderValue
                    {
                        Public = true,
                        MaxAge = TimeSpan.FromDays(365)
                    };
                }
                else
                {
                    var headers = ctx.Context.Response.GetTypedHeaders();
                    headers.CacheControl = new CacheControlHeaderValue
                    {
                        Public = true,
                        MaxAge = TimeSpan.FromDays(0),
                        NoCache = true,
                        NoStore = true
                    };
                }
            }
        });
        
        app.UseRateLimiter();
        
        app.UseEndpoints(endpoints =>
        {
            endpoints.MapControllerRoute(
                name: "areas",
                pattern: "/api/{area:exists}/{controller}/").RequireRateLimiting(slidingPolicy).RequireCors("DefaultExternalOrigins");
            endpoints.MapControllerRoute(
                name: "default",
                pattern: "/api/{controller}/{action=Index}/{id?}").RequireRateLimiting(slidingPolicy).RequireCors("DefaultExternalOrigins");
            if (!env.IsDevelopment())
            {
                endpoints.MapFallbackToFile("index.html");
            }
        });
        app.UseSentryTracing();
    }
    
    public static async Task EnsureBucketsExits(AmazonS3Client client, MinioConfig minioConfig)
    {
        var buckets = await client.ListBucketsAsync();
        var bucketNames = new List<string>();

        foreach (var bucket in buckets.Buckets)
        {
            bucketNames.Add(bucket.BucketName);
        }

        if (!bucketNames.Contains(minioConfig.BucketName))
        {
            await client.PutBucketAsync(minioConfig.BucketName);
        }
        
        if (!bucketNames.Contains(minioConfig.VideoBucketName))
        {
            await client.PutBucketAsync(minioConfig.VideoBucketName);
        }
    }
}