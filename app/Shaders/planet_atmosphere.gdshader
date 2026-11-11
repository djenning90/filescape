// Planets ("Lantern Cosmos", original art): a soft atmosphere halo, drawn on a shell slightly larger than the planet.
// Additive and unlit: brightest just outside the planet's limb (limb_f, set from the shell/planet radius ratio), fading
// to nothing at the shell's edge and over the planet's face, so the planet reads as a glowing pastel world.
shader_type spatial;
render_mode unshaded, blend_add, depth_draw_never, cull_back, shadows_disabled;

uniform vec3 color : source_color = vec3(0.8, 0.6, 1.0);
uniform float intensity = 0.9;
uniform float limb_f = 0.5;     // 1 − cos(view angle) on the shell where a view ray grazes the planet

void fragment() {
	float f = 1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float inside = smoothstep(limb_f * 0.72, limb_f * 1.02, f);
	float outside = pow(clamp((1.0 - f) / max(1.0 - limb_f, 0.001), 0.0, 1.0), 1.6);
	ALBEDO = color * inside * outside * intensity;
}
