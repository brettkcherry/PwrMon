# Third-party notices

PwrMon bundles or depends on the following open-source components:

| Component | License | Source |
|-----------|---------|--------|
| [LibreHardwareMonitorLib](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) | MPL-2.0 | CPU/GPU sensor access |
| [ScottPlot](https://scottplot.net/) | MIT | History charts |
| System.Management, System.Diagnostics.PerformanceCounter (Microsoft) | MIT | WMI + performance counter access |
| .NET Runtime and Windows Desktop Runtime (Microsoft) | MIT | Runtime (self-contained builds bundle it) |

LibreHardwareMonitorLib is distributed under the Mozilla Public License 2.0; its
source code is available at the repository linked above. PwrMon uses it as
an unmodified library dependency.

**Not bundled:** PwrMon can optionally download the official
[PawnIO](https://pawnio.eu/) installer (GPL-2.0 with an IOCTL-interface exception)
from its official release page when the user explicitly requests it. PawnIO is a
separate product and is never redistributed inside PwrMon packages.
