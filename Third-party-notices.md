# Bundled document-reading libraries

The application bundles the unmodified .NET Framework assemblies from these official NuGet packages. Copies of their licenses and applicable notices are in the `licenses` folder. They require no account or internet connection at runtime.

| Package | Version | License | Source |
| --- | --- | --- | --- |
| PdfPig | 0.1.16 | Apache-2.0 (see bundled notices for additional included components) | https://www.nuget.org/packages/PdfPig/0.1.16 |
| Microsoft.Bcl.HashCode | 6.0.0 | MIT | https://www.nuget.org/packages/Microsoft.Bcl.HashCode/6.0.0 |
| System.Buffers | 4.6.0 | MIT | https://www.nuget.org/packages/System.Buffers/4.6.0 |
| System.Memory | 4.6.0 | MIT | https://www.nuget.org/packages/System.Memory/4.6.0 |
| System.Numerics.Vectors | 4.6.0 | MIT | https://www.nuget.org/packages/System.Numerics.Vectors/4.6.0 |
| System.Runtime.CompilerServices.Unsafe | 6.1.0 | MIT | https://www.nuget.org/packages/System.Runtime.CompilerServices.Unsafe/6.1.0 |

PdfPig assemblies target .NET Framework 4.7.1 and come from source commit `a7bb35662bbbf405efddad50aedc9bcdcf515afc` in https://github.com/UglyToad/PdfPig. Its LICENSE and NOTICES.txt are preserved verbatim. Microsoft package manifests declare the MIT license; its standard license is preserved from https://github.com/dotnet/runtime/blob/main/LICENSE.TXT.

The setup script separately downloads Ollama and the Gemma model, governed by their own terms linked in README.md. Those large runtime/model files are not bundled in the pet ZIP.
