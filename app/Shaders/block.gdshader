// Classic FSN blocks and pedestals (MultiMesh). INSTANCE_CUSTOM.xyz carries the instance's world-space
// scale, so edge lines keep a constant width. INSTANCE_CUSTOM.w carries style bits:
// 1 = hidden/system (dimmed), 2 = problem (red edge), 4 = link (cyan edge), 8 = cloud (pale edge).
shader_type spatial;
render_mode blend_mix, cull_back, diffuse_burley, specular_schlick_ggx;

uniform float edge_width = 0.035;
uniform vec3 edge_color : source_color = vec3(0.02, 0.03, 0.06);
uniform float edge_strength = 0.7;
uniform float emission_strength = 0.08;
uniform float roughness_value = 0.42;
uniform float metallic_value = 0.08;
uniform float top_lift = 0.18;

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

bool has_bit(float bits, float bit) {
	return mod(floor(bits / bit), 2.0) >= 1.0;
}

float edge_factor(float width) {
	vec3 p = (local_pos + 0.5) * inst_scale;
	vec3 d = min(p, inst_scale - p);
	vec3 an = abs(local_normal);
	if (an.x > 0.5) { d.x = 1e5; } else if (an.y > 0.5) { d.y = 1e5; } else { d.z = 1e5; }
	float m = min(min(d.x, d.y), d.z);
	return 1.0 - smoothstep(width * 0.4, width, m);
}

void fragment() {
	vec3 base = COLOR.rgb;
	if (local_normal.y > 0.5) { base *= 1.0 + top_lift; }
	if (has_bit(inst_style, 1.0)) { base *= 0.42; }
	vec3 edge_col = edge_color;
	float e_strength = edge_strength;
	float width = edge_width;
	vec3 glow = vec3(0.0);
	if (has_bit(inst_style, 2.0)) { edge_col = vec3(1.0, 0.12, 0.08); width *= 2.2; glow = edge_col * 1.6; e_strength = 1.0; }
	else if (has_bit(inst_style, 4.0)) { edge_col = vec3(0.2, 0.95, 1.0); width *= 2.0; glow = edge_col * 1.3; e_strength = 1.0; }
	else if (has_bit(inst_style, 8.0)) { edge_col = vec3(0.85, 0.92, 1.0); width *= 1.8; glow = edge_col * 0.9; e_strength = 1.0; }
	float e = edge_factor(width);
	ALBEDO = mix(base, edge_col, e * e_strength);
	ROUGHNESS = roughness_value;
	METALLIC = metallic_value;
	EMISSION = base * emission_strength + glow * e;
}
