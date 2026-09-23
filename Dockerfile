FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY src/Catalogo/Catalogo.csproj src/Catalogo/
RUN dotnet restore src/Catalogo/Catalogo.csproj

COPY src/ src/
RUN dotnet publish src/Catalogo/Catalogo.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# O Npgsql carrega a biblioteca Kerberos ao abrir a conexão, e ela não vem na imagem de
# runtime: sem isto a aplicação sobe e falha com `libgssapi_krb5.so.2: cannot open shared
# object file`.
RUN apt-get update \
    && apt-get install -y --no-install-recommends libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# A plataforma injeta a porta em PORT e encaminha o tráfego para ela (ADR-018).
ENV ASPNETCORE_HTTP_PORTS=10000
EXPOSE 10000

ENTRYPOINT ["dotnet", "Catalogo.dll"]
