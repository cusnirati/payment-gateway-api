# built from the repository root, it needs the shared Directory.Build.props / Directory.Packages.props:
# docker build -f src/Dockerfile -t payment-gateway-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/PaymentGatewayApi.csproj src/
RUN dotnet restore src/PaymentGatewayApi.csproj

COPY src/ src/
RUN dotnet publish src/PaymentGatewayApi.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "PaymentGatewayApi.dll"]
