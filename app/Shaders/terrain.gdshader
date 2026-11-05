// Places (WALK-11): walkable terrain — terrace, stairs, ramps, the overlook, jump blocks, tier terraces. Unlike the
// gate frames (dark metal that reads as wireframe), these are solid and lit so their shape is clear: a pale
// treadable top with a faint tile grid, darker risers/sides shaded by the light, and a bright nosing along each top
// edge so step edges read from above and from below. The instance colour is the place's calm accent.
// INSTANCE_CUSTOM.xyz = world size of the box (for edge and tile widths in metres).
shader_type spatial;
render_mode blend_mix, cull_back, diffuse_burley, specular_schlick_ggx;

varying vec3 local_pos;
varying vec3 local_normal;
varying vec3 inst_scale;

void vertex() {
	local_pos = VERTEX;
	local_normal = NORMAL;
	inst_scale = max(INSTANCE_CUSTOM.xyz, vec3(0.001));
}

void fragment() {
	vec3 p = (local_pos + 0.5) * inst_scale;   // metres from the box corner
	vec3 an = abs(local_normal);
	vec3 accent = COLOR.rgb;
	bool top = local_normal.y > 0.5;
	bool bottom = local_normal.y < -0.5;
	// Distance to the top edge (for sides) or to the nearest rim (for the top face), in metres.
	float rim;
	if (top) {
		rim = min(min(p.x, inst_scale.x - p.x), min(p.z, inst_scale.z - p.z));
	} else {
		rim = inst_scale.y - p.y;
	}
	float fw = max(fwidth(rim), 1e-4);
	float nosing = 1.0 - smoothstep(0.06, 0.06 + fw * 1.5, rim);
	vec3 base;
	if (top) {
		// A pale, slightly tinted walking surface with a 1 m tile grid (scale cue, and it shows the slope of ramps).
		vec2 t = p.xz;
		vec2 g = abs(fract(t) - 0.5);
		float gw = fwidth(t.x) + fwidth(t.y);
		float grid = 1.0 - smoothstep(0.47, 0.47 + gw, max(g.x, g.y));
		base = mix(vec3(0.34, 0.37, 0.44), accent * 0.55 + vec3(0.18), 0.35) * (1.0 - 0.18 * grid);
	} else if (bottom) {
		base = vec3(0.05);
	} else {
		// Risers and sides: darker so each step's height reads against the next tread.
		base = mix(vec3(0.12, 0.13, 0.17), accent * 0.3, 0.3);
	}
	ALBEDO = base;
	ROUGHNESS = top ? 0.62 : 0.5;
	METALLIC = 0.15;
	// Treads glow faintly (a lit walking surface in a night scene); risers stay darker, so each step reads.
	EMISSION = accent * (1.6 * nosing) + base * (top ? 0.45 : 0.08);
}
