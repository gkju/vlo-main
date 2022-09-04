using System.Reflection;
using AccountsData.Data;
using AccountsData.Models.DataModels;
using Amazon.S3;
using IdentityModel.AspNetCore.AccessTokenValidation;
using Meilisearch;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.UI.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using VLO_BOARDS;
using VLO_BOARDS.Auth;

namespace vlo_main;

public partial class Startup
    {
        public Startup(IConfiguration configuration, IWebHostEnvironment environment)
        {
            Configuration = configuration;
            env = environment;
        }

        public IConfiguration Configuration { get; }
        public IWebHostEnvironment env { get; }

        /// This method gets called by the runtime. Use this method to add services to the container.
        public void ConfigureServices(IServiceCollection services)
        {
            // all configuration options except for baseorigin (config)
            string
                authAuthority = "",
                authAudience = "",
                authSecret = "",
                appDbContextNpgsqlConnection = "",
                migrationsAssembly = "",
                captchaKey = "",
                captchaPk = "",
                mailKey = "",
                mailUrl = "",
                mailDomain = "",
                minioEndpoint = "",
                minioSecret = "",
                minioAccessToken = "",
                bucketName = "boards",
                videoBucketName = "video",
                clamHost = "",
                clamPort = "",
                meiliHost = "",
                meiliMasterKey = "";
            List<string> corsorigins = new ();

            // get configuration options (!add docker secrets for prod)
            {
                authAuthority = Configuration["Auth:Authority"];
                authAudience = Configuration["Auth:Audience"];
                authSecret = Configuration["Auth:Secret"];
                appDbContextNpgsqlConnection = Configuration.GetConnectionString("NPGSQL");
                migrationsAssembly = typeof(Startup).GetTypeInfo().Assembly.GetName().Name;
                captchaPk = Configuration["CaptchaCredentials:PrivateKey"];
                captchaKey = Configuration["CaptchaCredentials:PublicKey"];
                
                corsorigins = Configuration.GetSection("cors:origins").Get<List<string>>();
                
                minioEndpoint = Configuration["minio:endpoint"];
                minioSecret = Configuration["minio:secret"];
                minioAccessToken =  Configuration["minio:access"];
                
                clamHost = Configuration["Clam:Host"];
                clamPort = Configuration["Clam:Port"];
                
                meiliHost = Configuration["Meili:Host"];
                meiliMasterKey = Configuration["Meili:MasterKey"];
                
                // TODO: remove temporary credentials from mailgun before publishing source code
                mailKey = Configuration["Mailgun:ApiKey"];
                mailDomain = Configuration["Mailgun:MailDomain"];
            }

            if (env.IsDevelopment())
            {
                services.AddDatabaseDeveloperPageExceptionFilter();
            }
            
            services.AddTransient(o => new MinioConfig {BucketName = bucketName, VideoBucketName = videoBucketName});
            services.AddTransient(o => new ClamConfig {Host = clamHost, Port = Int32.Parse(clamPort)});
            
            var s3config = new AmazonS3Config()
            {
                AuthenticationRegion = MinioConfig.AuthenticationRegion,
                ServiceURL = minioEndpoint,
                ForcePathStyle = true
            };
            
            services.AddTransient((o) => new AmazonS3Client(minioAccessToken, minioSecret, s3config));

            services.AddScoped(x => new MeilisearchClient(meiliHost, meiliMasterKey));
            services.AddScoped(x => new MailgunConfig {ApiKey = mailKey, DomainName = mailDomain});
            services.AddScoped<IEmailSender, EmailSender>();
            services.AddHttpClient();
            services.AddScoped<EmailTemplates>();
            
            
            services.AddTransient(x => new CaptchaCredentials(captchaPk, captchaKey));
            services.AddTransient<Captcha>();
            
            services.AddScoped<FileInterceptor>();
            
            services.AddDbContext<ApplicationDbContext>((sp, options) =>
            {
                options.AddInterceptors(sp.GetRequiredService<FileInterceptor>());
                options.UseNpgsql(appDbContextNpgsqlConnection, sql => sql.MigrationsAssembly(migrationsAssembly));
            });
            
            services.AddScoped<IPasswordHasher<ApplicationUser>, Argon2IDHasher<ApplicationUser>>();
            
            services.AddSwaggerGen(c =>
            {
                c.SwaggerDoc("v1", new OpenApiInfo {Title = "ASP.NETCore_suvlo", Version = "v1", Contact = new OpenApiContact()
                {
                    Name = "GJusz",
                    Email = "gkjuszczyk@gmail.com"
                }});
                
                c.CustomSchemaIds(type => type.ToString());
                c.CustomSchemaIds(type => type.FullName.Replace("+", "_"));
                c.EnableAnnotations();

                var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
                var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
                c.IncludeXmlComments(xmlPath);
            });

            AspNetIdentityStartup.AddAspNetIdentity(services);

            services.AddControllersWithViews();
            
            services.AddAuthentication(o =>
                {
                    o.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                    o.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                })
                .AddJwtBearer(options =>
                {
                    options.Authority = authAuthority;
                    options.Audience = authAudience;

                    options.TokenValidationParameters.ValidTypes = new[] { "at+jwt" };

                    //reference token
                    options.ForwardDefaultSelector = Selector.ForwardReferenceToken("introspection");
                })
                .AddOAuth2Introspection("introspection", options =>
                {
                    options.Authority = authAuthority;

                    options.ClientId =  authAudience;
                    options.ClientSecret = authSecret;
                });

            services.AddCors(options =>
            {
                options.AddPolicy(name: "DefaultExternalOrigins",
                    builder =>
                    {
                        builder.WithOrigins(corsorigins.ToArray())
                            .AllowAnyHeader()
                            .AllowAnyMethod()
                            .AllowCredentials();
                    });
            });
        }
    }