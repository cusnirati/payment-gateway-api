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

ENTRYPOINT ["dotnet", "PaymentGatewayApi.dll"]
