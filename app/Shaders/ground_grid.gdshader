// The classic FSN-style landscape: deep indigo ground with an anti-aliased world-space grid that fades with distance.
shader_type spatial;
render_mode diffuse_burley;

uniform vec3 base_color : source_color = vec3(0.05, 0.07, 0.17);
uniform vec3 line_color : source_color = vec3(0.22, 0.33, 0.75);
uniform float grid_size = 4.0;
uniform float line_width = 0.03;
uniform float fade_distance = 700.0;
uniform float line_glow = 0.55;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec2 coord = world_pos.xz / grid_size;
	vec2 g = abs(fract(coord - 0.5) - 0.5) / fwidth(coord);
	float line = 1.0 - min(min(g.x, g.y), 1.0);
	float dist = length(world_pos.xz - CAMERA_POSITION_WORLD.xz);
	float fade = 1.0 - smoothstep(fade_distance * 0.25, fade_distance, dist);
	ALBEDO = base_color;
	ROUGHNESS = 0.85;
	EMISSION = line_color * line * fade * line_glow;
}
