FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build-env
WORKDIR /app

RUN apt update
RUN apt install -y clang zlib1g-dev

COPY *.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o out

FROM alpine:latest
WORKDIR /app

COPY --from=build-env /app/out/ ./
RUN chmod +x ./snoopest

ENTRYPOINT ["./snoopest"]