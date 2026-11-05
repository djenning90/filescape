// Aerial world (AERIAL-01): a place seen from the air — a dark disc with a glowing rim in the folder's colour (or its
// state colour), an inner ring like the plaza's gate ring, and lit sides. Style bits in INSTANCE_CUSTOM.w:
// 1 = pulse (being listed), 2 = hatched rim (cannot be listed: denied, unavailable), 4 = veiled (not listed yet),
// 8 = visited (a warm inner ring), 16 = selected (brighter). INSTANCE_CUSTOM.x = radius in metres.
shader_type spatial;
render_mode blend_mix, cull_back, diffuse_burley, specular_schlick_ggx;

varying vec3 local_pos;
varying vec3 local_normal;
varying float style;
varying float radius;

void vertex() {
	local_pos = VERTEX;
	local_normal = NORMAL;
	style = INSTANCE_CUSTOM.w;
	radius = max(INSTANCE_CUSTOM.x, 1.0);
}

bool has_bit(float bits, float bit) { return mod(floor(bits / bit), 2.0) >= 1.0; }

void fragment() {
	vec3 accent = COLOR.rgb;
	float r = length(local_pos.xz) * 2.0;   // 0 at the centre, 1 at the rim (the mesh is a unit-diameter cylinder)
	float m = radius * (1.0 - r);           // metres in from the rim
	float fw = max(fwidth(m), 1e-3);
	float rim = 1.0 - smoothstep(0.8, 0.8 + fw * 2.0, m);
	float inner = 1.0 - smoothstep(0.35, 0.35 + fw * 2.0, abs(radius * (0.72 - r)));
	float k = 2.6 * rim + 0.9 * inner;
	vec3 base = vec3(0.03, 0.035, 0.055);
	if (local_normal.y < 0.5) {
		k = 1.1; // the sides glow softly: a floating platform reads from far away
		base = accent * 0.12;
	}
	if (has_bit(style, 1.0)) { k *= 0.55 + 0.45 * sin(TIME * 3.0); }
	if (has_bit(style, 2.0)) { k *= step(0.5, fract((local_pos.x + local_pos.z) * radius * 0.4)); }
	if (has_bit(style, 4.0)) { k *= 0.45; base = mix(base, vec3(0.1, 0.11, 0.16), 0.6); }
	vec3 glow = accent * k;
	if (has_bit(style, 8.0) && local_normal.y > 0.5) {
		float warm = 1.0 - smoothstep(0.5, 0.5 + fw * 2.0, abs(radius * (0.55 - r)));
		glow += vec3(1.0, 0.82, 0.4) * 1.8 * warm;
	}
	if (has_bit(style, 16.0)) { glow *= 1.8; }
	ALBEDO = base;
	ROUGHNESS = 0.35;
	METALLIC = 0.6;
	EMISSION = glow;
}
