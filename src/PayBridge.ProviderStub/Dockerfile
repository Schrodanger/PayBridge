# syntax=docker/dockerfile:1.7

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PayBridge.Shared/PayBridge.Shared.csproj                 src/PayBridge.Shared/
COPY src/PayBridge.Contracts/PayBridge.Contracts.csproj           src/PayBridge.Contracts/
COPY src/PayBridge.PaymentApi/PayBridge.PaymentApi.csproj         src/PayBridge.PaymentApi/
COPY src/PayBridge.FraudService/PayBridge.FraudService.csproj     src/PayBridge.FraudService/
COPY src/PayBridge.ProviderStub/PayBridge.ProviderStub.csproj     src/PayBridge.ProviderStub/
COPY src/PayBridge.SettlementConsumer/PayBridge.SettlementConsumer.csproj src/PayBridge.SettlementConsumer/

RUN dotnet restore src/PayBridge.ProviderStub/PayBridge.ProviderStub.csproj

COPY src/ src/
RUN dotnet publish src/PayBridge.ProviderStub/PayBridge.ProviderStub.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080
ENTRYPOINT ["dotnet", "PayBridge.ProviderStub.dll"]
