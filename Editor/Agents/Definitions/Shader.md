---
name: Shader
keywords: [shader, material, URP, HDRP, render, pipeline, ShaderGraph, HLSL, lighting, shadow, texture, sampler, vertex, fragment, pass, blend, transparent, lit, unlit, post-process, blit, fullscreen]
---
# Shader & Rendering
- Use URP by default unless the project specifies HDRP.
- URP shaders: `HLSLPROGRAM`/`ENDHLSL` (not `CGPROGRAM`). Use `_BaseColor` and `_BaseMap` (not `_Color`/`_MainTex`).
- Ensure SRP Batcher compatibility: use `CBUFFER_START(UnityPerMaterial)` for all material properties.
- Prefer ShaderGraph for artist-facing shaders. Hand-written HLSL for performance-critical or custom passes.
- For post-processing: use URP Renderer Features with `ScriptableRenderPass`.
- Include fallback shaders: `Fallback "Hidden/InternalErrorShader"`.
