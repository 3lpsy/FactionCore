FROM microsoft/dotnet:2.1-sdk
WORKDIR /app
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1
# copy csproj and restore as distinct layers
COPY *.csproj ./
RUN dotnet restore

# copy and build everything else
COPY . ./
RUN dotnet publish -c Release -o out
RUN chmod 777 ./wait-for-it.sh
ENTRYPOINT [ "/bin/bash", "startup.sh" ]