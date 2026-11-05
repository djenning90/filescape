// EXPERIMENT (PLANET-02): a small world's surface — soft latitude bands and a fine grid in two of its colours, a
// glowing equator, lit by the scene. Original look for the experiment only.
shader_type spatial;
render_mode blend_mix, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 base_a : source_color = vec3(0.1, 0.35, 0.35);
uniform vec3 base_b : source_color = vec3(0.05, 0.2, 0.25);
uniform vec3 glow : source_color = vec3(0.3, 1.0, 0.85);
uniform float radius = 40.0;

varying vec3 n;

void vertex() { n = normalize(VERTEX); }

void fragment() {
	float lat = asin(clamp(n.y, -1.0, 1.0));
	float lon = atan(n.z, n.x);
	float bands = 0.5 + 0.5 * sin(lat * 9.0);
	vec2 g = vec2(lat, lon) * radius / 4.0;       // ≈ 4 m grid cells on the surface
	vec2 f = abs(fract(g) - 0.5);
	float gw = fwidth(g.x) + fwidth(g.y);
	float grid = 1.0 - smoothstep(0.46, 0.46 + gw, max(f.x, f.y));
	float equator = 1.0 - smoothstep(0.0, 0.03, abs(lat));
	ALBEDO = mix(base_b, base_a, bands) * (1.0 - 0.25 * grid);
	ROUGHNESS = 0.7;
	EMISSION = glow * (0.35 * grid + 1.6 * equator) + mix(base_b, base_a, bands) * 0.15;
}
