# Third-party notices

Compositor's existing C kernels and original design are distributed under the repository's MIT license, Copyright (c) 2026 Wonder Assembly LLC. The complete notice is in `LICENSE.txt` in published output.

The Windows build uses these packages (full resolved versions are recorded in `packages.lock.json`):

- SkiaSharp and SkiaSharp.Views: MIT, Microsoft Corporation and contributors. https://github.com/mono/SkiaSharp/blob/main/LICENSE.txt
- Skia native graphics library: BSD-style licenses, Google and contributors. https://github.com/google/skia/blob/main/LICENSE
- OpenTK and GLWpfControl (transitive WPF view dependencies): MIT. https://github.com/opentk/opentk and https://github.com/opentk/GLWpfControl
- .NET runtime (included in self-contained output): Microsoft and contributors. Runtime distribution includes its license and third-party notices. https://github.com/dotnet/runtime/blob/main/LICENSE.TXT

Upstream native binaries include additional third-party components. Preserve their distribution notices when packaging. xUnit and Microsoft.NET.Test.Sdk are build/test dependencies and are not shipped with the application.

## ONNX Runtime
Microsoft.ML.OnnxRuntime 1.30.0 / Microsoft.ML.OnnxRuntime.Managed — MIT license. Source and license: https://github.com/microsoft/onnxruntime . Native CPU inference dependency. U2Net weights are currently development verification artifacts and are not included in the distributed package.

## Optional U²-Net background models
Models are downloaded on demand from the rembg v0.0.0 GitHub release or imported by the user; weights are not bundled. Upstream project: https://github.com/xuebinqin/U-2-Net . Upstream repository license: Apache-2.0, https://github.com/xuebinqin/U-2-Net/blob/master/LICENSE . ONNX distribution and input reference: https://github.com/danielgatis/rembg . Only the two SHA-256 pinned model files are supported; provenance and hashes are recorded in docs/background-removal.md. Model prediction can miss subjects; this is not an Apple Vision equivalent-quality guarantee.
