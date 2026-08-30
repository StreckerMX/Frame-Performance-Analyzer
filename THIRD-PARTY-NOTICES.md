# Third-party notices — Frame Performance Analyzer

Frame Performance Analyzer itself is distributed under the MIT License (see `LICENSE`).

The self-contained single-file distribution embeds the following third-party
components and their transitive dependencies. License identifiers below are
taken from each package's published metadata. License texts are available at
the linked license locations.

## Direct dependencies

| Package | Version | License | Source |
|---|---|---|---|
| CommunityToolkit.Mvvm | 8.4.2 | MIT | https://github.com/CommunityToolkit/dotnet |
| CsvHelper | 33.1.0 | MS-PL **or** Apache-2.0 (dual) | https://joshclose.github.io/CsvHelper/ |
| ScottPlot | 5.1.59 | MIT | https://scottplot.net/ |
| Serilog | 4.4.0 | Apache-2.0 | https://serilog.net/ |
| Serilog.Sinks.File | 7.0.0 | Apache-2.0 | https://github.com/serilog/serilog-sinks-file |
| Microsoft.Extensions.DependencyInjection | 10.0.11 | MIT | https://dot.net/ |

## Notable transitive dependencies

- **SkiaSharp** (used by ScottPlot) — MIT — https://github.com/mono/SkiaSharp
- **Microsoft.Extensions.\*** supporting packages — MIT — https://dot.net/
- **System.\*** runtime packages — MIT — https://dot.net/

## Not distributed

- **BenchmarkDotNet** (0.15.8, MIT) is used only by the developer benchmark
  suite and is **not** part of the distributed application.

## License text locations

- MIT: https://licenses.nuget.org/MIT
- Apache-2.0: https://licenses.nuget.org/Apache-2.0
- MS-PL: https://licenses.nuget.org/MS-PL

Apache-2.0 components (Serilog, Serilog.Sinks.File) are redistributed in
unmodified binary form in accordance with their license terms.
