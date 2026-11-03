// District ground: dark wet asphalt. Value noise varies roughness, so puddles reflect (with SSR on High quality).
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform vec3 base_color : source_color = vec3(0.016, 0.018, 0.026);
uniform vec3 lot_line_color : source_color = vec3(0.12, 0.1, 0.3);
uniform float puddle_scale = 0.11;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(41.3, 289.1))) * 43758.5453); }
float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

void fragment() {
	float n = noise(world_pos.xz * puddle_scale) * 0.65 + noise(world_pos.xz * puddle_scale * 3.7) * 0.35;
	float puddle = smoothstep(0.52, 0.62, n);
	vec2 coord = world_pos.xz / 12.0;
	vec2 g = abs(fract(coord - 0.5) - 0.5) / fwidth(coord);
	float line = 1.0 - min(min(g.x, g.y), 1.0);
	float dist = length(world_pos.xz - CAMERA_POSITION_WORLD.xz);
	ALBEDO = base_color * (1.0 - 0.5 * puddle);
	ROUGHNESS = mix(0.55, 0.04, puddle);
	METALLIC = 0.0;
	SPECULAR = 0.6;
	EMISSION = lot_line_color * line * (1.0 - smoothstep(80.0, 400.0, dist)) * 0.35;
}
