FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/Catalogo/Catalogo.csproj src/Catalogo/
RUN dotnet restore src/Catalogo/Catalogo.csproj

COPY src/ src/
RUN dotnet publish src/Catalogo/Catalogo.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# A plataforma injeta a porta em PORT e encaminha o tráfego para ela (ADR-018).
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "Catalogo.dll"]
