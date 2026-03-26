FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
WORKDIR /src
COPY Northwoods.slnx .
COPY BuildingBlocks/Northwoods.Contracts/Northwoods.Contracts.csproj BuildingBlocks/Northwoods.Contracts/
COPY BuildingBlocks/Northwoods.Tenancy/Northwoods.Tenancy.csproj BuildingBlocks/Northwoods.Tenancy/
COPY Services/Northwoods.Api/Northwoods.Api.csproj Services/Northwoods.Api/
RUN dotnet restore Services/Northwoods.Api/Northwoods.Api.csproj

COPY BuildingBlocks/ BuildingBlocks/
COPY Services/Northwoods.Api/ Services/Northwoods.Api/
RUN dotnet publish Services/Northwoods.Api/Northwoods.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Northwoods.Api.dll"]
