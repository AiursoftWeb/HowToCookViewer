ARG CSPROJ_PATH="./src/Aiursoft.HowToCookViewer/"
ARG PROJ_NAME="Aiursoft.HowToCookViewer"

# ============
# Prepare NPM Environment
FROM --platform=$BUILDPLATFORM hub.aiursoft.com/node:24-alpine AS npm-env
ARG CSPROJ_PATH
WORKDIR /src
# Only copy package files to take advantage of Docker cache
COPY ${CSPROJ_PATH}wwwroot/package.json ${CSPROJ_PATH}wwwroot/package-lock.json* ${CSPROJ_PATH}wwwroot/.npmrc* /src/${CSPROJ_PATH}wwwroot/
RUN npm install --prefix "${CSPROJ_PATH}wwwroot" --loglevel verbose

# ============================
# Prepare Building Environment
FROM --platform=$BUILDPLATFORM hub.aiursoft.com/aiursoft/internalimages/dotnet AS build-env
ARG CSPROJ_PATH
ARG PROJ_NAME
ARG TARGETARCH

WORKDIR /src
COPY . .
# Copy node_modules from npm-env
COPY --from=npm-env /src/${CSPROJ_PATH}wwwroot/node_modules /src/${CSPROJ_PATH}wwwroot/node_modules

# Build
RUN if [ "$TARGETARCH" = "arm64" ]; then \
        RID="linux-arm64"; \
    elif [ "$TARGETARCH" = "amd64" ]; then \
        RID="linux-x64"; \
    else \
        RID="linux-$TARGETARCH"; \
    fi && \
    echo "Building for arch: $TARGETARCH, using .NET RID: $RID" && \
    dotnet publish ${CSPROJ_PATH}${PROJ_NAME}.csproj --configuration Release --no-self-contained --runtime $RID --output /app

RUN cp -r ${CSPROJ_PATH}/wwwroot/* /app/wwwroot

# ============================
# Prepare Runtime Environment
FROM hub.aiursoft.com/aiursoft/internalimages/dotnetonlyruntime
ARG PROJ_NAME
WORKDIR /app
COPY --from=build-env /app .

# Edit appsettings.json
RUN sed -i 's/DataSource=app.db/DataSource=\/data\/app.db/g' appsettings.json
RUN sed -i 's/\/tmp\/data/\/data/g' appsettings.json
RUN mkdir -p /data

VOLUME /data
EXPOSE 5000

ENV SRC_SETTINGS=/app/appsettings.json
ENV VOL_SETTINGS=/data/appsettings.json
ENV DLL_NAME=${PROJ_NAME}.dll

#ENTRYPOINT dotnet $DLL_NAME --urls http://*:5000
ENTRYPOINT ["/bin/bash", "-c", "\
    if [ ! -f \"$VOL_SETTINGS\" ]; then \
    cp $SRC_SETTINGS $VOL_SETTINGS; \
    fi && \
    if [ -f \"$SRC_SETTINGS\" ]; then \
    rm $SRC_SETTINGS; \
    fi && \
    ln -s $VOL_SETTINGS $SRC_SETTINGS && \
    dotnet $DLL_NAME --urls http://*:5000 \
    "]

HEALTHCHECK --interval=10s --timeout=3s --start-period=180s --retries=3 CMD \
    wget --quiet --tries=1 --spider http://localhost:5000/health || exit 1