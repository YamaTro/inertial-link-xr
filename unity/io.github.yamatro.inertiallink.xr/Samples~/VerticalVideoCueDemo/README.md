# Vertical Video + Protected-Margin Cues

This hardware-free sample keeps a 9:16 video, the Camera, and the XR Origin fixed. Only a generated field of
small cues in the left and right margins responds to the synthetic vehicle-motion source.

1. Import the sample from Unity Package Manager.
2. Add `VerticalVideoCueDemoBootstrap` to an empty GameObject.
3. Optionally assign a `VideoClip`, or call `ConfigureLocalVideoPath` with an absolute path to a local
   `.mp4`, `.webm`, `.ogv`, or `.mov` file before enabling the component.
4. Enter Play Mode. The diagnostics panel compares the measured synthetic acceleration with a deliberately
   under-responsive virtual acceleration and shows a bounded correction suggestion.

Network video URLs are refused. If no local video is assigned, the sample uses an animated generated texture.
The sample demonstrates the integration and measurement mechanism; it is not evidence that this implementation
reduces motion sickness.
