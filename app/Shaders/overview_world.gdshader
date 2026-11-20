// The overview's miniature worlds (PLANET-92, AD-77): one shared material for every solid world, so 1,500 worlds stay
// one MultiMesh draw. Per instance: COLOR is the base colour, INSTANCE_CUSTOM.rgb the second colour and
// INSTANCE_CUSTOM.a the pattern and its phase packed as (pattern + phase) / 8 (pattern 0 plain, 1 bands, 2 spots,
// 3 caps, 4 swirl; phase 0..1 shifts the pattern and sets its fineness). Both come from Core's OverviewWorldLook, from
// the folder's character and a seed of its names, so a world looks the same in every run.
// Cheap: one or two octaves of value noise per pixel; the pattern lives on the unit sphere's own points, so it stays
// put on the world as it is scaled and moved.
shader_type spatial;
render_mode cull_back, shadows_disabled;

varying vec3 sphere_point;
varying vec4 look;

void vertex() {
	sphere_point = VERTEX;
	look = INSTANCE_CUSTOM;
}

float hash(vec3 p) {
	p = fract(p * 0.3183099 + 0.1);
	p *= 17.0;
	return fract(p.x * p.y * p.z * (p.x + p.y + p.z));
}

float value_noise(vec3 x) {
	vec3 i = floor(x);
	vec3 f = fract(x);
	f = f * f * (3.0 - 2.0 * f);
	return mix(mix(mix(hash(i + vec3(0, 0, 0)), hash(i + vec3(1, 0, 0)), f.x), mix(hash(i + vec3(0, 1, 0)), hash(i + vec3(1, 1, 0)), f.x), f.y),
		mix(mix(hash(i + vec3(0, 0, 1)), hash(i + vec3(1, 0, 1)), f.x), mix(hash(i + vec3(0, 1, 1)), hash(i + vec3(1, 1, 1)), f.x), f.y), f.z);
}

void fragment() {
	float code = look.a * 8.0;
	float pattern = floor(code + 0.001);
	float phase = fract(code);
	vec3 p = normalize(sphere_point);
	float n = value_noise(p * 3.0 + phase * 17.0);
	float m;
	if (pattern < 0.5) {
		// Plain: a rocky mottle of the second colour.
		m = smoothstep(0.5, 0.75, n) * 0.55;
	} else if (pattern < 1.5) {
		// Bands: 5..11 latitude bands, wavy.
		float f = 5.0 + phase * 6.0;
		m = smoothstep(-0.25, 0.55, sin((p.y + (n - 0.5) * 0.25) * f * 3.14159));
	} else if (pattern < 2.5) {
		// Spots: patches of the second colour.
		m = smoothstep(0.6, 0.68, value_noise(p * (4.0 + phase * 3.0) + 5.0));
	} else if (pattern < 3.5) {
		// Caps: bright poles and a darker equator.
		m = smoothstep(0.62, 0.72, abs(p.y) + (n - 0.5) * 0.2);
	} else {
		// Swirl: bands twisted round the axis.
		float a = atan(p.z, p.x);
		m = smoothstep(-0.3, 0.5, sin(p.y * 7.0 + a * 2.0 + n * 2.5 + phase * 6.283));
	}

	ALBEDO = mix(COLOR.rgb, look.rgb, m);
	ROUGHNESS = 0.8;
	RIM = 0.45;
	RIM_TINT = 0.6;
}
