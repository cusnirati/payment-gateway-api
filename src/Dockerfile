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

# run as the image's built-in non-root user; app files stay root-owned, only the log dir is writable
RUN mkdir -p /app/z-logs && chown $APP_UID /app/z-logs
USER $APP_UID

ENTRYPOINT ["dotnet", "PaymentGatewayApi.dll"]
