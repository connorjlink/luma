# LUMA FRONTEND
Luma frontend (_luma_) is an elegant and modern C#/WinUI 3-based Window desktop application used as a graphical frontend for the luma ray-tracing program. The app communicates with a render subprocess—of which there are currently two choices—using native Windows IPC methods: either shared-memory software rendering (via memory-mapping files) or hardware-accelerated blitting via GPU compute (via DirectX/wgpu). The app presents a series of rendering options to control the visual fidelity of the rendered output and a detailed status reporting system that depicts system load and application performance metrics.

![Luma Frontend Example Output Image](/example.png)

## RUN INSTRUCTIONS
Clone this repository and its sibling project _raytracer_, then open _raytracer.sln_ in Visual Studio 2022. Select _luma_ as the startup project, set the build mode to _Connected_ (a custom compile option adjacent to Debug/Release), and then compile and run the project. This frontend C# program will automatically master the C++/Rust-side build and subprocess the primary rendering work upon opening.

## PLANNED FEATURES
- More granular render option control
- Looser subprocess integration to enable running third-party backends
- Robust error handling and logging to lessen the impact of backend issues that currently also affect the frontend

## KNOWN ISSUES
None currently
