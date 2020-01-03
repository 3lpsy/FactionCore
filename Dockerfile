FROM mcr.microsoft.com/dotnet/core/sdk:3.0

# when building, the default config will run dotnet restore
# if using a local/dev version of Faction.Common (as a volume), this will fail
# set this to 0 to disable restoring and publishing on build. should only be used locally
ARG PUBLISH_ENABLED=1

# the default run target just assumes the project has been built during build time
# valid run targets are: "watch" and "published"
# watch will watch for changes using dotnet watch and is best used for dev with volumes
ENV DOCKER_RUN_TARGET published

# if the image is built with PUBLISH_ENABLED=0, 
# then set DOCKER_PUBLISH_ON_RUN=1 to publish when run
# this allows for using compiled target but still a local version of Faction.Common
ENV DOCKER_PUBLISH_ON_RUN 0

# disable telemetry
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1

# allows for mounting the dll from the host, used for development
RUN mkdir -p /Faction.Common/bin

WORKDIR /app

# startup has three commands: publish, restore, and the empty/default/no command
# the empty/default command just runs the project
COPY startup.sh /opt/startup.sh
RUN dotnet tool install dotnet-ef --version 3.0 --tool-path /usr/local/bin/ &&\
  chmod +x /opt/startup.sh

# copy csproj before the rest of the project to pull
# dependencies early to cache properly
COPY *.csproj ./
RUN /opt/startup.sh restore $PUBLISH_ENABLED

# copy and build everything else
COPY . ./
RUN chmod 777 ./wait-for-it.sh

# publish if enabled
RUN /opt/startup.sh publish $PUBLISH_ENABLED

ENTRYPOINT [ "/bin/bash", "/opt/startup.sh"]