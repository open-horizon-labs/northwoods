FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY src/Northwoods.slnx .
COPY src/BuildingBlocks/Northwoods.Contracts/Northwoods.Contracts.csproj BuildingBlocks/Northwoods.Contracts/
COPY src/BuildingBlocks/Northwoods.Tenancy/Northwoods.Tenancy.csproj BuildingBlocks/Northwoods.Tenancy/
COPY src/Services/Northwoods.Api/Northwoods.Api.csproj Services/Northwoods.Api/
RUN dotnet restore Services/Northwoods.Api/Northwoods.Api.csproj

COPY src/BuildingBlocks/ BuildingBlocks/
COPY src/Services/Northwoods.Api/ Services/Northwoods.Api/
RUN dotnet publish Services/Northwoods.Api/Northwoods.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app
COPY --from=build /app .
COPY samples/intakes/*.pdf /app/samples/intakes/
EXPOSE 8080
ENTRYPOINT ["dotnet", "Northwoods.Api.dll"]
