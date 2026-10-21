# Third-party notices

FlowRate Client is licensed under the MIT license (see [LICENSE](LICENSE)). It uses the components
below, each under its own license.

## NuGet packages

| Component | License |
|---|---|
| Avalonia (Avalonia.Desktop, .Skia, .Themes.Fluent, .Android, .Fonts.Inter) | MIT |
| Inter font (bundled by Avalonia.Fonts.Inter) | SIL Open Font License 1.1 |
| CommunityToolkit.Mvvm | MIT |
| Dapper, Dapper.AOT | Apache-2.0 |
| DynamicData | MIT |
| GstSharp.Net (.App, .Sdp, .WebRTC) | LGPL-2.1-or-later |
| MessageFormat | MIT |
| Microsoft.Data.Sqlite.Core, Microsoft.Extensions.* | MIT |
| ReactiveUI, ReactiveUI.Avalonia, System.Reactive | MIT |
| Serilog, Serilog.Extensions.Logging, Serilog.Sinks.Console, Serilog.Sinks.File | Apache-2.0 |
| Silk.NET.OpenAL | MIT |
| Silk.NET.OpenAL.Soft.Native (OpenAL Soft) | LGPL-2.0-or-later |
| SkiaSharp | MIT |
| SQLite3MC.PCLRaw.bundle (SQLite3 Multiple Ciphers) | MIT |
| Svg.Controls.Skia.Avalonia | MIT |
| System.Management | MIT |
| Tmds.DBus.Protocol | MIT |
| Velopack | MIT |
| Xamarin.Firebase.Core, Xamarin.Firebase.Messaging (Android) | MIT and Apache-2.0 |
| Xaml.Behaviors.Avalonia | MIT |

## GStreamer runtime

The desktop builds ship their own GStreamer runtime in the `gstreamer/` folder next to the
executable. It contains no GPL components. The libraries are separate, replaceable files, as the
LGPL requires; their full list and license texts are in `THIRD-PARTY-NOTICES.txt` and `licenses/`
next to the executable. How the runtime is built is described in
[src/Client/Client.GStreamer/Build/README.md](src/Client/Client.GStreamer/Build/README.md).

## Included source code

### FluentAvalonia.ProgressRing

`src/Client/Client.Avalonia.Controls/Controls/LoadingIndicators/ProgressRing.axaml` and
`ProgressRing.axaml.cs` are adapted from
[FluentAvalonia.ProgressRing](https://github.com/ymg2006/FluentAvalonia.ProgressRing), licensed under
the MIT license:

```
MIT License

Copyright (c) 2022 ymg2006

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
