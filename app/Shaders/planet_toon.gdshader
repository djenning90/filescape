// Planets ("Lantern Cosmos", original art): soft toon surface for planets, props and the traveller.
// Vertex colours carry the albedo (linear). Vertex alpha below 1 marks a glowing part: emission = colour × (1 − alpha) ×
// glow_energy, so a lantern needs no extra material. Lighting: wrap diffuse with a soft two-tone terminator, a cool
// violet tint in the shade (shadows are coloured, never black), a sun-coloured rim on the lit side and a faint sky rim on
// every silhouette so forms read against the dark sky. A gentle object-space speckle keeps large areas from looking flat.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform vec3 tint : source_color = vec3(1.0);
uniform float wrap = 0.55;                  // 0 = Lambert, 1 = fully wrapped
uniform float terminator_softness = 0.2;    // width of the light/shade band, in wrapped N·L
uniform float toon_mix = 0.65;              // 0 = smooth wrap, 1 = hard two-tone
uniform vec3 shade_color : source_color = vec3(0.46, 0.36, 0.78);
uniform float shade_strength = 0.42;
uniform float sun_rim = 0.55;
uniform vec3 sky_rim_color : source_color = vec3(0.62, 0.52, 1.0);
uniform float sky_rim = 0.16;
uniform float glow_energy = 2.6;
uniform float speckle = 0.05;
uniform float speckle_per_metre = 1.4;      // speckle frequency, cycles per metre (object space)
uniform float roughness = 0.82;

varying vec3 local_pos;

float hash(vec3 p) {
	p = fract(p * 0.3183099 + vec3(0.11, 0.17, 0.13));
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float vnoise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
			mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void vertex() {
	local_pos = VERTEX;
}

void fragment() {
	vec3 base = COLOR.rgb * tint;
	float n = vnoise(local_pos * speckle_per_metre) * 0.65 + vnoise(local_pos * speckle_per_metre * 3.7) * 0.35;
	base *= 1.0 + speckle * (n * 2.0 - 1.0);
	ALBEDO = base;
	ROUGHNESS = roughness;
	SPECULAR = 0.2;
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float rim = pow(1.0 - facing, 3.0);
	EMISSION = base * (1.0 - COLOR.a) * glow_energy + sky_rim_color * rim * sky_rim;
}

void light() {
	float ndl = dot(NORMAL, LIGHT);
	float w = clamp((ndl + wrap) / (1.0 + wrap), 0.0, 1.0);
	float band = smoothstep(0.5 - terminator_softness, 0.5 + terminator_softness, w);
	float lit = mix(w, band, toon_mix) * ATTENUATION;
	vec3 light_col = LIGHT_COLOR / PI;
	DIFFUSE_LIGHT += light_col * ALBEDO * lit;
	DIFFUSE_LIGHT += light_col * shade_color * ALBEDO * (1.0 - lit) * shade_strength;
	float facing = clamp(dot(NORMAL, VIEW), 0.0, 1.0);
	float rim = smoothstep(0.5, 0.95, 1.0 - facing) * smoothstep(-0.1, 0.5, ndl) * ATTENUATION;
	vec3 h = normalize(LIGHT + VIEW);
	float glint = smoothstep(0.94, 0.985, dot(NORMAL, h)) * (1.0 - ROUGHNESS) * ATTENUATION;
	SPECULAR_LIGHT += light_col * (rim * sun_rim + glint * 0.8);
}
