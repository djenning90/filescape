// District towers (MultiMesh): dark glass bodies with procedural lit windows in the instance colour (file
// category). INSTANCE_CUSTOM.xyz = world scale, .w = the style bits from block.gdshader.
shader_type spatial;
render_mode blend_mix, cull_back, diffuse_burley, specular_schlick_ggx;

uniform vec3 body_color : source_color = vec3(0.028, 0.032, 0.048);
uniform float lit_ratio = 0.52;
uniform float window_glow = 3.2;
uniform float rim_glow = 2.4;
uniform float cell_w = 0.22;
uniform float cell_h = 0.30;

varying vec3 local_pos;
varying vec3 local_normal;
varying vec3 inst_scale;
varying float inst_style;
varying float inst_seed;

void vertex() {
	local_pos = VERTEX;
	local_normal = NORMAL;
	inst_scale = max(INSTANCE_CUSTOM.xyz, vec3(0.001));
	inst_style = INSTANCE_CUSTOM.w;
	inst_seed = float(INSTANCE_ID) * 0.6180339;
}

bool has_bit(float bits, float bit) { return mod(floor(bits / bit), 2.0) >= 1.0; }

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

void fragment() {
	vec3 p = (local_pos + 0.5) * inst_scale;
	vec3 an = abs(local_normal);
	vec3 accent = COLOR.rgb;
	bool dim = has_bit(inst_style, 1.0);
	vec3 albedo = body_color;
	vec3 emission = vec3(0.0);
	float rough = 0.12;

	if (an.y > 0.5) {
		// Roof: dark with a glowing rim in the accent colour.
		vec2 q = vec2(p.x, p.z);
		vec2 d = min(q, inst_scale.xz - q);
		float rim = 1.0 - smoothstep(0.03, 0.07, min(d.x, d.y));
		albedo = body_color * 1.6;
		emission = accent * rim * rim_glow;
		rough = 0.4;
	} else {
		float u = an.x > 0.5 ? p.z : p.x;
		float face = an.x > 0.5 ? (local_normal.x > 0.0 ? 1.0 : 2.0) : (local_normal.z > 0.0 ? 3.0 : 4.0);
		vec2 cell = floor(vec2(u / cell_w, p.y / cell_h));
		vec2 f = fract(vec2(u / cell_w, p.y / cell_h));
		bool in_window = f.x > 0.18 && f.x < 0.82 && f.y > 0.22 && f.y < 0.72 && p.y > 0.18 && p.y < inst_scale.y - 0.1;
		float h = hash(cell + vec2(inst_seed * 17.0, face * 3.1));
		float lit = (in_window && h < lit_ratio) ? (0.55 + 0.45 * hash(cell.yx + inst_seed)) : 0.0;
		// A vertical accent strip at the corners.
		float side_u = min(u, (an.x > 0.5 ? inst_scale.z : inst_scale.x) - u);
		float corner = 1.0 - smoothstep(0.015, 0.04, side_u);
		albedo = body_color + (in_window ? accent * 0.05 : vec3(0.0));
		emission = accent * (lit * window_glow + corner * 0.6);
	}
	if (dim) { emission *= 0.25; }
	if (has_bit(inst_style, 2.0)) { emission += vec3(1.0, 0.1, 0.06) * 1.8; }
	if (has_bit(inst_style, 4.0)) { emission = vec3(0.2, 0.95, 1.0) * 1.4; }
	if (has_bit(inst_style, 8.0)) { emission += vec3(0.7, 0.85, 1.0) * 0.6; }
	ALBEDO = albedo;
	ROUGHNESS = rough;
	METALLIC = 0.55;
	EMISSION = emission;
}
