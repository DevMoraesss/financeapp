using FinanceMove.Shared;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace FinanceMove.Api;

/// <summary>
/// Traduz erros para o formato ProblemDetails (RFC 9457) descrito em docs/api.md secao 3.
/// </summary>
/// <remarks>
/// Regra de ouro: detalhe tecnico (stack trace, mensagem do Postgres) nunca vai no corpo da
/// resposta. O que liga a reclamacao do usuario a linha de log e o traceId.
/// </remarks>
internal sealed class DomainExceptionHandler(ILogger<DomainExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var traceId = httpContext.TraceIdentifier;

        ProblemDetails problem;

        if (exception is BadHttpRequestException badRequest)
        {
            // Falha de leitura da requisicao (query string ou corpo que nao casam com a
            // assinatura) e erro do CLIENTE, nao do servidor. Sem este ramo, o ASP.NET deixa a
            // excecao subir e ela viraria 500, escondendo do front que o pedido estava errado.
            problem = new ProblemDetails
            {
                Type = "https://financemove.app/errors/validation",
                Title = "Requisicao invalida",
                Status = StatusCodes.Status400BadRequest,
                Detail = "Um ou mais parametros da requisicao estao em formato invalido.",
                Instance = httpContext.Request.Path,
            };

            logger.LogInformation(
                "Requisicao mal formada em {Path}: {Message} (traceId {TraceId})",
                httpContext.Request.Path,
                badRequest.Message,
                traceId);
        }
        else if (exception is DomainException domain)
        {
            // Erro esperado de regra de negocio: a mensagem foi escrita para o usuario ler.
            problem = new ProblemDetails
            {
                Type = $"https://financemove.app/errors/{domain.ErrorType ?? "domain-rule"}",
                Title = domain.StatusCode == 409 ? "Conflito com o estado atual" : "Operacao nao permitida",
                Status = domain.StatusCode,
                Detail = domain.Message,
                Instance = httpContext.Request.Path,
            };

            logger.LogInformation(
                "Regra de negocio bloqueou a operacao {Path}: {Message} (traceId {TraceId})",
                httpContext.Request.Path,
                domain.Message,
                traceId);
        }
        else
        {
            problem = new ProblemDetails
            {
                Type = "https://financemove.app/errors/internal",
                Title = "Erro inesperado",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "Algo deu errado do nosso lado. Tente novamente em instantes.",
                Instance = httpContext.Request.Path,
            };

            logger.LogError(exception, "Falha nao tratada em {Path} (traceId {TraceId})", httpContext.Request.Path, traceId);
        }

        problem.Extensions["traceId"] = traceId;

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        await httpContext.Response.WriteAsJsonAsync(problem, cancellationToken);

        return true;
    }
}
