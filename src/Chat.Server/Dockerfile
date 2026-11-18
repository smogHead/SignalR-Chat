# Build context is the repo root (see docker-compose.yml):
#   docker build -f src/Chat.Server/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Chat.Shared/Chat.Shared.csproj src/Chat.Shared/
COPY src/Chat.Server/Chat.Server.csproj src/Chat.Server/
RUN dotnet restore src/Chat.Server/Chat.Server.csproj
COPY src/Chat.Shared/ src/Chat.Shared/
COPY src/Chat.Server/ src/Chat.Server/
RUN dotnet publish src/Chat.Server/Chat.Server.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Chat.Server.dll"]
