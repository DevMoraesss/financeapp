# Imagem da API do FinanceMove, usada pelo Railway (variavel RAILWAY_DOCKERFILE_PATH=Dockerfile).
# Duas etapas: o SDK compila; a imagem final so tem o runtime, sem compilador nem shell.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Primeiro so os arquivos de projeto: enquanto nenhum .csproj mudar, o restore fica em cache e o
# build seguinte so recompila o codigo.
COPY Directory.Build.props .editorconfig ./
COPY src/Api/FinanceMove.Api.csproj src/Api/
COPY src/Shared/FinanceMove.Shared.csproj src/Shared/
COPY src/Modules/Identity/Identity/FinanceMove.Modules.Identity.csproj src/Modules/Identity/Identity/
COPY src/Modules/Identity/Identity.Contracts/FinanceMove.Modules.Identity.Contracts.csproj src/Modules/Identity/Identity.Contracts/
COPY src/Modules/Accounts/Accounts/FinanceMove.Modules.Accounts.csproj src/Modules/Accounts/Accounts/
COPY src/Modules/Accounts/Accounts.Contracts/FinanceMove.Modules.Accounts.Contracts.csproj src/Modules/Accounts/Accounts.Contracts/
COPY src/Modules/Transactions/Transactions/FinanceMove.Modules.Transactions.csproj src/Modules/Transactions/Transactions/
COPY src/Modules/Transactions/Transactions.Contracts/FinanceMove.Modules.Transactions.Contracts.csproj src/Modules/Transactions/Transactions.Contracts/
COPY src/Modules/Budget/Budget/FinanceMove.Modules.Budget.csproj src/Modules/Budget/Budget/
COPY src/Modules/Budget/Budget.Contracts/FinanceMove.Modules.Budget.Contracts.csproj src/Modules/Budget/Budget.Contracts/
RUN dotnet restore src/Api/FinanceMove.Api.csproj

COPY src/ src/
RUN dotnet publish src/Api/FinanceMove.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# chiseled: sem shell, sem gerenciador de pacotes, roda como usuario sem privilegio.
# "-extra" traz o ICU e o tzdata: o app precisa do fuso America/Sao_Paulo (IClock) e da
# cultura pt-BR (exportacao em CSV).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=0 \
    TZ=America/Sao_Paulo

# GC de estacao de trabalho: um processo pequeno com poucos usuarios gasta bem menos memoria
# (e o Railway cobra por memoria).
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "FinanceMove.Api.dll"]
