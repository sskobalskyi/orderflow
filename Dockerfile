ARG SERVICE
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG SERVICE
WORKDIR /src
COPY src/Orderflow.Contracts/*.csproj src/Orderflow.Contracts/
COPY src/Orderflow.${SERVICE}/*.csproj src/Orderflow.${SERVICE}/
RUN dotnet restore src/Orderflow.${SERVICE}
COPY src src
RUN dotnet publish src/Orderflow.${SERVICE} -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
ARG SERVICE
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENV APP_DLL=Orderflow.${SERVICE}.dll
ENTRYPOINT ["sh", "-c", "exec dotnet $APP_DLL"]
