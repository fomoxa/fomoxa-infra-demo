FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish MyGame.Backend/MyGame.Backend.csproj -c Release -o /app \
 && dotnet publish fomoxa-rpc/Health/Health.csproj -c Release -o /app/health

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app .
USER app
EXPOSE 9600 9601 9602
HEALTHCHECK --interval=10s --timeout=5s --start-period=5s CMD ["dotnet", "/app/health/fomoxa-rpc-health.dll", "127.0.0.1:9600"]
ENTRYPOINT ["dotnet", "MyGame.Backend.dll"]
