FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY PaymentGatewayApi.csproj .
RUN dotnet restore PaymentGatewayApi.csproj

COPY . .
RUN dotnet publish PaymentGatewayApi.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# curl for the compose healthcheck, the aspnet runtime image ships without it
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENTRYPOINT ["dotnet", "PaymentGatewayApi.dll"]
