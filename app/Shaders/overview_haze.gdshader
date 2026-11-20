// The overview's folded systems (PLANET-91, AD-77): a soft camera-facing haze with a faint rim, the size of the system's
// ring, over a world whose children are not drawn at this distance. It says "more inside: come closer or select it".
// One MultiMesh for every folded system; per instance COLOR (the world's tint, alpha = how folded it is now, faded
// by the app as systems open and fold). The instance's scale is the haze's radius (the quad is 2 x 2).
// PLANET-108 (AD-81): inside the haze a cluster of tiny lights, the folded worlds seen from afar, so a distant system
// reads as a crowd of stars rather than an empty glow. Their places come from the folder (INSTANCE_CUSTOM.x, a seed
// from its node id: stable as the camera moves and across relayouts), their number grows with the haze's strength (how much is folded), and they are as faint as the haze.
shader_type spatial;
render_mode unshaded, blend_mix, depth_draw_never, cull_disabled, shadows_disabled;

/// The most lights in one cluster (the loop bound): enough to read as a crowd, cheap on a quad that covers few pixels.
const int MAX_LIGHTS = 18;

varying float seed;

void vertex() {
	float s = length(MODEL_MATRIX[0].xyz);
	MODELVIEW_MATRIX = VIEW_MATRIX * mat4(INV_VIEW_MATRIX[0] * s, INV_VIEW_MATRIX[1] * s, INV_VIEW_MATRIX[2] * s, MODEL_MATRIX[3]);
	MODELVIEW_NORMAL_MATRIX = mat3(MODELVIEW_MATRIX);
	seed = INSTANCE_CUSTOM.x * 1000.0;
}

float hash(float n) { return fract(sin(n) * 43758.5453); }

void fragment() {
	vec2 q = (UV - vec2(0.5)) * 2.0;
	float r = length(q);
	// A soft glow falling off to the edge, and a thin rim at the ring's radius.
	float glow = (1.0 - smoothstep(0.0, 1.0, r)) * 0.55;
	float rim = (1.0 - smoothstep(0.0, 0.05, abs(r - 0.9))) * 0.8;
	// The cluster: points scattered in the disc, denser toward the middle; each a few hundredths of the radius across.
	float lights = 0.0;
	int count = int(clamp(COLOR.a * 70.0, 4.0, float(MAX_LIGHTS)));
	for (int i = 0; i < MAX_LIGHTS; i++) {
		if (i >= count) break;
		float a = hash(seed + float(i) * 3.1) * 6.2832;
		float d = sqrt(hash(seed + float(i) * 7.7)) * 0.8;
		vec2 p = vec2(cos(a), sin(a)) * d;
		float size = 0.025 + 0.02 * hash(seed + float(i) * 1.9);
		lights += 1.0 - smoothstep(0.0, size, length(q - p));
	}
	// With no lights this is the haze exactly as before; a light is the tint whitened, as strong as the haze allows.
	float l = clamp(lights, 0.0, 1.0);
	ALBEDO = mix(COLOR.rgb, mix(COLOR.rgb, vec3(1.0), 0.6), l);
	ALPHA = clamp(COLOR.a * (glow + rim) + l * COLOR.a * 2.2, 0.0, 1.0);
}
