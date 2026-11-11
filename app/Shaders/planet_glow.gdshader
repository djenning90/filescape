// Planets ("Lantern Cosmos", original art): a softly glowing solid (lantern cores, the launcher's heart). Unlit and
// brighter than white so the scene's glow blooms around it; a gentle optional pulse; a whiter centre where it faces you.
shader_type spatial;
render_mode unshaded, cull_back, shadows_disabled;

uniform vec3 color : source_color = vec3(1.0, 0.8, 0.4);
uniform float energy = 2.6;
uniform float pulse = 0.0;          // 0..1 pulse depth
uniform float pulse_speed = 2.0;    // radians per second

void fragment() {
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float p = 1.0 + pulse * sin(TIME * pulse_speed);
	ALBEDO = color * energy * p * mix(0.65, 1.0, facing) + vec3(1.0) * pow(facing, 5.0) * 0.35 * energy;
}
