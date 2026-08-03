# Third-party notices

PwrMon redistributes the following open-source components. Everything listed here ships
inside the portable, standalone, and installer packages — not just the four packages PwrMon
references directly, but everything those pull in transitively.

Regenerate this list after any dependency change with:

```powershell
powershell -ExecutionPolicy Bypass -File tools/list-shipped-assemblies.ps1
```

## Direct dependencies

| Component | License | Source |
|-----------|---------|--------|
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 0.9.6 | MPL-2.0 | CPU/iGPU sensor access |
| [ScottPlot / ScottPlot.WPF](https://scottplot.net/) 5.1.59 | MIT | History charts |
| [System.Management](https://dot.net/) | MIT | WMI access |
| [System.Diagnostics.PerformanceCounter](https://dot.net/) | MIT | Windows EMI energy counters |
| [System.Threading.AccessControl](https://dot.net/) | MIT | ACLs on the cross-instance signals |

## Transitive dependencies that ship in the binary

| Component | License | Pulled in by |
|-----------|---------|--------------|
| [RAMSPDToolkit-NDD](https://github.com/Blacktempel/RAMSPDToolkit) 1.4.2 | **MPL-2.0** | LibreHardwareMonitorLib |
| [DiskInfoToolkit](https://github.com/Blacktempel/DiskInfoToolkit) 1.1.2 | **MPL-2.0** | LibreHardwareMonitorLib |
| [BlackSharp.Core](https://github.com/Blacktempel/BlackSharp) 1.0.7 | **MPL-2.0** | LibreHardwareMonitorLib |
| [HidSharp](https://software.seekye.com/hidsharp) 2.6.4 | **Apache-2.0** | LibreHardwareMonitorLib |
| [SkiaSharp](https://github.com/mono/SkiaSharp) 3.119.0 (+ Views.WPF, Views.Desktop.Common, HarfBuzz) | MIT | ScottPlot.WPF |
| [HarfBuzzSharp](https://github.com/mono/SkiaSharp) 8.3.1.1 | MIT | ScottPlot.WPF |
| [OpenTK](https://github.com/opentk/opentk) 4.9.4 (Core, Graphics, Mathematics, Input, Compute, Audio.OpenAL, Windowing.*) | MIT | ScottPlot.WPF |
| [OpenTK.GLWpfControl](https://github.com/varon/GLWpfControl) 4.3.3 | MIT | ScottPlot.WPF |
| [GLFW](https://www.glfw.org/) 3.4 (`glfw3.dll`, via OpenTK.redist.glfw) | **zlib/libpng** | ScottPlot.WPF |
| [Mono.Posix.NETStandard](https://github.com/mono/mono) 1.0.0 | MIT | LibreHardwareMonitorLib |
| [System.IO.Ports](https://dot.net/) | MIT | LibreHardwareMonitorLib |
| [System.CodeDom](https://dot.net/) | MIT | System.Management |
| [System.Configuration.ConfigurationManager](https://dot.net/) | MIT | System.Diagnostics.PerformanceCounter |
| [System.Diagnostics.EventLog](https://dot.net/) | MIT | System.Diagnostics.PerformanceCounter |
| [System.Security.Cryptography.ProtectedData](https://dot.net/) | MIT | System.Configuration.ConfigurationManager |
| .NET Runtime and Windows Desktop Runtime (Microsoft) | MIT | Runtime (self-contained builds bundle it) |

## MPL-2.0 source availability

LibreHardwareMonitorLib, RAMSPDToolkit-NDD, DiskInfoToolkit, and BlackSharp.Core are
distributed under the Mozilla Public License 2.0. PwrMon uses all four as **unmodified
library dependencies**; no MPL-covered file has been altered. Source for each is available
at the repository linked in the tables above, and a copy of the MPL-2.0 text is at
<https://mozilla.org/MPL/2.0/>.

## Apache-2.0 notice

HidSharp is Copyright 2010–2025 James F. Bellinger, licensed under the Apache License,
Version 2.0. You may obtain a copy of the License at
<http://www.apache.org/licenses/LICENSE-2.0>. HidSharp is redistributed unmodified.

## zlib/libpng notice (GLFW)

`glfw3.dll` is Copyright © 2002–2006 Marcus Geelnard and © 2006–2019 Camilla Löwy,
distributed under the zlib/libpng license. The software is provided "as-is", without any
express or implied warranty. PwrMon redistributes it unaltered.

## Not bundled

PwrMon can optionally download the official [PawnIO](https://pawnio.eu/) installer
(GPL-2.0 with an IOCTL-interface exception) from its official release page, but only when
the user explicitly asks for it, and only after PwrMon has verified the download's
Authenticode signature and shown the signer for confirmation. PawnIO is a separate product
and is never redistributed inside PwrMon packages.
