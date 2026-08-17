using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FinanceMove.Api;
using FinanceMove.Api.Endpoints;
using FinanceMove.Modules.Accounts;
using FinanceMove.Modules.Budget;
using FinanceMove.Modules.Identity;
using FinanceMove.Modules.Transactions;
using FinanceMove.Shared;
using FinanceMove.Shared.Events;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// Configuracao obrigatoria
// ---------------------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("Postgres")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:Postgres nao configurada. Rode 'docker compose up -d' e confira "
        + "appsettings.Development.json, ou defina a variavel de ambiente ConnectionStrings__Postgres.");

var signingKey = builder.Configuration["Jwt:SigningKey"];

if (string.IsNullOrWhiteSpace(signingKey) || signingKey.Length < 32)
{
    throw new InvalidOperationException(
        "Jwt:SigningKey ausente ou curta demais. Gere uma chave de pelo menos 32 caracteres e "
        + "defina em appsettings.Development.json (dev) ou na variavel Jwt__SigningKey (producao).");
}

// ---------------------------------------------------------------------------
// Modulos: cada um registra o proprio DbContext e os proprios contratos.
// O host nao conhece nada de dentro deles (docs/arquitetura.md secao 2).
// ---------------------------------------------------------------------------
builder.Services.AddIdentityModule(connectionString, builder.Configuration);
builder.Services.AddAccountsModule(connectionString);
builder.Services.AddTransactionsModule(connectionString);
builder.Services.AddBudgetModule(connectionString);

// ---------------------------------------------------------------------------
// Servicos compartilhados
// ---------------------------------------------------------------------------

// Relogio injetavel: fora de Production aceita TEST_TODAY para avancar o calendario e testar
// fatura fechando e recorrencia disparando (SPEC secao 10).
builder.Services.AddSingleton<IClock>(_ =>
{
    var configuredToday = builder.Configuration["TEST_TODAY"];

    return !builder.Environment.IsProduction() && DateOnly.TryParse(configuredToday, out var fixedToday)
        ? new SystemClock(fixedToday)
        : new SystemClock();
});

// Scoped, e nao Singleton: o barramento resolve os ouvintes pelo container, e eles dependem de
// DbContext, que vive no escopo da requisicao. Como singleton, ele receberia o provider raiz e
// estouraria ao tentar criar um servico com escopo.
builder.Services.AddScoped<IEventBus, InProcessEventBus>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpContextCurrentUser>();
builder.Services.AddMemoryCache();

builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddProblemDetails();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    // Enums viajam como texto em snake_case ("credit_card", "income"), conforme docs/api.md.
    // Numero magico no JSON obrigaria o front a manter uma tabela de traducao.
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
    options.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.Never;
});

builder.Services
    .AddAuthentication("Bearer")
    .AddJwtBearer("Bearer", options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "financemove",
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "financemove-spa",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),

            // Sem tolerancia de relogio: o padrao de 5 minutos deixaria um token expirado valer
            // por mais tempo do que a configuracao diz.
            ClockSkew = TimeSpan.Zero,
        };
    });

builder.Services.AddAuthorization();

// CORS de origem UNICA, nunca "*" (ADR-001, Decisao 4). Em desenvolvimento o Vite faz proxy,
// entao nem chega a ser exercitado; em producao e o que separa a SPA do resto da internet.
var allowedOrigin = builder.Configuration["Cors:AllowedOrigin"];

builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
{
    if (!string.IsNullOrWhiteSpace(allowedOrigin))
    {
        policy.WithOrigins(allowedOrigin)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    }
}));

builder.Services.AddHealthChecks()
    .AddNpgSql(connectionString, name: "postgres", tags: ["ready"]);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

// Depois da autenticacao: materializa recorrencias vencidas na primeira requisicao do dia.
app.UseMiddleware<RecurrenceCatchUpMiddleware>();

// Liveness: o processo esta de pe? Nao toca no banco, para o Railway nao matar o container
// quando o problema esta no Postgres.
app.MapHealthChecks("/health", new HealthCheckOptions
{
    Predicate = _ => false,
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

// Readiness: da para atender requisicao de verdade? Aqui sim o banco e verificado.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    ResponseWriter = HealthResponseWriter.WriteAsync,
});

app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapAccountEndpoints();
app.MapTransactionEndpoints();
app.MapStatementEndpoints();
app.MapBudgetEndpoints();
app.MapDashboardEndpoints();

app.Run();

/// <summary>
/// Publico e parcial para que o WebApplicationFactory dos testes de integracao consiga
/// referenciar a aplicacao.
/// </summary>
public partial class Program;
