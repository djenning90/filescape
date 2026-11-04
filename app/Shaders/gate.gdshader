// Places (PLACE-04/11): gate frames (pillars and lintel). Dark metal with glowing edges in the instance colour:
// the target folder's accent, or a state colour for unknown listings. INSTANCE_CUSTOM.xyz = world scale,
// .w = style: 1 = pulse (pending), 2 = hatched (sealed link), 4 = dim (empty).
shader_type spatial;
render_mode blend_mix, cull_back, diffuse_burley, specular_schlick_ggx;

varying vec3 local_pos;
varying vec3 local_normal;
varying vec3 inst_scale;
varying float inst_style;

void vertex() {
	local_pos = VERTEX;
	local_normal = NORMAL;
	inst_scale = max(INSTANCE_CUSTOM.xyz, vec3(0.001));
	inst_style = INSTANCE_CUSTOM.w;
}

bool has_bit(float bits, float bit) { return mod(floor(bits / bit), 2.0) >= 1.0; }

void fragment() {
	vec3 p = (local_pos + 0.5) * inst_scale;
	vec3 d = min(p, inst_scale - p);
	vec3 an = abs(local_normal);
	if (an.x > 0.5) { d.x = 1e5; } else if (an.y > 0.5) { d.y = 1e5; } else { d.z = 1e5; }
	float m = min(min(d.x, d.y), d.z);
	float fw = max(fwidth(m), 1e-4);
	float edge = 1.0 - smoothstep(0.05, 0.05 + fw * 1.5, m);
	float strip = 0.0;
	if (an.y < 0.5) {
		// A thin vertical light strip down the middle of each face.
		float u = an.x > 0.5 ? p.z : p.x;
		float w = an.x > 0.5 ? inst_scale.z : inst_scale.x;
		strip = 1.0 - smoothstep(0.03, 0.03 + fwidth(u) * 1.5, abs(u - w * 0.5));
	}
	float k = 2.4 * edge + 1.2 * strip;
	if (has_bit(inst_style, 1.0)) { k *= 0.55 + 0.45 * sin(TIME * 3.0); }
	if (has_bit(inst_style, 2.0)) { k *= step(0.5, fract((p.x + p.y + p.z) * 1.5)); }
	if (has_bit(inst_style, 4.0)) { k *= 0.4; }
	ALBEDO = vec3(0.03, 0.035, 0.05);
	ROUGHNESS = 0.32;
	METALLIC = 0.7;
	EMISSION = COLOR.rgb * k;
}
