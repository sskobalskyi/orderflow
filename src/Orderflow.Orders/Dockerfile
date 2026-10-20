FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/Orderflow.Contracts/*.csproj src/Orderflow.Contracts/
COPY src/Orderflow.Orders/*.csproj src/Orderflow.Orders/
RUN dotnet restore src/Orderflow.Orders
COPY src src
RUN dotnet publish src/Orderflow.Orders -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Orderflow.Orders.dll"]
