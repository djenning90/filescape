// Places (PLACE-14): the plaza floor. Dark wet glass that keeps reflections (SSR on High) but avoids mirror-sharp
// micro-detail, plus anti-aliased emissive rings in the folder's accent colour: a rim, the gate ring and a compass
// around the landmark. Outside `radius` the fragment is discarded so the wet asphalt ground shows.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform vec3 accent : source_color = vec3(0.3, 0.8, 1.0);
uniform float radius = 30.0;
uniform float gate_ring = 24.0;
uniform float ring_step = 6.0;
uniform float glow = 0.6;

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

// A line of world width `w` at distance d, anti-aliased by the screen-space derivative.
float aa_line(float d, float w) {
	float fw = max(fwidth(d), 1e-4);
	return 1.0 - smoothstep(w * 0.5, w * 0.5 + fw * 1.5, abs(d));
}

void fragment() {
	vec2 q = world_pos.xz;
	float r = length(q);
	if (r > radius) { discard; }
	float ang = atan(q.x, -q.y);
	float n = noise(q * 0.09) * 0.6 + noise(q * 0.37) * 0.4;

	// Concentric guide rings, fading out far from the camera so they never alias into shimmer.
	float cam = length(world_pos.xz - CAMERA_POSITION_WORLD.xz);
	float fade = 1.0 - smoothstep(60.0, 180.0, cam);
	float rings = aa_line(mod(r + ring_step * 0.5, ring_step) - ring_step * 0.5, 0.04) * 0.14 * fade;
	float rim = aa_line(r - (radius - 0.6), 0.35) * 1.6 + aa_line(r - (radius - 1.5), 0.08) * 0.6;
	float gate = aa_line(r - gate_ring, 0.18) * 0.9;
	float compass = aa_line(r - 3.2, 0.1) * 1.0 + aa_line(r - 3.8, 0.04) * 0.5;
	float spokes = aa_line(sin(ang * 18.0) * r / 18.0, 0.03) * 0.12 * (1.0 - smoothstep(3.0, 9.0, r)) * step(3.2, r);

	ALBEDO = vec3(0.018, 0.02, 0.03) * (0.85 + 0.3 * n);
	// Roughness never drops to mirror level: sub-pixel highlights on near-zero roughness are a flicker source (VIS-11).
	ROUGHNESS = mix(0.16, 0.3, n);
	METALLIC = 0.25;
	SPECULAR = 0.5;
	EMISSION = accent * glow * (rings + rim + gate + compass + spokes);
}
