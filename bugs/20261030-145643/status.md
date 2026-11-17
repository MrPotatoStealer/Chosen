# 20261030-145643: AO needs to take the light into account? the dark edges looks pretty bad with the fog

Status: fixed

## History

- 2026-10-30 14:56:43 reported in maps/e1m1.map
- 2026-10-30 fixed. Cause: AO darkened the finished image after the opaque pass: the dynamic lights and the fog Godot shades in (its depth fade, its lights' glow) went dark in the corners too
- 2026-10-30 Fix: AmbientOcclusion runs before the opaque pass (after the depth prepass: depth, normals, roughness) into ao_texture, and the opaque Build shaders multiply only their emission (Build's shading, the ambient) by it (ShaderApply); translucent ones and the water don't
