# FlowRate.Client.GStreamer.Runtime.&lt;rid&gt;

The GStreamer runtime the FlowRate client ships, for hosts of the `FlowRate.*` libraries such as bots:
`FlowRate.Client.GStreamer.Runtime.linux-x64`, `.win-x64` and `.osx-arm64`. It is the same runtime as in
the client, built by the scripts of `Client.GStreamer/Build` (no GPL components, AAC through fdk-aac).

## Use

```xml
<PackageReference Include="FlowRate.Client" Version="..." />
<PackageReference Include="FlowRate.Client.GStreamer.Runtime.linux-x64" Version="1.28.7" />
```

On build and publish the package puts `gstreamer/`, `licenses/` and `THIRD-PARTY-NOTICES.txt` next to
the executable, where `GstNativeLibraryResolver.Register()` finds the runtime by default. It does so for
a build for its RID, or for a build without a RID on that system; the packages of several RIDs can be
referenced together. A build on Windows for Linux or macOS cannot mark the plugin scanner executable:
run `chmod +x gstreamer/libexec/*/gst-plugin-scanner` where it is deployed.

Without the package, `FlowRate.Client.GStreamer` takes the GStreamer installed on the system
(`Register(useInstalledRuntime: true)`).

## Version

The version is that of GStreamer. A fourth number (`1.28.7.1`) is the same GStreamer built anew, after a
change of the build scripts.

## Packing

`src/Scripts/bash/pack.sh --gstreamer-runtime <rid> [version] [output]` packs it from
`Build/artifacts/<rid>`, which the scripts of `Build/<os>` produce. It stages the package in
`Build/artifacts/<rid>/package`: the runtime under `runtime/`, `runtime.targets` with the RID filled in
under `buildTransitive/`, this README and the icon, then packs `runtime.nuspec` over it.
