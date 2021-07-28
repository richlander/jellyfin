#!/usr/bin/env bash

# Build configuration
NETCORE_COMPOSITE=true
NETCORE_INCLUDE_ASPNET=true
ASPNET_COMPOSITE=false
APP_COMPOSITE=false
SELF_CONTAINED=false

OUTPUT_DIR=/jellyfin
rm -rf $OUTPUT_DIR
mkdir $OUTPUT_DIR

dotnet new console -o testapp
dotnet publish testapp/testapp.csproj /p:PublishReadyToRun=true
rm -rf testapp

# Identify .NET Core and ASP.NET framework locations
DOTNET_ROOT=/usr/share/dotnet
find $DOTNET_ROOT -name System.Private.CoreLib.dll
SPC_PATH=`find $DOTNET_ROOT/shared -name System.Private.CoreLib.dll`
ASP_PATH=`find $DOTNET_ROOT/shared -name Microsoft.AspNetCore.dll`
NETCORE_PATH=$(dirname "${SPC_PATH}")
ASPNETCORE_PATH=$(dirname "${ASP_PATH}")

echo "Dotnet root:             $DOTNET_ROOT"
echo "Using .NET Core path:    $NETCORE_PATH"
echo "Using ASP.NET Core path: $ASPNETCORE_PATH"
echo "Output directory:        $OUTPUT_DIR"
echo ".NET Core composite:     $NETCORE_COMPOSITE"
echo ".NET Core + ASP.NET:     $NETCORE_INCLUDE_ASPNET"
echo "ASP.NET composite:       $ASPNET_COMPOSITE"
echo "Jellyfin composite:      $APP_COMPOSITE"

#  /p:PublishReadyToRunCrossgen2ExtraArgs=--inputbubble%3b--instruction-set:avx2

# First publish the app as non-self-contained; we'll inject
# the compiled framework to it later.
PUBLISH_CMD="dotnet publish Jellyfin.Server"
# because of changes in docker and systemd we need to not build in parallel at the moment
# see https://success.docker.com/article/how-to-reserve-resource-temporarily-unavailable-errors-due-to-tasksmax-setting
PUBLISH_CMD+=" --disable-parallel"
PUBLISH_CMD+=" --configuration Release"
PUBLISH_CMD+=" --output $OUTPUT_DIR"
PUBLISH_CMD+=" --runtime linux-x64"
PUBLISH_CMD+=" --self-contained $SELF_CONTAINED"
PUBLISH_CMD+=" -p:DebugSymbols=false;DebugType=none"
PUBLISH_CMD+=" /p:PublishReadyToRun=$APP_COMPOSITE"
PUBLISH_CMD+=" /p:PublishReadyToRunComposite=$APP_COMPOSITE"

echo "Publishing Jellyfin.Server: $PUBLISH_CMD"
$PUBLISH_CMD

# Locate crossgen2
CROSSGEN2_PATH=`find / -name crossgen2`

echo "Using Crossgen2 path:    $CROSSGEN2_PATH"

BASE_CMD=""
BASE_CMD+=" --composite"
BASE_CMD+=" --targetos:Linux"
BASE_CMD+=" --targetarch:x64"
BASE_CMD+=" --inputbubble"
BASE_CMD+=" --instruction-set:avx2"

if [[ "$NETCORE_COMPOSITE" == "true" ]]; then
    NETCORE_CMD="$CROSSGEN2_PATH"
    NETCORE_CMD+=" -o:$NETCORE_PATH/framework.r2r.dll"
    NETCORE_CMD+=$BASE_CMD
    NETCORE_CMD+=" $NETCORE_PATH/*.dll"
    if [[ "$NETCORE_INCLUDE_ASPNET" == "true" ]]; then
        NETCORE_CMD+=" $ASPNETCORE_PATH/*.dll"
    fi
    echo "Compiling framework: $NETCORE_CMD"
    $NETCORE_CMD
else
    cp $NETCORE_PATH/*.dll $OUTPUT_DIR
fi

if [[ "$ASPNET_COMPOSITE" == "true" && "$NETCORE_INCLUDES_ASPNET" != "true" ]]; then
    ASPNET_CMD="$CROSSGEN2_PATH"
    ASPNET_CMD+=$BASE_CMD
    ASPNET_CMD+=" -o:$ASPNETCORE_PATH/aspnetcore.r2r.dll"
    ASPNET_CMD+=" $ASPNETCORE_PATH/*.dll"
    ASPNET_CMD+=" -r:$NETCORE_PATH/*.dll"
    echo "Compiling ASP.NET Core: $ASPNET_CMD"
    $ASPNET_CMD
fi
